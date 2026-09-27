using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Ankus.Generators.Tests;

public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Local owners and inline shared cells compile with real selected-header operations and a guarded acquisition boundary.
    /// </summary>
    [TestMethod]
    public void SpinLocksCompileWithLocalAndInlineSharedValues()
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            using Ankus;
            public readonly struct State(int initial)
            {
                public readonly PgSpinLockValue<int> Counter = new(initial);
            }
            public static class Functions
            {
                private static readonly PgShared<State> Shared = new("test.spinlock");
                private static PgSpinLock<int>? Local;
                [PgModuleLoad]
                public static void Load()
                {
                    PgSharedMemory.Initialize(Shared, static () => new State(11));
                    Local = new PgSpinLock<int>(13);
                }
                [PgFunction]
                public static int Add(int amount) => Shared.Read((in State value) =>
                {
                    using PgSpinLockGuard<int> guard = value.Counter.Lock();
                    guard.Value += amount;
                    return guard.Value;
                });
                [PgFunction]
                public static int ReadLocal()
                {
                    using PgSpinLockGuard<int> guard = Local!.Lock();
                    return guard.Value;
                }
            }
            """);
        AssertInitializationCompilationSucceeds(compilation, diagnostics);
        string native = ManifestValue(compilation, "Ankus.NativeSource");
        Assert.Contains("#include \"storage/spin.h\"", native);
        Assert.Contains("sizeof(slock_t) <= sizeof(uint64)", native);
        Assert.Contains("offsetof(AnkusSpinLockAlignment, value) <= sizeof(uint64)", native);
        Assert.Contains("ANKUS_MEMORY_SPIN = 36", native);
        Assert.Contains("request->operation != ANKUS_MEMORY_SPIN", native);
        string spin = native[native.IndexOf("typedef struct AnkusSpinLockAlignment", StringComparison.Ordinal)..
            native.IndexOf("ankus_memory_execute(AnkusMemoryApi", StringComparison.Ordinal)];
        Assert.Contains("SpinLockInit(lock);", spin);
        Assert.Contains("SpinLockAcquire(lock);", spin);
        Assert.Contains("SpinLockRelease((slock_t *) address);", spin);
        Assert.Contains("#if PG_VERSION_NUM < 190000", spin);
        Assert.Contains("result->value = !SpinLockFree(lock);", spin);
        Assert.DoesNotContain("CHECK_FOR_INTERRUPTS", spin);
    }

    /// <summary>
    /// Copied guard values cannot expose mutable references and shared input references cannot escape through closures.
    /// </summary>
    [TestMethod]
    [DataRow("public static ref int Escape(PgSpinLockGuard<int> guard) => ref guard.Value;", "CS8156")]
    [DataRow("public static Action Escape(in PgSpinLockValue<int> value) => () => value.Lock();", "CS1628")]
    public void SpinGuardValuesPreserveReferenceBoundaries(string declaration, string expected)
    {
        (Compilation compilation, _) = Generate($$"""
            using System;
            using Ankus;
            public static class Functions
            {
                {{declaration}}
            }
            """);
        Diagnostic[] errors = [.. compilation.GetDiagnostics(context.CancellationToken).Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)];
        Assert.Contains(expected, errors.Select(static diagnostic => diagnostic.Id),
            string.Join(Environment.NewLine, errors.Select(static diagnostic => diagnostic.ToString())));
    }
}
