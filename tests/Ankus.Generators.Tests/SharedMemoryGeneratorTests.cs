using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Ankus.Generators.Tests;

public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Ordinary static shared descriptors compile without extra discovery attributes and bind startup before registration.
    /// </summary>
    [TestMethod]
    public void SharedMemoryRegistrationCompilesWithSelectedHeaderHooks()
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            using Ankus;
            public static class Shared
            {
                private static readonly PgLwLock<long> State = new("test.state");
                [PgModuleLoad]
                public static void Load() => PgSharedMemory.Initialize(State, () => -731);
                [PgFunction]
                public static long Read()
                {
                    using PgLwLockShareGuard<long> guard = State.Share();
                    return guard.Value;
                }
            }
            """);
        AssertInitializationCompilationSucceeds(compilation, diagnostics);
        string native = ManifestValue(compilation, "Ankus.NativeSource").ReplaceLineEndings("\n");
        string loader = native[native.IndexOf("PGDLLEXPORT void _PG_init(void)\n", StringComparison.Ordinal)..];
        AssertOrdered(loader, ["ankus_shared_initialize = ankus_shared_run_initializer;", "ankus_registration_complete = true;", "ankus_ensure_module_loaded();"]);
        Assert.Contains("ankus_shared_acquire_lock(AddinShmemInitLock, LW_EXCLUSIVE);", native);
        Assert.Contains("LWLockConditionalAcquire(lock, mode)", native);
        Assert.Contains("#if PG_VERSION_NUM >= 150000\nstatic shmem_request_hook_type", native);
        Assert.Contains("#if PG_VERSION_NUM < 150000\n            ankus_shared_request_one(entry);", native);
        Assert.DoesNotContain("MainLWLockArray[21]", native);
        string startup = native[native.IndexOf("static void\nankus_shared_startup(void)", StringComparison.Ordinal)..];
        AssertOrdered(startup, ["ankus_shared_previous_startup();", "ankus_shared_attach(entry, !IsUnderPostmaster);"]);
        string attach = native[native.IndexOf("static void\nankus_shared_attach", StringComparison.Ordinal)..];
        AssertOrdered(attach, ["if (!found)", "if (!create)", "ankus_shared_initialize(entry->initialize", "header->initialized = true;"]);
        string release = native[native.IndexOf("static void\nankus_shared_release", StringComparison.Ordinal)..native.IndexOf("static void\nankus_memory_shared", StringComparison.Ordinal)];
        Assert.DoesNotContain("ereport(", release);
        Assert.DoesNotContain("palloc(", release);
        AssertOrdered(release, ["entry->lease != (uint64) request->other", "return;", "entry->lease = 0;", "LWLockHeldByMe(entry->lock)", "LWLockRelease(entry->lock);"]);
    }
}
