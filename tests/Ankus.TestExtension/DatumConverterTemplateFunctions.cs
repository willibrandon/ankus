using System.Numerics;

namespace Ankus.TestExtension;

/// <summary>
/// Selects three finite SQL representations using one constrained converter definition.
/// </summary>
/// <param name="Number">The detached logical number.</param>
[PgDatumType(typeof(TemplateNumber<int>), "int4", typeof(TemplateNumberConverter<>), Origin = PgTypeOrigin.External, Schema = "pg_catalog")]
[PgDatumType(typeof(TemplateNumber<long>), "int8", typeof(TemplateNumberConverter<>), Origin = PgTypeOrigin.External, Schema = "pg_catalog")]
[PgDatumType(typeof(TemplateNumber<short>), "int2", typeof(TemplateNumberConverter<>), Origin = PgTypeOrigin.External, Schema = "pg_catalog")]
public readonly record struct TemplateNumber<T>(T Number);

/// <summary>
/// Preserves every native integer bit through statically inferred generic math implementations.
/// </summary>
public sealed class TemplateNumberConverter<T> : IPgDatumReader<TemplateNumber<T>>, IPgDatumWriter<TemplateNumber<T>>
    where T : unmanaged, IBinaryInteger<T>
{
    /// <summary>
    /// Records the exact lazy construction without exposing static state from a generic type.
    /// </summary>
    public TemplateNumberConverter() => TemplateConverterObservations.Record(typeof(T));

    /// <inheritdoc />
    public TemplateNumber<T> Read(PgDatum value)
    {
        TemplateConverterObservations.Reads++;
        TemplateConverterObservations.Captured = value;
        nuint bits = value.DangerousGetBits();
        long word = typeof(T) == typeof(short) ? unchecked((short)bits) :
            typeof(T) == typeof(int) ? unchecked((int)bits) : unchecked((long)bits);
        if (word == 9011)
        {
            throw new PgException("P8604", "inferred converter reader failed", detail: "word 9011", hint: "use another word");
        }

        return new(~T.CreateChecked(word));
    }

    /// <inheritdoc />
    public PgDatum Write(TemplateNumber<T> value, uint typeOid, PgMemoryContext destination)
    {
        TemplateConverterObservations.Writes++;
        if (value.Number == T.CreateChecked(9012))
        {
            throw new PgException("P8605", "inferred converter writer failed", detail: "number 9012", hint: "use another number");
        }

        return PgDatum.DangerousCreate(unchecked((nuint)long.CreateChecked(~value.Number)), typeOid, destination);
    }
}

/// <summary>
/// Observes construction, direction and native lifetime independently of a generic static member.
/// </summary>
public static class TemplateConverterObservations
{
    /// <summary>
    /// Gets the narrow factory count.
    /// </summary>
    public static int IntFactories { get; private set; }

    /// <summary>
    /// Gets the wide factory count.
    /// </summary>
    public static int LongFactories { get; private set; }

    /// <summary>
    /// Gets the raw-only factory count.
    /// </summary>
    public static int ShortFactories { get; private set; }

    /// <summary>
    /// Gets or sets the total reader invocation count.
    /// </summary>
    public static int Reads { get; set; }

    /// <summary>
    /// Gets or sets the total writer invocation count.
    /// </summary>
    public static int Writes { get; set; }

    /// <summary>
    /// Gets or sets the last borrowed native operand.
    /// </summary>
    public static PgDatum? Captured { get; set; }

    /// <summary>
    /// Records only supported concrete numeric constructions.
    /// </summary>
    internal static void Record(Type argument)
    {
        if (argument == typeof(int))
        {
            IntFactories++;
        }
        else if (argument == typeof(long))
        {
            LongFactories++;
        }
        else if (argument == typeof(short))
        {
            ShortFactories++;
        }
        else
        {
            throw new InvalidOperationException("Unexpected converter construction.");
        }
    }
}

/// <summary>
/// Selects a raw-only root whose converter reverses the containing and immediate argument positions.
/// </summary>
/// <param name="Number">The independently transformed native number.</param>
[PgDatumType(typeof(TemplatePair<int, long>), "int4", typeof(TemplateFamily<>.Converter<>), Origin = PgTypeOrigin.External, Schema = "pg_catalog")]
public readonly record struct TemplatePair<TFirst, TSecond>(int Number);

