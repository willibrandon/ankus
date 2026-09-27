using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Ankus.Generators.Tests;

public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Exclusive guard mutations compile through the public API and emit independent native write admission checks.
    /// </summary>
    [TestMethod]
    public void GuardMutatorsCompile()
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            using Ankus;
            public struct Value(long count)
            {
                public long Count = count;
                public readonly PgAtomicValue<long> Atomic = new(3);
                public readonly PgSpinLockValue<int> Child = new(7);
            }
            public static class Shared
            {
                private static readonly PgLwLock<Value> State = new("test.mutation");
                [PgModuleLoad]
                public static void Load() => PgSharedMemory.Initialize(State, () => new Value(0));
                [PgFunction]
                public static long Update(long count)
                {
                    using PgLwLockExclusiveGuard<Value> guard = State.Exclusive();
                    _ = guard.Mutate((ref Value value) =>
                    {
                        value.Atomic.Exchange(count);
                        return value.Count = count;
                    });
                    return guard.Read(static (in Value value) =>
                    {
                        using PgSpinLockGuard<int> child = value.Child.Lock();
                        _ = child.Mutate(static (ref int stored) => ++stored);
                        return value.Count;
                    });
                }
                [PgFunction]
                public static int Local()
                {
                    PgSpinLock<int> owner = new(1);
                    using PgSpinLockGuard<int> guard = owner.Lock();
                    return guard.Mutate(static (ref int value) => ++value);
                }
            }
            """);
        AssertInitializationCompilationSucceeds(compilation, diagnostics);
        string native = ManifestValue(compilation, "Ankus.NativeSource").ReplaceLineEndings("\n");
        string admission = native[native.IndexOf("static int\nankus_shared_value_address", StringComparison.Ordinal)..
            native.IndexOf("static void\nankus_memory_shared", StringComparison.Ordinal)];
        AssertOrdered(admission, ["entry->lease != (uint64) request->other", "LWLockHeldByMe(entry->lock)",
            "request->length != entry->size", "request->flags == 7 && !entry->exclusive",
            "error->sqlstate = ERRCODE_OBJECT_NOT_IN_PREREQUISITE_STATE", "return 1;",
            "result->data = (intptr_t) ankus_shared_data(entry->header)"]);
        Assert.DoesNotContain("ereport(", admission);
        Assert.DoesNotContain("palloc(", admission);
        string invoke = native[native.IndexOf("static int\nankus_memory_invoke(AnkusMemoryApi *api", StringComparison.Ordinal)..];
        AssertOrdered(invoke, ["if (ankus_memory_error_cleanup)", "request->flags == 6 || request->flags == 7",
            "ankus_shared_value_address(request, result, error)", "PG_TRY();"]);
    }

    /// <summary>
    /// Shared guards cannot mutate, and mutable references cannot escape either exclusive guard callback.
    /// </summary>
    /// <param name="guard">The public guard type.</param>
    /// <param name="expected">The compiler's ownership diagnostic.</param>
    [TestMethod]
    [DataRow("PgLwLockShareGuard<int>", "CS1061")]
    [DataRow("PgLwLockExclusiveGuard<int>", "CS1628")]
    [DataRow("PgSpinLockGuard<int>", "CS1628")]
    public void GuardMutatorsRejectInvalidAccess(string guard, string expected)
    {
        (Compilation compilation, _) = Generate($$"""
            using System;
            using Ankus;
            public static class Functions
            {
                public static Func<int> Escape({{guard}} guard)
                    => guard.Mutate(static (ref int value) => new Func<int>(() => value));
            }
            """);
        Diagnostic[] errors = [.. compilation.GetDiagnostics(context.CancellationToken).Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)];
        Assert.Contains(expected, errors.Select(static diagnostic => diagnostic.Id),
            string.Join(Environment.NewLine, errors.Select(static diagnostic => diagnostic.ToString())));
    }

    /// <summary>
    /// Both lightweight-lock guards compile original readers with nested synchronization and no raw public address.
    /// </summary>
    [TestMethod]
    public void LwLockGuardReadersCompile()
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            using Ankus;
            public readonly struct Nested(int value)
            {
                public readonly PgAtomicValue<int> Atomic = new(value);
                public readonly PgSpinLockValue<int> Child = new(value);
            }
            public static class Shared
            {
                private static readonly PgLwLock<Nested> State = new("test.readers");
                [PgModuleLoad]
                public static void Load() => PgSharedMemory.Initialize(State, () => new Nested(3));
                [PgFunction]
                public static int Read()
                {
                    using PgLwLockShareGuard<Nested> guard = State.Share();
                    return guard.Read(static (in Nested value) =>
                    {
                        using PgSpinLockGuard<int> child = value.Child.Lock();
                        return value.Atomic.Exchange(child.Value);
                    });
                }
                [PgFunction]
                public static int ReadExclusive()
                {
                    using PgLwLockExclusiveGuard<Nested> guard = State.Exclusive();
                    return guard.Read(static (in Nested value) => value.Atomic.Value);
                }
            }
            """);
        AssertInitializationCompilationSucceeds(compilation, diagnostics);
        string native = ManifestValue(compilation, "Ankus.NativeSource").ReplaceLineEndings("\n");
        string read = native[native.IndexOf("static int\nankus_shared_value_address", StringComparison.Ordinal)..
            native.IndexOf("static void\nankus_memory_shared", StringComparison.Ordinal)];
        Assert.DoesNotContain("ereport(", read);
        Assert.DoesNotContain("palloc(", read);
        Assert.DoesNotContain("ankus_shared_attach(", read);
        AssertOrdered(read, ["entry->lease != (uint64) request->other", "LWLockHeldByMe(entry->lock)",
            "request->length != entry->size", "result->data = (intptr_t) ankus_shared_data(entry->header)", "result->length = entry->size"]);
        string invoke = native[native.IndexOf("static int\nankus_memory_invoke(AnkusMemoryApi *api", StringComparison.Ordinal)..];
        AssertOrdered(invoke, ["if (ankus_memory_error_cleanup)", "request->flags == 6", "ankus_shared_value_address(request, result, error)", "PG_TRY();"]);
    }

    /// <summary>
    /// Scoped readonly readers reject reference capture and direct replacement in either guard mode.
    /// </summary>
    /// <param name="guard">The public guard type.</param>
    /// <param name="body">The forbidden callback body.</param>
    /// <param name="expected">The compiler's reference-boundary diagnostic.</param>
    [TestMethod]
    [DataRow("PgLwLockShareGuard<int>", "return guard.Read(static (in int value) => new Func<int>(() => value));", "CS1628")]
    [DataRow("PgLwLockExclusiveGuard<int>", "return guard.Read(static (in int value) => { value = 99; return new Func<int>(() => 0); });", "CS8331")]
    public void LwLockGuardReadersRejectReferenceEscapes(string guard, string body, string expected)
    {
        (Compilation compilation, _) = Generate($$"""
            using System;
            using Ankus;
            public static class Functions
            {
                public static Func<int> Escape({{guard}} guard)
                {
                    {{body}}
                }
            }
            """);
        Diagnostic[] errors = [.. compilation.GetDiagnostics(context.CancellationToken).Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)];
        Assert.Contains(expected, errors.Select(static diagnostic => diagnostic.Id),
            string.Join(Environment.NewLine, errors.Select(static diagnostic => diagnostic.ToString())));
    }

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
