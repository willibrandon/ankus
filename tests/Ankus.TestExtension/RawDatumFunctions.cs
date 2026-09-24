using System.Globalization;
using Ankus;

[assembly: PgSql("raw-types", """
    CREATE TYPE raw_values.u24;
    CREATE DOMAIN raw_values.positive AS integer CHECK (VALUE > 0);
    CREATE DOMAIN raw_values.required AS integer NOT NULL;
    CREATE TYPE raw_values.pair AS (number integer, label text);
    """, Requires = ["raw-schema"])]
[assembly: PgSql("raw-u24", """
    CREATE TYPE raw_values.u24 (INPUT=raw_values.u24_in, OUTPUT=raw_values.u24_out, LIKE=int4);
    """, Requires = ["raw-in", "raw-out"])]

namespace Ankus.TestExtension;

/// <summary>
/// Exercises manually represented base types and explicitly bound raw function signatures.
/// </summary>
[PgSchema("raw_values", Id = "raw-schema")]
public static class RawDatumFunctions
{
    /// <summary>
    /// Parses the pgrx hand-rolled unsigned 24-bit example through PostgreSQL's real input ABI.
    /// </summary>
    /// <param name="input">The native cstring input.</param>
    /// <param name="call">The declared SQL signature, excluding extra native input slots.</param>
    /// <returns>A by-value datum with the custom type's exact identity.</returns>
    [PgFunction(Name = "u24_in", Id = "raw-in", Requires = ["raw-types"])]
    [return: PgSqlType("u24", Schema = "raw_values")]
    public static PgDatum U24In([PgSqlType("cstring", Schema = "pg_catalog")] PgDatum input, PgFunctionContext call)
    {
        if (call.Arguments.Count != 1 || call.Arguments[0].TypeOid != 2275)
        {
            throw new InvalidOperationException("Type input metadata included undeclared native slots.");
        }

        if (!uint.TryParse(input.ToPostgresString(), NumberStyles.None, CultureInfo.InvariantCulture, out uint value) || value > 0x00FFFFFF)
        {
            throw new PgException("22003", "value exceeds 24 bits");
        }

        return PgDatum.DangerousCreate(value, call.ResultTypeOid, PgMemoryContext.Current);
    }

    /// <summary>
    /// Returns a context-owned cstring from a custom by-value input.
    /// </summary>
    /// <param name="value">The exact custom type.</param>
    /// <returns>The decimal output in the server encoding.</returns>
    [PgFunction(Name = "u24_out", Id = "raw-out", Requires = ["raw-types"])]
    [return: PgSqlType("cstring", Schema = "pg_catalog")]
    public static PgDatum U24Out([PgSqlType("u24", Schema = "raw_values")] PgDatum value)
        => PgFunctions.CallRaw("pg_catalog.int4out", PgMemoryContext.Current,
            PgFunctionArgument.Create(checked((int)value.DangerousGetBits())));

    /// <summary>
    /// Returns an unmapped built-in type without changing its exact bits.
    /// </summary>
    /// <param name="value">A PostgreSQL WAL location or SQL NULL.</param>
    /// <returns>The unchanged location.</returns>
    [PgFunction]
    [return: PgSqlType("pg_lsn", Schema = "pg_catalog")]
    public static PgDatum? RawLsn([PgSqlType("pg_lsn", Schema = "pg_catalog")] PgDatum? value) => value;

    /// <summary>
    /// Resolves an explicit polymorphic raw binding at the SQL call site.
    /// </summary>
    /// <param name="value">The caller-selected PostgreSQL type.</param>
    /// <returns>The exact value and type.</returns>
    [PgFunction]
    [return: PgSqlType("anyelement", Schema = "pg_catalog")]
    public static PgDatum? RawPoly([PgSqlType("anyelement", Schema = "pg_catalog")] PgDatum? value) => value;

    /// <summary>
    /// Captures a single unconstrained SQL argument with its actual concrete type.
    /// </summary>
    /// <param name="value">The arbitrary input.</param>
    /// <returns>Its server-formatted text or NULL marker.</returns>
    [PgFunction]
    public static string RawAny([PgSqlType("any", Schema = "pg_catalog")] PgDatum? value)
        => value?.ToPostgresString() ?? "NULL";

