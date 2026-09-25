using Ankus;
using Ankus.TestExtension;

[assembly: PgSql("a-mapped-range", "CREATE TYPE range_mappings.bounds AS RANGE (subtype=range_mappings.bound);", Requires = ["mapped-range-schema"])]
[assembly: PgSqlTypeProvider("a-mapped-range", typeof(PgRange<RangeNumber<long>>))]
[assembly: PgSql("z-mapped-bound", """
    CREATE DOMAIN range_mappings.bound AS integer
        CHECK (VALUE > 0 AND coalesce(current_setting('ankus.range_reject', true), 'off') <> 'on');
    """, Requires = ["mapped-range-schema"])]
[assembly: PgSqlTypeProvider("z-mapped-bound", typeof(RangeNumber<long>))]
[assembly: PgSql("mapped-text-range", "CREATE TYPE range_mappings.words AS RANGE (subtype=text, collation=\"C\");", Requires = ["mapped-range-schema"])]
[assembly: PgSqlTypeProvider("mapped-text-range", typeof(PgRange<RangeWord>))]

namespace Ankus.TestExtension;

/// <summary>
/// Carries a detached variable-length bound with exact text storage identity.
/// </summary>
/// <param name="Text">The copied UTF-16 text.</param>
[PgDatumType("text", typeof(RangeWordConverter), Origin = PgTypeOrigin.External, Schema = "pg_catalog")]
[PgRangeType("words", Schema = "range_mappings")]
public readonly record struct RangeWord(string Text);

/// <summary>
/// Reads variable-length native bounds and can return a caller-owned alias without transferring ownership.
/// </summary>
public sealed class RangeWordConverter : IPgDatumReader<RangeWord>, IPgDatumWriter<RangeWord>
{
    /// <inheritdoc />
    public RangeWord Read(PgDatum value)
    {
        DatumRangeObservations.Captured = value;
        return new(value.Read<string>());
    }

    /// <inheritdoc />
    public PgDatum Write(RangeWord value, uint typeOid, PgMemoryContext destination)
    {
        if (value.Text == "borrowed" && DatumRangeObservations.Alias is { } alias)
        {
            return alias;
        }

        using SpiRawResult result = Spi.QueryRaw("SELECT $1", SpiParameter.Create(value.Text));
        return result[0][0].CopyTo(destination);
    }
}

/// <summary>
/// Selects independent scalar and range identities through finite generic metadata.
/// </summary>
/// <param name="Number">The detached logical bound, offset from its native value.</param>
[PgDatumType(typeof(RangeNumber<int>), "int4", typeof(RangeNumberConverter<>), Origin = PgTypeOrigin.External, Schema = "pg_catalog")]
[PgRangeType(typeof(RangeNumber<int>), "int4range", Origin = PgTypeOrigin.External, Schema = "pg_catalog")]
[PgDatumType(typeof(RangeNumber<long>), "bound", typeof(RangeNumberConverter<>), Schema = "range_mappings")]
[PgRangeType(typeof(RangeNumber<long>), "bounds", Schema = "range_mappings")]
[PgDatumType(typeof(RangeNumber<short>), "int8", typeof(RangeNumberConverter<>), Origin = PgTypeOrigin.External, Schema = "pg_catalog")]
[PgRangeType(typeof(RangeNumber<short>), "int4range", Origin = PgTypeOrigin.External, Schema = "pg_catalog")]
public readonly record struct RangeNumber<T>(int Number);

/// <summary>
/// Shares scalar conversion with finite range bounds while exposing lifetime and diagnostic observations.
/// </summary>
public sealed class RangeNumberConverter<T> : IPgDatumReader<RangeNumber<T>>, IPgDatumWriter<RangeNumber<T>>
{
    /// <summary>
    /// Records construction independently for each selected scalar root.
    /// </summary>
    public RangeNumberConverter() => DatumRangeObservations.Factories++;

    /// <inheritdoc />
    public RangeNumber<T> Read(PgDatum value)
    {
        DatumRangeObservations.Reads++;
        DatumRangeObservations.Captured = value;
        int number = unchecked((int)value.DangerousGetBits());
        if (number == 9001)
        {
            throw new PgException("P8611", "mapped range reader failed", detail: "bound 9001", hint: "choose another bound");
        }

        return new(checked(number + 1000));
    }

    /// <inheritdoc />
    public PgDatum Write(RangeNumber<T> value, uint typeOid, PgMemoryContext destination)
    {
        DatumRangeObservations.Writes++;
        if (value.Number == 9012)
        {
            throw new PgException("P8612", "mapped range writer failed", detail: "bound 9012", hint: "choose another bound");
        }

        return PgDatum.DangerousCreate(unchecked((nuint)checked(value.Number - 1000)), typeOid, destination, isNull: value.Number == 9013);
    }
}

