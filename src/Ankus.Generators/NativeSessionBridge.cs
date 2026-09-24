namespace Ankus.Generators;

/// <summary>
/// Tracks scoped SPI connection identities in their PostgreSQL procedure memory contexts.
/// </summary>
internal static class NativeSessionBridge
{
    /// <summary>
    /// Gets session registration, stack validation, and native invalidation callbacks.
    /// </summary>
    internal const string Source = """
        #include "utils/resowner.h"

        typedef struct AnkusSessionPlan
        {
            SPIPlanPtr plan;
            struct AnkusSessionPlan *next;
        } AnkusSessionPlan;

        typedef struct AnkusSession
        {
            int64 identity;
            MemoryContext caller_context;
            ResourceOwner caller_owner;
            int caller_nest_level;
            int caller_internal_subtransaction_depth;
            MemoryContext context;
            AnkusSessionPlan *plans;
            MemoryContextCallback cleanup;
            struct AnkusSession *previous;
        } AnkusSession;

        static AnkusSession *ankus_session = NULL;
        static int64 ankus_next_session_id = 0;

        static void
        ankus_forget_session(void *argument)
        {
            AnkusSession *session = (AnkusSession *) argument;
            AnkusSession **position = &ankus_session;
            AnkusSessionPlan *entry;
            /* Saved plans participate in invalidation but are still owned by this scope until detached. */
            for (entry = session->plans; entry != NULL; entry = entry->next)
            {
                if (entry->plan != NULL)
                {
                    SPI_freeplan(entry->plan);
                    entry->plan = NULL;
                }
            }

            while (*position != NULL)
            {
                if (*position == session)
                {
                    *position = session->previous;
                    return;
                }

                position = &(*position)->previous;
            }
        }

        static void
        ankus_register_session(AnkusRequest *request, MemoryContext caller_context, ResourceOwner caller_owner,
            int caller_nest_level, int caller_internal_subtransaction_depth)
        {
            AnkusSession *session;
            if (ankus_next_session_id == PG_INT64_MAX)
            {
                ereport(ERROR, (errcode(ERRCODE_PROGRAM_LIMIT_EXCEEDED), errmsg("SPI session identity capacity exhausted")));
            }

            /* SPI_connect has made its procedure context current. SPI_finish invalidates this entry. */
            session = palloc0(sizeof(AnkusSession));
            session->identity = ++ankus_next_session_id;
            session->caller_context = caller_context;
            session->caller_owner = caller_owner;
            session->caller_nest_level = caller_nest_level;
            session->caller_internal_subtransaction_depth = caller_internal_subtransaction_depth;
            session->context = CurrentMemoryContext;
            session->previous = ankus_session;
            session->cleanup.func = ankus_forget_session;
            session->cleanup.arg = session;
            MemoryContextRegisterResetCallback(CurrentMemoryContext, &session->cleanup);
            ankus_session = session;
            request->session_id = session->identity;
        }

        static void
        ankus_require_session(int64 identity)
        {
            if (ankus_session == NULL || ankus_session->identity != identity)
            {
                ereport(ERROR, (errcode(ERRCODE_OBJECT_NOT_IN_PREREQUISITE_STATE),
                    errmsg("The SPI session is no longer the active connection")));
            }
        }

        static void
        ankus_register_session_plan(SPIPlanPtr plan)
        {
            AnkusSessionPlan *entry = MemoryContextAllocZero(ankus_session->context, sizeof(AnkusSessionPlan));
            entry->plan = plan;
            entry->next = ankus_session->plans;
            ankus_session->plans = entry;
        }

        static void
        ankus_detach_session_plan(SPIPlanPtr plan)
        {
            AnkusSessionPlan **position = &ankus_session->plans;
            while (*position != NULL)
            {
                AnkusSessionPlan *entry = *position;
                if (entry->plan == plan)
                {
                    *position = entry->next;
                    pfree(entry);
                    return;
                }

                position = &entry->next;
            }
        }

        """;
}
