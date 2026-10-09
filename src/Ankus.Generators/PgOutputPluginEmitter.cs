namespace Ankus.Generators;

/// <summary>
/// Emits the <c>_PG_output_plugin_init</c> export, which passes PostgreSQL's callback table to a managed initializer.
/// </summary>
internal static class PgOutputPluginEmitter
{
    /// <summary>
    /// Renders a statically bound dispatcher and its native export from an immutable invocation contract.
    /// </summary>
    /// <param name="declaration">The validated initializer and dispatcher identities.</param>
    /// <returns>The independently cached managed, native and export artifacts.</returns>
    /// <remarks>
    /// The export dispatches through the native callback boundary, which initializes the managed runtime on first use,
    /// enters the backend capabilities and raises a PostgreSQL error only after the managed dispatcher has returned.
    /// </remarks>
    internal static OutputPluginEmission Emit(OutputPluginDeclaration declaration)
    {
        string callback = declaration.Callback;
        string managed = $$"""
                [global::System.Runtime.InteropServices.UnmanagedCallersOnly(
                    EntryPoint = "{{callback}}", CallConvs = [typeof(global::System.Runtime.CompilerServices.CallConvCdecl)])]
                private static int {{callback}}(global::Ankus.CompilerServices.NativeCallArgument* arguments, nuint count,
                    nint result, nuint resultSize, global::Ankus.CompilerServices.NativeCallbackContext* context)
                {
                    if (context is null || context->Error == 0)
                    {
                        return 1;
                    }

                    nint previousBackend = global::Ankus.CompilerServices.NativeBackend.Enter(context->Execute);
                    nint previousRead = global::Ankus.CompilerServices.NativeGuc.Enter(context->Read);
                    nint previousLog = global::Ankus.CompilerServices.NativeLog.Enter(context->Log);
                    nint previousMemory = 0;
                    bool memoryEntered = false;
                    try
                    {
                        previousMemory = global::Ankus.CompilerServices.NativeMemoryContext.Enter(context->Memory);
                        memoryEntered = true;
                        global::Ankus.CompilerServices.NativeRawCallback.ValidateFrame(arguments, count, 1, result, resultSize, -1);
                        {{declaration.Target}}(global::Ankus.CompilerServices.NativeRawCallback.ReadRecordPointer<{{declaration.CallbacksType}}>(arguments[0]));
                        return 0;
                    }
                    catch (global::System.Exception exception)
                    {
                        global::Ankus.CompilerServices.NativeError.Write(exception, (global::Ankus.CompilerServices.NativeCallError*)context->Error);
                        return 1;
                    }
                    finally
                    {
                        if (memoryEntered)
                        {
                            global::Ankus.CompilerServices.NativeMemoryContext.Exit(previousMemory);
                        }

                        global::Ankus.CompilerServices.NativeLog.Exit(previousLog);
                        global::Ankus.CompilerServices.NativeGuc.Exit(previousRead);
                        global::Ankus.CompilerServices.NativeBackend.Exit(previousBackend);
                    }
                }

            """;
        string native = $$"""
            #include "replication/output_plugin.h"

            extern int {{callback}}(const AnkusNativeCallArgument *, size_t, void *, size_t, void *);
            PGDLLEXPORT void {{OutputPluginDeclaration.ExportName}}(OutputPluginCallbacks *callbacks);

            /* PostgreSQL loads the plugin library by name and passes the callback table that the managed initializer fills. */
            PGDLLEXPORT void
            {{OutputPluginDeclaration.ExportName}}(OutputPluginCallbacks *callbacks)
            {
                AnkusNativeCallArgument arguments[] = { { &callbacks, sizeof(callbacks) } };
                ankus_dispatch_native_callback({{callback}}, arguments, 1, NULL, 0);
            }

            """;
        return new(declaration, managed, native, OutputPluginDeclaration.ExportName + "\n");
    }
}
