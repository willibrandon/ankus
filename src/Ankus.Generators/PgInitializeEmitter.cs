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
                    private static int {{callback}}(global::Ankus.NativeCallError* error, nint read, nint execute, nint log, nint memory)
                    {
                        nint previousBackend = global::Ankus.NativeBackend.Enter(execute);
                        nint previousRead = global::Ankus.NativeGuc.Enter(read);
                        nint previousLog = global::Ankus.NativeLog.Enter(log);
                        nint previousMemory = 0;
                        bool memoryEntered = false;
                        try
                        {
                            previousMemory = global::Ankus.NativeMemoryContext.Enter(memory);
                            memoryEntered = true;
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
                            if (memoryEntered)
                            {
                                global::Ankus.NativeMemoryContext.Exit(previousMemory);
                            }

                            global::Ankus.NativeLog.Exit(previousLog);
                            global::Ankus.NativeGuc.Exit(previousRead);
                            global::Ankus.NativeBackend.Exit(previousBackend);
                        }
                    }

                """);
            native.AppendLine(NativeErrorBridge.InitializationLogging);
        }

        string forkDeclaration = method is not null || hasHooks ? """
            #ifndef WIN32
            extern int32_t RhEnableForkSupport(void);
            #endif
            """ : string.Empty;
        string forkEnable = method is not null || hasHooks ? """
                    #ifndef WIN32
                    if (IsPostmasterEnvironment && !IsUnderPostmaster)
                    {
                        int32_t fork_status = RhEnableForkSupport();
                        if (fork_status != 1)
                        {
                            ereport(ERROR, (errcode(ERRCODE_INTERNAL_ERROR),
                                errmsg("Ankus runtime fork support failed: %d", fork_status)));
                        }
                    }
                    #endif

            """ : string.Empty;
        string ensureDeclaration = method is null ? string.Empty : "static void ankus_ensure_initialized(void);\n";
        string errorDeclaration = method is null ? string.Empty : """
                AnkusMemoryApi memory = {0};
                ankus_memory_initialize(&memory);
                AnkusError *error = MemoryContextAllocZero(caller, sizeof(AnkusError));
                volatile bool snapshot_owned = false;
            """;
        string invocation = method is null ? string.Empty : $$"""
                    if (IsTransactionState() && !ActiveSnapshotSet())
                    {
                        PushActiveSnapshot(GetTransactionSnapshot());
                        snapshot_owned = true;
                    }

                    int status = {{callback}}(error, ankus_read_guc,
                        IsTransactionState() ? ankus_spi_execute : NULL, ankus_initialization_log, &memory);
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
            #if defined(WIN32) && PG_VERSION_NUM < 180000
            #include "access/parallel.h"
            #endif

            {{(method is null ? string.Empty : $"extern int {callback}(AnkusError *, AnkusGucReadBinding, AnkusExecute, AnkusInitializationLog, AnkusMemoryApi *);")}}
            {{forkDeclaration}}
            static int ankus_initialization_state = 0;
            static bool ankus_registration_complete = false;
            {{ensureDeclaration}}

            PGDLLEXPORT void _PG_init(void);
            PGDLLEXPORT void _PG_init(void)
            {
                if (ankus_initialization_state == 1)
                {
                    ereport(ERROR, (errcode(ERRCODE_OBJECT_NOT_IN_PREREQUISITE_STATE),
                        errmsg("Ankus extension initialization is already in progress")));
                }

                if (ankus_registration_complete)
                {
            {{(method is null ? "        return;" : "        ankus_ensure_initialized();\n        return;")}}
                }

                MemoryContext caller = CurrentMemoryContext;
                ankus_initialization_state = 1;
                PG_TRY();
                {
            {{registration}}
            {{(method is null ? forkEnable : string.Empty)}}
                    ankus_registration_complete = true;
                    ankus_initialization_state = 0;
                }
                PG_CATCH();
                {
                    ankus_initialization_state = 0;
                    MemoryContextSwitchTo(caller);
                    PG_RE_THROW();
                }
                PG_END_TRY();
                MemoryContextSwitchTo(caller);
            {{(method is null ? "    ankus_initialization_state = 2;" : """
                #if defined(WIN32) && PG_VERSION_NUM < 180000
                if (IsParallelWorker())
                {
                    return;
                }
                #endif

                ankus_ensure_initialized();
            """)}}
            }

            {{(method is null ? string.Empty : $$"""
            static void
            ankus_ensure_initialized(void)
            {
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
            {{invocation}}
            {{forkEnable}}
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

            """)}}
            """);
        exports.AppendLine("_PG_init");
    }
}
