using Ankus.Postgres;

namespace Ankus.Examples.RewriteManip;

/// <summary>
/// Ports pgrx's <c>rewrite_manip</c> example: query-tree <c>Var</c> manipulation through <c>rewrite/rewriteManip.h</c>.
/// </summary>
/// <remarks>
/// Each function builds one <c>Var</c> node with PostgreSQL's <c>makeVar</c>, passes it to a rewriter utility and reads
/// the changed field. The node is allocated in the call's current memory context, which PostgreSQL releases after the
/// call. Range-table indexes are unsigned on PostgreSQL 13 and 14 and signed from PostgreSQL 15, so the conversions are
/// selected for the headers the extension is built against.
/// </remarks>
public static class RewriteManipFunctions
{
    /// <summary>
    /// The attribute number of the demonstration column reference.
    /// </summary>
    private const short AttributeNumber = 1;

    /// <summary>
    /// The type modifier PostgreSQL uses for an <c>integer</c> column without one.
    /// </summary>
    private const int NoTypeModifier = -1;

    /// <summary>
    /// PostgreSQL's <c>InvalidOid</c>, used for the column's absent collation.
    /// </summary>
    private const uint InvalidOid = 0;

    /// <summary>
    /// Creates a <c>Var</c> for range-table entry <paramref name="oldVarno"/> and remaps it to <paramref name="newVarno"/>.
    /// </summary>
    /// <param name="oldVarno">The range-table index referenced by the new <c>Var</c>.</param>
    /// <param name="newVarno">The replacement range-table index.</param>
    /// <returns>The <c>Var</c>'s range-table index after <c>ChangeVarNodes</c>.</returns>
    [PgFunction]
    public static int DemoChangeVarNodes(int oldVarno, int newVarno)
    {
        unsafe
        {
            Var* var = MakeVar(oldVarno, 0);
            NativeMethods.ChangeVarNodes((Node*)var, oldVarno, newVarno, 0);
            return RangeTableIndex(var->varno);
        }
    }

    /// <summary>
    /// Creates a <c>Var</c> for range-table entry 1 and shifts it by <paramref name="offset"/>.
    /// </summary>
    /// <param name="offset">The amount added to every matching range-table index.</param>
    /// <returns>The <c>Var</c>'s range-table index after <c>OffsetVarNodes</c>.</returns>
    [PgFunction]
    public static int DemoOffsetVarNodes(int offset)
    {
        unsafe
        {
            Var* var = MakeVar(1, 0);
            NativeMethods.OffsetVarNodes((Node*)var, offset, 0);
            return RangeTableIndex(var->varno);
        }
    }

    /// <summary>
    /// Creates a <c>Var</c> for range-table entry <paramref name="varno"/> and asks whether a node tree uses
    /// <paramref name="checkRtIndex"/>.
    /// </summary>
    /// <param name="varno">The range-table index referenced by the new <c>Var</c>.</param>
    /// <param name="checkRtIndex">The range-table index to look for.</param>
    /// <returns>Whether <c>rangeTableEntry_used</c> finds a reference to <paramref name="checkRtIndex"/>.</returns>
    [PgFunction]
    public static bool DemoRangeTableEntryUsed(int varno, int checkRtIndex)
    {
        unsafe
        {
            Var* var = MakeVar(varno, 0);
            return NativeMethods.rangeTableEntry_used((Node*)var, checkRtIndex, 0);
        }
    }

    /// <summary>
    /// Creates a <c>Var</c> at subquery level <paramref name="initialLevel"/> and increments that level by
    /// <paramref name="delta"/>.
    /// </summary>
    /// <param name="initialLevel">The <c>varlevelsup</c> value, reinterpreted as PostgreSQL's unsigned <c>Index</c>.</param>
    /// <param name="delta">The amount added to each level at or above zero.</param>
    /// <returns>The resulting <c>varlevelsup</c>, reinterpreted as a signed integer as pgrx does.</returns>
    [PgFunction]
    public static int DemoIncrementVarSublevelsUp(int initialLevel, int delta)
    {
        unsafe
        {
            Var* var = MakeVar(1, unchecked((uint)initialLevel));
            NativeMethods.IncrementVarSublevelsUp((Node*)var, delta, 0);
            return unchecked((int)var->varlevelsup);
        }
    }

    /// <summary>
    /// Allocates an <c>integer</c> column reference in the current memory context.
    /// </summary>
    /// <param name="varno">The referenced range-table index.</param>
    /// <param name="levelsUp">The number of subquery levels between the reference and its range table.</param>
    /// <returns>The PostgreSQL-owned node.</returns>
    private static unsafe Var* MakeVar(int varno, uint levelsUp)
        => NativeMethods.makeVar(NativeRangeTableIndex(varno), AttributeNumber, (uint)PgBuiltInOid.Int4Oid, NoTypeModifier,
            InvalidOid, levelsUp);

#if ANKUS_PG13 || ANKUS_PG14
    /// <summary>
    /// Converts a SQL integer to PostgreSQL 13 and 14's unsigned range-table index, rejecting negative values as pgrx does.
    /// </summary>
    private static uint NativeRangeTableIndex(int value) => value >= 0 ? (uint)value
        : throw new ArgumentOutOfRangeException(nameof(value), value, "PostgreSQL 13 and 14 range-table indexes are unsigned.");

    /// <summary>
    /// Converts PostgreSQL 13 and 14's unsigned range-table index to a SQL integer, rejecting values above its range.
    /// </summary>
    private static int RangeTableIndex(uint value) => value <= int.MaxValue ? (int)value
        : throw new OverflowException($"Range-table index {value} does not fit in a SQL integer.");
#else
    /// <summary>
    /// Returns the signed range-table index used by PostgreSQL 15 and later.
    /// </summary>
    private static int NativeRangeTableIndex(int value) => value;

    /// <summary>
    /// Returns the signed range-table index used by PostgreSQL 15 and later.
    /// </summary>
    private static int RangeTableIndex(int value) => value;
#endif
}
