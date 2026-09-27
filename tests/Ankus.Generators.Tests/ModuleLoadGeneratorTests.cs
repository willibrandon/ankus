using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Ankus.Generators.Tests;

public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Immediate module registration compiles independently and alongside deferred initialization.
    /// </summary>
    /// <param name="ready">Whether the assembly also declares a deferred initializer.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ModuleLoadCompilesBeforeDeferredInitialization(bool ready)
    {
        string source = "public static class Startup { [Ankus.PgModuleLoad] public static void Load() { } " +
            (ready ? "[Ankus.PgInitialize] public static void Ready() { }" : string.Empty) + " }";
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate(source);
        AssertInitializationCompilationSucceeds(compilation, diagnostics);
        Assert.AreSequenceEqual(["Pg_magic_func", "_PG_init"], ManifestValue(compilation, "Ankus.Exports").Split('\n', StringSplitOptions.RemoveEmptyEntries));
        Assert.AreEqual("-- No installable objects declared.\n", ManifestValue(compilation, "Ankus.Sql").ReplaceLineEndings("\n"));
        IMethodSymbol[] dispatchers = [.. compilation.GetTypeByMetadataName("Ankus.Generated.ExtensionDispatchers")!.GetMembers().OfType<IMethodSymbol>()];
        Assert.HasCount(ready ? 2 : 1, dispatchers);
        string native = ManifestValue(compilation, "Ankus.NativeSource").ReplaceLineEndings("\n");
        string loader = native[native.IndexOf("PGDLLEXPORT void _PG_init(void)\n", StringComparison.Ordinal)..];
        AssertOrdered(loader,
        [
            "if (ankus_registration_complete)", "ankus_ensure_module_loaded();",
            "if (!ankus_worker_restore_in_progress())", "ankus_ensure_initialized();",
            "ankus_registration_complete = true;", "ankus_ensure_module_loaded();",
            "if (ankus_worker_restore_in_progress())", "return;", "ankus_ensure_initialized();",
            "static void\nankus_ensure_initialized(void)", "ankus_ensure_module_loaded();",
            "if (ankus_initialization_state == 2)", "ankus_initialization_state = 1;",
            "RhEnableForkSupport();", "ankus_initialization_state = 2;",
        ]);
        string registration = native[native.IndexOf("static void\nankus_ensure_module_loaded(void)", StringComparison.Ordinal)..native.IndexOf("PGDLLEXPORT void _PG_init(void);", StringComparison.Ordinal)];
        AssertOrdered(registration,
        [
            "if (ankus_module_loading)", "errcode(ERRCODE_OBJECT_NOT_IN_PREREQUISITE_STATE)",
            "if (ankus_module_loaded)", "return;",
            "bool transactional = IsTransactionState() && !ankus_worker_restore_in_progress();",
            "ankus_module_loading = true;", "PG_TRY();", "if (transactional && !ActiveSnapshotSet())",
            "PushActiveSnapshot(GetTransactionSnapshot());", "snapshot_owned = true;",
            "transactional ? ankus_spi_execute : NULL", "PopActiveSnapshot();",
            "if (status != 0)", "ankus_raise_error(error);",
            "ankus_module_loaded = true;", "ankus_module_loading = false;",
            "PG_CATCH();", "ankus_module_loading = false;", "MemoryContextSwitchTo(caller);",
            "PopActiveSnapshot();", "ankus_release_error(error);", "pfree(error);", "PG_RE_THROW();",
            "PG_END_TRY();", "MemoryContextSwitchTo(caller);", "ankus_release_error(error);", "pfree(error);",
        ]);
        Assert.DoesNotContain("RhEnableForkSupport();", registration);
    }

    /// <summary>
    /// Module registration rejects unsupported signatures and conflicting phase declarations.
    /// </summary>
    /// <param name="declaration">The invalid module registration declaration.</param>
    [TestMethod]
    [DataRow("[Ankus.PgModuleLoad] public void Load() { }")]
    [DataRow("[Ankus.PgModuleLoad] private static void Load() { }")]
    [DataRow("[Ankus.PgModuleLoad] public static int Load() => 1;")]
    [DataRow("[Ankus.PgModuleLoad] public static void Load(int value) { }")]
    [DataRow("[Ankus.PgModuleLoad] public static void Load<T>() { }")]
    [DataRow("[Ankus.PgModuleLoad] public static async void Load() { await System.Threading.Tasks.Task.Yield(); }")]
    [DataRow("[Ankus.PgModuleLoad, Ankus.PgInitialize] public static void Load() { }")]
    [DataRow("[Ankus.PgModuleLoad, Ankus.PgFunction] public static void Load() { }")]
    [DataRow("[Ankus.PgModuleLoad, System.Diagnostics.Conditional(\"DEBUG\")] public static void Load() { }")]
    [DataRow("[Ankus.PgModuleLoad] public static void First() { } [Ankus.PgModuleLoad] public static void Second() { }")]
    public void InvalidModuleLoadDeclarationsAreRejected(string declaration)
        => AssertInvalidInitialization("public class Startup { " + declaration + " }");
}
