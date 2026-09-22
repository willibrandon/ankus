using System.Text;
using Microsoft.CodeAnalysis;

namespace Ankus.Generators;

/// <summary>
/// Emits managed trigger callbacks and PostgreSQL trigger function entry points.
/// </summary>
internal static class PgTriggerEmitter
{
    /// <summary>
    /// Appends trigger callback dispatch, the native boundary, SQL, and linker exports.
    /// </summary>
    /// <param name="method">The validated trigger method.</param>
    /// <param name="declaration">The common SQL declaration options.</param>
    /// <param name="callback">The assembly-specific managed callback symbol.</param>
    /// <param name="managed">The generated managed source.</param>
    /// <param name="native">The generated native source.</param>
    /// <param name="sql">The installation SQL.</param>
    /// <param name="exports">The native linker export list.</param>
    internal static void Emit(IMethodSymbol method, FunctionDeclaration declaration, string callback,
        StringBuilder managed, StringBuilder native, StringBuilder sql, StringBuilder exports)
    {
        string nativeName = callback.Replace("ankus_managed_", "ankus_fn_");
        managed.AppendLine("    [global::System.Runtime.InteropServices.UnmanagedCallersOnly(");
        managed.AppendLine($"        EntryPoint = \"{callback}\",");
        managed.AppendLine("        CallConvs = new[] { typeof(global::System.Runtime.CompilerServices.CallConvCdecl) })]");
        managed.AppendLine($"    private static int {callback}(");
        managed.AppendLine("        global::Ankus.NativeValue* arguments, global::Ankus.NativeValue* result,");
        managed.AppendLine("        global::Ankus.NativeCallError* error, nint execute)");
        managed.AppendLine("    {");
        managed.AppendLine("        nint previous = global::Ankus.NativeBackend.Enter(execute);");
        managed.AppendLine("        try");
        managed.AppendLine("        {");
        managed.AppendLine("            global::Ankus.PgTriggerContext context = global::Ankus.NativeValue.ReadTriggerContext(");
        managed.AppendLine("                new global::System.ReadOnlySpan<global::Ankus.NativeValue>(arguments, 12));");
        managed.AppendLine("            global::Ankus.PgHeapTuple? value = " +
            method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ".@" + method.Name + "(context);");
        managed.AppendLine("            if (context.Timing == global::Ankus.PgTriggerTiming.After)");
        managed.AppendLine("            {");
        managed.AppendLine("                result->IsNull = 1;");
        managed.AppendLine("                return 0;");
        managed.AppendLine("            }");
        managed.AppendLine();
        managed.AppendLine("            if (context.Level == global::Ankus.PgTriggerLevel.Statement)");
        managed.AppendLine("            {");
        managed.AppendLine("                if (value is not null)");
        managed.AppendLine("                {");
        managed.AppendLine("                    throw new global::Ankus.PgException(\"39P01\", \"A BEFORE STATEMENT trigger must return null.\");");
        managed.AppendLine("                }");
        managed.AppendLine();
        managed.AppendLine("                result->IsNull = 1;");
        managed.AppendLine("                return 0;");
        managed.AppendLine("            }");
        managed.AppendLine();
        managed.AppendLine("            if (value is null)");
        managed.AppendLine("            {");
        managed.AppendLine("                result->IsNull = 1;");
        managed.AppendLine("                return 0;");
        managed.AppendLine("            }");
        managed.AppendLine();
        managed.AppendLine("            if (context.Operation == global::Ankus.PgTriggerOperation.Delete)");
        managed.AppendLine("            {");
        managed.AppendLine("                result->IsNull = 0;");
        managed.AppendLine("                return 0;");
        managed.AppendLine("            }");
        managed.AppendLine();
        managed.AppendLine("            *result = global::Ankus.NativeValue.FromTuple(value);");
        managed.AppendLine("            return 0;");
        managed.AppendLine("        }");
        managed.AppendLine("        catch (global::System.Exception exception)");
        managed.AppendLine("        {");
        managed.AppendLine("            global::Ankus.NativeError.Write(exception, error);");
        managed.AppendLine("            return 1;");
        managed.AppendLine("        }");
        managed.AppendLine("        finally");
        managed.AppendLine("        {");
        managed.AppendLine("            global::Ankus.NativeBackend.Exit(previous);");
        managed.AppendLine("        }");
        managed.AppendLine("    }");
        managed.AppendLine();
        native.AppendLine($"extern int {callback}(const AnkusValue *, AnkusValue *, AnkusError *, AnkusExecute);");
        native.AppendLine($"PG_FUNCTION_INFO_V1({nativeName});");
        native.AppendLine($"PGDLLEXPORT Datum {nativeName}(PG_FUNCTION_ARGS)");
        native.AppendLine("{");
        native.AppendLine($"    return ankus_trigger_call(fcinfo, {callback});");
        native.AppendLine("}");
        native.AppendLine();
        sql.AppendLine($"CREATE {(declaration.Replace ? "OR REPLACE " : string.Empty)}FUNCTION {declaration.QualifiedName}()");
        sql.AppendLine($"RETURNS trigger AS 'MODULE_PATHNAME', '{nativeName}' LANGUAGE c {declaration.Options};");
        exports.AppendLine(nativeName);
        exports.AppendLine("pg_finfo_" + nativeName);
    }
}
