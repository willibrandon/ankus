using System.Text;

namespace Ankus.Generators;

/// <summary>
/// Emits base-type SQL and managed I/O callbacks through the ordinary scalar error boundary.
/// </summary>
internal static class PgTypeEmitter
{
    /// <summary>
    /// Emits the shell, I/O functions, and completed variable-length base type in dependency order.
    /// </summary>
    internal static string Emit(CustomTypeDeclaration type, bool ensureInitialized, StringBuilder managed, StringBuilder native, StringBuilder exports)
    {
        FunctionType custom = FunctionType.Create(type.Type)!;
        FunctionType text = FunctionType.CreateIoBuffer("cstring", "cstring");
        var sql = new StringBuilder("CREATE TYPE " + type.Sql + ";\n");
        EmitFunction("in", "Input", FunctionType.CreateIoBuffer("cstring", "cstring", type.NullInputErrorMessage is not null), custom);
        EmitFunction("out", "Output", custom, text);
        if (type.BinaryProtocol)
        {
            EmitFunction("recv", "Binary", FunctionType.CreateIoBuffer("internal", "type_receive"), custom);
            EmitFunction("send", "Binary", custom, FunctionType.CreateIoBuffer("bytea", "bytea"));
        }

        sql.AppendLine("CREATE TYPE " + type.Sql + " (");
        sql.AppendLine("    INTERNALLENGTH = variable, INPUT = " + type.Function("in") + ", OUTPUT = " + type.Function("out") + ",");
        if (type.BinaryProtocol)
        {
            sql.AppendLine("    RECEIVE = " + type.Function("recv") + ", SEND = " + type.Function("send") + ",");
        }

        sql.AppendLine("    ALIGNMENT = int4, STORAGE = extended);");
        return sql.ToString();

        void EmitFunction(string role, string operation, FunctionType input, FunctionType result)
        {
            string callback = "ankus_managed_" + type.Symbol + "_type_" + role;
            string symbol = type.NativeFunction(role);
            EmitManaged(callback, operation, type, managed);
            PgFunctionEmitter.EmitNative(symbol, callback, [input], result, ensureInitialized, native);
            sql.AppendLine("CREATE FUNCTION " + type.Function(role) + "(" + input.Sql + ") RETURNS " + result.Sql +
                " AS 'MODULE_PATHNAME', '" + symbol + "' LANGUAGE c IMMUTABLE " +
                (role == "in" && type.NullInputErrorMessage is not null ? "CALLED ON NULL INPUT" : "STRICT") + " PARALLEL SAFE;");
            exports.AppendLine(symbol);
            exports.AppendLine("pg_finfo_" + symbol);
        }
    }

    /// <summary>
    /// Runs a closed codec operation with owned diagnostics and deterministic backend-scope restoration.
    /// </summary>
    private static void EmitManaged(string callback, string operation, CustomTypeDeclaration type, StringBuilder source)
    {
        source.AppendLine("    [global::System.Runtime.InteropServices.UnmanagedCallersOnly(");
        source.AppendLine("        EntryPoint = \"" + callback + "\",");
        source.AppendLine("        CallConvs = new[] { typeof(global::System.Runtime.CompilerServices.CallConvCdecl) })]");
        source.AppendLine("    private static int " + callback + "(global::Ankus.NativeValue* arguments, global::Ankus.NativeValue* result,");
        source.AppendLine("        global::Ankus.NativeCallError* error, nint execute, nint memory, nint functionCall)");
        source.AppendLine("    {");
        source.AppendLine("        nint previous = global::Ankus.NativeBackend.Enter(execute);");
        source.AppendLine("        nint previousMemory = 0;");
        source.AppendLine("        bool memoryEntered = false;");
        source.AppendLine("        try");
        source.AppendLine("        {");
        source.AppendLine("            previousMemory = global::Ankus.NativeMemoryContext.Enter(memory);");
        source.AppendLine("            memoryEntered = true;");
        if (operation == "Input" && type.NullInputErrorMessage is { } nullMessage)
        {
            source.AppendLine("            if (arguments[0].IsNull != 0) throw new global::Ankus.PgException(\"22004\", " +
                Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(nullMessage, true) + ");");
        }

        source.AppendLine("            *result = global::Ankus.PgTypeRegistry." + operation + "<" + type.Managed + ">(arguments[0]);");
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
        source.AppendLine();
        source.AppendLine("            global::Ankus.NativeBackend.Exit(previous);");
        source.AppendLine("        }");
        source.AppendLine("    }");
        source.AppendLine();
    }
}
