using System.Globalization;
using Ankus;

[assembly: PgSql("provider-types", """
    CREATE TYPE type_providers.pair AS (number integer, label text);
    CREATE TYPE type_providers.other_pair AS (number integer, label text);
    CREATE DOMAIN type_providers.positive AS integer CHECK (VALUE > 0);
    CREATE DOMAIN type_providers.required AS integer NOT NULL;
    """)]
[assembly: PgSqlTypeProvider("provider-types", "pair", Schema = "type_providers")]
[assembly: PgSqlTypeProvider("provider-types", "other_pair", Schema = "type_providers")]
[assembly: PgSqlTypeProvider("provider-types", "positive", Schema = "type_providers")]
[assembly: PgSqlTypeProvider("provider-types", "required", Schema = "type_providers")]
[assembly: PgSql("provider-shell", "CREATE TYPE type_providers.u24;", Requires = ["provider-schema"])]
[assembly: PgSql("provider-complete", """
    CREATE TYPE type_providers.u24 (INPUT=type_providers.u24_in, OUTPUT=type_providers.u24_out, LIKE=int4);
    """, Requires = ["provider-in", "provider-out"])]
[assembly: PgSqlTypeProvider("provider-complete", "u24", Schema = "type_providers")]

namespace Ankus.TestExtension;

/// <summary>
/// Consumes SQL-provided catalog types without per-consumer installation prerequisites.
/// </summary>
[PgSchema("type_providers", Id = "provider-schema")]
public static class DeclaredTypeProviderFunctions
{
    private static int s_finally;

    /// <summary>
    /// Parses a manual four-byte by-value type after its shell and before its completed provider.
    /// </summary>
    /// <param name="text">The input cstring.</param>
    /// <param name="call">The exact return type identity.</param>
    /// <returns>The unsigned 24-bit value.</returns>
    [PgFunction(Name = "u24_in", Id = "provider-in", Requires = ["provider-shell"])]
    [return: PgSqlType("u24", Schema = "type_providers")]
    public static PgDatum Input([PgSqlType("cstring", Schema = "pg_catalog")] PgDatum text, PgFunctionContext call)
    {
        if (!uint.TryParse(text.ToPostgresString(), NumberStyles.None, CultureInfo.InvariantCulture, out uint value) || value > 0xFFFFFF)
        {
            throw new PgException("22003", "provider value exceeds 24 bits");
        }

        return PgDatum.DangerousCreate(value, call.ResultTypeOid, PgMemoryContext.Current);
    }

    /// <summary>
    /// Uses the built-in integer formatter without changing the declared manual input identity.
    /// </summary>
    /// <param name="value">The custom datum.</param>
    /// <returns>An owned cstring.</returns>
    [PgFunction(Name = "u24_out", Id = "provider-out", Requires = ["provider-shell"])]
    [return: PgSqlType("cstring", Schema = "pg_catalog")]
    public static PgDatum Output([PgSqlType("u24", Schema = "type_providers")] PgDatum value)
        => PgFunctions.CallRaw("pg_catalog.int4out", PgMemoryContext.Current,
            PgFunctionArgument.Create(checked((int)value.DangerousGetBits())));

    /// <summary>
    /// Retains zero, nonzero and SQL NULL under the completed type's inferred prerequisite.
    /// </summary>
    /// <param name="value">The custom value or SQL NULL.</param>
    /// <returns>The original exact value.</returns>
    [PgFunction(Name = "echo")]
    [return: PgSqlType("u24", Schema = "type_providers")]
    public static PgDatum? Echo([PgSqlType("u24", Schema = "type_providers")] PgDatum? value) => value;

    /// <summary>
    /// Retains a whole raw array using the leaf type's provider.
    /// </summary>
    /// <param name="value">The array with its original shape.</param>
    /// <returns>The unchanged array.</returns>
    [PgFunction(Name = "raw_array")]
    [return: PgSqlType("u24", Schema = "type_providers", IsArray = true)]
    public static PgDatum? RawArray([PgSqlType("u24", Schema = "type_providers", IsArray = true)] PgDatum? value) => value;

