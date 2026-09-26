namespace Ankus.Build;

internal static partial class NativeBindingRecordCSharp
{
    private sealed partial class Writer
    {
        private readonly Dictionary<int, string> _emptyValues = [];

        /// <summary>
        /// Emits typed managed calls and pure native address imports beside their shared record declarations.
        /// </summary>
        private void Methods(IReadOnlyList<NativeBindingCall> selected)
        {
            Summary("Calls selected native PostgreSQL functions through the active callback's error guard.");
            Line("public static partial class NativeMethods\n{");
            var members = new HashSet<string>(selected.Select(static call => call.Name), StringComparer.Ordinal) { "NativeMethods" };
            foreach (NativeBindingCall call in selected)
            {
                // A parameterless Finalize declaration has special C# destructor diagnostics even on a static class.
                string method = call.Name == "NativeMethods" || call is { Name: "Finalize", Parameters.Count: 0 }
                    ? Unique(members, "Native_" + call.Name) : call.Name;
                string accessor = Unique(members, "GetNativeBody_" + call.Name);
                Method(call, method, accessor);
                Summary("Obtains a native body address without entering PostgreSQL.", "    ");
                Line($"    [global::System.Runtime.InteropServices.LibraryImport(\"Ankus.NativeBodies\", EntryPoint = \"{NativeBindingCallImports.Prefix}{call.Name}\")]");
                Line("    [global::System.Runtime.InteropServices.UnmanagedCallConv(CallConvs = [typeof(global::System.Runtime.CompilerServices.CallConvCdecl)])]");
                Line($"    private static partial nint @{accessor}();\n");
            }

            Line("}\n");
            foreach (string name in _emptyValues.Values)
            {
                Summary("Represents the unique logical value of a native zero-size record without transporting CLR storage bytes.");
                Line($"public readonly struct @{name}\n{{\n}}\n");
            }
        }

        /// <summary>
        /// Copies exact value bytes into independently aligned native storage and releases owned storage on every exit.
        /// </summary>
        private void Method(NativeBindingCall call, string method, string accessor)
        {
            NativeBindingCallFrame frame = NativeBindingCallFrameLayout.Create(graph, call);
            var names = new HashSet<string>(StringComparer.Ordinal) { "allocation", "storage", "arguments", "alignment" };
            var parameters = new List<(string Name, Value Value)>();
            for (int index = 0; index < call.Parameters.Count; index++)
            {
                string observed = index < call.Symbol.ParameterNames.Count ? call.Symbol.ParameterNames[index] : "";
                string name = Unique(names, observed.Length == 0 ? "argument" + Number(index) : observed);
                parameters.Add((name, CallValue(call.Parameters[index])));
            }

            Value? result = call.Result is NativeBindingCallValue nativeResult ? CallValue(nativeResult) : null;
            Summary("Invokes " + call.Symbol.NativeName + " beneath the active PostgreSQL error guard.", "    ");
            foreach ((string name, _) in parameters)
            {
                Line($"    /// <param name=\"{name}\">The exact native value; referenced addresses must remain valid for the complete call.</param>");
            }

            if (result is not null) { Line("    /// <returns>The exact native result. Referenced storage retains its native ownership and lifetime.</returns>"); }

            Line("    /// <remarks>Requires the matching native binding and an active backend callback. Raw pointers and callback addresses remain the caller's responsibility.</remarks>");
            string signature = string.Join(", ", parameters.Select(static parameter => parameter.Value.Code + " @" + parameter.Name));
            string unsafeModifier = frame.AllocationSize == 0 ? "" : "unsafe ";
            string hide = parameters.Count == 0 && method != "Equals" ? Hide(method) : "";
            Line($"    public {hide}static {unsafeModifier}{result?.Code ?? "void"} @{method}({signature})\n    {{");
            Line($"        global::Ankus.NativeRawCall.ValidateBinding(\"__ANKUS_RECORD_IDENTITY__\"u8, {Number(graph.Target.PostgresVersion / 10000)});");
            if (frame.AllocationSize == 0)
            {
                Line($"        global::Ankus.NativeRawCall.Invoke(@{accessor}(), [], 0, 0);\n    }}\n");
                return;
            }

            bool heap = frame.AllocationSize > 4096;
            Line(heap ? $"        byte* allocation = (byte*)global::System.Runtime.InteropServices.NativeMemory.Alloc({NativeSize(frame.AllocationSize)});"
                : $"        byte* allocation = stackalloc byte[{Number(frame.AllocationSize)}];");
            if (heap) { Line("        try\n        {"); }

            string indent = heap ? "            " : "        ";
            Line($"{indent}nuint alignment = {NativeSize(frame.Alignment - 1)};");
            Line($"{indent}nuint storage = checked((nuint)allocation + alignment) & ~alignment;");
            if (parameters.Count != 0)
            {
                Line($"{indent}global::System.Span<global::Ankus.NativeCallArgument> arguments = new((void*)storage, {Number(parameters.Count)});");
                for (int index = 0; index < parameters.Count; index++)
                {
                    (string name, Value value) = parameters[index];
                    string address = "storage + " + NativeSize(frame.Arguments[index]);
                    Line($"{indent}arguments[{Number(index)}] = new((nint)({address}), {NativeSize(value.Size)});");
                    if (value.Size != 0)
                    {
                        Line($"{indent}global::System.Runtime.CompilerServices.Unsafe.WriteUnaligned((void*)({address}), @{name});");
                    }
                }
            }

            string resultAddress = result is null ? "0" : "(nint)(storage + " + NativeSize(frame.Result) + ")";
            Line($"{indent}global::Ankus.NativeRawCall.Invoke(@{accessor}(), {(parameters.Count == 0 ? "[]" : "arguments")}, {resultAddress}, {NativeSize(result?.Size ?? 0)});");
            if (result is not null)
            {
                Line(result.Size == 0 ? indent + "return default;"
                    : $"{indent}return global::System.Runtime.CompilerServices.Unsafe.ReadUnaligned<{result.Code}>((void*)(storage + {NativeSize(frame.Result)}));");
            }

            if (heap) { Line("        }\n        finally { global::System.Runtime.InteropServices.NativeMemory.Free(allocation); }"); }

            Line("    }\n");
        }

        /// <summary>
        /// Maps actual native bytes, using a distinct logical token for complete records with no bytes.
        /// </summary>
        private Value CallValue(NativeBindingCallValue value)
        {
            NativeRecordType type = Canonical(value.StorageType);
            if (type.Size == 0 && type.Declaration is int declaration && type.Kind == "record")
            {
                if (!_emptyValues.TryGetValue(declaration, out string? name))
                {
                    name = Unique(_names, _declarations[declaration] + "Value");
                    _emptyValues.Add(declaration, name);
                }

                return new("@" + name, 0, null);
            }

            Value mapped = Map(value.StorageType);
            if (mapped.Size != graph.Types[value.StorageType].Size)
            {
                throw new FormatException("A managed native call requires the exact declared object size.");
            }

            return mapped;
        }

        /// <summary>
        /// Emits a size already proved representable by the selected target, including 64-bit frame offsets.
        /// </summary>
        private static string NativeSize(long value) => value <= uint.MaxValue ? Number(value) + "U" : "unchecked((nuint)" + Number(value) + "UL)";
    }
}
