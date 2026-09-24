namespace Ankus;

/// <summary>
/// Describes the PostgreSQL function call and provides checked raw copies of its SQL arguments.
/// </summary>
/// <remarks>
/// Declare this type as a function parameter to receive it without adding a SQL argument.
/// Metadata is an independent managed snapshot. Argument storage belongs to the scalar callback's
/// memory context or the set function's multi-call context. Copy an argument to another context
/// when it needs a longer lifetime. Native argument access requires the owning backend thread.
/// </remarks>
public sealed class PgFunctionContext
{
    /// <summary>
    /// Creates an immutable snapshot after native argument copies have completed.
    /// </summary>
    /// <param name="functionOid">The invoked function's catalog identity.</param>
    /// <param name="resultTypeOid">The resolved result type.</param>
    /// <param name="collationOid">The call's collation, or zero when no collation applies.</param>
    /// <param name="arguments">The checked raw SQL arguments in declaration order.</param>
    internal PgFunctionContext(uint functionOid, uint resultTypeOid, uint collationOid, PgDatum[] arguments)
    {
        FunctionOid = functionOid;
        ResultTypeOid = resultTypeOid;
        CollationOid = collationOid;
        Arguments = Array.AsReadOnly(arguments);
    }

    /// <summary>
    /// Gets the catalog OID of the PostgreSQL function being invoked.
    /// </summary>
    public uint FunctionOid { get; }

    /// <summary>
    /// Gets the resolved PostgreSQL result type OID, including domains and record results.
    /// </summary>
    public uint ResultTypeOid { get; }

    /// <summary>
    /// Gets the collation selected for this call, or zero when no collation applies.
    /// </summary>
    public uint CollationOid { get; }

    /// <summary>
    /// Gets the SQL arguments by zero-based ordinal, excluding injected managed parameters.
    /// Every value retains its actual type OID and SQL NULL flag.
    /// </summary>
    public IReadOnlyList<PgDatum> Arguments { get; }
}
