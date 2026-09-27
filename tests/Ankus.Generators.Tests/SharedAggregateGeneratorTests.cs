using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Ankus.Generators.Tests;

public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Ordinary aggregate readers compile and retain native layout, attachment and retirement checks.
    /// </summary>
    [TestMethod]
    public void SharedAggregateRegistrationEmitsNativeAccessProtocol()
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            using Ankus;
            public readonly struct Counts(long initial)
            {
                public readonly PgAtomicValue<long> Completed = new(initial);
                public readonly PgAtomicValue<bool> Ready = new(true);
                public int Version { get; } = 73;
            }
            public static class Shared
            {
                private static readonly PgShared<Counts> State = new("test.aggregate");
                [PgModuleLoad]
                public static void Load() => PgSharedMemory.Initialize(State, () => new Counts(19));
                [PgFunction]
                public static long Increment() => State.Read(static (in Counts value) => value.Completed.Increment());
            }
            """);
        AssertInitializationCompilationSucceeds(compilation, diagnostics);
        string native = ManifestValue(compilation, "Ankus.NativeSource").ReplaceLineEndings("\n");
        Assert.Contains("definition->kind > 2", native);
        Assert.Contains("header->kind != entry->kind", native);
        Assert.Contains("if (entry->kind == 0)\n    {\n        RequestNamedLWLockTranche", native);
        Assert.Contains("ankus_shared_has_access |= entry->kind != 0;", native);
        string attach = native[native.IndexOf("static void\nankus_shared_attach", StringComparison.Ordinal)..
            native.IndexOf("static void\nankus_shared_startup", StringComparison.Ordinal)];
        AssertOrdered(attach, ["if (entry->kind != 0)", "pg_atomic_write_u64(&entry->access.address", "pg_atomic_write_u32(&entry->access.readers, 0)"]);
    }

    /// <summary>
    /// Safe C# cannot mutate ordinary shared fields or return or capture their borrowed references.
    /// </summary>
    [TestMethod]
    [DataRow("State.Read(static (in Counts value) => ++value.Version);", "CS8332")]
    [DataRow("State.Read(static (in Counts value) => new ReadOnlySpan<Counts>(in value));", "CS9244")]
    [DataRow("State.Read(static (in Counts value) => (Func<int>)(() => value.Version));", "CS1628")]
    public void SharedAggregateCallbacksPreserveScopedReadonlyReferences(string body, string expected)
    {
        (Compilation compilation, _) = Generate($$"""
            using System;
            using Ankus;
            public struct Counts
            {
                public int Version;
            }
            public static class Shared
            {
                private static readonly PgShared<Counts> State = new("test.aggregate");
                public static void Invalid()
                {
                    {{body}}
                }
            }
            """);
        Diagnostic[] errors = [.. compilation.GetDiagnostics(context.CancellationToken).Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)];
        string[] identifiers = [.. errors.Select(static diagnostic => diagnostic.Id)];
        Assert.Contains(expected, identifiers, string.Join(Environment.NewLine, errors.Select(static diagnostic => diagnostic.ToString())));
    }
}
