using System.Globalization;

namespace Ankus.TestExtension;

/// <summary>
/// Gives array probes a dedicated integer mapping with observable element conversion.
/// </summary>
/// <param name="Value">The stored integer or deliberate failure selector.</param>
[PgDatumType("int4", typeof(ArrayValueConverter), Schema = "pg_catalog", Origin = PgTypeOrigin.External)]
public readonly record struct ArrayValue(int Value);

/// <summary>
/// Tracks every element owner and creates selected later-element failures.
/// </summary>
public sealed class ArrayValueConverter : IPgDatumReader<ArrayValue>, IPgDatumWriter<ArrayValue>
{
    /// <summary>
    /// Counts lazy factories shared by scalar, vector and shaped adapters.
    /// </summary>
    public static int Constructions { get; private set; }

    /// <summary>
    /// Counts present reads since the probe reset.
    /// </summary>
    public static int Reads { get; private set; }

    /// <summary>
    /// Counts present writes since the probe reset.
    /// </summary>
    public static int Writes { get; private set; }

    /// <summary>
    /// Retains checked inputs solely to verify extraction-owner cleanup.
    /// </summary>
    internal static List<PgDatum> Inputs { get; } = [];

    /// <summary>
    /// Retains writer destinations solely to verify eager-construction cleanup.
    /// </summary>
    internal static List<PgMemoryContext> Destinations { get; } = [];

    /// <summary>
    /// Records lazy factory creation.
    /// </summary>
    public ArrayValueConverter() => Constructions++;

    /// <summary>
    /// Clears per-operation probes without pretending the shared lazy factory was recreated.
    /// </summary>
    public static void Reset()
    {
        Reads = 0;
        Writes = 0;
        Inputs.Clear();
        Destinations.Clear();
    }

    /// <inheritdoc />
    public ArrayValue Read(PgDatum value)
    {
        Reads++;
        Inputs.Add(value);
        int number = value.Read<int>();
        if (number == -777)
        {
            throw new PgException("P8521", "mapped array reader failed", detail: "later element", hint: "replace the sentinel");
        }

        return new ArrayValue(number);
    }

    /// <inheritdoc />
    public PgDatum Write(ArrayValue value, uint typeOid, PgMemoryContext destination)
    {
        Writes++;
        Destinations.Add(destination);
        if (value.Value == -888)
        {
            throw new PgException("P8522", "mapped array writer failed", detail: "later element", hint: "replace the sentinel");
        }

        if (value.Value == -444)
        {
            using PgMemoryContext expired = PgMemoryContext.Create("mapped array expired writer");
            return PgDatum.DangerousCreate(0, typeOid, expired, isNull: true);
        }

        if (value.Value == -333)
        {
            destination.Reset();
        }

        return PgDatum.DangerousCreate(unchecked((nuint)value.Value), value.Value == -555 ? 20U : typeOid,
            destination, isNull: value.Value is -666 or -555);
    }
}

/// <summary>
/// Supplies dedicated by-reference array data and input/destination lifetime probes.
/// </summary>
/// <param name="Value">The independent text.</param>
[PgDatumType("text", typeof(ArrayTextConverter), Schema = "pg_catalog", Origin = PgTypeOrigin.External)]
public sealed record ArrayText(string Value);

/// <summary>
/// Copies text while retaining only checked test witnesses of the temporary owners.
/// </summary>
public sealed class ArrayTextConverter : IPgDatumReader<ArrayText>, IPgDatumWriter<ArrayText>
{
    /// <summary>
    /// Retains all reader inputs for post-conversion checks.
    /// </summary>
    internal static List<PgDatum> Inputs { get; } = [];

    /// <summary>
    /// Retains all supplied writer destinations for cleanup checks.
    /// </summary>
    internal static List<PgMemoryContext> Destinations { get; } = [];

    /// <summary>
    /// Supplies an independently owned source that a writer may return without transferring ownership.
    /// </summary>
    public static PgDatum? Borrowed { get; set; }

