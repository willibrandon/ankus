using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Ankus.Generators.Tests;

public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Worker-only assemblies compile guarded cdecl dispatchers and export native entries without SQL functions.
    /// </summary>
    /// <param name="declaration">The supported declaration form.</param>
    [TestMethod]
    [DataRow("public static class Workers { [Ankus.PgBackgroundWorker] public static void Run(nuint argument) { } }")]
    [DataRow("internal class Outer { internal struct Inner { [Ankus.PgBackgroundWorker] internal static void Run(nuint argument) { } } }")]
    [DataRow("public partial class Workers { [Ankus.PgBackgroundWorker] public static partial void Run(nuint argument); } public partial class Workers { public static partial void Run(nuint argument) { } }")]
    [DataRow("public interface Workers { [Ankus.PgBackgroundWorker] public static void Run(nuint argument) { } }")]
    public void WorkerEntriesCompile(string declaration)
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate(declaration);
        AssertInitializationCompilationSucceeds(compilation, diagnostics);
        Assert.AreSequenceEqual(["Pg_magic_func", "_PG_init", "Run"],
            ManifestValue(compilation, "Ankus.Exports").Split('\n', StringSplitOptions.RemoveEmptyEntries));
        Assert.AreEqual("-- No installable objects declared.\n", ManifestValue(compilation, "Ankus.Sql").ReplaceLineEndings("\n"));
        string native = ManifestValue(compilation, "Ankus.NativeSource").ReplaceLineEndings("\n");
        string entry = native[native.IndexOf("PGDLLEXPORT void Run(Datum argument)\n", StringComparison.Ordinal)..];
        AssertOrdered(entry, ["ankus_worker_active = true", "ankus_worker_attach(3)", "ankus_ensure_initialized()",
            "ankus_fork_host_enter()", "int status =", "if (status != 0)", "ankus_raise_error(error)", "PG_FINALLY()",
            "ankus_worker_active = false", "ankus_release_error(error)", "ankus_fork_host_exit()"]);
        Assert.DoesNotContain("PG_FUNCTION_INFO_V1(Run)", native);
    }

    /// <summary>
    /// Export names accept the final byte that fits PostgreSQL's symbol field and reject the next byte.
    /// </summary>
    /// <param name="length">The ASCII native symbol length.</param>
    /// <param name="valid">Whether the symbol fits with its terminator.</param>
    [TestMethod]
    [DataRow(1, true)]
    [DataRow(95, true)]
    [DataRow(96, false)]
    public void WorkerEntriesPreserveSymbolBoundaries(int length, bool valid)
    {
        string symbol = new('a', length);
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate($$"""
            public static class Workers
            {
                [Ankus.PgBackgroundWorker(EntryPoint = "{{symbol}}")]
                public static void Run(nuint argument) { }
            }
            """);
        if (valid)
        {
            AssertInitializationCompilationSucceeds(compilation, diagnostics);
            Assert.Contains(symbol, ManifestValue(compilation, "Ankus.Exports").Split('\n', StringSplitOptions.RemoveEmptyEntries));
        }
        else
        {
            Assert.Contains("ANKUS022", diagnostics.Select(static diagnostic => diagnostic.Id));
        }
    }

    /// <summary>
    /// Explicit native names and multiple entries remain distinct alongside ordinary SQL and preload methods.
    /// </summary>
    [TestMethod]
    public void WorkerEntriesComposeWithInitializationAndSql()
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            using Ankus;
            public static class Workers
            {
                [PgBackgroundWorker(EntryPoint = "worker_first")]
                public static void First(nuint value) => PgBackgroundWorker.Connect("postgres");
                [PgBackgroundWorker]
                public static void Second(nuint value) => PgBackgroundWorker.RunTransaction(static () => Spi.Execute("SELECT 42"));
                [PgModuleLoad]
                public static void Load() => PgBackgroundWorker.Register(new("worker", "workers", "worker_first") { DatabaseAccess = true });
                [PgFunction]
                public static int Answer() => 42;
            }
            """);
        AssertInitializationCompilationSucceeds(compilation, diagnostics);
        string[] exports = ManifestValue(compilation, "Ankus.Exports").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains("worker_first", exports);
        Assert.Contains("Second", exports);
        Assert.ContainsSingle(exports.Where(static name => name == "_PG_init"));
        Assert.Contains("answer", ManifestValue(compilation, "Ankus.Sql"));
        Assert.DoesNotContain("worker_first", ManifestValue(compilation, "Ankus.Sql"));
    }

    /// <summary>
    /// Invalid signatures and conflicting native metadata fail before emitting an unusable worker export.
    /// </summary>
    /// <param name="source">The invalid declaration.</param>
    [TestMethod]
    [DataRow("public class Workers { [Ankus.PgBackgroundWorker] public void Run(nuint argument) { } }")]
    [DataRow("public static class Workers { [Ankus.PgBackgroundWorker] private static void Run(nuint argument) { } }")]
    [DataRow("public static class Workers { [Ankus.PgBackgroundWorker] public static int Run(nuint argument) => 1; }")]
    [DataRow("public static class Workers { [Ankus.PgBackgroundWorker] public static void Run(long argument) { } }")]
    [DataRow("public static class Workers { [Ankus.PgBackgroundWorker] public static void Run(ref nuint argument) { } }")]
    [DataRow("public static class Workers { [Ankus.PgBackgroundWorker] public static void Run() { } }")]
    [DataRow("public static class Workers { [Ankus.PgBackgroundWorker] public static void Run<T>(nuint argument) { } }")]
    [DataRow("public static class Workers<T> { [Ankus.PgBackgroundWorker] public static void Run(nuint argument) { } }")]
    [DataRow("public static class Workers { [Ankus.PgBackgroundWorker] public static async void Run(nuint argument) { await System.Threading.Tasks.Task.Yield(); } }")]
    [DataRow("public static class Workers { [Ankus.PgBackgroundWorker, Ankus.PgFunction] public static void Run(nuint argument) { } }")]
    [DataRow("public static class Workers { [Ankus.PgBackgroundWorker, System.Diagnostics.Conditional(\"DEBUG\")] public static void Run(nuint argument) { } }")]
    [DataRow("public static class Workers { [Ankus.PgBackgroundWorker(EntryPoint = \"\")] public static void Run(nuint argument) { } }")]
    [DataRow("public static class Workers { [Ankus.PgBackgroundWorker(EntryPoint = \"for\")] public static void Run(nuint argument) { } }")]
    [DataRow("public static class Workers { [Ankus.PgBackgroundWorker(EntryPoint = \"ankus_managed_worker\")] public static void Run(nuint argument) { } }")]
    [DataRow("public static class Workers { [Ankus.PgBackgroundWorker(EntryPoint = \"Pg_magic_func\")] public static void Run(nuint argument) { } }")]
    [DataRow("public static class Workers { [Ankus.PgBackgroundWorker(EntryPoint = \"bad-name\")] public static void Run(nuint argument) { } }")]
    [DataRow("public static class Workers { [Ankus.PgBackgroundWorker] public static void Run(nuint argument) { } } public static class Other { [Ankus.PgBackgroundWorker] public static void Run(nuint argument) { } }")]
    public void WorkerEntriesRejectInvalidDeclarations(string source)
    {
        (_, ImmutableArray<Diagnostic> diagnostics) = Generate(source);
        Assert.Contains("ANKUS022", diagnostics.Select(static diagnostic => diagnostic.Id));
        Assert.DoesNotContain("CS8785", diagnostics.Select(static diagnostic => diagnostic.Id));
    }
}
