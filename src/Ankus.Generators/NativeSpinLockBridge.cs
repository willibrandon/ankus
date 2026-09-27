namespace Ankus.Generators;

/// <summary>
/// Emits selected-header spinlock operations with a nonthrowing release entry point.
/// </summary>
internal static class NativeSpinLockBridge
{
    /// <summary>
    /// Gets the checked inline storage contract and actual PostgreSQL spinlock operations.
    /// </summary>
    internal const string Source = """
        #include "storage/spin.h"

        typedef struct AnkusSpinLockAlignment
        {
            char prefix;
            slock_t value;
        } AnkusSpinLockAlignment;

        StaticAssertDecl(sizeof(slock_t) <= sizeof(uint64), "Ankus spinlock storage is too small for the selected headers");
        StaticAssertDecl(offsetof(AnkusSpinLockAlignment, value) <= sizeof(uint64), "Ankus spinlock alignment is insufficient");

        static void
        ankus_spin_release(intptr_t address)
        {
            SpinLockRelease((slock_t *) address);
        }

        static void
        ankus_memory_spin(AnkusMemoryRequest *request, AnkusMemoryResult *result)
        {
            if (request->pointer == 0 || request->length != sizeof(uint64) ||
                (uintptr_t) request->pointer % sizeof(uint64) != 0)
            {
                ereport(ERROR, (errcode(ERRCODE_INVALID_PARAMETER_VALUE), errmsg("invalid Ankus spinlock storage")));
            }

            slock_t *lock = (slock_t *) request->pointer;
            switch (request->flags)
            {
                case 0:
                    SpinLockInit(lock);
        #if PG_VERSION_NUM < 190000
                    result->value = 1;
        #endif
                    break;
                case 1:
                    SpinLockAcquire(lock);
                    break;
                case 2:
        #if PG_VERSION_NUM < 190000
                    result->value = !SpinLockFree(lock);
        #else
                    ereport(ERROR, (errcode(ERRCODE_FEATURE_NOT_SUPPORTED), errmsg("PostgreSQL 19 does not expose spinlock state queries")));
        #endif
                    break;
                case 3:
                    result->pointer = (intptr_t) &ankus_spin_release;
                    break;
                default:
                    ereport(ERROR, (errcode(ERRCODE_INVALID_PARAMETER_VALUE), errmsg("invalid Ankus spinlock operation")));
            }
        }

        """;
}
