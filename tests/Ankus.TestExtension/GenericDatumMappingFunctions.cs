namespace Ankus.TestExtension;

/// <summary>
/// Represents one exact closed managed view over externally stored int4 words.
/// </summary>
/// <param name="Number">The detached logical number.</param>
[PgDatumType("int4", typeof(GenericBoxConverter), Origin = PgTypeOrigin.External, Schema = "pg_catalog")]
public readonly record struct GenericBox<T>(int Number);

/// <summary>
/// Selects independent readers and writers for two closed constructions of one SQL type.
/// </summary>
public sealed class GenericBoxConverter : IPgDatumReader<GenericBox<int>>, IPgDatumWriter<GenericBox<int>>,
    IPgDatumReader<GenericBox<long>>, IPgDatumWriter<GenericBox<long>>
{
    /// <summary>
    /// Gets the number of lazy converter constructions in this backend.
    /// </summary>
    public static int Constructions { get; private set; }

    /// <summary>
    /// Gets the number of int-tagged reads.
    /// </summary>
    public static int IntReads { get; private set; }

    /// <summary>
    /// Gets the number of long-tagged reads.
    /// </summary>
    public static int LongReads { get; private set; }

    /// <summary>
    /// Gets the last checked callback operand.
    /// </summary>
    public static PgDatum? Captured { get; private set; }

    /// <summary>
    /// Records construction separately for each statically registered closed identity.
    /// </summary>
    public GenericBoxConverter() => Constructions++;

    /// <inheritdoc />
    GenericBox<int> IPgDatumReader<GenericBox<int>>.Read(PgDatum value)
    {
        IntReads++;
        Captured = value;
        int word = unchecked((int)value.DangerousGetBits());
        if (word == 9011)
        {
            throw new PgException("P8601", "generic mapped reader failed", detail: "int-tagged word", hint: "use another word");
        }

        return new(word + 1000);
    }

    /// <inheritdoc />
    GenericBox<long> IPgDatumReader<GenericBox<long>>.Read(PgDatum value)
    {
        LongReads++;
        Captured = value;
        return new(unchecked((int)value.DangerousGetBits()) + 2000);
    }

    /// <inheritdoc />
    PgDatum IPgDatumWriter<GenericBox<int>>.Write(GenericBox<int> value, uint typeOid, PgMemoryContext destination)
    {
        if (value.Number == 9002)
        {
            throw new PgException("P8602", "generic mapped writer failed", detail: "int-tagged result", hint: "use another result");
        }

        return PgDatum.DangerousCreate(unchecked((nuint)(nint)(value.Number - 1000)), typeOid, destination);
    }

    /// <inheritdoc />
    PgDatum IPgDatumWriter<GenericBox<long>>.Write(GenericBox<long> value, uint typeOid, PgMemoryContext destination)
        => PgDatum.DangerousCreate(unchecked((nuint)(nint)(value.Number - 2000)), typeOid, destination);
}

/// <summary>
/// Carries a concrete containing type argument into a nested mapped identity.
/// </summary>
public static class GenericOuter<T>
{
    /// <summary>
    /// Represents a nested closed datum view.
    /// </summary>
    /// <param name="Number">The detached logical number.</param>
    [PgDatumType("int4", typeof(GenericNestedConverter), Origin = PgTypeOrigin.External, Schema = "pg_catalog")]
    public readonly record struct Value(int Number);
}

/// <summary>
/// Reads one finite constructed nested identity without choosing the open definition.
/// </summary>
public sealed class GenericNestedConverter : IPgDatumReader<GenericOuter<int>.Value>
{
    /// <inheritdoc />
    public GenericOuter<int>.Value Read(PgDatum value) => new(unchecked((int)value.DangerousGetBits()) + 3000);
}

/// <summary>
/// Exercises closed generic scalar, NULL, array, error and lifetime paths in Native AOT.
/// </summary>
[PgSchema("generic_mappings")]
public static class GenericDatumMappingFunctions
{
    /// <summary>
    /// Reads the int-tagged managed value.
    /// </summary>
    [PgFunction(Name = "read_int")]
    public static int ReadInt(GenericBox<int> value) => value.Number;

    /// <summary>
    /// Reads the long-tagged managed value from the same SQL storage.
    /// </summary>
    [PgFunction(Name = "read_long")]
    public static int ReadLong(GenericBox<long> value) => value.Number;

    /// <summary>
    /// Writes an int-tagged managed value.
    /// </summary>
    [PgFunction(Name = "make_int")]
    public static GenericBox<int> MakeInt(int number) => new(number);

    /// <summary>
    /// Writes a long-tagged managed value.
    /// </summary>
    [PgFunction(Name = "make_long")]
    public static GenericBox<long> MakeLong(int number) => new(number);

    /// <summary>
    /// Distinguishes SQL NULL from a present zero-valued word.
    /// </summary>
    [PgFunction(Name = "optional")]
    public static int Optional(GenericBox<int>? value) => value?.Number ?? -1;

    /// <summary>
    /// Preserves the original lower bounds, shape and NULL cells of a mapped generic array.
    /// </summary>
    [PgFunction(Name = "echo_array")]
    public static PgArray<GenericBox<int>?>? EchoArray(PgArray<GenericBox<int>?>? value) => value;

    /// <summary>
    /// Reads a constructed containing type without registering its open definition.
    /// </summary>
    [PgFunction(Name = "read_nested")]
    public static int ReadNested(GenericOuter<int>.Value value) => value.Number;

    /// <summary>
    /// Selects three exact closed readers over one raw datum and both typed SPI scalar results.
    /// </summary>
    [PgFunction(Name = "raw_and_spi")]
    public static string RawAndSpi()
    {
        GenericBox<int> first;
        GenericBox<long> second;
        GenericOuter<int>.Value nested;
        using (SpiRawResult result = Spi.QueryRaw("SELECT 17::integer"))
        {
            PgDatum datum = result[0][0];
            first = datum.Read<GenericBox<int>>();
            second = datum.Read<GenericBox<long>>();
            nested = datum.Read<GenericOuter<int>.Value>();
        }

        GenericBox<int> intResult = Spi.ExecuteScalar<GenericBox<int>>("SELECT 19::integer");
        GenericBox<long> longResult = Spi.ExecuteScalar<GenericBox<long>>("SELECT 23::integer");
        return $"{first.Number}|{second.Number}|{nested.Number}|{intResult.Number}|{longResult.Number}";
    }

    /// <summary>
    /// Reports the independently lazy converter and requested reader counts.
    /// </summary>
    [PgFunction(Name = "counts")]
    public static string Counts() => $"{GenericBoxConverter.Constructions}|{GenericBoxConverter.IntReads}|{GenericBoxConverter.LongReads}";

    /// <summary>
    /// Checks that captured native callback storage expired after managed conversion.
    /// </summary>
    [PgFunction(Name = "expired")]
    public static bool Expired()
    {
        PgDatum value = GenericBoxConverter.Captured ?? throw new InvalidOperationException("No generic operand was captured.");
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