    /// <inheritdoc />
    public ArrayText Read(PgDatum value)
    {
        Inputs.Add(value);
        string text = value.Read<string>();
        if (text == "array-reader-error")
        {
            throw new PgException("P8523", "mapped text array reader failed", detail: "detached text", hint: "use another element");
        }

        return new ArrayText(text);
    }

    /// <inheritdoc />
    public PgDatum Write(ArrayText value, uint typeOid, PgMemoryContext destination)
    {
        Destinations.Add(destination);
        if (value.Value == "borrowed")
        {
            return Borrowed ?? throw new InvalidOperationException("The borrowed source was not installed.");
        }

        if (value.Value == "array-writer-error")
        {
            throw new PgException("P8524", "mapped text array writer failed", detail: "managed text", hint: "use another element");
        }

        using SpiRawResult result = Spi.QueryRaw("SELECT $1", SpiParameter.Create(value.Value));
        return result[0][0].CopyTo(destination);
    }
}

/// <summary>
/// Uses a byte-backed CLR enum without conflating its vector with bytea.
/// </summary>
[PgDatumType("int2", typeof(ArrayByteConverter), Schema = "pg_catalog", Origin = PgTypeOrigin.External)]
public enum ArrayByte : byte
{
    /// <summary>
    /// Represents zero; every unnamed underlying byte remains valid too.
    /// </summary>
    Zero,
}

/// <summary>
/// Preserves all byte bits through a PostgreSQL smallint scalar.
/// </summary>
public sealed class ArrayByteConverter : IPgDatumReader<ArrayByte>, IPgDatumWriter<ArrayByte>
{
    /// <inheritdoc />
    public ArrayByte Read(PgDatum value) => (ArrayByte)checked((byte)value.Read<short>());

    /// <inheritdoc />
    public PgDatum Write(ArrayByte value, uint typeOid, PgMemoryContext destination)
        => PgDatum.DangerousCreate((byte)value, typeOid, destination);
}

/// <summary>
/// Resolves a dedicated external domain and its array again after live catalog changes.
/// </summary>
/// <param name="Value">The detached integer.</param>
[PgDatumType("value", typeof(ArrayLiveConverter), Schema = "mapped_array_live", Origin = PgTypeOrigin.External)]
public readonly record struct ArrayLive(int Value);

/// <summary>
/// Counts writes so stale retained parameters cannot silently invoke user conversion.
/// </summary>
public sealed class ArrayLiveConverter : IPgDatumReader<ArrayLive>, IPgDatumWriter<ArrayLive>
{
    /// <summary>
    /// Counts actual present writes.
    /// </summary>
    public static int Writes { get; private set; }

    /// <inheritdoc />
    public ArrayLive Read(PgDatum value) => new(value.Read<int>());

    /// <inheritdoc />
    public PgDatum Write(ArrayLive value, uint typeOid, PgMemoryContext destination)
    {
        Writes++;
        return PgDatum.DangerousCreate(unchecked((nuint)value.Value), typeOid, destination);
    }
}

/// <summary>
/// Explicitly maps an outer array domain as one scalar rather than weakening the array adapter's identity.
/// </summary>
/// <param name="Value">The independently described contained values.</param>
[PgDatumType("outer_array", typeof(ArrayDomainScalarConverter), Schema = "mapped_array_domains", Origin = PgTypeOrigin.External)]
public readonly record struct ArrayDomainScalar(string Value);

/// <summary>
/// Uses the existing raw polymorphic representation inside an explicitly selected scalar contract.
/// </summary>
public sealed class ArrayDomainScalarConverter : IPgDatumReader<ArrayDomainScalar>
{
    /// <inheritdoc />
    public ArrayDomainScalar Read(PgDatum value)
    {
        PgAnyArray array = value.Read<PgAnyArray>();
        return new ArrayDomainScalar(string.Join('|', array.Select(static item =>
            item is null ? "NULL" : item.Read<MappedPositive>().Value.ToString(CultureInfo.InvariantCulture))));
    }
}
