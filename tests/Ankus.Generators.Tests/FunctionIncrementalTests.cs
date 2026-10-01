using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Ankus.Generators.Tests;

public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Body edits and source movement preserve function conversion values without claiming final output caching.
    /// </summary>
    /// <param name="move">Whether to move the declaration instead of editing its implementation.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void FunctionModelsPreserveContractsAcrossImplementationEdits(bool move)
    {
        const string Source = "public static class Functions { [Ankus.PgFunction] public static int Value(int value = 7) => value + 1; }";
        CSharpCompilation initial = ModuleCompilation(Source);
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        string editedSource = move ? "\n\n" + Source : Source.Replace("value + 1", "value + 2", StringComparison.Ordinal);
        CSharpCompilation edited = initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(),
            CSharpSyntaxTree.ParseText(editedSource, path: "Module.cs", cancellationToken: context.CancellationToken));
        driver = RunModule(driver, edited, out Compilation second);

        Assert.AreEqual(move ? IncrementalStepRunReason.Unchanged : IncrementalStepRunReason.Cached, ModuleStep(driver, "FunctionModel"));
        Assert.AreEqual(InstallationBody(first), InstallationBody(second));
        Assert.Contains("FUNCTION \"value\"(\"value\" integer DEFAULT ((7)::integer))", InstallationBody(second));
    }

    /// <summary>
    /// Invalid duplicate C# signatures produce generator diagnostics instead of crashing identity lookup.
    /// </summary>
    [TestMethod]
    public void DuplicateFunctionDeclarationsDoNotCrashModelLookup()
    {
        CSharpCompilation compilation = ModuleCompilation("""
            public static class Functions
            {
                [Ankus.PgFunction] public static int Value(int value) => value;
                [Ankus.PgFunction] public static int Value(int other) => other + 1;
            }
            """);
        GeneratorDriver driver = ModuleDriver().RunGeneratorsAndUpdateCompilation(compilation, out _,
            out ImmutableArray<Diagnostic> diagnostics, context.CancellationToken);

        Assert.IsNull(Assert.ContainsSingle(driver.GetRunResult().Results).Exception);
        Assert.Contains(static value => value.Id == "ANKUS002", diagnostics);
        Assert.DoesNotContain(static value => value.Id == "CS8785", diagnostics);
    }

    /// <summary>
    /// Scalar and TABLE dispatcher rendering remains cached after body edits or source movement.
    /// </summary>
    /// <param name="move">Whether the declaration moves instead of changing its body.</param>
    /// <param name="table">Whether the function returns named iterator columns.</param>
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public void FunctionEmissionRemainsCachedAcrossImplementationEdits(bool move, bool table)
    {
        string source = FunctionCacheSource(table);
        CSharpCompilation initial = ModuleCompilation(source);
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        string replacement = move ? "\n\n" + source : source.Replace("value + 1", "value + 2", StringComparison.Ordinal);
        CSharpCompilation edited = initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(),
            CSharpSyntaxTree.ParseText(replacement, path: "Module.cs", cancellationToken: context.CancellationToken));
        driver = RunModule(driver, edited, out Compilation second);

        Assert.AreEqual(IncrementalStepRunReason.Cached, TrackedFunctionEmission(driver, "value").Reason);
        Assert.AreEqual(InstallationBody(first), InstallationBody(second));
        Assert.AreEqual(ManifestValue(first, "Ankus.NativeSource"), ManifestValue(second, "Ankus.NativeSource"));
        Assert.AreEqual(ManifestValue(first, "Ankus.Exports"), ManifestValue(second, "Ankus.Exports"));
        Assert.Contains(table ? "TABLE (\"number\" integer, \"label\" text)" : "RETURNS integer", InstallationBody(second));
    }

    /// <summary>
    /// Changes to a referenced precision constant rerender only the function whose managed conversion depends on it.
    /// </summary>
    [TestMethod]
    public void FunctionEmissionTracksDependentNumericConstants()
    {
        const string Source = """
            public static class Functions
            {
                [Ankus.PgFunction] public static decimal Value([Ankus.PgNumericPrecision(12, Options.Scale)] decimal value) => value;
                [Ankus.PgFunction] public static int Other(int value) => value;
            }
            """;
        CSharpCompilation initial = ModuleCompilation(Source).AddSyntaxTrees(CSharpSyntaxTree.ParseText(
            "public static class Options { public const int Scale = 2; }", path: "Options.cs", cancellationToken: context.CancellationToken));
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out _);
        FunctionEmission before = TrackedFunctionEmission(driver, "value").Emission;
        CSharpCompilation edited = initial.ReplaceSyntaxTree(initial.SyntaxTrees.Last(), CSharpSyntaxTree.ParseText(
            "public static class Options { public const int Scale = 3; }", path: "Options.cs", cancellationToken: context.CancellationToken));
        driver = RunModule(driver, edited, out Compilation second);
        (FunctionEmission emission, IncrementalStepRunReason reason) = TrackedFunctionEmission(driver, "value");

        Assert.AreEqual(IncrementalStepRunReason.Modified, reason);
        Assert.AreEqual(IncrementalStepRunReason.Cached, TrackedFunctionEmission(driver, "other").Reason);
        Assert.Contains(".ReadNumeric().Rescale(12, 2).ToDecimal()", before.Managed);
        Assert.Contains(".ReadNumeric().Rescale(12, 3).ToDecimal()", emission.Managed);
        Assert.AreEqual(before.Native, emission.Native);
        Assert.Contains("FUNCTION \"value\"(\"value\" numeric)", InstallationBody(second));
    }

    /// <summary>
    /// SQL execution-option edits update installation DDL while retaining the same cached dispatcher artifacts.
    /// </summary>
    [TestMethod]
    public void FunctionEmissionRemainsCachedAfterSqlOptionsChange()
    {
        const string Source = "public static class Functions { [Ankus.PgFunction(Volatility = Ankus.PgVolatility.Immutable)] public static int Value(int value) => value; }";
        CSharpCompilation initial = ModuleCompilation(Source);
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        CSharpCompilation edited = initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(
            Source.Replace("Immutable", "Stable", StringComparison.Ordinal), path: "Module.cs", cancellationToken: context.CancellationToken));
        driver = RunModule(driver, edited, out Compilation second);

        Assert.AreEqual(IncrementalStepRunReason.Cached, TrackedFunctionEmission(driver, "value").Reason);
        Assert.Contains("LANGUAGE c IMMUTABLE", InstallationBody(first));
        Assert.Contains("LANGUAGE c STABLE", InstallationBody(second));
        Assert.AreEqual(ManifestValue(first, "Ankus.NativeSource"), ManifestValue(second, "Ankus.NativeSource"));
    }

    /// <summary>
    /// Adding extension initialization changes the composed native entry while its scalar or TABLE boundary stays cached.
    /// </summary>
    /// <param name="table">Whether the function returns named iterator columns.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void FunctionEmissionComposesChangedInitialization(bool table)
    {
        CSharpCompilation initial = ModuleCompilation(FunctionCacheSource(table));
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        CSharpCompilation edited = initial.AddSyntaxTrees(CSharpSyntaxTree.ParseText(
            "public static class Lifecycle { [Ankus.PgInitialize] public static void Ready() { } }", path: "Lifecycle.cs", cancellationToken: context.CancellationToken));
        driver = RunModule(driver, edited, out Compilation second);
        (FunctionEmission emission, IncrementalStepRunReason reason) = TrackedFunctionEmission(driver, "value");
        string uninitialized = (emission.Native.Header + emission.Native.Body).ReplaceLineEndings("\n");
        string initialized = new StringBuilder(emission.Native.Header).AppendLine("    ankus_ensure_initialized();").Append(emission.Native.Body).ToString().ReplaceLineEndings("\n");

        Assert.AreEqual(IncrementalStepRunReason.Cached, reason);
        Assert.AreEqual(IncrementalStepRunReason.Cached, TrackedFunctionSqlEmission(driver, "value").Reason);
        Assert.Contains(uninitialized, ManifestValue(first, "Ankus.NativeSource"));
        Assert.DoesNotContain(initialized, ManifestValue(first, "Ankus.NativeSource"));
        Assert.Contains(initialized, ManifestValue(second, "Ankus.NativeSource"));
        Assert.DoesNotContain(uninitialized, ManifestValue(second, "Ankus.NativeSource"));
        Assert.AreEqual(InstallationBody(first), InstallationBody(second));
    }

    /// <summary>
    /// Explicit reference nullability changes invalidate the cached SQL input/result conversions and STRICT inference.
    /// </summary>
    [TestMethod]
    public void FunctionEmissionTracksNullability()
    {
        const string Source = "#nullable enable\npublic static class Functions { [Ankus.PgFunction] public static string Value(string value) => value; }";
        CSharpCompilation initial = ModuleCompilation(Source);
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        FunctionEmission before = TrackedFunctionEmission(driver, "value").Emission;
        CSharpCompilation edited = initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(
            Source.Replace("string", "string?", StringComparison.Ordinal), path: "Module.cs", cancellationToken: context.CancellationToken));
        driver = RunModule(driver, edited, out Compilation second);
        (FunctionEmission emission, IncrementalStepRunReason reason) = TrackedFunctionEmission(driver, "value");

        Assert.AreEqual(IncrementalStepRunReason.Modified, reason);
        Assert.Contains(" STRICT ", InstallationBody(first));
        Assert.Contains(" CALLED ON NULL INPUT ", InstallationBody(second));
        Assert.Contains("if (PG_ARGISNULL(0))", before.Native.Body);
        Assert.DoesNotContain("if (PG_ARGISNULL(0))", emission.Native.Body);
        Assert.AreNotEqual(before.Managed, emission.Managed);
    }

    /// <summary>
    /// Cached ambiguity diagnostics attach to current source trees and disappear after the SQL reference contract is repaired.
    /// </summary>
    /// <param name="move">Whether diagnostics move instead of retaining identical source coordinates.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void FunctionDiagnosticsFollowCurrentTreesAndRecover(bool move)
    {
        const string Source = "#nullable disable\npublic static class Functions { [Ankus.PgFunction] public static string Value(string value) => value; }";
        CSharpCompilation initial = ModuleCompilation(Source);
        GeneratorDriver driver = ModuleDriver().RunGeneratorsAndUpdateCompilation(initial, out _, out _, context.CancellationToken);
        string replacement = move ? "\n\n" + Source : Source.Replace("=> value", "=> value /* edit */", StringComparison.Ordinal);
        SyntaxTree current = CSharpSyntaxTree.ParseText(replacement, path: "Current.cs", cancellationToken: context.CancellationToken);
        CSharpCompilation edited = initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), current);
        driver = driver.RunGeneratorsAndUpdateCompilation(edited, out _, out ImmutableArray<Diagnostic> diagnostics, context.CancellationToken);
        Diagnostic[] ambiguity = [.. diagnostics.Where(static value => value.Id == "ANKUS024")];

        Assert.AreEqual(IncrementalStepRunReason.Cached, ModuleStep(driver, "FunctionEmission"));
        Assert.HasCount(2, ambiguity);
        Assert.IsNull(Assert.ContainsSingle(driver.GetRunResult().Results).Exception);
        foreach (Diagnostic diagnostic in ambiguity)
        {
            Assert.AreSame(current, diagnostic.Location.SourceTree);
            Assert.AreEqual("Current.cs", diagnostic.Location.GetLineSpan().Path);
            Assert.AreEqual(move ? 3 : 1, diagnostic.Location.GetLineSpan().StartLinePosition.Line);
            Assert.AreEqual("string", current.GetText(context.CancellationToken).ToString(diagnostic.Location.SourceSpan));
        }

        CSharpCompilation repaired = edited.ReplaceSyntaxTree(current, CSharpSyntaxTree.ParseText(
            replacement.Replace("#nullable disable", "#nullable enable", StringComparison.Ordinal), path: "Current.cs", cancellationToken: context.CancellationToken));
        driver = RunModule(driver, repaired, out Compilation output);
        Assert.AreEqual(IncrementalStepRunReason.Modified, TrackedFunctionEmission(driver, "value").Reason);
        Assert.Contains("FUNCTION \"value\"(\"value\" text)", InstallationBody(output));
    }

    /// <summary>
    /// Removing a function drops its SQL and exports while preserving the surviving contract across positional changes.
    /// </summary>
    /// <param name="removeFirst">Whether removal shifts Roslyn's source-table position for the surviving function.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void FunctionRemovalPreservesSurvivorContracts(bool removeFirst)
    {
        const string Source = "public static class First { [Ankus.PgFunction] public static int Value() => 1; }";
        CSharpCompilation initial = ModuleCompilation(Source).AddSyntaxTrees(CSharpSyntaxTree.ParseText(
            "public static class Second { [Ankus.PgFunction] public static int Other() => 2; }", path: "Second.cs", cancellationToken: context.CancellationToken));
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out _);
        string removedName = removeFirst ? "value" : "other";
        string survivorName = removeFirst ? "other" : "value";
        FunctionEmission removed = TrackedFunctionEmission(driver, removedName).Emission;
        FunctionEmission survivor = TrackedFunctionEmission(driver, survivorName).Emission;
        FunctionSqlEmission survivorSql = TrackedFunctionSqlEmission(driver, survivorName).Emission;
        CSharpCompilation edited = initial.RemoveSyntaxTrees(removeFirst ? initial.SyntaxTrees.First() : initial.SyntaxTrees.Last());
        driver = RunModule(driver, edited, out Compilation output);

        (FunctionEmission emission, IncrementalStepRunReason reason) = TrackedFunctionEmission(driver, survivorName);
        Assert.AreEqual(removeFirst ? IncrementalStepRunReason.Modified : IncrementalStepRunReason.Cached, reason);
        Assert.AreEqual(survivor, emission);
        Assert.AreEqual(removeFirst ? IncrementalStepRunReason.Modified : IncrementalStepRunReason.Cached, TrackedFunctionSqlEmission(driver, survivorName).Reason);
        Assert.AreEqual(survivorSql, TrackedFunctionSqlEmission(driver, survivorName).Emission);
        Assert.DoesNotContain(removed.NativeName, ManifestValue(output, "Ankus.NativeSource"));
        Assert.DoesNotContain(removed.NativeName, ManifestValue(output, "Ankus.Exports"));
        Assert.DoesNotContain("\"" + removedName + "\"", InstallationBody(output));
        Assert.Contains("FUNCTION \"" + survivorName + "\"()", InstallationBody(output));
    }

    /// <summary>
    /// Provides matching scalar and named TABLE declarations for cache and initialization tests.
    /// </summary>
    /// <param name="table">Whether to return a named tuple iterator.</param>
    /// <returns>A complete attributed declaration with independently checked SQL output.</returns>
    private static string FunctionCacheSource(bool table)
        => table ? "#nullable enable\npublic static class Functions { [Ankus.PgFunction] public static System.Collections.Generic.IEnumerable<(int Number, string? Label)> Value(int value = 7) => new[] { (value + 1, (string?)\"a\") }; }"
            : "public static class Functions { [Ankus.PgFunction] public static int Value(int value = 7) => value + 1; }";

    /// <summary>
    /// Reads an individual production function's actual render result and cache decision.
    /// </summary>
    /// <param name="driver">The executed production generator.</param>
    /// <param name="sqlName">The function's conventional SQL name.</param>
    /// <returns>The cached artifact and its observed incremental decision.</returns>
    private static (FunctionEmission Emission, IncrementalStepRunReason Reason) TrackedFunctionEmission(GeneratorDriver driver, string sqlName)
    {
        (object value, IncrementalStepRunReason reason) = Assert.ContainsSingle(Assert.ContainsSingle(driver.GetRunResult().Results)
            .TrackedSteps["FunctionEmission"].SelectMany(static step => step.Outputs).Where(output =>
                output.Reason != IncrementalStepRunReason.Removed && output.Value is FunctionEmission emission &&
                emission.NativeName.EndsWith("_" + sqlName, StringComparison.Ordinal)));
        return (Assert.IsInstanceOfType<FunctionEmission>(value), reason);
    }
}
