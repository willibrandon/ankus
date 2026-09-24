using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Ankus.Generators;

/// <summary>
/// Emits aggregate support dispatchers and PostgreSQL aggregate declarations.
/// </summary>
internal static class PgAggregateEmitter
{
    /// <summary>
    /// Emits one support function through the aggregate-specific ownership and context boundary.
    /// </summary>
    /// <param name="helper">The aggregate helper contract.</param>
    /// <param name="callback">The assembly-specific managed callback symbol.</param>
    /// <param name="ensureInitialized">Whether the native entry point must complete deferred managed initialization.</param>
    /// <param name="managed">The generated managed source.</param>
    /// <param name="native">The generated native source.</param>
    /// <param name="exports">The native linker export list.</param>
    /// <returns>The helper's installation SQL.</returns>
    internal static string EmitHelper(AggregateHelper helper, string callback, bool ensureInitialized,
        StringBuilder managed, StringBuilder native, StringBuilder exports)
    {
        string nativeName = callback.Replace("ankus_managed_", "ankus_fn_");
        managed.AppendLine("    [global::System.Runtime.InteropServices.UnmanagedCallersOnly(");
        managed.AppendLine($"        EntryPoint = \"{callback}\",");
        managed.AppendLine("        CallConvs = new[] { typeof(global::System.Runtime.CompilerServices.CallConvCdecl) })]");
        managed.AppendLine($"    private static int {callback}(");
        managed.AppendLine("        global::Ankus.NativeValue* arguments, global::Ankus.NativeValue* result,");
        managed.AppendLine("        global::Ankus.NativeCallError* error, nint execute, global::Ankus.NativeValue* metadata,");
        managed.AppendLine("        int metadataCount, nint owner, nint api, nint memory)");
        managed.AppendLine("    {");
        managed.AppendLine("        nint previous = global::Ankus.NativeBackend.Enter(execute);");
        managed.AppendLine("        nint previousMemory = 0;");
        managed.AppendLine("        bool memoryEntered = false;");
        managed.AppendLine("        try");
        managed.AppendLine("        {");
        managed.AppendLine("            previousMemory = global::Ankus.NativeMemoryContext.Enter(memory);");
        managed.AppendLine("            memoryEntered = true;");
        managed.AppendLine("            global::Ankus.PgAggregateContext context = global::Ankus.NativeAggregate.Enter(");
        managed.AppendLine("                new global::System.ReadOnlySpan<global::Ankus.NativeValue>(metadata, metadataCount), owner, api);");
        managed.AppendLine("            try");
        managed.AppendLine("            {");
        var arguments = new List<string>();
        if (helper.ContextParameter)
        {
            arguments.Add("context");
        }

        for (int index = 0; index < helper.Types.Length; index++)
        {
            arguments.Add(helper.Types[index].Read("arguments[" + index.ToString(CultureInfo.InvariantCulture) + "]", helper.Parameters[index].GetAttributes()));
        }

        string invocation = helper.Method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ".@" + helper.Method.Name +
            "(" + string.Join(", ", arguments) + ")";
        managed.AppendLine("                " + helper.Result.Managed + " value = " + invocation + ";");
        if (helper.Result.IsInternal)
        {
            managed.AppendLine("                *result = global::Ankus.NativeAggregate.Write(value);");
        }
        else
        {
            FunctionType datum = helper.Result.Datum!;
            if (datum.Nullable || datum.Reference)
            {
                managed.AppendLine("                if (value is null)");
                managed.AppendLine("                {");
                managed.AppendLine("                    result->IsNull = 1;");
                managed.AppendLine("                    return 0;");
                managed.AppendLine("                }");
                managed.AppendLine();
            }

            string value = datum.Nullable && !datum.Reference ? "value.Value" : "value";
            managed.AppendLine("                " + ManagedConversion.Write(datum, value, "result", NumericConstraint.Rescale(helper.Method.GetReturnTypeAttributes())));
        }

        managed.AppendLine("                return 0;");
        managed.AppendLine("            }");
        managed.AppendLine("            finally");
        managed.AppendLine("            {");
        managed.AppendLine("                global::Ankus.NativeAggregate.Exit(context);");
        managed.AppendLine("            }");
        managed.AppendLine("        }");
        managed.AppendLine("        catch (global::System.Exception exception)");
        managed.AppendLine("        {");
        managed.AppendLine("            global::Ankus.NativeError.Write(exception, error);");
        managed.AppendLine("            return 1;");
        managed.AppendLine("        }");
        managed.AppendLine("        finally");
        managed.AppendLine("        {");
        managed.AppendLine("            if (memoryEntered)");
        managed.AppendLine("            {");
        managed.AppendLine("                global::Ankus.NativeMemoryContext.Exit(previousMemory);");
        managed.AppendLine("            }");
        managed.AppendLine("            global::Ankus.NativeBackend.Exit(previous);");
        managed.AppendLine("        }");
        managed.AppendLine("    }");
        managed.AppendLine();

        string[] required = [.. helper.Types.Select(static value => value.Nullable ? "false" : "true"), .. helper.Deserialize ? new[] { "false" } : []];
        string[] internalArguments = [.. helper.Types.Select(static value => value.IsInternal ? "true" : "false"), .. helper.Deserialize ? new[] { "false" } : []];
        string count = required.Length.ToString(CultureInfo.InvariantCulture);
        string capacity = Math.Max(1, required.Length).ToString(CultureInfo.InvariantCulture);
        native.AppendLine($"extern int {callback}(const AnkusValue *, AnkusValue *, AnkusError *, AnkusExecute, const AnkusValue *, int, void *, void *, AnkusMemoryApi *);");
        native.AppendLine($"PG_FUNCTION_INFO_V1({nativeName});");
        native.AppendLine($"PGDLLEXPORT Datum {nativeName}(PG_FUNCTION_ARGS)");
        native.AppendLine("{");
        if (ensureInitialized)
        {
            native.AppendLine("    ankus_ensure_initialized();");
        }

        native.AppendLine($"    const bool required[{capacity}] = {{{(required.Length == 0 ? "false" : string.Join(", ", required))}}};");
        native.AppendLine($"    const bool internal_arguments[{capacity}] = {{{(internalArguments.Length == 0 ? "false" : string.Join(", ", internalArguments))}}};");
        native.AppendLine($"    return ankus_aggregate_call(fcinfo, {callback}, {count}, required, internal_arguments, " +
            $"{(helper.Result.IsInternal ? "true" : "false")}, {(helper.Deserialize ? "true" : "false")});");
        native.AppendLine("}");
        native.AppendLine();
        exports.AppendLine(nativeName);
        exports.AppendLine("pg_finfo_" + nativeName);
        return $"CREATE {(helper.Declaration.Replace ? "OR REPLACE " : string.Empty)}FUNCTION {helper.Declaration.QualifiedName}({helper.Arguments})\n" +
            $"RETURNS {helper.Result.Sql} AS 'MODULE_PATHNAME', '{nativeName}' LANGUAGE c {helper.Declaration.Options};\n";
    }

