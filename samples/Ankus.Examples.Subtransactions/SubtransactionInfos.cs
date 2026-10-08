using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Ankus.Postgres;

namespace Ankus.Examples.Subtransactions;

/// <summary>
/// Ports pgrx's <c>subtrans_infos</c> example: status, subtransaction ancestry and commit time for a transaction ID.
/// </summary>
/// <remarks>
/// The function reads PostgreSQL's commit log, <c>pg_subtrans</c> and commit-timestamp data through generated
/// native bindings. It holds <c>XactTruncationLock</c> in shared mode, as PostgreSQL's own status functions do,
/// so concurrent truncation cannot remove the data being read.
/// </remarks>
public static class SubtransactionInfos
{
    /// <summary>
    /// The position of <c>XactTruncationLock</c> in PostgreSQL's main lightweight-lock array on every supported major.
    /// </summary>
    private const int XactTruncationLockIndex = 44;

    private const string StatusInProgress = "in progress";
    private const string StatusCommitted = "committed";
    private const string StatusAborted = "aborted";

    /// <summary>
    /// Describes one transaction ID, optionally qualified by its 32-bit epoch in the high bits.
    /// </summary>
    /// <param name="xidInput">The transaction ID; the low 32 bits are the ID and the high 32 bits its epoch.</param>
    /// <returns>
    /// One row with the ID, its status, direct and top-level parents, nesting level and commit time. The parent
    /// columns are SQL NULL for top-level transactions and for transactions older than this backend's
    /// <c>TransactionXmin</c>, whose <c>pg_subtrans</c> data may already be truncated. The commit time requires
    /// <c>track_commit_timestamp</c>.
    /// </returns>
    [PgFunction]
    public static IEnumerable<(int Xid, string Status, int? ParentXid, int? TopParentXid, int? SubLevel, PgTimestamp? CommitTimestamp)> SubtransInfos(long xidInput)
    {
        unsafe
        {
            LWLock* truncation = &NativeGlobals.MainLWLockArray[XactTruncationLockIndex].@lock;
            _ = NativeMethods.LWLockAcquire(truncation, LWLockMode.LW_SHARED);
            try
            {
                uint xid = RecentTransaction(xidInput);
                uint transactionXmin = NativeGlobals.TransactionXmin;
                if (!NativeMethods.TransactionIdFollowsOrEquals(xid, transactionXmin))
                {
                    // pg_subtrans is only guaranteed for transactions at or after TransactionXmin.
                    (string oldStatus, PgTimestamp? oldCommit) = Status(xid);
                    return [(ToInt32(xid), oldStatus, null, null, null, oldCommit)];
                }

                uint parent = NativeMethods.SubTransGetParent(xid);
                (uint? topParent, int? subLevel) = TopParent(xid);
                (string status, PgTimestamp? commit) = Status(xid);
                return [(ToInt32(xid), status, parent == PgTransactionId.Invalid.Value ? null : ToInt32(parent),
                    topParent is uint top ? ToInt32(top) : null, subLevel, commit)];
            }
            finally
            {
                NativeMethods.LWLockRelease(truncation);
            }
        }
    }

    /// <summary>
    /// Validates that a transaction ID is not in the future and that its commit-log data is still available.
    /// </summary>
    /// <param name="input">The ID with its epoch in the high 32 bits.</param>
    /// <returns>The 32-bit transaction ID.</returns>
    private static uint RecentTransaction(long input)
    {
        ulong withEpoch = unchecked((ulong)input);
        uint epoch = (uint)(withEpoch >> 32);
        var xid = new PgTransactionId(unchecked((uint)withEpoch));
        if (!xid.IsValid)
        {
            Reject(withEpoch, "invalid transaction ID");
        }

        // Bootstrap and frozen IDs are always committed and do not require commit-log data.
        if (!xid.IsNormal)
        {
            return xid.Value;
        }

        unsafe
        {
            FullTransactionId next = NativeMethods.ReadNextFullTransactionId();
            uint nextXid = unchecked((uint)next.value);
            uint nextEpoch = (uint)(next.value >> 32);
            if (withEpoch >= next.value)
            {
                Reject(withEpoch, "transaction ID is in the future");
            }

            if ((ulong)epoch + 1 < nextEpoch ||
                ((ulong)epoch + 1 == nextEpoch && NativeMethods.TransactionIdPrecedes(xid.Value, nextXid)) ||
                NativeMethods.TransactionIdPrecedes(xid.Value, OldestClogXid()))
            {
                Reject(withEpoch, "transaction ID is too old and CLOG data is unavailable");
            }
        }

        return xid.Value;
    }