    /// <summary>
    /// Installs a cast only after the completed raw provider exists.
    /// </summary>
    /// <param name="value">The by-value datum.</param>
    /// <returns>The exact integer.</returns>
    [PgFunction(Name = "as_integer"), PgCast]
    public static int AsInteger([PgSqlType("u24", Schema = "type_providers")] PgDatum value)
        => checked((int)value.DangerousGetBits());

    /// <summary>
    /// Installs a binary operator through its backing function's provider prerequisites.
    /// </summary>
    /// <param name="left">The first exact type.</param>
    /// <param name="right">The second exact type.</param>
    /// <returns>Whether the datum words are equal.</returns>
    [PgFunction(Name = "equal"), PgOperator("@=")]
    public static bool Equal([PgSqlType("u24", Schema = "type_providers")] PgDatum left,
        [PgSqlType("u24", Schema = "type_providers")] PgDatum right) => left.DangerousGetBits() == right.DangerousGetBits();

    /// <summary>
    /// Exchanges a named composite through a closed SPI owner.
    /// </summary>
    /// <param name="value">The composite or whole-value NULL.</param>
    /// <returns>The copied composite.</returns>
    [PgFunction(Name = "pair_echo")]
    [return: PgCompositeType("pair", Schema = "type_providers")]
    public static PgHeapTuple? PairEcho([PgCompositeType("pair", Schema = "type_providers")] PgHeapTuple? value)
        => ArrayFunctions.Exchange(value, 1);

    /// <summary>
    /// Retains composite array identity independently of dimensions and NULL elements.
    /// </summary>
    /// <param name="value">The named composite array.</param>
    /// <returns>The copied shape and cells.</returns>
    [PgFunction(Name = "pair_array")]
    [return: PgCompositeType("pair", Schema = "type_providers")]
    public static PgArray<PgHeapTuple?>? PairArray([PgCompositeType("pair", Schema = "type_providers")] PgArray<PgHeapTuple?>? value)
        => ArrayFunctions.Exchange(value, 1);

    /// <summary>
    /// Retains a domain's exact identity rather than silently using its integer base type.
    /// </summary>
    /// <param name="value">The checked value.</param>
    /// <returns>The domain value.</returns>
    [PgFunction(Name = "positive_echo")]
    [return: PgSqlType("positive", Schema = "type_providers")]
    public static PgDatum? Positive([PgSqlType("positive", Schema = "type_providers")] PgDatum? value) => value;

    /// <summary>
    /// Returns SQL NULL so the declared result domain's NOT NULL check must execute.
    /// </summary>
    /// <returns>A SQL NULL wrapper.</returns>
    [PgFunction(Name = "required_null")]
    [return: PgSqlType("required", Schema = "type_providers")]
    public static PgDatum? RequiredNull() => null;

    /// <summary>
    /// Streams retained raw composites and completes cleanup on success, error and early executor stop.
    /// </summary>
    /// <param name="value">The copied composite.</param>
    /// <param name="fail">Whether the second advance fails.</param>
    /// <returns>Two copies and a whole-row NULL.</returns>
    [PgFunction(Name = "pair_rows")]
    [return: PgSqlType("pair", Schema = "type_providers")]
    public static IEnumerable<PgDatum?> PairRows([PgSqlType("pair", Schema = "type_providers")] PgDatum value, bool fail)
    {
        try
        {
            yield return value;
            if (fail)
            {
                throw new PgException("P8401", "provider iterator failed", detail: "second advance", hint: "retry without failure");
            }

            yield return value;
            yield return null;
        }
        finally
        {
            s_finally++;
        }
    }

    /// <summary>
    /// Materializes the same raw result contract under a separately generated wrapper.
    /// </summary>
    /// <param name="value">The copied composite.</param>
    /// <param name="fail">Whether materialization fails.</param>
    /// <returns>The materialized rows.</returns>
    [PgFunction(Name = "pair_materialized", SetMode = PgSetMode.Materialize)]
    [return: PgSqlType("pair", Schema = "type_providers")]
    public static IEnumerable<PgDatum?> PairMaterialized([PgSqlType("pair", Schema = "type_providers")] PgDatum value, bool fail)
        => PairRows(value, fail);

