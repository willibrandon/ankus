using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Ankus.Generators;

/// <summary>
/// Emits typed managed iterator callbacks and native PostgreSQL set entry points.
/// </summary>
internal static class PgSetEmitter
{
    /// <summary>
    /// Appends a set function's managed callbacks, native wrapper, SQL, and export names.
    /// </summary>
    internal static void Emit(IMethodSymbol method, FunctionDeclaration declaration, SetResult set, string callback,
        StringBuilder managed, StringBuilder native, StringBuilder sql, StringBuilder exports)
    {
        string nativeName = callback.Replace("ankus_managed_", "ankus_fn_");
        managed.AppendLine("    [global::System.Runtime.InteropServices.UnmanagedCallersOnly(");
        managed.AppendLine($"        EntryPoint = \"{callback}\",");
        managed.AppendLine("        CallConvs = new[] { typeof(global::System.Runtime.CompilerServices.CallConvCdecl) })]");
        managed.AppendLine($"    private static int {callback}(int operation, nint* iterator, global::Ankus.NativeValue* arguments,");
        managed.AppendLine("        global::Ankus.NativeValue* columns, global::Ankus.NativeCallError* error, nint execute, nint memory)");
        managed.AppendLine("    {");
        managed.AppendLine("        nint previous = global::Ankus.NativeBackend.Enter(execute, operation == 3);");
        managed.AppendLine("        nint previousMemory = 0;");
        managed.AppendLine("        bool memoryEntered = false;");
        managed.AppendLine("        try");
        managed.AppendLine("        {");
        managed.AppendLine("            previousMemory = global::Ankus.NativeMemoryContext.Enter(memory);");
        managed.AppendLine("            memoryEntered = true;");
        managed.AppendLine("            if (operation is 2 or 3)");
        managed.AppendLine("            {");
        managed.AppendLine("                global::Ankus.NativeSet.Dispose(ref *iterator);");
        managed.AppendLine("                return 0;");
        managed.AppendLine("            }");
        managed.AppendLine();
        managed.AppendLine("            if (operation == 0)");
        managed.AppendLine("            {");
        string arguments = string.Join(", ", method.Parameters.Select((parameter, index) =>
            ManagedConversion.Read(FunctionType.Create(parameter)!, "arguments[" + index.ToString(CultureInfo.InvariantCulture) + "]",
                NumericConstraint.Rescale(parameter.GetAttributes()))));
        managed.AppendLine($"                *iterator = global::Ankus.NativeSet.Create<{set.Managed}>(" +
            method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ".@" + method.Name + "(" + arguments + "));");
        managed.AppendLine("                return 0;");
        managed.AppendLine("            }");
        managed.AppendLine();
        managed.AppendLine($"            if (!global::Ankus.NativeSet.MoveNext<{set.Managed}>(*iterator, out {set.Managed} value))");
        managed.AppendLine("            {");
        managed.AppendLine("                return 2;");
        managed.AppendLine("            }");
        managed.AppendLine();
        for (int index = 0; index < set.Columns.Length; index++)
        {
            FunctionType column = set.Columns[index];
            string ordinal = index.ToString(CultureInfo.InvariantCulture);
            string value = "cell" + ordinal;
            managed.AppendLine($"            {column.Managed}{(column.Nullable ? "?" : string.Empty)} {value} = {set.Values[index]};");
            bool optional = column.Nullable || column.Reference;
            if (optional)
            {
                managed.AppendLine($"            if ({value} is null)");
                managed.AppendLine("            {");
                managed.AppendLine($"                columns[{ordinal}].IsNull = 1;");
                managed.AppendLine("            }");
                managed.AppendLine("            else");
                managed.AppendLine("            {");
            }

            string present = column.Nullable && !column.Reference ? value + ".Value" : value;
            managed.AppendLine((optional ? "                " : "            ") + ManagedConversion.Write(column, present,
                "(columns + " + ordinal + ")", NumericConstraint.Rescale(method.GetReturnTypeAttributes())));
            if (optional)
            {
                managed.AppendLine("            }");
                managed.AppendLine();
            }
        }

        managed.AppendLine("            return 0;");
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
        managed.AppendLine("            global::Ankus.NativeBackend.Exit(previous, operation == 3);");
        managed.AppendLine("        }");
        managed.AppendLine("    }");
        managed.AppendLine();

        AttributeData? attribute = method.GetAttributes().FirstOrDefault(static item => item.AttributeClass?.ToDisplayString() == "Ankus.PgFunctionAttribute");
        int mode = attribute is null ? 0 : AttributeValues.Get(attribute, "SetMode", 0);
        string required = method.Parameters.Length == 0 ? "false" : string.Join(", ", method.Parameters.Select(static parameter =>
            FunctionType.Create(parameter)!.Nullable ? "false" : "true"));
        native.AppendLine($"extern int {callback}(int, void **, const AnkusValue *, AnkusValue *, AnkusError *, AnkusExecute, AnkusMemoryApi *);");
        native.AppendLine($"PG_FUNCTION_INFO_V1({nativeName});");
        native.AppendLine($"PGDLLEXPORT Datum {nativeName}(PG_FUNCTION_ARGS)");
        native.AppendLine("{");
        native.AppendLine($"    const bool required[] = {{ {required} }};");
        native.AppendLine($"    return ankus_set_execute(fcinfo, {callback}, {set.Columns.Length.ToString(CultureInfo.InvariantCulture)}, " +
            $"{method.Parameters.Length.ToString(CultureInfo.InvariantCulture)}, required, {mode.ToString(CultureInfo.InvariantCulture)}, " +
            $"{(set.Columns.Length == 1 && set.Columns[0].IsComposite ? "true" : "false")});");
        native.AppendLine("}");
        native.AppendLine();
        sql.AppendLine($"CREATE {(declaration.Replace ? "OR REPLACE " : string.Empty)}FUNCTION {declaration.QualifiedName}({declaration.Arguments})");
        sql.AppendLine($"RETURNS {set.Sql} AS 'MODULE_PATHNAME', '{nativeName}' LANGUAGE c {declaration.Options};");
        exports.AppendLine(nativeName);
        exports.AppendLine("pg_finfo_" + nativeName);
    }
}