    /// <summary>
    /// Formats the complete aggregate SQL after its support functions have been registered in the graph.
    /// </summary>
    internal static string EmitAggregate(AggregateDeclaration aggregate)
    {
        AggregateHelper transition = aggregate.Helpers["Transition"];
        string inputs = string.Join(", ", transition.Parameters.Skip(1).Select((parameter, index) =>
            (parameter.IsParams ? "VARIADIC " : string.Empty) + SqlText.Identifier(AggregateHelper.ParameterName(parameter)) + " " + aggregate.Inputs[index].Sql));
        string signature;
        if (aggregate.Kind == 0)
        {
            signature = inputs.Length == 0 ? "*" : inputs;
        }
        else
        {
            AggregateHelper? final = aggregate.Helpers.TryGetValue("Final", out AggregateHelper? value) ? value : null;
            string direct = final is null ? string.Empty : string.Join(", ", final.Parameters.Skip(1).Take(aggregate.Direct.Length).Select((parameter, index) =>
                SqlText.Identifier(AggregateHelper.ParameterName(parameter)) + " " + aggregate.Direct[index].Sql));
            signature = (direct.Length == 0 ? string.Empty : direct + " ") + "ORDER BY " + inputs;
        }

        var options = new List<string>
        {
            "SFUNC = " + transition.Declaration.QualifiedName,
            "STYPE = " + transition.Result.Sql,
        };
        AddHelper("Final", "FINALFUNC");
        if (aggregate.FinalExtra)
        {
            options.Add("FINALFUNC_EXTRA");
        }

        options.Add("FINALFUNC_MODIFY = " + Modify(aggregate.FinalModify));
        AddHelper("Combine", "COMBINEFUNC");
        AddHelper("Serialize", "SERIALFUNC");
        AddHelper("Deserialize", "DESERIALFUNC");
        int size = AttributeValues.Get(aggregate.Attribute, "StateSize", 0);
        if (size != 0)
        {
            options.Add("SSPACE = " + size.ToString(CultureInfo.InvariantCulture));
        }

        if (aggregate.Initial is not null)
        {
            options.Add("INITCOND = " + SqlText.Literal(aggregate.Initial));
        }

        if (aggregate.Helpers.TryGetValue("MovingTransition", out AggregateHelper? moving))
        {
            AddHelper("MovingTransition", "MSFUNC");
            AddHelper("MovingInverse", "MINVFUNC");
            options.Add("MSTYPE = " + moving.Result.Sql);
            AddHelper("MovingFinal", "MFINALFUNC");
            if (aggregate.MovingFinalExtra)
            {
                options.Add("MFINALFUNC_EXTRA");
            }

            options.Add("MFINALFUNC_MODIFY = " + Modify(aggregate.MovingFinalModify));
            int movingSize = AttributeValues.Get(aggregate.Attribute, "MovingStateSize", 0);
            if (movingSize != 0)
            {
                options.Add("MSSPACE = " + movingSize.ToString(CultureInfo.InvariantCulture));
            }

            if (aggregate.MovingInitial is not null)
            {
                options.Add("MINITCOND = " + SqlText.Literal(aggregate.MovingInitial));
            }
        }
        else
        {
            if (aggregate.MovingFinalExtra)
            {
                options.Add("MFINALFUNC_EXTRA");
            }

            if (aggregate.Attribute.NamedArguments.Any(static argument => argument.Key == "MovingFinalModify"))
            {
                options.Add("MFINALFUNC_MODIFY = " + Modify(aggregate.MovingFinalModify));
            }
        }

        if (aggregate.SortOperator is not null)
        {
            options.Add("SORTOP = " + aggregate.SortOperator);
        }

        options.Add("PARALLEL = " + (aggregate.Parallel switch { 1 => "RESTRICTED", 2 => "SAFE", _ => "UNSAFE" }));
        if (aggregate.Kind == 2)
        {
            options.Add("HYPOTHETICAL");
        }

        return $"CREATE AGGREGATE {aggregate.QualifiedName}({signature}) (\n    " + string.Join(",\n    ", options) + "\n);\n";

        void AddHelper(string role, string option)
        {
            if (aggregate.Helpers.TryGetValue(role, out AggregateHelper? helper))
            {
                options.Add(option + " = " + helper.Declaration.QualifiedName);
            }
        }
    }

    private static string Modify(int value) => value switch { 2 => "SHAREABLE", 3 => "READ_WRITE", _ => "READ_ONLY" };
}
