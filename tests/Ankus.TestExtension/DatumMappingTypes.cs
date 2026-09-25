namespace Ankus.TestExtension;

/// <summary>
/// Stores an unsigned 24-bit integer directly in a PostgreSQL datum word.
/// </summary>
/// <param name="Value">The unsigned value.</param>
[PgDatumType("u24", typeof(U24DatumConverter), Schema = "datum_mappings")]
public readonly record struct MappedU24(uint Value);

/// <summary>
/// Gives the same SQL value a distinct managed identity and conversion.
/// </summary>
/// <param name="Value">The SQL value increased by one.</param>
[PgDatumType("u24", typeof(U24AliasConverter), Schema = "datum_mappings")]
public readonly record struct MappedU24Alias(uint Value);

/// <summary>
/// Converts a manual by-value type and counts actual user-code entry.
/// </summary>
public sealed class U24DatumConverter : IPgDatumReader<MappedU24>, IPgDatumWriter<MappedU24>
{
    /// <summary>
    /// Counts converter construction, which must occur only on a present conversion.
    /// </summary>
    public static int Constructions { get; private set; }

    /// <summary>
    /// Counts present reads.
    /// </summary>
    public static int Reads { get; private set; }

    /// <summary>
    /// Counts present writes.
    /// </summary>
    public static int Writes { get; private set; }

    /// <summary>
    /// Records lazy construction inside the managed boundary.
    /// </summary>
    public U24DatumConverter() => Constructions++;

    /// <inheritdoc />
    public MappedU24 Read(PgDatum value)
    {
        Reads++;
        return new MappedU24(checked((uint)value.DangerousGetBits()));
    }

    /// <inheritdoc />
    public PgDatum Write(MappedU24 value, uint typeOid, PgMemoryContext destination)
    {
        Writes++;
        if (value.Value > 0xFFFFFF)
        {
            throw new PgException("22003", "mapped value exceeds 24 bits");
        }

        return PgDatum.DangerousCreate(value.Value, typeOid, destination);
    }
}

/// <summary>
/// Uses independently observable conversion for another wrapper of u24.
/// </summary>
public sealed class U24AliasConverter : IPgDatumReader<MappedU24Alias>, IPgDatumWriter<MappedU24Alias>
{
    /// <inheritdoc />
    public MappedU24Alias Read(PgDatum value) => new(checked((uint)value.DangerousGetBits() + 1));

    /// <inheritdoc />
    public PgDatum Write(MappedU24Alias value, uint typeOid, PgMemoryContext destination)
        => PgDatum.DangerousCreate(checked(value.Value - 1), typeOid, destination);
}

/// <summary>
/// Stores two doubles in PostgreSQL's fixed-size by-reference representation.
/// </summary>
/// <param name="Real">The first component.</param>
/// <param name="Imaginary">The second component.</param>
[PgDatumType("complex", typeof(ComplexDatumConverter), Schema = "datum_mappings")]
public readonly record struct MappedComplex(double Real, double Imaginary);

/// <summary>
/// Copies the fixed sixteen-byte representation without retaining native pointers.
/// </summary>
public sealed class ComplexDatumConverter : IPgDatumReader<MappedComplex>, IPgDatumWriter<MappedComplex>
{
    /// <inheritdoc />
    public unsafe MappedComplex Read(PgDatum value)
    {
        double* components = (double*)value.DangerousGetBits();
        return new MappedComplex(components[0], components[1]);
    }

    /// <inheritdoc />
    public unsafe PgDatum Write(MappedComplex value, uint typeOid, PgMemoryContext destination)
    {
        PgAllocation allocation = destination.Allocate<double>(2);
        allocation.Write(value.Real);
        allocation.Write(value.Imaginary, sizeof(double));
        return PgDatum.DangerousCreate((nuint)allocation.DangerousGetPointer(), typeOid, destination);
    }
}

/// <summary>
/// Retains the exact positive-domain identity separately from its integer storage.
/// </summary>
/// <param name="Value">The underlying integer.</param>
[PgDatumType("positive", typeof(PositiveDatumConverter), Schema = "datum_mappings")]
public readonly record struct MappedPositive(int Value);

