namespace Ankus.Build;

internal static partial class NativeBindingRecordCSharp
{
    private sealed partial class Writer
    {
        /// <summary>
        /// Exposes actual native objects through typed guarded properties and explicit dangerous address access.
        /// </summary>
        private void Globals(IReadOnlyList<NativeBindingGlobalContract> selected)
        {
            Summary("Accesses selected native PostgreSQL globals through the active callback's error guard.");
            Line("public static partial class NativeGlobals\n{");
            var members = new HashSet<string>(selected.Select(static global => global.Name), StringComparer.Ordinal) { "NativeGlobals" };
            foreach (NativeBindingGlobalContract global in selected)
            {
                string member = global.Name == "NativeGlobals" ? Unique(members, "Native_" + global.Name) : global.Name;
                string hide = member == "Finalize" ? "" : Hide(member);
                if (global.IsComplete)
                {
                    var native = new NativeBindingCallValue(global.Symbol.Type, global.StorageType);
                    Value value = CallValue(native);
                    string read = Unique(members, "Read_" + global.Name);
                    string readAccessor = Unique(members, "GetNativeRead_" + global.Name);
                    string? write = global.CanWrite ? Unique(members, "Write_" + global.Name) : null;
                    Summary("Reads" + (write is null ? "" : " or writes") + " the current native value of " + global.Symbol.NativeName + ".", "    ");
                    Line("    /// <remarks>Requires an active backend callback. Values are copies; referenced native addresses retain their original ownership and lifetime. Native synchronization remains the caller's responsibility.</remarks>");
                    if (write is null)
                    {
                        Line($"    public {hide}static {value.Code} @{member} => @{read}();\n");
                    }
                    else
                    {
                        Line($"    public {hide}static {value.Code} @{member}\n    {{\n        get => @{read}();\n        set => @{write}(value);\n    }}\n");
                    }

                    Method(new(global.Name, global.Symbol, [], native), read, readAccessor, "private");
                    GlobalImport(global.Name, NativeBindingGlobalOperation.Read, readAccessor);
                    if (write is not null)
                    {
                        string writeAccessor = Unique(members, "GetNativeWrite_" + global.Name);
                        Method(new(global.Name, global.Symbol, [native], null), write, writeAccessor, "private");
                        GlobalImport(global.Name, NativeBindingGlobalOperation.Write, writeAccessor);
                    }
                }

                string address = Unique(members, "DangerousAddressOf_" + global.Name);
                string addressAccessor = Unique(members, "GetNativeAddress_" + global.Name);
                Summary("Obtains the original native address of " + global.Symbol.NativeName + ".", "    ");
                Line("    /// <returns>The original object address, without extending its lifetime or supplying an unknown array extent.</returns>");
                Line("    /// <remarks>Requires an active backend callback. The caller must preserve native const/volatile qualifications, bounds, synchronization and ownership. Thread-local addresses belong to the active backend thread.</remarks>");
                Line($"    public static unsafe nint @{address}()\n    {{");
                Line($"        global::Ankus.CompilerServices.NativeRawCall.ValidateBinding(\"__ANKUS_RECORD_IDENTITY__\"u8, {Number(graph.Target.PostgresVersion / 10000)});");
                Line("        nint address = 0;");
                Line($"        global::Ankus.CompilerServices.NativeRawCall.Invoke(@{addressAccessor}(), [], (nint)(&address), (nuint)sizeof(nint));");
                Line("        return address;\n    }\n");
                GlobalImport(global.Name, NativeBindingGlobalOperation.Address, addressAccessor);
            }

            Line("}\n");
        }

        /// <summary>
        /// Imports a pure address accessor in the disjoint native global-body namespace.
        /// </summary>
        private void GlobalImport(string name, NativeBindingGlobalOperation operation, string accessor)
        {
            string entry = NativeBindingGlobalImports.Prefix + NativeBindingGlobalSource.OperationName(operation) + "_" + name;
            Summary("Obtains a native global body address without entering PostgreSQL.", "    ");
            Line($"    [global::System.Runtime.InteropServices.LibraryImport(\"Ankus.NativeBodies\", EntryPoint = \"{entry}\")]");
            Line("    [global::System.Runtime.InteropServices.UnmanagedCallConv(CallConvs = [typeof(global::System.Runtime.CompilerServices.CallConvCdecl)])]");
            Line($"    private static partial nint @{accessor}();\n");
        }
    }
}
