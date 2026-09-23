using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Ankus.Generators;

/// <summary>
/// Emits managed dispatch, native conversion and cleanup, and SQL declarations for one supported function.
/// </summary>
internal static class PgFunctionEmitter
{
    /// <summary>
    /// Appends a function's complete boundary and installation declarations to the extension artifacts.
    /// </summary>
    /// <param name="method">The attributed managed method.</param>
    /// <param name="parameterModels">The validated managed parameters and their SQL slots.</param>
    /// <param name="declaration">The validated SQL declaration.</param>
    /// <param name="callback">The assembly-specific managed callback symbol.</param>
    /// <param name="managed">The generated managed source.</param>
    /// <param name="native">The generated native source.</param>
    /// <param name="sql">The installation SQL.</param>
    /// <param name="exports">The native linker export list.</param>
    internal static void Emit(IMethodSymbol method, FunctionParameter[] parameterModels, FunctionDeclaration declaration, string callback,
        StringBuilder managed, StringBuilder native, StringBuilder sql, StringBuilder exports)
    {
        FunctionType[] parameters = [.. parameterModels.Where(static parameter => !parameter.IsMemoryContext).Select(static parameter => parameter.Type!)];
        FunctionType result = FunctionType.CreateResult(method)!;
        string nativeName = callback.Replace("ankus_managed_", "ankus_fn_");
        EmitManaged(method, callback, parameterModels, result, managed);
        EmitNative(nativeName, callback, parameters, result, native);
        sql.AppendLine($"CREATE {(declaration.Replace ? "OR REPLACE " : string.Empty)}FUNCTION {declaration.QualifiedName}({declaration.Arguments})");
        sql.AppendLine($"RETURNS {result.Sql} AS 'MODULE_PATHNAME', '{nativeName}' LANGUAGE c {declaration.Options};");
        exports.AppendLine(nativeName);
        exports.AppendLine("pg_finfo_" + nativeName);
    }

    private static void EmitManaged(
        IMethodSymbol method, string callback, FunctionParameter[] parameters, FunctionType result, StringBuilder source)
    {
        source.AppendLine("    [global::System.Runtime.InteropServices.UnmanagedCallersOnly(");
        source.AppendLine($"        EntryPoint = \"{callback}\",");
        source.AppendLine("        CallConvs = new[] { typeof(global::System.Runtime.CompilerServices.CallConvCdecl) })]");
        source.AppendLine($"    private static int {callback}(");
        source.AppendLine("        global::Ankus.NativeValue* arguments, global::Ankus.NativeValue* result,");
        source.AppendLine("        global::Ankus.NativeCallError* error, nint execute, nint memory)");
        source.AppendLine("    {");
        source.AppendLine("        nint previous = global::Ankus.NativeBackend.Enter(execute);");
        source.AppendLine("        nint previousMemory = 0;");
        source.AppendLine("        bool memoryEntered = false;");
        source.AppendLine("        try");
        source.AppendLine("        {");
        source.AppendLine("            previousMemory = global::Ankus.NativeMemoryContext.Enter(memory);");
        source.AppendLine("            memoryEntered = true;");
        IEnumerable<string> arguments = parameters.Select(static parameter => parameter.ReadExpression());

        string typeName = method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        string invocation = $"{typeName}.@{method.Name}({string.Join(", ", arguments)})";
        if (result.Managed == "void")
        {
            source.AppendLine($"            {invocation};");
        }
        else
        {
            source.AppendLine($"            {result.Managed}{(result.Nullable ? "?" : string.Empty)} value = {invocation};");
            bool canBeNull = result.Nullable || result.Reference;
            string value = result.Nullable && !result.Reference ? "value.Value" : "value";
            if (canBeNull)
            {
                source.AppendLine("            if (value is null)");
                source.AppendLine("            {");
                source.AppendLine("                result->IsNull = 1;");
                source.AppendLine("                return 0;");
                source.AppendLine("            }");
                source.AppendLine();
            }

            source.AppendLine("            " + ManagedConversion.Write(result, value, "result", NumericConstraint.Rescale(method.GetReturnTypeAttributes())));
        }

        source.AppendLine("            return 0;");
        source.AppendLine("        }");
        source.AppendLine("        catch (global::System.Exception exception)");
        source.AppendLine("        {");
        source.AppendLine("            global::Ankus.NativeError.Write(exception, error);");
        source.AppendLine("            return 1;");
        source.AppendLine("        }");
        source.AppendLine("        finally");
        source.AppendLine("        {");
        source.AppendLine("            if (memoryEntered)");
        source.AppendLine("            {");
        source.AppendLine("                global::Ankus.NativeMemoryContext.Exit(previousMemory);");
        source.AppendLine("            }");
        source.AppendLine("            global::Ankus.NativeBackend.Exit(previous);");
        source.AppendLine("        }");
        source.AppendLine("    }");
        source.AppendLine();
    }