/// <summary>
/// Deliberately delegates domain constraints to PostgreSQL after managed frames unwind.
/// </summary>
public sealed class PositiveDatumConverter : IPgDatumReader<MappedPositive>, IPgDatumWriter<MappedPositive>
{
    /// <inheritdoc />
    public MappedPositive Read(PgDatum value) => new(unchecked((int)value.DangerousGetBits()));

    /// <inheritdoc />
    public PgDatum Write(MappedPositive value, uint typeOid, PgMemoryContext destination)
        => PgDatum.DangerousCreate(unchecked((nuint)value.Value), typeOid, destination);
}

/// <summary>
/// Wraps built-in text without claiming ownership of its SQL declaration.
/// </summary>
/// <param name="Value">The detached text.</param>
[PgDatumType("text", typeof(TextDatumConverter), Schema = "pg_catalog", Origin = PgTypeOrigin.External)]
public record MappedText(string Value);

/// <summary>
/// Supplies a runtime subtype to verify declared generic parameter conversion.
/// </summary>
/// <param name="Value">The inherited text.</param>
public sealed record DerivedMappedText(string Value) : MappedText(Value);

/// <summary>
/// Reads and writes text through independent ordinary and raw backend owners.
/// </summary>
public sealed class TextDatumConverter : IPgDatumReader<MappedText>, IPgDatumWriter<MappedText>
{
    /// <inheritdoc />
    public MappedText Read(PgDatum value)
    {
        string text = value.Read<string>();
        if (text == "reader-error")
        {
            throw new PgException("P8501", "mapped reader failed", detail: "detached text", hint: "use another value");
        }

        return new MappedText(text);
    }

    /// <inheritdoc />
    public PgDatum Write(MappedText value, uint typeOid, PgMemoryContext destination)
    {
        if (value.Value == "writer-error")
        {
            throw new PgException("P8502", "mapped writer failed", detail: "managed text", hint: "use another value");
        }

        using SpiRawResult result = Spi.QueryRaw("SELECT $1", SpiParameter.Create(value.Value));
        PgDatum copy = result[0][0].CopyTo(destination);
        if (copy.TypeOid != typeOid)
        {
            throw new InvalidOperationException("Text writer received an unexpected target identity.");
        }

        return copy;
    }
}

/// <summary>
/// Exposes only SQL-to-managed integer conversion.
/// </summary>
/// <param name="Value">The original SQL integer plus one hundred.</param>
[PgDatumType("int4", typeof(ReadIntConverter), Schema = "pg_catalog", Origin = PgTypeOrigin.External)]
public readonly record struct ReadMappedInt(int Value);

/// <summary>
/// Implements a genuinely read-only conversion.
/// </summary>
public sealed class ReadIntConverter : IPgDatumReader<ReadMappedInt>
{
    /// <inheritdoc />
    public ReadMappedInt Read(PgDatum value) => new(checked(value.Read<int>() + 100));
}

/// <summary>
/// Exposes only managed-to-SQL integer conversion.
/// </summary>
/// <param name="Value">The managed integer to negate.</param>
[PgDatumType("int4", typeof(WriteIntConverter), Schema = "pg_catalog", Origin = PgTypeOrigin.External)]
public readonly record struct WriteMappedInt(int Value);

/// <summary>
/// Implements an independently observable write-only conversion.
/// </summary>
public sealed class WriteIntConverter : IPgDatumWriter<WriteMappedInt>
{
    /// <inheritdoc />
    public PgDatum Write(WriteMappedInt value, uint typeOid, PgMemoryContext destination)
        => PgDatum.DangerousCreate(unchecked((nuint)checked(-value.Value)), typeOid, destination);
}

/// <summary>
/// Selects a deliberately valid or invalid writer result.
/// </summary>
/// <param name="Mode">The output invariant to exercise.</param>
[PgDatumType("int4", typeof(AdversarialIntConverter), Schema = "pg_catalog", Origin = PgTypeOrigin.External)]
public readonly record struct AdversarialMappedInt(int Mode);

