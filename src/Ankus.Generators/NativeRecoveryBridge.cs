namespace Ankus.Generators;

/// <summary>
/// Contains failures until managed callbacks return when PostgreSQL cannot open a recovery savepoint.
/// </summary>
internal static class NativeRecoveryBridge
{
    /// <summary>
    /// Gets synchronous callback frames and first-error transport for older parallel transactions.
    /// </summary>
    internal const string Source = """
        #include "access/xact.h"

        typedef struct AnkusRecoveryFrame
        {
            struct AnkusRecoveryFrame *previous;
            AnkusError failure;
            bool failed;
        } AnkusRecoveryFrame;

        static AnkusRecoveryFrame *ankus_recovery_frame;

        static bool
        ankus_parallel_without_subtransactions(void)
        {
        #if PG_VERSION_NUM < 170000
            return IsInParallelMode();
        #else
            return false;
        #endif
        }

        static bool
        ankus_recovery_failed(AnkusError *error)
        {
            for (AnkusRecoveryFrame *frame = ankus_recovery_frame; frame != NULL; frame = frame->previous)
            {
                if (!frame->failed)
                    continue;

                /* The frame owns these buffers until managed dispatch has returned.
                 * The guarded caller copies them before releasing its borrowed view. */
                *error = frame->failure;
                for (int index = 0; index < ANKUS_ERROR_FIELD_COUNT; index++)
                    error->fields[index].release = NULL;
                return true;
            }

            return false;
        }

        static void
        ankus_recovery_record(ErrorData *data)
        {
            if (ankus_parallel_without_subtransactions() && ankus_recovery_frame != NULL &&
                !ankus_recovery_frame->failed)
            {
                ankus_capture_error(data, &ankus_recovery_frame->failure);
                ankus_recovery_frame->failed = true;
            }
        }

        static int
        ankus_recovery_finish(AnkusRecoveryFrame *frame, AnkusError *error, int status)
        {
            ankus_recovery_frame = frame->previous;
            if (frame->failed)
            {
                ankus_release_error(error);
                *error = frame->failure;
                memset(&frame->failure, 0, sizeof(frame->failure));
                return 1;
            }

            return status;
        }

        /* Managed entry points contain every exception. No PostgreSQL call or
         * longjmp may occur between installing this stack frame and removing it. */
        #define ANKUS_MANAGED_INVOKE(status, error, expression) \
            do \
            { \
                AnkusRecoveryFrame ankus_invocation = {0}; \
                ankus_invocation.previous = ankus_recovery_frame; \
                ankus_recovery_frame = &ankus_invocation; \
                (status) = (expression); \
                (status) = ankus_recovery_finish(&ankus_invocation, (error), (status)); \
            } while (0)

        """;
}