    /// <summary>
    /// Reads a raw internal word through the checked native-pointer API.
    /// </summary>
    /// <param name="value">The borrowed opaque word or SQL NULL.</param>
    /// <returns>The updated pointee, zero pointer marker, or NULL marker.</returns>
    [PgFunction]
    public static long RawInternal([PgSqlType("internal", Schema = "pg_catalog")] PgDatum? value)
        => InternalFunctions.InternalNativeRead(value is null ? null : new PgInternal(value));

    /// <summary>
    /// Retains an entire array's type, shape, lower bounds, and NULL cells.
    /// </summary>
    /// <param name="value">The raw array.</param>
    /// <returns>The same array value.</returns>
    [PgFunction]
    [return: PgSqlType("pg_lsn", Schema = "pg_catalog", IsArray = true)]
    public static PgDatum? RawArray([PgSqlType("pg_lsn", Schema = "pg_catalog", IsArray = true)] PgDatum? value) => value;

    /// <summary>
    /// Copies a detoasted raw value out of a temporary owner before deleting that owner.
    /// </summary>
    /// <param name="value">The raw text value.</param>
    /// <returns>An independently owned text datum.</returns>
    [PgFunction]
    [return: PgSqlType("text", Schema = "pg_catalog")]
    public static PgDatum RawText([PgSqlType("text", Schema = "pg_catalog")] PgDatum value)
    {
        using PgMemoryContext temporary = PgMemoryContext.Create("raw text copy");
        return value.CopyTo(temporary).CopyTo(PgMemoryContext.Current);
    }

    /// <summary>
    /// Returns a raw named composite or whole-value SQL NULL.
    /// </summary>
    /// <param name="value">The composite input.</param>
    /// <returns>The exact composite value.</returns>
    [PgFunction(Requires = ["raw-types"])]
    [return: PgSqlType("pair", Schema = "raw_values")]
    public static PgDatum? RawPair([PgSqlType("pair", Schema = "raw_values")] PgDatum? value) => value;

    /// <summary>
    /// Preserves domain identity independently of its underlying integer storage.
    /// </summary>
    /// <param name="value">The checked domain input.</param>
    /// <returns>The domain value.</returns>
    [PgFunction(Requires = ["raw-types"])]
    [return: PgSqlType("positive", Schema = "raw_values")]
    public static PgDatum? RawDomain([PgSqlType("positive", Schema = "raw_values")] PgDatum? value) => value;

    /// <summary>
    /// Attempts a NULL domain result so PostgreSQL must apply its NOT NULL constraint.
    /// </summary>
    /// <returns>A SQL NULL wrapper.</returns>
    [PgFunction(Requires = ["raw-types"])]
    [return: PgSqlType("required", Schema = "raw_values")]
    public static PgDatum? RawRequired() => null;

    /// <summary>
    /// Supplies valid, incompatible, typed-NULL, and stale raw scalar results.
    /// </summary>
    /// <param name="mode">The requested result invariant.</param>
    /// <returns>The intentionally selected result.</returns>
    [PgFunction]
    [return: PgSqlType("int4", Schema = "pg_catalog")]
    public static PgDatum? RawResult(int mode)
    {
        if (mode == 4)
        {
            return null;
        }

        uint type = mode is 1 or 3 ? 20U : 23U;
        bool absent = mode is 2 or 3 or 6;
        if (mode is 5 or 6)
        {
            using PgMemoryContext owner = PgMemoryContext.Create("expired raw result");
            return PgDatum.DangerousCreate(42, type, owner, absent);
        }

        return PgDatum.DangerousCreate(mode == 7 ? 0U : 42U, type, PgMemoryContext.Current, absent);
    }

    /// <summary>
    /// Streams retained raw text and a NULL row across iterator advances.
    /// </summary>
    /// <param name="value">The copied input.</param>
    /// <returns>Two present rows followed by SQL NULL.</returns>
    [PgFunction]
    [return: PgSqlType("text", Schema = "pg_catalog")]
    public static IEnumerable<PgDatum?> RawRows([PgSqlType("text", Schema = "pg_catalog")] PgDatum value)
    {
        yield return value;
        yield return value;
        yield return null;
    }

    /// <summary>
    /// Materializes raw text using the same exact output contract.
    /// </summary>
    /// <param name="value">The copied input.</param>
    /// <returns>The retained rows.</returns>
    [PgFunction(SetMode = PgSetMode.Materialize)]
    [return: PgSqlType("text", Schema = "pg_catalog")]
    public static IEnumerable<PgDatum?> RawMaterialized([PgSqlType("text", Schema = "pg_catalog")] PgDatum value) => RawRows(value);