/// <summary>
/// Retains observations outside generic types and never owns borrowed reader storage.
/// </summary>
public static class DatumRangeObservations
{
    /// <summary>
    /// Gets or sets the number of scalar converter constructions.
    /// </summary>
    public static int Factories { get; set; }

    /// <summary>
    /// Gets or sets finite bound reads.
    /// </summary>
    public static int Reads { get; set; }

    /// <summary>
    /// Gets or sets finite bound writes.
    /// </summary>
    public static int Writes { get; set; }

    /// <summary>
    /// Gets or sets the last borrowed finite bound.
    /// </summary>
    public static PgDatum? Captured { get; set; }

    /// <summary>
    /// Gets or sets a native word whose owner remains with the calling function.
    /// </summary>
    public static PgDatum? Alias { get; set; }
}

/// <summary>
/// Exercises mapped ranges through ordinary callbacks, raw owners, typed SPI and native operations.
/// </summary>
[PgSchema("range_mappings", Id = "mapped-range-schema")]
public static class DatumRangeFunctions
{
    /// <summary>
    /// Preserves variable-length range bounds through generated raw conversion.
    /// </summary>
    [PgFunction(Name = "echo_words")]
    public static PgRange<RangeWord>? EchoWords(PgRange<RangeWord>? value) => value;

    /// <summary>
    /// Observes the exact text copied from both bounds independently of range serialization.
    /// </summary>
    [PgFunction(Name = "read_words")]
    public static string ReadWords(PgRange<RangeWord> value) => $"{value.Lower?.Text}|{value.Upper?.Text}";

    /// <summary>
    /// Confirms range construction retains borrowed writer storage until its caller disposes that storage.
    /// </summary>
    [PgFunction(Name = "borrowed_word")]
    public static string BorrowedWord()
    {
        using SpiRawResult result = Spi.QueryRaw("SELECT 'alpha'::text");
        PgDatum alias = result[0][0];
        DatumRangeObservations.Alias = alias;
        try
        {
            var range = new PgRange<RangeWord>(new("borrowed"), new("zulu"));
            string formatted = range.ToPostgresString();
            return formatted + "|" + alias.Read<string>();
        }
        finally
        {
            DatumRangeObservations.Alias = null;
        }
    }

    /// <summary>
    /// Exposes independent logical bounds and flags without converting them back to SQL range storage.
    /// </summary>
    [PgFunction(Name = "read")]
    public static string Read(PgRange<RangeNumber<int>>? value) => Describe(value);

    /// <summary>
    /// Reads the owned range without reassigning its domain bounds.
    /// </summary>
    [PgFunction(Name = "read_domain")]
    public static string ReadDomain(PgRange<RangeNumber<long>>? value) => Describe(value);

    /// <summary>
    /// Constructs independently supplied logical bounds for PostgreSQL canonicalization.
    /// </summary>
    [PgFunction(Name = "make")]
    public static PgRange<RangeNumber<int>> Make(int? lower, int? upper, bool lowerInclusive, bool upperInclusive)
        => new(lower is { } first ? new(first) : null, upper is { } second ? new(second) : null, lowerInclusive, upperInclusive);

    /// <summary>
    /// Constructs domain bounds subject to their write-side CHECK constraints.
    /// </summary>
    [PgFunction(Name = "make_domain")]
    public static PgRange<RangeNumber<long>> MakeDomain(int? lower, int? upper)
        => new(lower is { } first ? new(first) : null, upper is { } second ? new(second) : null);

    /// <summary>
    /// Preserves an empty, unbounded, present or absent range through both directions.
    /// </summary>
    [PgFunction(Name = "echo")]
    public static PgRange<RangeNumber<int>>? Echo(PgRange<RangeNumber<int>>? value) => value;

    /// <summary>
    /// Preserves shaped arrays with SQL NULL cells and empty ranges.
    /// </summary>
    [PgFunction(Name = "echo_array")]
    public static PgArray<PgRange<RangeNumber<int>>?>? EchoArray(PgArray<PgRange<RangeNumber<int>>?>? value) => value;

    /// <summary>
    /// Reads ranges through raw and typed SPI owners, then uses their detached bounds after disposal.
    /// </summary>
    [PgFunction(Name = "raw_and_spi")]
    public static string RawAndSpi()
    {
        PgRange<RangeNumber<int>> raw;
        using (SpiRawResult result = Spi.QueryRaw("SELECT '[3,8)'::int4range"))
        {
            raw = result[0][0].Read<PgRange<RangeNumber<int>>>();
        }

        PgRange<RangeNumber<int>> typed = Spi.ExecuteScalar<PgRange<RangeNumber<int>>>("SELECT '[11,17)'::int4range");
        PgRange<RangeNumber<int>> written = Spi.ExecuteScalar<PgRange<RangeNumber<int>>>("SELECT $1", SpiParameter.Create(raw));
        return Describe(raw) + "|" + Describe(typed) + "|" + Describe(written);
    }

