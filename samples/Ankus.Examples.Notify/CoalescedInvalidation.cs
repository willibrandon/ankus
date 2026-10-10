using System.Globalization;

[assembly: PgSql("create-inventory-trigger", """
    CREATE TABLE inventory (
        id        bigserial NOT NULL PRIMARY KEY,
        sku       text NOT NULL,
        category  bigint NOT NULL
    );

    CREATE TRIGGER inventory_coalesce
        AFTER INSERT OR UPDATE OR DELETE ON inventory
        FOR EACH ROW EXECUTE PROCEDURE inventory_coalesce();
    """, Requires = ["inventory-coalesce"])]

namespace Ankus.Examples.Notify;

/// <summary>
/// Ports pgrx's coalesced cache invalidation: one notification per changed category, sent just before commit.
/// </summary>
/// <remarks>
/// Notifying once per row overruns PostgreSQL's notification queue when one statement changes a million rows. The row
/// trigger here only records the affected <c>category</c> in backend-local state. A <c>PreCommit</c> transaction
/// callback then sends one <c>NOTIFY category_invalidation, '&lt;category&gt;'</c> per distinct category while the
/// transaction can still notify, and an <c>Abort</c> callback discards the categories of a rolled-back transaction. A
/// PostgreSQL backend runs one transaction at a time on one thread, so static state is per backend and per transaction.
/// </remarks>
public static class CoalescedInvalidation
{
    /// <summary>
    /// The channel that announces changed inventory categories.
    /// </summary>
    private const string CategoryInvalidationChannel = "category_invalidation";

    /// <summary>
    /// The distinct categories changed by the current transaction, notified in ascending order.
    /// </summary>
    private static readonly SortedSet<long> s_dirty = [];

    /// <summary>
    /// Whether the current transaction has registered its end-of-transaction callbacks.
    /// </summary>
    private static bool s_armed;

    /// <summary>
    /// Records the categories of the new row (INSERT and UPDATE) and the old row (UPDATE and DELETE).
    /// </summary>
    /// <param name="context">The <c>AFTER ... FOR EACH ROW</c> trigger context on <c>inventory</c>.</param>
    /// <returns>Null; PostgreSQL ignores the result of an AFTER trigger.</returns>
    /// <remarks>An UPDATE that moves a row to another category dirties both categories. No notification is sent here.</remarks>
    [PgTrigger]
    [PgFunction(Id = "inventory-coalesce")]
    public static PgHeapTuple? InventoryCoalesce(PgTriggerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Record(context.New);
        Record(context.Old);
        return null;

        static void Record(PgHeapTuple? row)
        {
            if (row?.Get<long?>("category") is long category)
            {
                MarkDirty(category);
            }
        }
    }

    /// <summary>
    /// Records a changed category and registers the transaction's flush on the first change.
    /// </summary>
    /// <param name="category">The changed category.</param>
    private static void MarkDirty(long category)
    {
        if (!s_armed)
        {
            // Register cleanup first: if a later registration fails, rollback still resets this backend's state.
            _ = PgTransaction.RegisterCallback(PgTransactionEvent.Abort, DiscardDirty);
            _ = PgTransaction.RegisterCallback(PgTransactionEvent.PrePrepare, RejectPrepare);
            _ = PgTransaction.RegisterCallback(PgTransactionEvent.PreCommit, FlushDirty);
            s_armed = true;
        }

        _ = s_dirty.Add(category);
    }

    /// <summary>
    /// Sends exactly one notification per dirtied category while the committing transaction is still in progress.
    /// </summary>
    private static void FlushDirty()
    {
        s_armed = false;
        long[] categories = [.. s_dirty];
        s_dirty.Clear();
        foreach (long category in categories)
        {
            Notifications.Notify(CategoryInvalidationChannel, category.ToString(CultureInfo.InvariantCulture));
        }
    }

    /// <summary>
    /// Discards the categories of a rolled-back transaction so they cannot leak into the next transaction.
    /// </summary>
    private static void DiscardDirty()
    {
        s_armed = false;
        s_dirty.Clear();
    }

    /// <summary>
    /// Rejects <c>PREPARE TRANSACTION</c>, as PostgreSQL does after <c>NOTIFY</c>, before the categories become orphaned.
    /// </summary>
    /// <remarks>
    /// A prepared transaction ends without <c>PreCommit</c> or <c>Abort</c> in this backend, and <c>COMMIT PREPARED</c>
    /// cannot run this backend's callbacks. The resulting rollback runs <see cref="DiscardDirty"/>.
    /// </remarks>
    private static void RejectPrepare()
        => throw new PgException(PgSqlStates.FeatureNotSupported,
            "cannot PREPARE a transaction that has pending category invalidations");
}