    /// <summary>
    /// Exercises raw composite SETOF expansion and whole-row NULL handling.
    /// </summary>
    /// <param name="value">The retained named composite.</param>
    /// <returns>A present composite followed by SQL NULL.</returns>
    [PgFunction(Requires = ["raw-types"])]
    [return: PgSqlType("pair", Schema = "raw_values")]
    public static IEnumerable<PgDatum?> RawPairs([PgSqlType("pair", Schema = "raw_values")] PgDatum value)
    {
        yield return value;
        yield return null;
    }

    /// <summary>
    /// Materializes a raw named composite using PostgreSQL's tuple descriptor.
    /// </summary>
    /// <param name="value">The retained input composite.</param>
    /// <returns>The composite rows.</returns>
    [PgFunction(Requires = ["raw-types"], SetMode = PgSetMode.Materialize)]
    [return: PgSqlType("pair", Schema = "raw_values")]
    public static IEnumerable<PgDatum?> RawPairsMaterialized([PgSqlType("pair", Schema = "raw_values")] PgDatum value) => RawPairs(value);

    /// <summary>
    /// Exercises result validation after a successful iterator advance.
    /// </summary>
    /// <param name="mode">The invalid second result shape.</param>
    /// <returns>A valid result followed by the selected invalid result.</returns>
    [PgFunction]
    [return: PgSqlType("int4", Schema = "pg_catalog")]
    public static IEnumerable<PgDatum?> RawErrorRows(int mode)
    {
        yield return RawResult(0);
        yield return RawResult(mode);
    }

    /// <summary>
    /// Returns independently bound TABLE columns with distinct raw types.
    /// </summary>
    /// <param name="location">The typed WAL location.</param>
    /// <param name="label">The typed text.</param>
    /// <returns>The columns and their NULL row.</returns>
    [PgFunction]
    [return: PgSqlType("pg_lsn", Schema = "pg_catalog", Column = "position")]
    [return: PgSqlType("text", Schema = "pg_catalog", Column = "description")]
    public static IEnumerable<(PgDatum? Position, PgDatum? Description)> RawTable(
        [PgSqlType("pg_lsn", Schema = "pg_catalog")] PgDatum location,
        [PgSqlType("text", Schema = "pg_catalog")] PgDatum label)
    {
        yield return (location, label);
        yield return (null, null);
    }

    /// <summary>
    /// Uses a manually declared base type in an ordinary generated SQL cast.
    /// </summary>
    /// <param name="value">The custom type's by-value bits.</param>
    /// <returns>The exact unsigned 24-bit integer.</returns>
    [PgFunction(Requires = ["raw-u24"]), PgCast]
    public static int U24Int([PgSqlType("u24", Schema = "raw_values")] PgDatum value)
        => checked((int)value.DangerousGetBits());
}

/// <summary>
/// Retains raw text under ordinary aggregate state owners, including parallel combination.
/// </summary>
[PgAggregate(Name = "raw_first", Schema = "raw_values", ParallelSafety = PgParallelSafety.Safe)]
public static class RawFirstAggregate
{
    /// <summary>
    /// Copies the first present input into the aggregate's long-lived owner.
    /// </summary>
    /// <param name="state">The retained state.</param>
    /// <param name="value">The current input.</param>
    /// <param name="context">The destination aggregate owner.</param>
    /// <returns>The first retained value.</returns>
    [return: PgSqlType("text", Schema = "pg_catalog")]
    public static PgDatum? Transition(PgAggregateContext context, [PgSqlType("text", Schema = "pg_catalog")] PgDatum? state,
        [PgSqlType("text", Schema = "pg_catalog")] PgDatum? value)
        => state ?? value?.CopyTo(context.MemoryContext);

    /// <summary>
    /// Copies the first present partial state into the destination owner.
    /// </summary>
    /// <param name="left">The destination state.</param>
    /// <param name="right">The worker's state.</param>
    /// <param name="context">The destination aggregate owner.</param>
    /// <returns>An independently retained state.</returns>
    [return: PgSqlType("text", Schema = "pg_catalog")]
    public static PgDatum? Combine(PgAggregateContext context, [PgSqlType("text", Schema = "pg_catalog")] PgDatum? left,
        [PgSqlType("text", Schema = "pg_catalog")] PgDatum? right)
        => left ?? right?.CopyTo(context.MemoryContext);
}
