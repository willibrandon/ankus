using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Ankus.Generators.Tests;

public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// The output plugin export stays cached across body edits and source movement with unchanged native and linker output.
    /// </summary>
    /// <param name="move">Whether the declaration moves rather than changing its implementation.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void OutputPluginEmissionRemainsCachedAcrossImplementationEdits(bool move)
    {
        string source = OutputPluginCacheSource("Initialize");
        CSharpCompilation initial = ModuleCompilation(source);
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        OutputPluginEmission before = TrackedOutputPluginEmission(driver).Emission;
        string replacement = move ? "\n\n" + source : source.Replace("{ }", "{ callbacks->shutdown_cb = 0; }", StringComparison.Ordinal);
        CSharpCompilation edited = initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(
            replacement, path: "Module.cs", cancellationToken: context.CancellationToken));
        driver = RunModule(driver, edited, out Compilation second);

        Assert.AreEqual(IncrementalStepRunReason.Cached, TrackedOutputPluginEmission(driver).Reason);
        Assert.AreEqual(before, TrackedOutputPluginEmission(driver).Emission);
        Assert.Contains("global::Plugins.@Initialize(", before.Managed);
        Assert.AreEqual(ManifestValue(first, "Ankus.NativeSource"), ManifestValue(second, "Ankus.NativeSource"));
        Assert.AreSequenceEqual(["Pg_magic_func", "_PG_init", "_PG_output_plugin_init"],
            ManifestValue(second, "Ankus.Exports").Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// Renaming the initializer refreshes its managed target and callback symbol while keeping PostgreSQL's export.
    /// </summary>
    [TestMethod]
    public void OutputPluginEmissionTracksTheManagedTarget()
    {
        CSharpCompilation initial = ModuleCompilation(OutputPluginCacheSource("Initialize"));
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out _);
        OutputPluginEmission before = TrackedOutputPluginEmission(driver).Emission;
        CSharpCompilation edited = initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(
            OutputPluginCacheSource("Start"), path: "Module.cs", cancellationToken: context.CancellationToken));
        driver = RunModule(driver, edited, out Compilation output);
        (OutputPluginEmission emission, IncrementalStepRunReason reason) = TrackedOutputPluginEmission(driver);

        Assert.AreEqual(IncrementalStepRunReason.Modified, reason);
        Assert.AreEqual(before.Exports, emission.Exports);
        Assert.AreNotEqual(before.Declaration.Callback, emission.Declaration.Callback);
        Assert.Contains("global::Plugins.@Start(", emission.Managed);
        Assert.Contains(emission.Native.ReplaceLineEndings("\n"), ManifestValue(output, "Ankus.NativeSource"));
        Assert.DoesNotContain(before.Declaration.Callback, ManifestValue(output, "Ankus.NativeSource"));
        ImmutableArray<Diagnostic> diagnostics = output.GetDiagnostics(context.CancellationToken);
        Assert.IsEmpty(diagnostics.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
    }

    private static string OutputPluginCacheSource(string name)
        => OutputPluginTypesSource + "public static unsafe partial class Plugins { [Ankus.PgOutputPlugin] public static void " + name +
            "(Ankus.Postgres.OutputPluginCallbacks* callbacks) { } }";

    /// <summary>
    /// Reads the production output plugin renderer's actual artifact and incremental reuse decision.
    /// </summary>
    /// <param name="driver">The executed production generator.</param>
    /// <returns>The immutable export emission and tracked cache decision.</returns>
    private static (OutputPluginEmission Emission, IncrementalStepRunReason Reason) TrackedOutputPluginEmission(GeneratorDriver driver)
    {
        (object value, IncrementalStepRunReason reason) = Assert.ContainsSingle(Assert.ContainsSingle(driver.GetRunResult().Results)
            .TrackedSteps["OutputPluginEmission"].SelectMany(static step => step.Outputs).Where(static output =>
                output.Reason != IncrementalStepRunReason.Removed && output.Value is OutputPluginEmission));
        return (Assert.IsInstanceOfType<OutputPluginEmission>(value), reason);
    }
}
