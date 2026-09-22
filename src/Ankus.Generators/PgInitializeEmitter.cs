using System.Text;
using Microsoft.CodeAnalysis;

namespace Ankus.Generators;

/// <summary>
/// Emits PostgreSQL library initialization and its managed exception boundary.
/// </summary>
internal static class PgInitializeEmitter
{
    /// <summary>
    /// Appends the managed callback, native loader entry point, and export without declaring a SQL function.
    /// </summary>
    /// <param name="method">The validated initialization method.</param>
    /// <param name="callback">The assembly-specific managed symbol.</param>
    /// <param name="managed">The managed dispatch source.</param>
    /// <param name="native">The native library source.</param>
    /// <param name="exports">The native linker exports.</param>
    internal static void Emit(IMethodSymbol method, string callback, StringBuilder managed, StringBuilder native, StringBuilder exports)
    {
        managed.AppendLine($$"""
                [global::System.Runtime.InteropServices.UnmanagedCallersOnly(
                    EntryPoint = "{{callback}}",
                    CallConvs = new[] { typeof(global::System.Runtime.CompilerServices.CallConvCdecl) })]
                private static int {{callback}}(global::Ankus.NativeCallError* error, nint execute)
                {
                    nint previous = global::Ankus.NativeBackend.Enter(execute);
                    try
                    {
                        {{method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}}.@{{method.Name}}();
                        return 0;
                    }
                    catch (global::System.Exception exception)
                    {
                        global::Ankus.NativeError.Write(exception, error);
                        return 1;
                    }
                    finally
                    {
                        global::Ankus.NativeBackend.Exit(previous);
                    }
                }

            """);
        native.AppendLine($$"""
            #include "utils/snapmgr.h"

            extern int {{callback}}(AnkusError *, AnkusExecute);
            static int ankus_initialization_state = 0;

            PGDLLEXPORT void _PG_init(void);
            PGDLLEXPORT void _PG_init(void)
            {
                if (IsPostmasterEnvironment && !IsUnderPostmaster)
                {
                    ereport(ERROR, (errcode(ERRCODE_FEATURE_NOT_SUPPORTED),
                        errmsg("Ankus managed initialization cannot run through shared_preload_libraries in the postmaster"),
                        errdetail("The .NET Native AOT runtime cannot be initialized before PostgreSQL forks backend processes."),
                        errhint("Use session_preload_libraries or LOAD to initialize the extension in a backend process.")));
                }

                if (ankus_initialization_state == 1)
                {
                    ereport(ERROR, (errcode(ERRCODE_OBJECT_NOT_IN_PREREQUISITE_STATE),
                        errmsg("Ankus extension initialization is already in progress")));
                }

                if (ankus_initialization_state == 2)
                {
                    return;
                }

                MemoryContext caller = CurrentMemoryContext;
                AnkusError *error = MemoryContextAllocZero(caller, sizeof(AnkusError));
                volatile bool snapshot_owned = false;
                ankus_initialization_state = 1;
                PG_TRY();
                {
                    if (IsTransactionState() && !ActiveSnapshotSet())
                    {
                        PushActiveSnapshot(GetTransactionSnapshot());
                        snapshot_owned = true;
                    }

                    int status = {{callback}}(error, IsTransactionState() ? ankus_spi_execute : NULL);
                    if (snapshot_owned)
                    {
                        snapshot_owned = false;
                        PopActiveSnapshot();
                    }

                    if (status != 0)
                    {
                        ankus_initialization_state = 0;
                        ankus_raise_error(error);
                    }

                    ankus_initialization_state = 2;
                }
                PG_CATCH();
                {
                    ankus_initialization_state = 0;
                    MemoryContextSwitchTo(caller);
                    if (snapshot_owned)
                    {
                        snapshot_owned = false;
                        PopActiveSnapshot();
                    }

                    ankus_release_error(error);
                    pfree(error);
                    PG_RE_THROW();
                }
                PG_END_TRY();
                MemoryContextSwitchTo(caller);
                ankus_release_error(error);
                pfree(error);
            }

            """);
        exports.AppendLine("_PG_init");
    }
}
