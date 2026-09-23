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
    /// <param name="method">The optional validated initialization method.</param>
    /// <param name="callback">The optional assembly-specific managed symbol.</param>
    /// <param name="hasHooks">Whether configuration registration can enter managed hooks.</param>
    /// <param name="registration">Native setting registration followed by prefix checking statements.</param>
    /// <param name="managed">The managed dispatch source.</param>
    /// <param name="native">The native library source.</param>
    /// <param name="exports">The native linker exports.</param>
    internal static void Emit(IMethodSymbol? method, string? callback, bool hasHooks, string registration,
        StringBuilder managed, StringBuilder native, StringBuilder exports)
    {
        if (method is not null)
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
        }

        string guard = method is not null || hasHooks ? """
                if (IsPostmasterEnvironment && !IsUnderPostmaster)
                {
                    ereport(ERROR, (errcode(ERRCODE_FEATURE_NOT_SUPPORTED),
                        errmsg("Ankus managed initialization cannot run through shared_preload_libraries in the postmaster"),
                        errdetail("The .NET Native AOT runtime cannot be initialized before PostgreSQL forks backend processes."),
                        errhint("Use session_preload_libraries or LOAD to initialize the extension in a backend process.")));
                }

            """ : string.Empty;
        string errorDeclaration = method is null ? string.Empty : """
                AnkusError *error = MemoryContextAllocZero(caller, sizeof(AnkusError));
                volatile bool snapshot_owned = false;
            """;
        string invocation = method is null ? string.Empty : $$"""
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

            """;
        string errorCleanup = method is null ? string.Empty : """
                    if (snapshot_owned)
                    {
                        snapshot_owned = false;
                        PopActiveSnapshot();
                    }

                    ankus_release_error(error);
                    pfree(error);
            """;
        string cleanup = method is null ? string.Empty : """
                ankus_release_error(error);
                pfree(error);
            """;
        native.AppendLine($$"""
            #include "miscadmin.h"
            #include "utils/memutils.h"
            #include "utils/snapmgr.h"

            {{(method is null ? string.Empty : $"extern int {callback}(AnkusError *, AnkusExecute);")}}
            static int ankus_initialization_state = 0;

            PGDLLEXPORT void _PG_init(void);
            PGDLLEXPORT void _PG_init(void)
            {
            {{guard}}
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
            {{errorDeclaration}}
                ankus_initialization_state = 1;
                PG_TRY();
                {
            {{registration}}
            {{invocation}}
                    ankus_initialization_state = 2;
                }
                PG_CATCH();
                {
                    ankus_initialization_state = 0;
                    MemoryContextSwitchTo(caller);
            {{errorCleanup}}
                    PG_RE_THROW();
                }
                PG_END_TRY();
                MemoryContextSwitchTo(caller);
            {{cleanup}}
            }

            """);
        exports.AppendLine("_PG_init");
    }
}
