using Ankus;
using Ankus.TestExtension;

[assembly: PgSqlTypeProvider("mapped-domains", typeof(ResultPositive))]

namespace Ankus.TestExtension;

/// <summary>
/// Selects an exact integer reader independently of other wrappers of int4.
/// </summary>
/// <param name="Value">The copied integer.</param>
[PgDatumType("int4", typeof(ResultIntConverter), Schema = "pg_catalog", Origin = PgTypeOrigin.External)]
public readonly record struct ResultInt(int Value);

/// <summary>
/// Records present reads and deliberately retains an input to verify temporary-owner expiry.
/// </summary>
public sealed class ResultIntConverter : IPgDatumReader<ResultInt>
{
    /// <summary>
    /// Counts lazy construction independently of reads.
    /// </summary>
    public static int Constructions { get; private set; }

    /// <summary>
    /// Counts present reads.
    /// </summary>
    public static int Reads { get; private set; }

    /// <summary>
    /// Retains the last input solely for checked lifetime probes.
    /// </summary>
    public static PgDatum? Captured { get; private set; }

    /// <summary>
    /// Records entry into the lazy result factory.
    /// </summary>
    public ResultIntConverter() => Constructions++;

    /// <inheritdoc />
    public ResultInt Read(PgDatum value)
    {
        Reads++;
        Captured = value;
        return new ResultInt(value.Read<int>());
    }
}

/// <summary>
/// Detaches text from temporary result storage without providing a writer.
/// </summary>
/// <param name="Value">The complete copied text.</param>
[PgDatumType("text", typeof(ResultTextConverter), Schema = "pg_catalog", Origin = PgTypeOrigin.External)]
public sealed record ResultText(string Value);

/// <summary>
/// Copies text and exposes precise managed-reader failures after SQL execution.
/// </summary>
public sealed class ResultTextConverter : IPgDatumReader<ResultText>
{
    /// <summary>
    /// Counts lazy result factory calls.
    /// </summary>
    public static int Constructions { get; private set; }

    /// <summary>
    /// Counts present reads, including failing reads.
    /// </summary>
    public static int Reads { get; private set; }

    /// <summary>
    /// Retains the checked input solely for expiry assertions.
    /// </summary>
    public static PgDatum? Captured { get; private set; }

    /// <summary>
    /// Records lazy factory execution.
    /// </summary>
    public ResultTextConverter() => Constructions++;

    /// <inheritdoc />
    public ResultText Read(PgDatum value)
    {
        Reads++;
        Captured = value;
        string text = value.Read<string>();
        return text switch
        {
            "reader-error" => throw new PgException("P8511", "typed reader failed", detail: "detached result", hint: "choose another result"),
            "ordinary-error" => throw new FormatException("ordinary typed reader failed"),
            "null-result" => null!,
            _ => new ResultText(text),
        };
    }
}

/// <summary>
/// Selects the owned positive domain while counting NULL bypass independently of its base type.
/// </summary>
/// <param name="Value">The copied domain integer.</param>
[PgDatumType("positive", typeof(ResultPositiveConverter), Schema = "datum_mappings")]
public readonly record struct ResultPositive(int Value);

/// <summary>
/// Counts present domain reads without supplying a writer.
/// </summary>
public sealed class ResultPositiveConverter : IPgDatumReader<ResultPositive>
{
    /// <summary>
    /// Counts present result conversions.
    /// </summary>
    public static int Reads { get; private set; }

    /// <inheritdoc />
    public ResultPositive Read(PgDatum value)
    {
        Reads++;
        return new ResultPositive(value.Read<int>());
    }
}

/// <summary>
/// Exercises a failing lazy result factory without requiring write support.
/// </summary>
/// <param name="Value">The unreachable converted value.</param>
[PgDatumType("int4", typeof(ResultFactoryConverter), Schema = "pg_catalog", Origin = PgTypeOrigin.External)]
public readonly record struct ResultFactoryValue(int Value);

/// <summary>
/// Preserves a factory's PostgreSQL diagnostics and caches its failure.
/// </summary>
public sealed class ResultFactoryConverter : IPgDatumReader<ResultFactoryValue>
{
    /// <summary>
    /// Counts the failing factory invocation.
    /// </summary>
    public static int Constructions { get; private set; }

    /// <summary>
    /// Throws a deliberate diagnostic inside the native error boundary.
    /// </summary>
    public ResultFactoryConverter()
    {
        Constructions++;
        throw new PgException("P8512", "typed factory failed", detail: "lazy result", hint: "use another mapping");
    }

    /// <inheritdoc />
    public ResultFactoryValue Read(PgDatum value) => throw new InvalidOperationException("The failing factory cannot supply a reader.");
}

/// <summary>
/// Exercises an ordinary constructor exception independently of a PostgreSQL exception.
/// </summary>
/// <param name="Value">The unreachable converted value.</param>
[PgDatumType("int4", typeof(ResultOrdinaryFactoryConverter), Schema = "pg_catalog", Origin = PgTypeOrigin.External)]
public readonly record struct ResultOrdinaryFactoryValue(int Value);

/// <summary>
/// Fails with an ordinary managed exception during lazy construction.
/// </summary>
public sealed class ResultOrdinaryFactoryConverter : IPgDatumReader<ResultOrdinaryFactoryValue>
{
    /// <summary>
    /// Throws the fixture's ordinary constructor failure.
    /// </summary>
    public ResultOrdinaryFactoryConverter() => throw new FormatException("ordinary typed factory failed");

    /// <inheritdoc />
    public ResultOrdinaryFactoryValue Read(PgDatum value) => throw new InvalidOperationException("The failing factory cannot supply a reader.");
}

/// <summary>
/// Resolves a separate external domain for typed-result DDL lifecycle assertions.
/// </summary>
/// <param name="Value">The copied current integer.</param>
[PgDatumType("value", typeof(ResultLiveConverter), Schema = "mapped_result_live", Origin = PgTypeOrigin.External)]
public readonly record struct ResultLive(int Value);

/// <summary>
/// Keeps the external identity out of converter state.
/// </summary>
public sealed class ResultLiveConverter : IPgDatumReader<ResultLive>
{
    /// <inheritdoc />
    public ResultLive Read(PgDatum value) => new(value.Read<int>());
}
