using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Ankus.Generators.Tests;

public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Atomic consumers compile and emit selected-header attachment, fork admission and retirement boundaries.
    /// </summary>
    [TestMethod]
    public void AtomicRegistrationCompilesWithSelectedHeaderLifetime()
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            using Ankus;
            public static class Shared
            {
                private static readonly PgAtomic<long> Counter = new("test.atomic");
                [PgModuleLoad]
                public static void Load() => PgSharedMemory.Initialize(Counter, () => 73);
                [PgFunction]
                public static long Increment() => Counter.Increment();
            }
            """);
        AssertInitializationCompilationSucceeds(compilation, diagnostics);
        string native = ManifestValue(compilation, "Ankus.NativeSource").ReplaceLineEndings("\n");
        Assert.Contains("#include \"port/atomics.h\"", native);
        Assert.Contains("StaticAssertDecl(sizeof(AnkusSharedAccess) == 16", native);
        Assert.Contains("offsetof(AnkusSharedAccess, readers.value) == 8", native);
        Assert.Contains("offsetof(AnkusSharedAccess, process_id.value) == 12", native);
        Assert.Contains("entry->kind == 1 && entry->size < sizeof(uint64) ? sizeof(uint64) : entry->size", native);
        Assert.Contains("if (entry->kind == 0)\n    {\n        RequestNamedLWLockTranche(entry->name, 1);", native);
        string close = native[native.IndexOf("static void\nankus_shared_close", StringComparison.Ordinal)..
            native.IndexOf("static void\nankus_shared_retire(void)", StringComparison.Ordinal)];
        AssertOrdered(close, ["pg_atomic_fetch_or_u32", "pg_atomic_write_u32(&entry->access.process_id, 0)",
            "pg_atomic_read_u32(&entry->access.readers)", "pg_atomic_write_u64(&entry->access.address, 0)"]);
        Assert.DoesNotContain("ankus_shared_initialize(", close);
        string prepare = native[native.IndexOf("static void\nankus_shared_prepare(void)\n{", StringComparison.Ordinal)..
            native.IndexOf("static void *\nankus_shared_data", StringComparison.Ordinal)];
        AssertOrdered(prepare, ["ankus_shared_exit_pid == MyProcPid", "before_shmem_exit(ankus_shared_before_exit",
            "pg_atomic_read_u32(&entry->access.process_id) != (uint32) MyProcPid",
            "pg_atomic_write_u32(&entry->access.readers", "pg_atomic_write_u32(&entry->access.process_id, (uint32) MyProcPid)"]);
        string attach = native[native.IndexOf("static void\nankus_shared_attach", StringComparison.Ordinal)..
            native.IndexOf("static void\nankus_shared_startup", StringComparison.Ordinal)];
        AssertOrdered(attach, ["header->kind != entry->kind", "pg_atomic_write_u64(&entry->access.address",
            "pg_memory_barrier();", "pg_atomic_write_u32(&entry->access.readers, 0)"]);
        Assert.Contains("ankus_shared_attach(entry, !IsUnderPostmaster);", native);
        Assert.Contains("result->data = (intptr_t) &entry->access;", native);
        Assert.Contains("result->length = sizeof(entry->access);", native);
        string retirement = native[native.IndexOf("static void\nankus_shared_retire(void)", StringComparison.Ordinal)..
            native.IndexOf("static void\nankus_shared_before_exit", StringComparison.Ordinal)];
        AssertOrdered(retirement, ["entry = ankus_shared_storage", "entry->kind != 0", "ankus_shared_close(entry);"]);
    }
}
