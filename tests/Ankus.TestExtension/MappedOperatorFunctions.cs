using Ankus;
using Ankus.TestExtension;

[assembly: PgSql("mapped-operator-type", """
    CREATE TYPE mapped_ops.key;
    CREATE FUNCTION mapped_ops.key_in(cstring) RETURNS mapped_ops.key LANGUAGE internal IMMUTABLE STRICT AS 'int4in';
    CREATE FUNCTION mapped_ops.key_out(mapped_ops.key) RETURNS cstring LANGUAGE internal IMMUTABLE STRICT AS 'int4out';
    CREATE TYPE mapped_ops.key(INPUT=mapped_ops.key_in,OUTPUT=mapped_ops.key_out,LIKE=int4);
    """, Requires = ["mapped-operator-schema"])]
[assembly: PgSqlTypeProvider("mapped-operator-type", typeof(MappedOperatorKey))]
[assembly: PgSqlTypeProvider("mapped-operator-type", typeof(MappedOperatorStorage))]
[assembly: PgSqlTypeProvider("mapped-operator-type", typeof(MappedOperatorAlias))]
[assembly: PgSqlTypeProvider("mapped-complex", typeof(MappedOperatorPair))]

namespace Ankus.TestExtension;

/// <summary>
/// Decodes a logical tens key while retaining the original word independently.
/// </summary>
/// <param name="Word">The original native word.</param>
[PgDatumType("key", typeof(MappedOperatorReader), Schema = "mapped_ops")]
[PgEquality]
[PgOrdering]
[PgHashing]
public readonly record struct MappedOperatorKey(int Word) : IComparable<MappedOperatorKey>, IPgHashable
{
    /// <summary>
    /// Gets the equality key independently of the ones digit.
    /// </summary>
    public int Key => Word / 10;

    /// <summary>
    /// Compares logical keys without comparing metadata.
    /// </summary>
    public bool Equals(MappedOperatorKey other)
    {
        if (Key == 903 || other.Key == 903)
        {
            throw new PgException("P8903", "mapped equality failed", detail: "logical key", hint: "use another key");
        }

        return Key == other.Key;
    }

    /// <inheritdoc />
    public int CompareTo(MappedOperatorKey other)
    {
        if (Key == 904 || other.Key == 904)
        {
            throw new PgException("P8904", "mapped comparison failed", detail: "logical key", hint: "use another key");
        }

        return Key > other.Key ? int.MinValue : Key < other.Key ? int.MaxValue : 0;
    }

    /// <inheritdoc />
    public int GetPostgresHashCode()
    {
        if (Key == 905)
        {
            throw new PgException("P8905", "mapped hash failed", detail: "logical key", hint: "use another key");
        }

        return 12345 + (Key & 1) * 37;
    }

    /// <inheritdoc />
    public override int GetHashCode() => GetPostgresHashCode();

    /// <summary>
    /// Compares the descending logical order.
    /// </summary>
    public static bool operator <(MappedOperatorKey left, MappedOperatorKey right) => left.CompareTo(right) < 0;
    /// <summary>
    /// Compares the descending logical order.
    /// </summary>
    public static bool operator <=(MappedOperatorKey left, MappedOperatorKey right) => left.CompareTo(right) <= 0;
    /// <summary>
    /// Compares the descending logical order.
    /// </summary>
    public static bool operator >(MappedOperatorKey left, MappedOperatorKey right) => left.CompareTo(right) > 0;
    /// <summary>
    /// Compares the descending logical order.
    /// </summary>
    public static bool operator >=(MappedOperatorKey left, MappedOperatorKey right) => left.CompareTo(right) >= 0;
}

/// <summary>
/// Supplies only the read direction required by generated operators.
/// </summary>
public sealed class MappedOperatorReader : IPgDatumReader<MappedOperatorKey>
{
    /// <summary>
    /// Gets the number of lazy constructor attempts.
    /// </summary>
    public static int Constructions { get; private set; }
    /// <summary>
    /// Gets the number of present operands decoded.
    /// </summary>
    public static int Reads { get; private set; }
    /// <summary>
    /// Gets or sets whether the first constructor fails in this backend.
    /// </summary>
    public static bool FailFactory { get; set; }
    /// <summary>
    /// Gets the last copied callback operand.
    /// </summary>
    public static PgDatum? Captured { get; private set; }
    /// <summary>
    /// Gets the exact nominal operand identity.
    /// </summary>
    public static uint TypeOid { get; private set; }

    /// <summary>
    /// Records lazy construction and optionally raises an ordinary managed error.
    /// </summary>
    public MappedOperatorReader()
    {
        Constructions++;
        if (FailFactory)
        {
            throw new InvalidOperationException("mapped operator factory failed");
        }
    }

    /// <inheritdoc />
    public MappedOperatorKey Read(PgDatum value)
    {
        Reads++;
        Captured = value;
        TypeOid = value.TypeOid;
        int word = unchecked((int)value.DangerousGetBits());
        if (word == 9011)
        {
            throw new PgException("P8901", "mapped operator reader failed", detail: "native word", hint: "use another word");
        }

        if (word == 9021)
        {
            throw new InvalidOperationException("ordinary mapped operator reader failed");
        }

        return new(word);
    }
}

