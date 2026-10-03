using System.Runtime.InteropServices;
using Ankus.Postgres;

namespace Ankus.Examples.CustomScans;

/// <summary>
/// Coordinates trace observations through PostgreSQL-owned dynamic shared memory.
/// </summary>
public static unsafe partial class TraceScan
{
    /// <summary>
    /// Measures the exact selected-header storage required by the provider.
    /// </summary>
    [PgNativeCallback(nameof(EstimateShared))]
    private static partial CustomExecMethods_EstimateDSMCustomScanCallback SharedEstimator { get; }

    /// <summary>
    /// Initializes the leader's shared observations before any worker attaches.
    /// </summary>
    [PgNativeCallback(nameof(InitializeShared))]
    private static partial CustomExecMethods_InitializeDSMCustomScanCallback SharedInitializer { get; }

    /// <summary>
    /// Resets shared observations for a new execution after previous workers have finished.
    /// </summary>
    [PgNativeCallback(nameof(ReinitializeShared))]
    private static partial CustomExecMethods_ReInitializeDSMCustomScanCallback SharedReinitializer { get; }

    /// <summary>
    /// Attaches each worker to the coordinate block supplied by PostgreSQL.
    /// </summary>
    [PgNativeCallback(nameof(InitializeWorker))]
    private static partial CustomExecMethods_InitializeWorkerCustomScanCallback WorkerInitializer { get; }

    /// <summary>
    /// Copies observations and releases the borrowed address before DSM teardown.
    /// </summary>
    [PgNativeCallback(nameof(Shutdown))]
    private static partial CustomExecMethods_ShutdownCustomScanCallback ShutdownHandler { get; }

    /// <summary>
    /// Reserves only native atomic storage; PostgreSQL supplies its alignment and lifetime.
    /// </summary>
    private static ulong EstimateShared(CustomScanState* address, ParallelContext* context)
    {
        _ = address;
        _ = context;
        return (ulong)sizeof(SharedState);
    }

    /// <summary>
    /// Constructs selected-header atomics before the leader publishes the coordinate block.
    /// </summary>
    private static void InitializeShared(CustomScanState* address, ParallelContext* context, void* coordinate)
    {
        _ = context;
        var shared = (SharedState*)coordinate;
        NativeMethods.pg_atomic_init_u64(&shared->_rows, 0);
        NativeMethods.pg_atomic_init_u64(&shared->_calls, 0);
        NativeMethods.pg_atomic_init_u64(&shared->_workers, 0);
        NativeMethods.pg_atomic_init_u64(&shared->_shutdowns, 0);
        NativeMethods.pg_atomic_init_u64(&shared->_generation, 1);
        var state = (State*)address;
        state->_shared = shared;
        state->_snapshot = default;
    }

    /// <summary>
    /// Resets shared values independently of the local ReScan callback.
    /// </summary>
    private static void ReinitializeShared(CustomScanState* address, ParallelContext* context, void* coordinate)
    {
        _ = context;
        var shared = (SharedState*)coordinate;
        ulong generation = checked(NativeMethods.pg_atomic_read_u64(&shared->_generation) + 1);
        NativeMethods.pg_atomic_write_u64(&shared->_rows, 0);
        NativeMethods.pg_atomic_write_u64(&shared->_calls, 0);
        NativeMethods.pg_atomic_write_u64(&shared->_workers, 0);
        NativeMethods.pg_atomic_write_u64(&shared->_shutdowns, 0);
        NativeMethods.pg_atomic_write_u64(&shared->_generation, generation);
        var state = (State*)address;
        state->_shared = shared;
        state->_snapshot = default;
    }

    /// <summary>
    /// Attaches process-local execution state without storing its address in shared memory.
    /// </summary>
    private static void InitializeWorker(CustomScanState* address, shm_toc* table, void* coordinate)
    {
        _ = table;
        var state = (State*)address;
        state->_shared = (SharedState*)coordinate;
        state->_worker = true;
        var shared = (SharedState*)coordinate;
        _ = NativeMethods.pg_atomic_fetch_add_u64(&shared->_workers, 1);
    }

    /// <summary>
    /// Copies a shutdown snapshot without waiting on a parent's still-active tuple queues.
    /// </summary>
    private static void Shutdown(CustomScanState* address)
    {
        var state = (State*)address;
        SharedState* shared = state->_shared;
        state->_shared = null;
        if (shared == null)
        {
            return;
        }

        if (state->_worker)
        {
            _ = NativeMethods.pg_atomic_fetch_add_u64(&shared->_shutdowns, 1);
        }

        state->_snapshot = new ParallelSnapshot
        {
            _rows = NativeMethods.pg_atomic_read_u64(&shared->_rows),
            _calls = NativeMethods.pg_atomic_read_u64(&shared->_calls),
            _workers = NativeMethods.pg_atomic_read_u64(&shared->_workers),
            _shutdowns = NativeMethods.pg_atomic_read_u64(&shared->_shutdowns),
            _generation = NativeMethods.pg_atomic_read_u64(&shared->_generation),
        };
    }

    /// <summary>
    /// Contains only selected-header native atomics at naturally aligned, equal-size offsets.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct SharedState
    {
        /// <summary>
        /// Counts tuples returned by every participant in this execution.
        /// </summary>
        internal pg_atomic_uint64 _rows;

        /// <summary>
        /// Counts executor entries, including each participant's end-of-scan check.
        /// </summary>
        internal pg_atomic_uint64 _calls;

        /// <summary>
        /// Counts actual worker attachments in this execution.
        /// </summary>
        internal pg_atomic_uint64 _workers;

        /// <summary>
        /// Counts worker shutdown callbacks already observed at snapshot time.
        /// </summary>
        internal pg_atomic_uint64 _shutdowns;

        /// <summary>
        /// Identifies the execution started by initialization or reinitialization.
        /// </summary>
        internal pg_atomic_uint64 _generation;
    }

    /// <summary>
    /// Owns copied diagnostic values without retaining a shared-memory address.
    /// </summary>
    private struct ParallelSnapshot
    {
        /// <summary>
        /// Retains observed shared tuples.
        /// </summary>
        internal ulong _rows;

        /// <summary>
        /// Retains observed shared executor entries.
        /// </summary>
        internal ulong _calls;

        /// <summary>
        /// Retains observed worker attachments.
        /// </summary>
        internal ulong _workers;

        /// <summary>
        /// Retains observed worker shutdowns.
        /// </summary>
        internal ulong _shutdowns;

        /// <summary>
        /// Retains the most recent DSM execution number.
        /// </summary>
        internal ulong _generation;
    }
}