/// <summary>
/// Carries an independently inferred outer argument into the nested reader implementation.
/// </summary>
public static class TemplateFamily<TOuter>
{
    /// <summary>
    /// Reads a root with reversed generic parameter positions.
    /// </summary>
    public sealed class Converter<TInner> : IPgDatumReader<TemplatePair<TInner, TOuter>>
    {
        /// <inheritdoc />
        public TemplatePair<TInner, TOuter> Read(PgDatum value)
            => new(checked(unchecked((int)value.DangerousGetBits()) +
                (typeof(TOuter) == typeof(long) && typeof(TInner) == typeof(int) ? 3000 : -3000)));
    }
}

/// <summary>
/// Executes inferred finite converters through actual PostgreSQL calls and Native AOT generic math.
/// </summary>
[PgSchema("template_mappings")]
public static class DatumConverterTemplateFunctions
{
    /// <summary>
    /// Exposes the independent logical int4 value.
    /// </summary>
    [PgFunction(Name = "read_int")]
    public static int ReadInt(TemplateNumber<int> value) => value.Number;

    /// <summary>
    /// Exposes the independent logical int8 value.
    /// </summary>
    [PgFunction(Name = "read_long")]
    public static long ReadLong(TemplateNumber<long> value) => value.Number;

    /// <summary>
    /// Writes a caller-supplied logical int4 value.
    /// </summary>
    [PgFunction(Name = "make_int")]
    public static TemplateNumber<int> MakeInt(int value) => new(value);

    /// <summary>
    /// Writes a caller-supplied logical int8 value.
    /// </summary>
    [PgFunction(Name = "make_long")]
    public static TemplateNumber<long> MakeLong(long value) => new(value);

    /// <summary>
    /// Distinguishes absent and present zero values without an absent-value factory.
    /// </summary>
    [PgFunction(Name = "optional")]
    public static string Optional(TemplateNumber<int>? value) => value?.Number.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "absent";

    /// <summary>
    /// Preserves native int8 array shape, null cells and exact extrema.
    /// </summary>
    [PgFunction(Name = "echo_array")]
    public static PgArray<TemplateNumber<long>?>? EchoArray(PgArray<TemplateNumber<long>?>? value) => value;

    /// <summary>
    /// Invokes raw-only and nested roots alongside a typed SPI scalar.
    /// </summary>
    [PgFunction(Name = "raw_and_spi")]
    public static string RawAndSpi()
    {
        TemplateNumber<short> narrow;
        TemplatePair<int, long> nested;
        using (SpiRawResult result = Spi.QueryRaw("SELECT 17::smallint, 23::integer"))
        {
            narrow = result[0][0].Read<TemplateNumber<short>>();
            nested = result[0][1].Read<TemplatePair<int, long>>();
        }

        TemplateNumber<long> wide = Spi.ExecuteScalar<TemplateNumber<long>>("SELECT 4294967297::bigint");
        return $"{narrow.Number}|{nested.Number}|{wide.Number}";
    }

    /// <summary>
    /// Attempts a present or NULL sibling SQL identity before any converter should run.
    /// </summary>
    [PgFunction(Name = "wrong_oid")]
    public static short WrongOid(bool absent)
    {
        using SpiRawResult result = Spi.QueryRaw(absent ? "SELECT NULL::bigint" : "SELECT 17::bigint");
        return result[0][0].Read<TemplateNumber<short>>().Number;
    }

    /// <summary>
    /// Reports independently selected factories and direction counts.
    /// </summary>
    [PgFunction(Name = "counts")]
    public static string Counts() => $"{TemplateConverterObservations.IntFactories}|{TemplateConverterObservations.LongFactories}|{TemplateConverterObservations.ShortFactories}|{TemplateConverterObservations.Reads}|{TemplateConverterObservations.Writes}";

    /// <summary>
    /// Checks captured borrowed storage after callback or SPI ownership expires.
    /// </summary>
    [PgFunction(Name = "expired")]
    public static bool Expired()
    {
        PgDatum value = TemplateConverterObservations.Captured ?? throw new InvalidOperationException("No operand was captured.");
        try
        {
            _ = value.DangerousGetBits();
            return false;
        }
        catch (ObjectDisposedException)
        {
            return true;
        }
    }
}