    /// <summary>
    /// Exercises every native range-valued operation through the mapped result reader.
    /// </summary>
    [PgFunction(Name = "combine")]
    public static PgRange<RangeNumber<int>> Combine(PgRange<RangeNumber<int>> left, PgRange<RangeNumber<int>> right, int operation)
        => operation switch
        {
            0 => left.Canonicalize(),
            1 => left.Union(right),
            2 => left.Intersect(right),
            3 => left.Except(right),
            4 => left.Merge(right),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };

    /// <summary>
    /// Exercises known scalar-returning native range operations with exact mapped subtype parameters.
    /// </summary>
    [PgFunction(Name = "test")]
    public static bool Test(PgRange<RangeNumber<int>> left, PgRange<RangeNumber<int>> right, int value, int operation)
        => operation switch
        {
            0 => left.Contains(new RangeNumber<int>(value)),
            1 => left.Contains(right),
            2 => left.Overlaps(right),
            3 => left.IsAdjacentTo(right),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };

    /// <summary>
    /// Exercises custom domain range input, output and detached reading of native results.
    /// </summary>
    [PgFunction(Name = "parse_domain")]
    public static string ParseDomain(string text)
    {
        PgRange<RangeNumber<long>> value = PgRange.Parse<RangeNumber<long>>(text);
        return Describe(value) + "|" + value.ToPostgresString();
    }

    /// <summary>
    /// Executes Boolean and range-valued functions against a non-built-in catalog range identity.
    /// </summary>
    [PgFunction(Name = "domain_operations")]
    public static string DomainOperations()
    {
        PgRange<RangeNumber<long>> left = PgRange.Parse<RangeNumber<long>>("[1,5)");
        PgRange<RangeNumber<long>> right = PgRange.Parse<RangeNumber<long>>("[5,7)");
        return $"{left.Contains(new RangeNumber<long>(1003))}|{left.IsAdjacentTo(right)}|{left.Union(right).ToPostgresString()}";
    }

    /// <summary>
    /// Distinguishes malformed native input from valid custom range text.
    /// </summary>
    [PgFunction(Name = "try_parse_domain")]
    public static bool TryParseDomain(string text) => PgRange.TryParse<RangeNumber<long>>(text, out _);

    /// <summary>
    /// Attempts sibling range storage before any scalar converter can be constructed.
    /// </summary>
    [PgFunction(Name = "wrong_oid")]
    public static string WrongOid(bool absent)
    {
        using SpiRawResult result = Spi.QueryRaw(absent ? "SELECT NULL::int8range" : "SELECT '[1,5)'::int8range");
        return Describe(result[0][0].Read<PgRange<RangeNumber<int>>?>());
    }

    /// <summary>
    /// Rejects metadata whose scalar identity differs from the real range subtype, including whole SQL NULL.
    /// </summary>
    [PgFunction(Name = "wrong_subtype")]
    public static string WrongSubtype(bool absent)
    {
        using SpiRawResult result = Spi.QueryRaw(absent ? "SELECT NULL::int4range" : "SELECT '[1,5)'::int4range");
        return Describe(result[0][0].Read<PgRange<RangeNumber<short>>?>());
    }

    /// <summary>
    /// Reports lazy scalar construction and finite bound direction counts.
    /// </summary>
    [PgFunction(Name = "counts")]
    public static string Counts() => $"{DatumRangeObservations.Factories}|{DatumRangeObservations.Reads}|{DatumRangeObservations.Writes}";

    /// <summary>
    /// Checks that temporary bound handles expire before callback completion.
    /// </summary>
    [PgFunction(Name = "expired")]
    public static bool Expired()
    {
        PgDatum captured = DatumRangeObservations.Captured ?? throw new InvalidOperationException("No bound was captured.");
        try
        {
            _ = captured.DangerousGetBits();
            return false;
        }
        catch (ObjectDisposedException)
        {
            return true;
        }
    }

    /// <summary>
    /// Describes managed bounds independently of native formatting or serialization.
    /// </summary>
    private static string Describe<T>(PgRange<RangeNumber<T>>? value) => value is null ? "null" : value.IsEmpty ? "empty" :
        $"{value.Lower?.Number.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "infinite"}|{value.Upper?.Number.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "infinite"}|{value.LowerInclusive}|{value.UpperInclusive}";
}