    private static void EmitNative(string name, string callback, FunctionType[] parameters, FunctionType result, StringBuilder source)
    {
        source.AppendLine($"extern int {callback}(const AnkusValue *, AnkusValue *, AnkusError *, AnkusExecute, AnkusMemoryApi *);");
        source.AppendLine($"PG_FUNCTION_INFO_V1({name});");
        source.AppendLine($"PGDLLEXPORT Datum {name}(PG_FUNCTION_ARGS)");
        source.AppendLine("{");
        string count = parameters.Length.ToString(CultureInfo.InvariantCulture);
        string capacity = Math.Max(1, parameters.Length).ToString(CultureInfo.InvariantCulture);
        bool hasBuffers = parameters.Any(static parameter => parameter.IsBuffer);
        source.AppendLine($"    AnkusValue arguments[{capacity}] = {{0}};");
        if (hasBuffers)
        {
            source.AppendLine($"    AnkusInputBuffer owned[{capacity}] = {{0}};");
        }

        source.AppendLine("    AnkusValue result = {0};");
        source.AppendLine("    AnkusError error = {0};");
        source.AppendLine("    int status;");
        source.AppendLine("    AnkusMemoryApi memory = {0};");
        source.AppendLine("    ankus_memory_initialize(&memory);");
        source.AppendLine("    Oid previous_function;");
        source.AppendLine("    volatile Datum datum = (Datum) 0;");
        source.AppendLine($"    if (PG_NARGS() != {count})");
        source.AppendLine("    {");
        source.AppendLine("        ereport(ERROR, (errmsg(\"Incorrect argument count for generated Ankus function\")));");
        source.AppendLine("    }");
        source.AppendLine();
        for (int index = 0; index < parameters.Length; index++)
        {
            if (!parameters[index].Nullable)
            {
                source.AppendLine($"    if (PG_ARGISNULL({index.ToString(CultureInfo.InvariantCulture)}))");
                source.AppendLine("    {");
                source.AppendLine("        PG_RETURN_NULL();");
                source.AppendLine("    }");
                source.AppendLine();
            }
        }

        bool compositeArguments = parameters.Any(static parameter => (parameter.Element ?? parameter).IsComposite);
        string inputIndent = compositeArguments ? "    " : string.Empty;
        if (compositeArguments)
        {
            source.AppendLine("    previous_function = ankus_function_oid;");
            source.AppendLine("    ankus_function_oid = fcinfo->flinfo->fn_oid;");
            source.AppendLine("    PG_TRY();");
            source.AppendLine("    {");
        }

        for (int index = 0; index < parameters.Length; index++)
        {
            FunctionType parameter = parameters[index];
            string argument = index.ToString(CultureInfo.InvariantCulture);
            source.AppendLine($"{inputIndent}    arguments[{argument}].is_null = PG_ARGISNULL({argument});");
            source.AppendLine($"{inputIndent}    if (!arguments[{argument}].is_null)");
            source.AppendLine(inputIndent + "    {");
            if (parameter.Element is not null)
            {
                source.AppendLine($"{inputIndent}        ankus_read_array(PG_GETARG_DATUM({argument}), &arguments[{argument}], &owned[{argument}]);");
            }
            else if (parameter.RangeSubtype is not null)
            {
                source.AppendLine($"{inputIndent}        ankus_read_range(PG_GETARG_DATUM({argument}), &arguments[{argument}], &owned[{argument}]);");
            }
            else if (parameter.Enumeration is not null)
            {
                source.AppendLine($"{inputIndent}        ankus_read_enum(PG_GETARG_DATUM({argument}), &arguments[{argument}], &owned[{argument}]);");
            }
            else if (parameter.IsComposite)
            {
                source.AppendLine($"{inputIndent}        ankus_read_tuple(PG_GETARG_DATUM({argument}), &arguments[{argument}], &owned[{argument}]);");
            }
            else if (parameter.IsTemporal)
            {
                source.AppendLine(
                    $"{inputIndent}        ankus_read_temporal(PG_GETARG_DATUM({argument}), &arguments[{argument}], {parameter.BufferOid});");
            }
            else if (parameter.IsBuffer)
            {
                source.AppendLine($"{inputIndent}        ankus_read_typed_buffer(PG_GETARG_DATUM({argument}),");
                source.AppendLine($"{inputIndent}            &arguments[{argument}], &owned[{argument}], {parameter.BufferOid});");
            }
            else if (parameter.Managed is "float" or "double")
            {
                string bits = parameter.Managed == "float" ? "int32" : "int64";
                source.AppendLine($"{inputIndent}        {parameter.Managed} floating = PG_GETARG_{parameter.Reader}({argument});");
                source.AppendLine($"{inputIndent}        {bits} bits;");
                source.AppendLine(inputIndent + "        memcpy(&bits, &floating, sizeof(bits));");
                source.AppendLine($"{inputIndent}        arguments[{argument}].integral = bits;");
            }
            else
            {
                string field = parameter.Field.ToLowerInvariant();
                string cast = parameter.Managed == "sbyte" ? "(int8) " : string.Empty;
                source.AppendLine($"{inputIndent}        arguments[{argument}].{field} = {cast}PG_GETARG_{parameter.Reader}({argument});");
            }

            source.AppendLine(inputIndent + "    }");
            source.AppendLine();
        }

        if (compositeArguments)
        {
            source.AppendLine("    }");
            source.AppendLine("    PG_FINALLY();");
            source.AppendLine("    {");
            source.AppendLine("        ankus_function_oid = previous_function;");
            source.AppendLine("    }");
            source.AppendLine("    PG_END_TRY();");
        }

        source.AppendLine("    previous_function = ankus_function_oid;");
        source.AppendLine("    ankus_function_oid = fcinfo->flinfo->fn_oid;");
        source.AppendLine($"    status = {callback}(arguments, &result, &error, ankus_spi_execute, &memory);");
        source.AppendLine("    ankus_function_oid = previous_function;");
        if (hasBuffers)
        {
            for (int index = 0; index < parameters.Length; index++)
            {
                source.AppendLine($"    ankus_free_input(&owned[{index.ToString(CultureInfo.InvariantCulture)}]);");
            }
        }

        source.AppendLine("    if (status != 0)");
        source.AppendLine("    {");
        source.AppendLine("        ankus_raise_error(&error);");
        source.AppendLine("    }");
        source.AppendLine();
        source.AppendLine("    if (result.is_null)");
        source.AppendLine("    {");
        if (result.IsComposite)
        {
            source.AppendLine("        AnkusParameter parameter = {0};");
            source.AppendLine("        parameter.type_oid = get_func_rettype(fcinfo->flinfo->fn_oid);");
            source.AppendLine("        parameter.value.is_null = true;");
            source.AppendLine("        (void) ankus_parameter_datum(&parameter);");
        }

        source.AppendLine("        PG_RETURN_NULL();");
        source.AppendLine("    }");
        source.AppendLine();
        source.AppendLine("    PG_TRY();");
        source.AppendLine("    {");
        if (result.Element is not null)
        {
            source.AppendLine($"        datum = ankus_write_array(&result, {(result.Element.Enumeration is null && !result.Element.IsComposite ? result.Element.ScalarOid : "get_element_type(get_func_rettype(fcinfo->flinfo->fn_oid))")});");
        }
        else if (result.RangeSubtype is not null)
        {
            source.AppendLine($"        datum = ankus_write_range(&result, {result.BufferOid});");
        }
        else if (result.Enumeration is not null)
        {
            source.AppendLine("        datum = ankus_write_enum(&result, get_func_rettype(fcinfo->flinfo->fn_oid));");
        }
        else if (result.IsComposite)
        {
            source.AppendLine("        TupleDesc descriptor = NULL;");
            source.AppendLine("        (void) get_call_result_type(fcinfo, NULL, &descriptor);");
            source.AppendLine("        datum = ankus_write_tuple(&result, get_func_rettype(fcinfo->flinfo->fn_oid), descriptor);");
        }
        else if (result.IsTemporal)
        {
            source.AppendLine($"        datum = ankus_write_temporal(&result, {result.BufferOid});");
        }
        else if (result.IsBuffer)
        {
            source.AppendLine($"        datum = ankus_write_typed_buffer(&result, {result.BufferOid});");
        }
        else if (result.Managed is "float" or "double")
        {
            string bits = result.Managed == "float" ? "int32" : "int64";
            source.AppendLine($"        {bits} bits = ({bits}) result.integral;");
            source.AppendLine($"        {result.Managed} floating;");
            source.AppendLine("        memcpy(&floating, &bits, sizeof(bits));");
            source.AppendLine($"        datum = {result.Writer}GetDatum(floating);");
        }
        else if (result.Managed != "void")
        {
            source.AppendLine($"        datum = {result.Writer}GetDatum(result.{result.Field.ToLowerInvariant()});");
        }

        source.AppendLine("    }");
        source.AppendLine("    PG_FINALLY();");
        source.AppendLine("    {");
        source.AppendLine("        if (result.release != NULL)");
        source.AppendLine("        {");
        source.AppendLine("            result.release(result.data);");
        source.AppendLine("        }");
        source.AppendLine("    }");
        source.AppendLine("    PG_END_TRY();");
        source.AppendLine("    return datum;");
        source.AppendLine("}");
        source.AppendLine();
    }
}
