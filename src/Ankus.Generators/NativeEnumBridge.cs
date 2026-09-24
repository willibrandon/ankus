namespace Ankus.Generators;

/// <summary>
/// Supplies enum label transport and catalog lookups confined to native error guards.
/// </summary>
internal static class NativeEnumBridge
{
    /// <summary>
    /// Gets enum conversion helpers and the currently executing SQL function identity.
    /// </summary>
    internal const string Source = """
        #include "access/htup_details.h"
        #include "catalog/dependency.h"
        #include "catalog/namespace.h"
        #include "catalog/pg_extension.h"
        #include "catalog/pg_enum.h"
        #include "catalog/pg_proc_d.h"
        #include "catalog/pg_type.h"
        #include "utils/syscache.h"
        #include "utils/lsyscache.h"

        static Oid ankus_function_oid = InvalidOid;
        static bool ankus_enum_supported(Oid type);

        static void
        ankus_check_result_enum(Oid type)
        {
            Oid element = get_element_type(type);
            Oid base = OidIsValid(element) ? getBaseType(element) : type;
            if (get_typtype(base) == TYPTYPE_ENUM && !ankus_enum_supported(base))
                ereport(ERROR, (errcode(ERRCODE_FEATURE_NOT_SUPPORTED),
                    errmsg("SPI enum type OID %u has no generated Ankus conversion", base)));
        }


        static void
        ankus_read_enum(Datum datum, AnkusValue *value, AnkusInputBuffer *owned)
        {
            char *converted;
            owned->serialized = DatumGetCString(DirectFunctionCall1(enum_out, datum));
            converted = pg_server_to_any(owned->serialized, strlen(owned->serialized), PG_UTF8);
            if (converted != owned->serialized)
                owned->converted = converted;
            value->data = (unsigned char *) converted;
            value->length = strlen(converted);
            value->auxiliary1 = -3;
        }

        static Datum
        ankus_write_enum(const AnkusValue *value, Oid type)
        {
            char *terminated = pnstrdup((char *) value->data, value->length);
            char *text = pg_any_to_server(terminated, value->length, PG_UTF8);
            Datum datum = DirectFunctionCall2(enum_in, CStringGetDatum(text), ObjectIdGetDatum(type));
            if (text != terminated)
                pfree(text);
            pfree(terminated);
            return datum;
        }

        static Oid
        ankus_resolve_named_type(const AnkusValue *name, const AnkusValue *schema, bool missing_ok, char kind)
        {
            char *type_name = pg_any_to_server((char *) name->data, name->length, PG_UTF8);
            Oid namespace_oid;
            Oid type;
            if (!schema->is_null)
            {
                char *schema_name = pg_any_to_server((char *) schema->data, schema->length, PG_UTF8);
                namespace_oid = get_namespace_oid(schema_name, missing_ok);
            }
            else
            {
                Oid extension = getExtensionOfObject(ProcedureRelationId, ankus_function_oid);
                if (OidIsValid(extension))
                {
                    HeapTuple tuple = SearchSysCache1(EXTENSIONOID, ObjectIdGetDatum(extension));
                    if (!HeapTupleIsValid(tuple))
                        elog(ERROR, "Could not find owning extension for Ankus function");
                    namespace_oid = ((Form_pg_extension) GETSTRUCT(tuple))->extnamespace;
                    ReleaseSysCache(tuple);
                }
                else
                    namespace_oid = get_func_namespace(ankus_function_oid);
            }

            type = GetSysCacheOid2(TYPENAMENSP, Anum_pg_type_oid,
                CStringGetDatum(type_name), ObjectIdGetDatum(namespace_oid));
            if (!OidIsValid(type) || get_typtype(type) != kind ||
                (kind == TYPTYPE_BASE && get_typlen(type) != -1))
            {
                if (missing_ok)
                    return InvalidOid;
                ereport(ERROR, (errcode(ERRCODE_UNDEFINED_OBJECT),
                    errmsg("PostgreSQL %s type \"%s\" does not exist in the declared schema",
                        kind == TYPTYPE_ENUM ? "enum" : "variable-length base", type_name)));
            }

            return type;
        }

        static Oid
        ankus_resolve_enum(const AnkusValue *name, const AnkusValue *schema, bool missing_ok)
        {
            return ankus_resolve_named_type(name, schema, missing_ok, TYPTYPE_ENUM);
        }

        """;

    /// <summary>
    /// Gets direct catalog operations for the shared guarded backend dispatcher.
    /// </summary>
    internal const string Operations = """
        static void
        ankus_enum_operation(AnkusRequest *request, AnkusResult *result)
        {
            Oid oid;
            if (request->scalar_operation == 4)
            {
                HeapTuple tuple = SearchSysCache1(ENUMOID, ObjectIdGetDatum((Oid) request->parameters[0].value.integral));
                Form_pg_enum entry;
                char *label;
                char *utf8;
                if (!HeapTupleIsValid(tuple))
                    ereport(ERROR, (errcode(ERRCODE_INVALID_BINARY_REPRESENTATION), errmsg("Invalid internal enum value: %u", (Oid) request->parameters[0].value.integral)));
                entry = (Form_pg_enum) GETSTRUCT(tuple);
                result->text.integral = entry->enumtypid;
                memcpy(&result->text.auxiliary1, &entry->enumsortorder, sizeof(float4));
                /* Copy before releasing the syscache pin, including when encoding conversion fails. */
                label = pstrdup(NameStr(entry->enumlabel));
                ReleaseSysCache(tuple);
                utf8 = pg_server_to_any(label, strlen(label), PG_UTF8);
                ankus_copy_owned(&result->text, (unsigned char *) utf8, strlen(utf8));
                return;
            }

            if (request->scalar_operation == 3)
            {
                oid = DatumGetObjectId(ankus_write_enum(&request->parameters[1].value,
                    (Oid) request->parameters[0].value.integral));
            }
            else if (request->scalar_operation == 2)
            {
                oid = get_array_type((Oid) request->parameters[0].value.integral);
                if (!OidIsValid(oid))
                    ereport(ERROR, (errcode(ERRCODE_UNDEFINED_OBJECT), errmsg("Enum array type does not exist")));
            }
            else
                oid = ankus_resolve_enum(&request->parameters[0].value, &request->parameters[1].value,
                    request->scalar_operation == 1);
            result->text.integral = oid;
        }

        """;
}
