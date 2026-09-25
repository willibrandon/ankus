namespace Ankus.Generators;

/// <summary>
/// Copies the complete pgrx PgProc metadata surface from a briefly pinned selected-header catalog tuple.
/// </summary>
internal static class NativeFunctionCatalogBridge
{
    /// <summary>
    /// Gets a cache-pin-safe snapshot with normalized argument arrays and exact text ownership.
    /// </summary>
    internal const string Source = """
        #include "catalog/pg_proc.h"
        #include "utils/syscache.h"

        static void
        ankus_function_catalog(AnkusRequest *request, AnkusResult *result)
        {
            Oid oid = (Oid) request->parameters[0].value.integral;
            HeapTuple tuple = SearchSysCache1(PROCOID, ObjectIdGetDatum(oid));
            if (!HeapTupleIsValid(tuple))
                return;
            PG_TRY();
            {
                static const int attributes[] = {
                    Anum_pg_proc_proowner,
                    Anum_pg_proc_procost,
                    Anum_pg_proc_prorows,
                    Anum_pg_proc_provariadic,
                    Anum_pg_proc_prosupport,
                    Anum_pg_proc_prokind,
                    Anum_pg_proc_prosecdef,
                    Anum_pg_proc_proleakproof,
                    Anum_pg_proc_prolang,
                    Anum_pg_proc_prosrc,
                    Anum_pg_proc_probin,
                    Anum_pg_proc_proconfig,
                    Anum_pg_proc_proargmodes,
                    Anum_pg_proc_pronargs,
                    Anum_pg_proc_pronargdefaults,
                    Anum_pg_proc_proargnames,
                    Anum_pg_proc_proargtypes,
                    Anum_pg_proc_proallargtypes,
                    Anum_pg_proc_prorettype,
                    Anum_pg_proc_proisstrict,
                    Anum_pg_proc_provolatile,
                    Anum_pg_proc_proparallel,
                    Anum_pg_proc_proretset,
                    Anum_pg_proc_proargdefaults
                };
                static const Oid types[] = {
                    OIDOID, FLOAT4OID, FLOAT4OID, OIDOID, OIDOID, CHAROID, BOOLOID, BOOLOID, OIDOID, TEXTOID, TEXTOID, TEXTARRAYOID, CHARARRAYOID, INT2OID, INT2OID, TEXTARRAYOID, OIDVECTOROID, OIDARRAYOID, OIDOID, BOOLOID, CHAROID, CHAROID, BOOLOID, TEXTOID
                };
                result->row_count = 1;
                result->column_count = lengthof(attributes);
                result->values = calloc(result->column_count, sizeof(AnkusValue));
                if (result->values == NULL)
                    ereport(ERROR, (errcode(ERRCODE_OUT_OF_MEMORY), errmsg("unable to copy function catalog metadata")));
                for (int index = 0; index < result->column_count; index++)
                {
                    bool is_null;
                    Datum value = SysCacheGetAttr(PROCOID, tuple, attributes[index], &is_null);
                    AnkusValue *output = &result->values[index];
                    output->is_null = is_null;
                    if (is_null)
                        continue;
                    if (types[index] == OIDVECTOROID)
                    {
                        oidvector *vector = (oidvector *) DatumGetPointer(value);
                        Datum *oids = palloc(sizeof(Datum) * Max(vector->dim1, 1));
                        for (int item = 0; item < vector->dim1; item++)
                            oids[item] = ObjectIdGetDatum(vector->values[item]);
                        ArrayType *array = construct_array(oids, vector->dim1, OIDOID, sizeof(Oid), true, TYPALIGN_INT);
                        ankus_result_value(PointerGetDatum(array), OIDARRAYOID, output);
                    }
                    else if (types[index] == CHARARRAYOID)
                    {
                        Datum *modes;
                        bool *nulls;
                        int count;
                        deconstruct_array(DatumGetArrayTypeP(value), CHAROID, 1, true, TYPALIGN_CHAR, &modes, &nulls, &count);
                        char *text = palloc(Max(count, 1));
                        for (int item = 0; item < count; item++)
                        {
                            if (nulls[item])
                                ereport(ERROR, (errcode(ERRCODE_INVALID_BINARY_REPRESENTATION), errmsg("null function argument mode")));
                            text[item] = DatumGetChar(modes[item]);
                        }

                        ankus_copy_owned(output, (unsigned char *) text, count);
                    }
                    else
                        ankus_result_value(value, types[index], output);
                }
            }
            PG_FINALLY();
            {
                ReleaseSysCache(tuple);
            }
            PG_END_TRY();
        }

        """;
}