    /// <summary>
    /// Reads the oldest transaction ID whose commit-log data has not been truncated.
    /// </summary>
    /// <returns>The value protected by the held truncation lock.</returns>
    private static unsafe uint OldestClogXid()
    {
#if ANKUS_PG13 || ANKUS_PG14 || ANKUS_PG15 || ANKUS_PG16
        return NativeGlobals.ShmemVariableCache->oldestClogXid;
#else
        return NativeGlobals.TransamVariables->oldestClogXid;
#endif
    }

    /// <summary>
    /// Follows <c>pg_subtrans</c> to the top-level transaction.
    /// </summary>
    /// <param name="xid">A transaction at or after <c>TransactionXmin</c>.</param>
    /// <returns>The top-level parent and nesting level, or nulls for a top-level transaction.</returns>
    private static (uint? TopParent, int? SubLevel) TopParent(uint xid)
    {
        unsafe
        {
            uint transactionXmin = NativeGlobals.TransactionXmin;
            if (!NativeMethods.TransactionIdFollowsOrEquals(xid, transactionXmin))
            {
                return (null, null);
            }

            uint parent = xid;
            uint previous = xid;
            int level = -1;
            while (parent != PgTransactionId.Invalid.Value)
            {
                previous = parent;
                if (NativeMethods.TransactionIdPrecedes(parent, transactionXmin))
                {
                    break;
                }

                parent = NativeMethods.SubTransGetParent(parent);
                level++;
                if (parent == PgTransactionId.Invalid.Value)
                {
                    break;
                }

                // A parent always precedes its child; anything else would loop forever.
                if (!NativeMethods.TransactionIdPrecedes(parent, previous))
                {
                    PgLog.Error(string.Create(CultureInfo.InvariantCulture,
                        $"pg_subtrans contains invalid entry: xid {previous} points to parent xid {parent}"));
                }
            }

            return level > 0 ? (previous, level) : (null, null);
        }
    }

    /// <summary>
    /// Classifies a transaction and reads its commit time when PostgreSQL tracks commit timestamps.
    /// </summary>
    /// <param name="xid">The transaction ID.</param>
    /// <returns>The status text and optional commit time.</returns>
    private static unsafe (string Status, PgTimestamp? CommitTimestamp) Status(uint xid)
    {
        if (NativeMethods.TransactionIdIsCurrentTransactionId(xid))
        {
            return (StatusInProgress, null);
        }

        if (NativeMethods.TransactionIdDidCommit(xid))
        {
            PgTimestamp? commit = null;
            long timestamp = 0;
            if (NativeGlobals.track_commit_timestamp && NativeMethods.TransactionIdGetCommitTsData(xid, &timestamp, null))
            {
                // pgrx reports the native timestamptz microseconds as a timestamp without time zone.
                commit = PgTimestamp.FromRawSaturating(timestamp);
            }

            return (StatusCommitted, commit);
        }

        SnapshotData* snapshot = NativeMethods.ActiveSnapshotSet() ? NativeMethods.GetActiveSnapshot() : null;
        return NativeMethods.TransactionIdDidAbort(xid) ||
            (snapshot != null && NativeMethods.TransactionIdPrecedes(xid, snapshot->xmin))
            ? (StatusAborted, null)
            : (StatusInProgress, null);
    }

    /// <summary>
    /// Reports an unusable transaction ID with pgrx's message format.
    /// </summary>
    [DoesNotReturn]
    private static void Reject(ulong input, string reason)
        => PgLog.Error(string.Create(CultureInfo.InvariantCulture, $"Invalid transaction ID {input}: {reason}"));

    /// <summary>
    /// Reinterprets a 32-bit transaction ID as PostgreSQL integer, wrapping IDs above <see cref="int.MaxValue"/>.
    /// </summary>
    private static int ToInt32(uint xid) => unchecked((int)xid);
}
