namespace Ankus.Generators;

/// <summary>
/// Defines typed SPI parameter conversion and native-owned result materialization under the PostgreSQL call guard.
/// </summary>
internal static class NativeSpiBridge
{
    /// <summary>
    /// Gets the SPI transport structures, datum conversion, and allocator-matched result cleanup functions.
    /// </summary>
    internal const string Source = """
        #include "executor/spi.h"
        #include "catalog/pg_type_d.h"
        #include "utils/lsyscache.h"
        #include "utils/memutils.h"

        typedef struct AnkusParameter
        {
            AnkusValue value;
            Oid type_oid;
        } AnkusParameter;

        enum AnkusSpiOperation
        {
            ANKUS_SPI_EXECUTE,
            ANKUS_SPI_PREPARE,
            ANKUS_SPI_EXECUTE_PLAN,
            ANKUS_SPI_FREE_PLAN
        };

        typedef struct AnkusRequest
        {
            const char *command;
            const AnkusParameter *parameters;
            SPIPlanPtr plan;
            int command_length;
            int parameter_count;
            int limit;
            uint8 operation;
            uint8 result_mode;
            uint8 read_only;
        } AnkusRequest;

        typedef struct AnkusColumn
        {
            Oid type_oid;
            Oid base_type_oid;
            AnkusValue name;
        } AnkusColumn;

        typedef struct AnkusResult
        {
            AnkusColumn *columns;
            AnkusValue *values;
            int64 processed;
            int32 row_count;
            int32 column_count;
            void (*release)(struct AnkusResult *);
        } AnkusResult;

        static void
        ankus_release_result(AnkusResult *result)
        {
            if (result->columns != NULL)
            {
                for (int column = 0; column < result->column_count; column++)
                {
                    free(result->columns[column].name.data);
                }
                free(result->columns);
                result->columns = NULL;
            }
            if (result->values != NULL)
            {
                Size count = (Size) result->row_count * result->column_count;
                for (Size index = 0; index < count; index++)
                {
                    free(result->values[index].data);
                }
                free(result->values);
                result->values = NULL;
            }
        }

        static void
        ankus_copy_owned(AnkusValue *value, const unsigned char *data, int length)
        {
            value->data = malloc((Size) length + 1);
            if (value->data == NULL)
            {
                ereport(ERROR, (errcode(ERRCODE_OUT_OF_MEMORY), errmsg("Unable to allocate SPI result buffer")));
            }
            value->length = length;
            memcpy(value->data, data, length);
            value->data[length] = '\0';
        }

        static Datum
        ankus_parameter_datum(const AnkusParameter *parameter)
        {
            const AnkusValue *value = &parameter->value;
            if (value->is_null)
            {
                return (Datum) 0;
            }
            switch (parameter->type_oid)
            {
                case BOOLOID: return BoolGetDatum(value->integral != 0);
                case CHAROID: return CharGetDatum(value->integral);
                case INT2OID: return Int16GetDatum(value->integral);
                case INT4OID: return Int32GetDatum(value->integral);
                case INT8OID: return Int64GetDatum(value->integral);
                case OIDOID: return ObjectIdGetDatum(value->integral);
                case FLOAT4OID:
                {
                    int32 bits = (int32) value->integral;
                    float4 floating;
                    memcpy(&floating, &bits, sizeof(floating));
                    return Float4GetDatum(floating);
                }
                case FLOAT8OID:
                {
                    float8 floating;
                    memcpy(&floating, &value->integral, sizeof(floating));
                    return Float8GetDatum(floating);
                }
                case TEXTOID: return ankus_write_buffer(value, true);
                case BYTEAOID: return ankus_write_buffer(value, false);
                default:
                    ereport(ERROR, (errcode(ERRCODE_FEATURE_NOT_SUPPORTED),
                        errmsg("SPI parameter type OID %u has no Ankus conversion", parameter->type_oid)));
            }
            return (Datum) 0;
        }

        static int
        ankus_run_spi_request(AnkusRequest *request)
        {
            Oid *types = NULL;
            Datum *values = NULL;
            char *nulls = NULL;
            char *sql;
            if (request->operation == ANKUS_SPI_FREE_PLAN)
            {
                SPIPlanPtr plan = request->plan;
                request->plan = NULL;
                return SPI_freeplan(plan);
            }
            if (request->parameter_count > 0)
            {
                types = palloc(sizeof(Oid) * (Size) request->parameter_count);
                values = palloc(sizeof(Datum) * (Size) request->parameter_count);
                nulls = palloc((Size) request->parameter_count);
                for (int index = 0; index < request->parameter_count; index++)
                {
                    types[index] = request->parameters[index].type_oid;
                    if (request->operation != ANKUS_SPI_PREPARE)
                    {
                        values[index] = ankus_parameter_datum(&request->parameters[index]);
                        nulls[index] = request->parameters[index].value.is_null ? 'n' : ' ';
                    }
                }
            }
            if (request->operation == ANKUS_SPI_EXECUTE_PLAN)
            {
                if (SPI_getargcount(request->plan) != request->parameter_count)
                {
                    return SPI_ERROR_PARAM;
                }
                for (int index = 0; index < request->parameter_count; index++)
                {
                    if (SPI_getargtypeid(request->plan, index) != types[index])
                    {
                        return SPI_ERROR_PARAM;
                    }
                }
                return SPI_execute_plan(request->plan, values, nulls, request->read_only != 0, request->limit);
            }
            sql = pg_any_to_server(request->command, request->command_length, PG_UTF8);
            if (request->operation == ANKUS_SPI_PREPARE)
            {
                SPIPlanPtr plan = SPI_prepare(sql, request->parameter_count, types);
                int code;
                if (plan == NULL)
                {
                    return SPI_result;
                }
                code = SPI_keepplan(plan);
                if (code == 0)
                {
                    request->plan = plan;
                }
                return code;
            }
            if (request->parameter_count == 0)
            {
                return SPI_execute(sql, request->read_only != 0, request->limit);
            }
            return SPI_execute_with_args(sql, request->parameter_count, types, values, nulls,
                request->read_only != 0, request->limit);
        }

        static void
        ankus_result_value(Datum datum, Oid type, AnkusValue *value)
        {
            switch (type)
            {
                case BOOLOID: value->integral = DatumGetBool(datum); break;
                case CHAROID: value->integral = (int8) DatumGetChar(datum); break;
                case INT2OID: value->integral = DatumGetInt16(datum); break;
                case INT4OID: value->integral = DatumGetInt32(datum); break;
                case INT8OID: value->integral = DatumGetInt64(datum); break;
                case OIDOID: value->integral = DatumGetObjectId(datum); break;
                case FLOAT4OID:
                {
                    float4 floating = DatumGetFloat4(datum);
                    int32 bits;
                    memcpy(&bits, &floating, sizeof(bits));
                    value->integral = bits;
                    break;
                }
                case FLOAT8OID:
                {
                    float8 floating = DatumGetFloat8(datum);
                    memcpy(&value->integral, &floating, sizeof(floating));
                    break;
                }
                case TEXTOID:
                case VARCHAROID:
                case BPCHAROID:
                case BYTEAOID:
                {
                    AnkusValue input = {0};
                    AnkusInputBuffer owned = {0};
                    ankus_read_buffer(datum, &input, &owned, type != BYTEAOID);
                    ankus_copy_owned(value, input.data, input.length);
                    ankus_free_input(&owned);
                    break;
                }
                default:
                    ereport(ERROR, (errcode(ERRCODE_FEATURE_NOT_SUPPORTED),
                        errmsg("SPI result type OID %u has no Ankus conversion", type)));
            }
        }

        static void
        ankus_collect_result(AnkusResult *result, bool scalar)
        {
            TupleDesc descriptor;
            Size count;
            uint64 rows;
            int columns;
            if (SPI_tuptable == NULL)
            {
                return;
            }
            descriptor = SPI_tuptable->tupdesc;
            rows = scalar ? Min(SPI_processed, 1) : SPI_processed;
            columns = scalar ? Min(descriptor->natts, 1) : descriptor->natts;
            if (rows > PG_INT32_MAX || (columns != 0 && rows > MaxAllocSize / sizeof(AnkusValue) / columns))
            {
                ereport(ERROR, (errcode(ERRCODE_PROGRAM_LIMIT_EXCEEDED), errmsg("SPI result exceeds managed array capacity")));
            }
            result->row_count = (int32) rows;
            result->column_count = columns;
            count = (Size) result->row_count * result->column_count;
            result->columns = calloc(Max(result->column_count, 1), sizeof(AnkusColumn));
            result->values = calloc(Max(count, 1), sizeof(AnkusValue));
            if (result->columns == NULL || result->values == NULL)
            {
                ereport(ERROR, (errcode(ERRCODE_OUT_OF_MEMORY), errmsg("Unable to allocate SPI result")));
            }
            for (int column = 0; column < result->column_count; column++)
            {
                Form_pg_attribute attribute = TupleDescAttr(descriptor, column);
                const char *name = NameStr(attribute->attname);
                char *utf8 = pg_server_to_any(name, strlen(name), PG_UTF8);
                result->columns[column].type_oid = attribute->atttypid;
                result->columns[column].base_type_oid = getBaseType(attribute->atttypid);
                ankus_copy_owned(&result->columns[column].name, (unsigned char *) utf8, strlen(utf8));
                if (utf8 != name)
                {
                    pfree(utf8);
                }
            }
            for (int row = 0; row < result->row_count; row++)
            {
                for (int column = 0; column < result->column_count; column++)
                {
                    bool is_null;
                    Datum datum = SPI_getbinval(SPI_tuptable->vals[row], descriptor, column + 1, &is_null);
                    AnkusValue *value = &result->values[(Size) row * result->column_count + column];
                    value->is_null = is_null;
                    if (!is_null)
                    {
                        ankus_result_value(datum, result->columns[column].base_type_oid, value);
                    }
                }
            }
        }

        """;
}