/// <summary>
/// Supplies a separate writer for the same SQL type without operator annotations.
/// </summary>
/// <param name="Word">The exact stored word.</param>
[PgDatumType("key", typeof(MappedOperatorStorageConverter), Schema = "mapped_ops")]
public readonly record struct MappedOperatorStorage(int Word);

/// <summary>
/// Distinguishes ordinary storage writes from read-only operator conversion.
/// </summary>
public sealed class MappedOperatorStorageConverter : IPgDatumReader<MappedOperatorStorage>, IPgDatumWriter<MappedOperatorStorage>
{
    /// <summary>
    /// Gets the present write count.
    /// </summary>
    public static int Writes { get; private set; }
    /// <inheritdoc />
    public MappedOperatorStorage Read(PgDatum value) => new(unchecked((int)value.DangerousGetBits()));
    /// <inheritdoc />
    public PgDatum Write(MappedOperatorStorage value, uint typeOid, PgMemoryContext destination)
    {
        Writes++;
        return PgDatum.DangerousCreate(unchecked((nuint)(nint)value.Word), typeOid, destination);
    }
}

/// <summary>
/// Deliberately interprets the same SQL word through a different declared reader.
/// </summary>
/// <param name="Number">The word plus one thousand.</param>
[PgDatumType("key", typeof(MappedOperatorAliasReader), Schema = "mapped_ops")]
public readonly record struct MappedOperatorAlias(int Number);

/// <summary>
/// Avoids canonical first-registration selection for an alias.
/// </summary>
public sealed class MappedOperatorAliasReader : IPgDatumReader<MappedOperatorAlias>
{
    /// <inheritdoc />
    public MappedOperatorAlias Read(PgDatum value) => new(unchecked((int)value.DangerousGetBits()) + 1000);
}

/// <summary>
/// Compares an existing manually stored by-reference pair through a copied real component.
/// </summary>
/// <param name="Real">The equality key.</param>
/// <param name="Imaginary">The preserved non-key component.</param>
[PgDatumType("complex", typeof(MappedOperatorPairReader), Schema = "datum_mappings")]
[PgEquality]
public readonly record struct MappedOperatorPair(double Real, double Imaginary)
{
    /// <summary>
    /// Compares only the first component.
    /// </summary>
    public bool Equals(MappedOperatorPair other) => Real.Equals(other.Real);
    /// <inheritdoc />
    public override int GetHashCode() => Real.GetHashCode();
}

/// <summary>
/// Copies both native doubles and exposes the callback lifetime independently.
/// </summary>
public sealed class MappedOperatorPairReader : IPgDatumReader<MappedOperatorPair>
{
    /// <summary>
    /// Gets the last native callback handle.
    /// </summary>
    public static PgDatum? Captured { get; private set; }
    /// <summary>
    /// Gets the last detached managed pair.
    /// </summary>
    public static MappedOperatorPair Last { get; private set; }
    /// <inheritdoc />
    public unsafe MappedOperatorPair Read(PgDatum value)
    {
        Captured = value;
        double* pair = (double*)value.DangerousGetBits();
        Last = new(pair[0], pair[1]);
        return Last;
    }
}

/// <summary>
/// Observes selected conversion, lazy failures and checked callback ownership.
/// </summary>
[PgSchema("mapped_ops", Id = "mapped-operator-schema")]
public static class MappedOperatorFunctions
{
    /// <summary>
    /// Writes independently selected native words through a different mapped view.
    /// </summary>
    [PgFunction(Name = "make")]
    public static MappedOperatorStorage Make(int word) => new(word);
    /// <summary>
    /// Reads the original word without invoking the operator reader.
    /// </summary>
    [PgFunction(Name = "word")]
    public static int Word(MappedOperatorStorage value) => value.Word;
    /// <summary>
    /// Reads the independently distinguishable alias.
    /// </summary>
    [PgFunction(Name = "alias")]
    public static int Alias(MappedOperatorAlias value) => value.Number;
    /// <summary>
    /// Reports constructors, reads and storage writes without further conversion.
    /// </summary>
    [PgFunction(Name = "counts")]
    public static string Counts() => $"{MappedOperatorReader.Constructions}|{MappedOperatorReader.Reads}|{MappedOperatorStorageConverter.Writes}";
    /// <summary>
    /// Enables a failure before the lazy reader has ever been requested.
    /// </summary>
    [PgFunction(Name = "fail_factory")]
    public static void FailFactory() => MappedOperatorReader.FailFactory = true;
    /// <summary>
    /// Returns the independently captured nominal operand OID.
    /// </summary>
    [PgFunction(Name = "captured_oid")]
    public static uint CapturedOid() => MappedOperatorReader.TypeOid;
    /// <summary>
    /// Proves copied callback handles expired without consuming detached managed state.
    /// </summary>
    [PgFunction(Name = "expired")]
    public static bool Expired(bool pair)
    {
        PgDatum value = (pair ? MappedOperatorPairReader.Captured : MappedOperatorReader.Captured)
            ?? throw new InvalidOperationException("No operand was captured.");
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

    /// <summary>
    /// Returns the copied by-reference value after the original callback owner expired.
    /// </summary>
    [PgFunction(Name = "detached_pair")]
    public static double[] DetachedPair() => [MappedOperatorPairReader.Last.Real, MappedOperatorPairReader.Last.Imaginary];
}
