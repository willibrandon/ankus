namespace Ankus.Generators;

/// <summary>
/// Owns aggregate callback state, native ordering, and managed roots across executor memory lifetimes.
/// </summary>
internal static class NativeAggregateBridge
{
    /// <summary>
    /// Gets guarded aggregate dispatch, state registration, and PostgreSQL sort support.
    /// </summary>
    internal const string Source = """
        #include "executor/nodeAgg.h"
        #include "nodes/nodeFuncs.h"
        #include "utils/hsearch.h"
        #include "utils/sortsupport.h"

        typedef int (*AnkusAggregateCallback)(const AnkusValue *, AnkusValue *, AnkusError *, AnkusExecute,
            const AnkusValue *, int, void *, void *, AnkusMemoryApi *);
        typedef int (*AnkusAggregateRelease)(void *, AnkusError *, AnkusExecute, AnkusMemoryApi *);

        typedef struct AnkusAggregateState
        {
            MemoryContextCallback reset;
            MemoryContext owner;
            void *handle;
            AnkusAggregateRelease release;
            uint64 magic;
        } AnkusAggregateState;

        typedef struct AnkusAggregateOwnerEntry
        {
            void *address;
            AnkusAggregateState *state;
        } AnkusAggregateOwnerEntry;

        typedef struct AnkusAggregateSortKey
        {
            int argument;
            Oid type;
            Oid ordering;
            Oid collation;
            bool nulls_first;
        } AnkusAggregateSortKey;

        typedef struct AnkusAggregateScope
        {
            MemoryContext owner;
            AnkusAggregateSortKey *keys;
            int key_count;
        } AnkusAggregateScope;

        static AnkusAggregateScope *ankus_aggregate_scope;
        static HTAB *ankus_aggregate_owners;
        #define ANKUS_AGGREGATE_STATE_MAGIC UINT64CONST(0x416E6B7573416767)

        static void
        ankus_aggregate_release(void *argument)
        {
            AnkusAggregateState *state = argument;
            AnkusAggregateScope *previous = ankus_aggregate_scope;
            AnkusError error = {0};
            AnkusMemoryApi memory = {0};
            AnkusMemoryProtection protection = {0};
            void *handle = state->handle;
            int status;
            if (handle == NULL)
                return;
            /* Invalidate before entering user cleanup. Reset callbacks can run on abort,
             * window restart, group rescan, or deletion of a temporary deserialized state. */
            state->handle = NULL;
            state->magic = 0;
            {
                void *address = state;
                (void) hash_search(ankus_aggregate_owners, &address, HASH_REMOVE, NULL);
            }

            ankus_aggregate_scope = NULL;
            ankus_memory_initialize(&memory);
            ankus_memory_protect(&protection, state->owner, true);
            PG_TRY();
            {
                status = state->release(handle, &error, ankus_spi_execute, &memory);
            }
            PG_FINALLY();
            {
                ankus_memory_protection = protection.previous;
                ankus_aggregate_scope = previous;
            }
            PG_END_TRY();
            ankus_release_error(&error);
            if (status != 0)
                ereport(WARNING, (errmsg("Ankus aggregate state cleanup failed")));
        }

        static AnkusAggregateState *
        ankus_aggregate_state(Datum datum)
        {
            void *address = DatumGetPointer(datum);
            AnkusAggregateOwnerEntry *entry = ankus_aggregate_owners == NULL ? NULL :
                hash_search(ankus_aggregate_owners, &address, HASH_FIND, NULL);
            /* Look up the address before dereferencing it. A foreign internal state
             * supplied by SQL is not necessarily large enough for our header. */
            if (entry == NULL || entry->state->magic != ANKUS_AGGREGATE_STATE_MAGIC || entry->state->handle == NULL)
                ereport(ERROR, (errcode(ERRCODE_OBJECT_NOT_IN_PREREQUISITE_STATE),
                    errmsg("Invalid or expired Ankus aggregate state")));
            return entry->state;
        }

        static int
        ankus_aggregate_compare(const AnkusParameter *values, int key_index)
        {
            AnkusAggregateSortKey *key;
            SortSupportData support = {0};
            Datum arguments[2] = {0};
            bool nulls[2];
            if (values == NULL || key_index < 0 || key_index >= ankus_aggregate_scope->key_count)
                ereport(ERROR, (errcode(ERRCODE_INVALID_PARAMETER_VALUE), errmsg("Invalid aggregate sort key")));
            key = &ankus_aggregate_scope->keys[key_index];
            for (int index = 0; index < 2; index++)
            {
                Oid source = getBaseType(values[index].type_oid);
                Oid target = getBaseType(key->type);
                bool text = source == TEXTOID && (target == VARCHAROID || target == BPCHAROID);
                if (source != target && !text && !IsBinaryCoercible(source, target))
                    ereport(ERROR, (errcode(ERRCODE_DATATYPE_MISMATCH),
                        errmsg("Aggregate comparison values do not match the sort key type")));
                nulls[index] = values[index].value.is_null;
                if (!nulls[index])
                {
                    arguments[index] = ankus_parameter_datum(&values[index]);
                    if (values[index].type_oid != target)
                        arguments[index] = ankus_coerce_value(arguments[index], &nulls[index],
                            values[index].type_oid, target, -1, key->collation);
                }
            }

            support.ssup_cxt = CurrentMemoryContext;
            support.ssup_collation = key->collation;
            support.ssup_nulls_first = key->nulls_first;
            PrepareSortSupportFromOrderingOp(key->ordering, &support);
            return ApplySortComparator(arguments[0], nulls[0], arguments[1], nulls[1], &support);
        }

        static int
        ankus_aggregate_api(int operation, void *handle, void *release, void **output,
            const AnkusParameter *values, int sort_key, AnkusError *error)
        {
            MemoryContext caller = CurrentMemoryContext;
            ResourceOwner resource_owner = CurrentResourceOwner;
            int nesting = GetCurrentTransactionNestLevel();
            volatile int status = 0;
            *output = NULL;
            /* Both the operation and diagnostic recovery have native guards. Neither
             * allocation failures nor a user comparison ERROR may cross managed frames. */
            PG_TRY();
            {
                PG_TRY();
                {
                    if (ankus_aggregate_scope == NULL)
                        ereport(ERROR, (errcode(ERRCODE_OBJECT_NOT_IN_PREREQUISITE_STATE),
                            errmsg("No active Ankus aggregate callback")));
                    if (operation == 0)
                    {
                        AnkusAggregateState *state;
                        AnkusAggregateOwnerEntry *entry;
                        void *address;
                        if (handle == NULL || release == NULL)
                            ereport(ERROR, (errcode(ERRCODE_INVALID_PARAMETER_VALUE),
                                errmsg("Aggregate state registration requires a managed owner")));
                        if (ankus_aggregate_owners == NULL)
                        {
                            HASHCTL control = {0};
                            control.keysize = sizeof(void *);
                            control.entrysize = sizeof(AnkusAggregateOwnerEntry);
                            control.hcxt = TopMemoryContext;
                            ankus_aggregate_owners = hash_create("Ankus aggregate owners", 32,
                                &control, HASH_ELEM | HASH_BLOBS | HASH_CONTEXT);
                        }

                        state = MemoryContextAllocZero(ankus_aggregate_scope->owner, sizeof(AnkusAggregateState));
                        state->owner = ankus_aggregate_scope->owner;
                        state->handle = handle;
                        state->release = (AnkusAggregateRelease) release;
                        state->magic = ANKUS_AGGREGATE_STATE_MAGIC;
                        state->reset.func = ankus_aggregate_release;
                        state->reset.arg = state;
                        address = state;
                        entry = hash_search(ankus_aggregate_owners, &address, HASH_ENTER, NULL);
                        entry->state = state;
                        MemoryContextRegisterResetCallback(state->owner, &state->reset);
                        *output = state;
                    }
                    else if (operation == 1)
                    {
                        MemoryContext temporary;
                        int comparison;
                        BeginInternalSubTransaction(NULL);
                        temporary = AllocSetContextCreate(CurrentMemoryContext, "Ankus aggregate comparison", ALLOCSET_SMALL_SIZES);
                        MemoryContextSwitchTo(temporary);
                        comparison = ankus_aggregate_compare(values, sort_key);
                        MemoryContextSwitchTo(caller);
                        MemoryContextDelete(temporary);
                        ReleaseCurrentSubTransaction();
                        MemoryContextSwitchTo(caller);
                        CurrentResourceOwner = resource_owner;
                        *output = (void *) (intptr_t) ((comparison > 0) - (comparison < 0));
                    }
                    else
                        ereport(ERROR, (errcode(ERRCODE_INVALID_PARAMETER_VALUE), errmsg("Invalid aggregate operation")));
                }
                PG_CATCH();
                {
                    ErrorData *data;
                    MemoryContext diagnostics;
                    MemoryContextSwitchTo(caller);
                    diagnostics = AllocSetContextCreate(caller, "Ankus aggregate diagnostics", ALLOCSET_SMALL_SIZES);
                    MemoryContextSwitchTo(diagnostics);
                    data = ankus_copy_error_data();
                    FlushErrorState();
                    while (GetCurrentTransactionNestLevel() > nesting)
                        RollbackAndReleaseCurrentSubTransaction();
                    MemoryContextSwitchTo(diagnostics);
                    CurrentResourceOwner = resource_owner;
                    ankus_capture_error(data, error);
                    ankus_free_error_data(data);
                    MemoryContextSwitchTo(caller);
                    MemoryContextDelete(diagnostics);
                    *output = NULL;
                    status = 1;
                }
                PG_END_TRY();
            }
            PG_CATCH();
            {
                MemoryContextSwitchTo(caller);
                FlushErrorState();
                ereport(FATAL, (errmsg("Unable to recover PostgreSQL state after a guarded aggregate failure")));
            }
            PG_END_TRY();
            return status;
        }

        static AnkusValue *
        ankus_aggregate_metadata(FunctionCallInfo fcinfo, int kind, AnkusAggregateScope *scope, int *count)
        {
            Aggref *reference = AggGetAggref(fcinfo);
            AnkusValue *metadata;
            ListCell *cell;
            int index = 0;
            scope->key_count = reference == NULL ? 0 : list_length(reference->aggorder);
            scope->keys = palloc0(sizeof(AnkusAggregateSortKey) * Max(1, scope->key_count));
            *count = 4 + 5 * scope->key_count;
            metadata = palloc0(sizeof(AnkusValue) * *count);
            metadata[0].integral = kind == AGG_CONTEXT_AGGREGATE ? 1 : 2;
            metadata[1].integral = AggStateIsShared(fcinfo);
            metadata[2].integral = PG_GET_COLLATION();
            metadata[3].integral = reference == NULL ? InvalidOid : reference->aggfnoid;
            if (reference == NULL)
                return metadata;
            foreach(cell, reference->aggorder)
            {
                SortGroupClause *clause = lfirst_node(SortGroupClause, cell);
                ListCell *argument;
                TargetEntry *entry = NULL;
                AnkusAggregateSortKey *key = &scope->keys[index];
                foreach(argument, reference->args)
                {
                    TargetEntry *candidate = lfirst_node(TargetEntry, argument);
                    if (candidate->ressortgroupref == clause->tleSortGroupRef)
                    {
                        entry = candidate;
                        break;
                    }
                }

                if (entry == NULL)
                    ereport(ERROR, (errmsg("Aggregate sort key has no input expression")));
                key->argument = entry->resno - 1;
                key->type = exprType((Node *) entry->expr);
                key->ordering = clause->sortop;
                key->collation = exprCollation((Node *) entry->expr);
                key->nulls_first = clause->nulls_first;
                metadata[4 + 5 * index].integral = key->argument;
                metadata[5 + 5 * index].integral = key->type;
                metadata[6 + 5 * index].integral = key->ordering;
                metadata[7 + 5 * index].integral = key->collation;
                metadata[8 + 5 * index].integral = key->nulls_first;
                index++;
            }

            return metadata;
        }

        static Datum
        ankus_aggregate_call(FunctionCallInfo fcinfo, AnkusAggregateCallback callback, int arguments_count,
            const bool *required, const bool *internal_arguments, bool internal_result, bool deserialize)
        {
            MemoryContext owner;
            int kind = AggCheckCallContext(fcinfo, &owner);
            MemoryContext caller = CurrentMemoryContext;
            MemoryContext temporary;
            AnkusAggregateScope *scope;
            AnkusAggregateScope *previous = ankus_aggregate_scope;
            Oid previous_function = ankus_function_oid;
            AnkusValue *arguments;
            AnkusInputBuffer *owned;
            AnkusValue *result;
            AnkusError *error;
            AnkusMemoryProtection protection = {0};
            volatile Datum datum = (Datum) 0;
            if (kind != AGG_CONTEXT_AGGREGATE && kind != AGG_CONTEXT_WINDOW)
                ereport(ERROR, (errcode(ERRCODE_OBJECT_NOT_IN_PREREQUISITE_STATE),
                    errmsg("Ankus aggregate support functions require an aggregate invocation")));
            if (PG_NARGS() != arguments_count)
                ereport(ERROR, (errcode(ERRCODE_INVALID_PARAMETER_VALUE), errmsg("Incorrect aggregate support argument count")));
            for (int index = 0; index < arguments_count; index++)
                if (required[index] && PG_ARGISNULL(index))
                    PG_RETURN_NULL();
            temporary = AllocSetContextCreate(caller, "Ankus aggregate callback", ALLOCSET_SMALL_SIZES);
            scope = MemoryContextAllocZero(temporary, sizeof(AnkusAggregateScope));
            scope->owner = deserialize ? caller : owner;
            arguments = MemoryContextAllocZero(temporary, sizeof(AnkusValue) * Max(arguments_count, 1));
            owned = MemoryContextAllocZero(temporary, sizeof(AnkusInputBuffer) * Max(arguments_count, 1));
            result = MemoryContextAllocZero(temporary, sizeof(AnkusValue));
            error = MemoryContextAllocZero(temporary, sizeof(AnkusError));
            ankus_aggregate_scope = scope;
            ankus_function_oid = fcinfo->flinfo->fn_oid;
            ankus_memory_protect(&protection, scope->owner, false);
            PG_TRY();
            {
                Oid *types;
                Oid result_type;
                int parameter_count;
                int metadata_count;
                AnkusValue *metadata;
                int status;
                MemoryContextSwitchTo(temporary);
                result_type = get_func_signature(fcinfo->flinfo->fn_oid, &types, &parameter_count);
                if (parameter_count != arguments_count)
                    ereport(ERROR, (errmsg("Aggregate support catalog signature does not match generated code")));
                metadata = ankus_aggregate_metadata(fcinfo, kind, scope, &metadata_count);
                for (int index = 0; index < arguments_count; index++)
                {
                    /* PostgreSQL supplies a non-NULL SQL internal dummy with Datum zero.
                     * It is not a state and has no corresponding managed argument. */
                    if (deserialize && index == arguments_count - 1)
                        continue;
                    arguments[index].is_null = PG_ARGISNULL(index);
                    if (!arguments[index].is_null)
                    {
                        if (internal_arguments[index])
                            arguments[index].integral = (intptr_t) ankus_aggregate_state(PG_GETARG_DATUM(index))->handle;
                        else
                            ankus_read_value(PG_GETARG_DATUM(index), types[index], &arguments[index], &owned[index]);
                    }
                }

                AnkusMemoryApi memory = {0};
                ankus_memory_initialize(&memory);
                status = callback(arguments, result, error, ankus_spi_execute, metadata, metadata_count,
                    scope->owner, (void *) ankus_aggregate_api, &memory);
                if (status != 0)
                    ankus_raise_error(error);
                fcinfo->isnull = result->is_null;
                MemoryContextSwitchTo(caller);
                if (!result->is_null)
                {
                    if (internal_result)
                    {
                        AnkusAggregateState *state = ankus_aggregate_state((Datum) (uintptr_t) result->integral);
                        if (state->owner != scope->owner)
                            ereport(ERROR, (errcode(ERRCODE_OBJECT_NOT_IN_PREREQUISITE_STATE),
                                errmsg("Aggregate state belongs to a different memory context; copy its value into a new state")));
                        datum = PointerGetDatum(state);
                    }
                    else
                    {
                        AnkusParameter value = {0};
                        value.type_oid = result_type;
                        value.value = *result;
                        datum = ankus_parameter_datum(&value);
                    }
                }
            }
            PG_FINALLY();
            {
                ankus_memory_protection = protection.previous;
                ankus_aggregate_scope = previous;
                ankus_function_oid = previous_function;
                MemoryContextSwitchTo(caller);
                for (int index = 0; index < arguments_count; index++)
                    ankus_free_input(&owned[index]);
                if (result->release != NULL)
                    result->release(result->data);
                ankus_release_error(error);
                MemoryContextDelete(temporary);
            }
            PG_END_TRY();
            return datum;
        }

        """;
}