/// <summary>
/// Supplies wrong identities, stale owners and live typed NULL without dereferencing invalid memory.
/// </summary>
public sealed class AdversarialIntConverter : IPgDatumWriter<AdversarialMappedInt>
{
    /// <inheritdoc />
    public PgDatum Write(AdversarialMappedInt value, uint typeOid, PgMemoryContext destination)
    {
        if (value.Mode < 2)
        {
            return PgDatum.DangerousCreate(42, 20, destination, isNull: value.Mode == 1);
        }

        if (value.Mode < 4)
        {
            using PgMemoryContext expired = PgMemoryContext.Create("mapped expired result");
            return PgDatum.DangerousCreate(42, typeOid, expired, isNull: value.Mode == 3);
        }

        return PgDatum.DangerousCreate(42, typeOid, destination, isNull: value.Mode == 4);
    }
}

/// <summary>
/// Resolves an external domain anew after same-session catalog changes.
/// </summary>
/// <param name="Value">The copied integer.</param>
[PgDatumType("value", typeof(LiveDatumConverter), Schema = "datum_mapping_live", Origin = PgTypeOrigin.External)]
public readonly record struct LiveMappedInt(int Value);

/// <summary>
/// Keeps catalog identities out of the shared converter instance.
/// </summary>
public sealed class LiveDatumConverter : IPgDatumReader<LiveMappedInt>, IPgDatumWriter<LiveMappedInt>
{
    /// <inheritdoc />
    public LiveMappedInt Read(PgDatum value) => new(value.Read<int>());

    /// <inheritdoc />
    public PgDatum Write(LiveMappedInt value, uint typeOid, PgMemoryContext destination)
        => PgDatum.DangerousCreate(unchecked((nuint)value.Value), typeOid, destination);
}

/// <summary>
/// Uses a CLR enum as a mapped integer representation without declaring a PostgreSQL enum.
/// </summary>
[PgDatumType("int4", typeof(SignDatumConverter), Schema = "pg_catalog", Origin = PgTypeOrigin.External)]
public enum MappedSign
{
    /// <summary>
    /// Represents negative one.
    /// </summary>
    Negative = -1,

    /// <summary>
    /// Represents a present zero.
    /// </summary>
    Zero,

    /// <summary>
    /// Represents positive one.
    /// </summary>
    Positive,
}

/// <summary>
/// Converts the enum's exact underlying integer independently of SQL enum labels.
/// </summary>
public sealed class SignDatumConverter : IPgDatumReader<MappedSign>, IPgDatumWriter<MappedSign>
{
    /// <inheritdoc />
    public MappedSign Read(PgDatum value) => (MappedSign)value.Read<int>();

    /// <inheritdoc />
    public PgDatum Write(MappedSign value, uint typeOid, PgMemoryContext destination)
        => PgDatum.DangerousCreate(unchecked((nuint)(int)value), typeOid, destination);
}

/// <summary>
/// Names a PostgreSQL pseudotype to exercise concrete-type lookup rejection.
/// </summary>
[PgDatumType("cstring", typeof(PseudoDatumConverter), Schema = "pg_catalog", Origin = PgTypeOrigin.External)]
public readonly record struct PseudoMappedValue;

/// <summary>
/// Must never execute because the mapping's SQL identity is not a concrete defined type.
/// </summary>
public sealed class PseudoDatumConverter : IPgDatumWriter<PseudoMappedValue>
{
    /// <inheritdoc />
    public PgDatum Write(PseudoMappedValue value, uint typeOid, PgMemoryContext destination)
        => throw new InvalidOperationException("The pseudotype converter must not execute.");
}

/// <summary>
/// Produces a typed NULL for a domain whose native NOT NULL constraint must still execute.
/// </summary>
/// <param name="Mode">Zero selects SQL NULL; other values select forty-two.</param>
[PgDatumType("required", typeof(RequiredDatumConverter), Schema = "datum_mappings")]
public readonly record struct MappedRequired(int Mode);

/// <summary>
/// Leaves domain checking to PostgreSQL after returning a valid exactly typed handle.
/// </summary>
public sealed class RequiredDatumConverter : IPgDatumWriter<MappedRequired>
{
    /// <inheritdoc />
    public PgDatum Write(MappedRequired value, uint typeOid, PgMemoryContext destination)
        => PgDatum.DangerousCreate(42, typeOid, destination, isNull: value.Mode == 0);
}