    /// <summary>
    /// Resolves each TABLE column's distinct provider without explicit block dependencies.
    /// </summary>
    /// <param name="sourceNumber">The raw by-value leaf.</param>
    /// <param name="sourcePair">The named composite leaf.</param>
    /// <returns>A present row and a row of SQL NULL columns.</returns>
    [PgFunction(Name = "paired_table")]
    [return: PgSqlType("u24", Schema = "type_providers", Column = "number")]
    [return: PgCompositeType("pair", Schema = "type_providers", Column = "pair")]
    public static IEnumerable<(PgDatum? Number, PgHeapTuple? Pair)> PairedTable(
        [PgSqlType("u24", Schema = "type_providers")] PgDatum sourceNumber,
        [PgCompositeType("pair", Schema = "type_providers")] PgHeapTuple sourcePair)
    {
        yield return (sourceNumber, sourcePair);
        yield return (null, null);
    }

    /// <summary>
    /// Reports iterator cleanup completed before returning to PostgreSQL.
    /// </summary>
    /// <returns>The completed iterator count.</returns>
    [PgFunction(Name = "cleanup_count")]
    public static int CleanupCount() => s_finally;

    /// <summary>
    /// Copies a large composite out of SPI and temporary storage before deleting its original relation.
    /// </summary>
    /// <returns>The independently owned composite.</returns>
    [PgFunction(Name = "pair_from_store")]
    [return: PgSqlType("pair", Schema = "type_providers")]
    public static PgDatum PairFromStore()
    {
        PgDatum copy;
        using (PgMemoryContext temporary = PgMemoryContext.Create("provider copied pair"))
        {
            using SpiRawResult result = Spi.QueryRaw("SELECT ROW(number,label)::type_providers.pair FROM provider_toast");
            copy = result[0][0].CopyTo(temporary).CopyTo(PgMemoryContext.Current);
        }

        Spi.Execute("DROP TABLE provider_toast");
        return copy;
    }

    /// <summary>
    /// Supplies incompatible OIDs and expired owners without unsafe pointer reads.
    /// </summary>
    /// <param name="mode">Wrong OID, wrong typed NULL, stale present or stale NULL.</param>
    /// <param name="call">The correct declared result OID.</param>
    /// <returns>The deliberately invalid result.</returns>
    [PgFunction(Name = "invalid_result")]
    [return: PgSqlType("u24", Schema = "type_providers")]
    public static PgDatum InvalidResult(int mode, PgFunctionContext call)
    {
        if (mode < 2)
        {
            return PgDatum.DangerousCreate(42, 23, PgMemoryContext.Current, isNull: mode == 1);
        }

        using PgMemoryContext owner = PgMemoryContext.Create("expired provider result");
        return PgDatum.DangerousCreate(42, call.ResultTypeOid, owner, isNull: mode == 3);
    }

    /// <summary>
    /// Returns a distinct equal-shaped composite under an incompatible declared result contract.
    /// </summary>
    /// <param name="value">The distinct nominal input.</param>
    /// <returns>The deliberately wrong composite OID.</returns>
    [PgFunction(Name = "wrong_pair")]
    [return: PgSqlType("pair", Schema = "type_providers")]
    public static PgDatum WrongPair([PgSqlType("other_pair", Schema = "type_providers")] PgDatum value) => value;
}

/// <summary>
/// Retains a SQL-provided composite under the aggregate owner's lifetime.
/// </summary>
[PgAggregate(Name = "first_pair", Schema = "type_providers")]
public static class DeclaredPairAggregate
{
    /// <summary>
    /// Copies the first present input beyond later transition callbacks.
    /// </summary>
    /// <param name="context">The aggregate destination owner.</param>
    /// <param name="state">The retained first composite.</param>
    /// <param name="value">The current input.</param>
    /// <returns>The first present composite or SQL NULL.</returns>
    [return: PgSqlType("pair", Schema = "type_providers")]
    public static PgDatum? Transition(PgAggregateContext context, [PgSqlType("pair", Schema = "type_providers")] PgDatum? state,
        [PgSqlType("pair", Schema = "type_providers")] PgDatum? value) => state ?? value?.CopyTo(context.MemoryContext);
}
