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
    /// <param name="name">The SQL name.</param>
    /// <param name="callback">The assembly-specific managed callback symbol.</param>
    /// <param name="managed">The generated managed source.</param>
    /// <param name="native">The generated native source.</param>
    /// <param name="sql">The installation SQL.</param>
    /// <param name="exports">The native linker export list.</param>
    internal static void Emit(IMethodSymbol method, string name, string callback,
        StringBuilder managed, StringBuilder native, StringBuilder sql, StringBuilder exports)
    {
        FunctionType[] parameters = [.. method.Parameters.Select(static parameter => FunctionType.Create(parameter.Type)!)];
        FunctionType result = FunctionType.Create(method.ReturnType)!;
        string nativeName = callback.Replace("ankus_managed_", "ankus_fn_");
        EmitManaged(method, callback, parameters, result, managed);
        EmitNative(nativeName, callback, parameters, result, native);
        string strict = parameters.All(static parameter => !parameter.Nullable) ? " STRICT" : string.Empty;
        sql.AppendLine($"CREATE FUNCTION \"{name}\"({string.Join(", ", parameters.Select((parameter, index) =>
            (method.Parameters[index].IsParams ? "VARIADIC " : string.Empty) + parameter.Sql))})");
        sql.AppendLine($"RETURNS {result.Sql} AS 'MODULE_PATHNAME', '{nativeName}' LANGUAGE c{strict};");
        exports.AppendLine(nativeName);
        exports.AppendLine("pg_finfo_" + nativeName);
    }

    private static void EmitManaged(
        IMethodSymbol method, string callback, FunctionType[] parameters, FunctionType result, StringBuilder source)
    {
        source.AppendLine("    [global::System.Runtime.InteropServices.UnmanagedCallersOnly(");
        source.AppendLine($"        EntryPoint = \"{callback}\",");
        source.AppendLine("        CallConvs = new[] { typeof(global::System.Runtime.CompilerServices.CallConvCdecl) })]");
        source.AppendLine($"    private static int {callback}(");
        source.AppendLine("        global::Ankus.NativeValue* arguments, global::Ankus.NativeValue* result,");
        source.AppendLine("        global::Ankus.NativeCallError* error, nint execute)");
        source.AppendLine("    {");
        source.AppendLine("        nint previous = global::Ankus.NativeBackend.Enter(execute);");
        source.AppendLine("        try");
        source.AppendLine("        {");
        var arguments = new List<string>();
        for (int index = 0; index < parameters.Length; index++)
        {
            FunctionType type = parameters[index];
            string slot = "arguments[" + index.ToString(CultureInfo.InvariantCulture) + "]";
            string numeric = slot + ".ReadNumeric()" + NumericConstraint.Rescale(method.Parameters[index].GetAttributes());
            string value = type.Managed switch
            {
                _ when type.Element is not null => slot + ".ReadArray<" + type.ElementManaged + ">()" +
                    (type.IsVector ? ".ToVector()" : string.Empty),
                "string" => slot + ".ReadString()",
                "byte[]" => slot + ".ReadBytes()",
                "global::System.Guid" => slot + ".ReadGuid()",
                "global::Ankus.PgJson" => slot + ".ReadJson()",
                "global::Ankus.PgJsonb" => slot + ".ReadJsonb()",
                "global::Ankus.PgNumeric" => numeric,
                "decimal" => numeric + ".ToDecimal()",
                "bool" => slot + ".Integral != 0",
                "float" => "global::System.BitConverter.Int32BitsToSingle((int)" + slot + ".Integral)",
                "double" => "global::System.BitConverter.Int64BitsToDouble(" + slot + ".Integral)",
                _ when type.IsTemporal => slot + ".Read" + type.TemporalName + "()" +
                    (type.ClrTemporalName.Length == 0 ? string.Empty : ".To" + type.ClrTemporalName + "()"),
                _ => "(" + type.Managed + ")" + slot + "." + type.Field,
            };
            arguments.Add(type.Nullable ? $"({slot}.IsNull != 0 ? ({type.Managed}?)null : {value})" : value);
        }

        string typeName = method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        string invocation = $"{typeName}.@{method.Name}({string.Join(", ", arguments)})";
        if (result.Managed == "void")
        {
            source.AppendLine($"            {invocation};");
        }
        else
        {
            source.AppendLine($"            var value = {invocation};");
            bool canBeNull = result.Nullable || result.Reference;
            string value = result.Nullable && !result.Reference ? "value.Value" : "value";
            if (canBeNull)
            {
                source.AppendLine("            if (value is null)");
                source.AppendLine("            {");
                source.AppendLine("                result->IsNull = 1;");
                source.AppendLine("                return 0;");
                source.AppendLine("            }");
            }

            string temporalValue = result.IsTemporal && result.ClrTemporalName.Length != 0
                ? $"global::Ankus.Pg{result.TemporalName}.From{result.ClrTemporalName}({value})" : value;
            string numericValue = (result.Managed == "decimal" ? $"global::Ankus.PgNumeric.FromDecimal({value})" : value)
                + NumericConstraint.Rescale(method.GetReturnTypeAttributes());
            source.AppendLine(result.Managed switch
            {
                _ when result.Element is not null => "            *result = global::Ankus.NativeValue.FromArray(" +
                    (result.IsVector ? "new global::Ankus.PgArray<" + result.ElementManaged + ">(" + value + ")" : value) + ");",
                "string" => $"            *result = global::Ankus.NativeValue.FromString({value});",
                "byte[]" => $"            *result = global::Ankus.NativeValue.FromBytes({value});",
                "global::System.Guid" => $"            *result = global::Ankus.NativeValue.FromGuid({value});",
                "global::Ankus.PgJson" or "global::Ankus.PgJsonb" =>
                    $"            *result = global::Ankus.NativeValue.FromString({value}.Text);",
                "global::Ankus.PgNumeric" or "decimal" => $"            *result = global::Ankus.NativeValue.FromString({numericValue}.Text);",
                "bool" => $"            result->Integral = {value} ? 1 : 0;",
                "float" => $"            result->Integral = global::System.BitConverter.SingleToInt32Bits({value});",
                "double" => $"            result->Integral = global::System.BitConverter.DoubleToInt64Bits({value});",
                _ when result.IsTemporal => $"            *result = global::Ankus.NativeValue.From{result.TemporalName}({temporalValue});",
                _ => $"            result->{result.Field} = {value};",
            });
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
        source.AppendLine("            global::Ankus.NativeBackend.Exit(previous);");
        source.AppendLine("        }");
        source.AppendLine("    }");
    }

    private static void EmitNative(string name, string callback, FunctionType[] parameters, FunctionType result, StringBuilder source)
    {
        source.AppendLine($"extern int {callback}(const AnkusValue *, AnkusValue *, AnkusError *, AnkusExecute);");
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
        source.AppendLine("    volatile Datum datum = (Datum) 0;");
        source.AppendLine($"    if (PG_NARGS() != {count})");
        source.AppendLine("    {");
        source.AppendLine("        ereport(ERROR, (errmsg(\"Incorrect argument count for generated Ankus function\")));");
        source.AppendLine("    }");
        for (int index = 0; index < parameters.Length; index++)
        {
            if (!parameters[index].Nullable)
            {
                source.AppendLine($"    if (PG_ARGISNULL({index.ToString(CultureInfo.InvariantCulture)}))");
                source.AppendLine("    {");
                source.AppendLine("        PG_RETURN_NULL();");
                source.AppendLine("    }");
            }
        }

        for (int index = 0; index < parameters.Length; index++)
        {
            FunctionType parameter = parameters[index];
            string argument = index.ToString(CultureInfo.InvariantCulture);
            source.AppendLine($"    arguments[{argument}].is_null = PG_ARGISNULL({argument});");
            source.AppendLine($"    if (!arguments[{argument}].is_null)");
            source.AppendLine("    {");
            if (parameter.Element is not null)
            {
                source.AppendLine($"        ankus_read_array(PG_GETARG_DATUM({argument}), &arguments[{argument}], &owned[{argument}]);");
            }
            else if (parameter.IsTemporal)
            {
                source.AppendLine(
                    $"        ankus_read_temporal(PG_GETARG_DATUM({argument}), &arguments[{argument}], {parameter.BufferOid});");
            }
            else if (parameter.IsBuffer)
            {
                source.AppendLine($"        ankus_read_typed_buffer(PG_GETARG_DATUM({argument}),");
                source.AppendLine($"            &arguments[{argument}], &owned[{argument}], {parameter.BufferOid});");
            }
            else if (parameter.Managed is "float" or "double")
            {
                string bits = parameter.Managed == "float" ? "int32" : "int64";
                source.AppendLine($"        {parameter.Managed} floating = PG_GETARG_{parameter.Reader}({argument});");
                source.AppendLine($"        {bits} bits;");
                source.AppendLine("        memcpy(&bits, &floating, sizeof(bits));");
                source.AppendLine($"        arguments[{argument}].integral = bits;");
            }
            else
            {
                string field = parameter.Field.ToLowerInvariant();
                string cast = parameter.Managed == "sbyte" ? "(int8) " : string.Empty;
                source.AppendLine($"        arguments[{argument}].{field} = {cast}PG_GETARG_{parameter.Reader}({argument});");
            }

            source.AppendLine("    }");
        }

        source.AppendLine($"    status = {callback}(arguments, &result, &error, ankus_spi_execute);");
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
        source.AppendLine("    if (result.is_null)");
        source.AppendLine("    {");
        source.AppendLine("        PG_RETURN_NULL();");
        source.AppendLine("    }");
        source.AppendLine("    PG_TRY();");
        source.AppendLine("    {");
        if (result.Element is not null)
        {
            source.AppendLine($"        datum = ankus_write_array(&result, {result.Element.ScalarOid});");
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
    }
}
