namespace Ankus.Generators;

/// <summary>
/// Emits native namespace and regtype lookups inside the guarded temporary operation context.
/// </summary>
internal static class NativeLookupBridge
{
    /// <summary>
    /// Gets selected-header name construction and exact native catalog lookup operations.
    /// </summary>
    internal const string Source = """
        #include "catalog/namespace.h"
        #include "nodes/value.h"

        static void
        ankus_lookup_operation(AnkusRequest *request, AnkusResult *result)
        {
            if (request->scalar_operation == 0 && request->parameter_count == 1)
            {
                result->text.integral = DatumGetObjectId(DirectFunctionCall1(regtypein,
                    ankus_write_cstring(&request->parameters[0].value)));
                return;
            }

            if (request->scalar_operation == 1 && request->parameter_count >= 2)
            {
                List *names = NIL;
                for (int index = 2; index < request->parameter_count; index++)
                {
                    char *component = DatumGetCString(ankus_write_cstring(&request->parameters[index].value));
                    names = lappend(names, makeString(component));
                }

                result->text.integral = OpernameGetOprid(names,
                    (Oid) request->parameters[0].value.integral, (Oid) request->parameters[1].value.integral);
                return;
            }

            ereport(ERROR, (errcode(ERRCODE_INVALID_PARAMETER_VALUE), errmsg("invalid catalog lookup request")));
        }

        """;
}
