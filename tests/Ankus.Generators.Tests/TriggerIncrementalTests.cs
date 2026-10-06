using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Ankus.Generators.Tests;

public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Row and event boundary and SQL renderers reuse exact contracts after implementation edits or source movement.
    /// </summary>
    /// <param name="eventTrigger">Whether to declare an event callback.</param>
    /// <param name="move">Whether the declaration moves rather than changing its body.</param>
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void TriggerBoundariesRemainCachedAcrossImplementationEdits(bool eventTrigger, bool move)
    {
        string source = TriggerCacheSource(eventTrigger);
        CSharpCompilation initial = ModuleCompilation(source);
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        FunctionEmission original = TrackedTriggerEmission(driver, eventTrigger, "value").Emission;
        string replacement = move ? "\n\n" + source : eventTrigger ?
            source.Replace("{ }", "{ _ = context.CommandTag; }", StringComparison.Ordinal) :
            source.Replace("=> null", "=> context.New", StringComparison.Ordinal);
        CSharpCompilation edited = initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(
            replacement, path: "Module.cs", cancellationToken: context.CancellationToken));
        driver = RunModule(driver, edited, out Compilation second);
        (FunctionEmission emission, IncrementalStepRunReason reason) = TrackedTriggerEmission(driver, eventTrigger, "value");

        Assert.AreEqual(IncrementalStepRunReason.Cached, reason);
        Assert.AreEqual(original, emission);
        Assert.AreEqual(IncrementalStepRunReason.Cached, TrackedTriggerSql(driver, eventTrigger, "value").Reason);
        Assert.AreEqual(InstallationBody(first), InstallationBody(second));
        Assert.Contains("FUNCTION \"value\"()\nRETURNS " + (eventTrigger ? "event_trigger" : "trigger"), InstallationBody(second).ReplaceLineEndings("\n"));
        Assert.AreEqual(ManifestValue(first, "Ankus.NativeSource"), ManifestValue(second, "Ankus.NativeSource"));
        Assert.AreEqual(ManifestValue(first, "Ankus.Exports"), ManifestValue(second, "Ankus.Exports"));
    }

    /// <summary>
    /// Dependent execution options update trigger SQL without rerendering boundaries or unrelated SQL.
    /// </summary>
    /// <param name="eventTrigger">Whether to declare event callbacks.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void TriggerSqlOptionsPreserveBoundaryCache(bool eventTrigger)
    {
        CSharpCompilation initial = ModuleCompilation(TriggerCacheSource(eventTrigger, "[Ankus.PgFunction(Cost = Options.Cost)]"))
            .AddSyntaxTrees(CSharpSyntaxTree.ParseText(TriggerCacheSource(eventTrigger).Replace("Value", "Other", StringComparison.Ordinal),
                path: "Other.cs", cancellationToken: context.CancellationToken), CSharpSyntaxTree.ParseText(
                "public static class Options { public const double Cost = 1; }", path: "Options.cs", cancellationToken: context.CancellationToken));
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        CSharpCompilation edited = initial.ReplaceSyntaxTree(initial.SyntaxTrees.Last(), CSharpSyntaxTree.ParseText(
            "public static class Options { public const double Cost = 2.5; }", path: "Options.cs", cancellationToken: context.CancellationToken));
        driver = RunModule(driver, edited, out Compilation second);

        Assert.AreEqual(IncrementalStepRunReason.Modified, TrackedTriggerSql(driver, eventTrigger, "value").Reason);
        Assert.AreEqual(IncrementalStepRunReason.Cached, TrackedTriggerSql(driver, eventTrigger, "other").Reason);
        Assert.AreEqual(IncrementalStepRunReason.Cached, TrackedTriggerEmission(driver, eventTrigger, "value").Reason);
        Assert.Contains("COST 1;", InstallationBody(first));
        Assert.Contains("COST 2.5;", InstallationBody(second));
        Assert.AreEqual(ManifestValue(first, "Ankus.NativeSource"), ManifestValue(second, "Ankus.NativeSource"));
    }

    /// <summary>
    /// Managed invocation renames invalidate cached entry identities even when the authored SQL name remains unchanged.
    /// </summary>
    /// <param name="eventTrigger">Whether to declare an event callback.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void TriggerBoundariesTrackInvocationRenames(bool eventTrigger)
    {
        string source = TriggerCacheSource(eventTrigger, "[Ankus.PgFunction(Name = \"value\")]");
        CSharpCompilation initial = ModuleCompilation(source);
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out _);
        FunctionEmission previous = TrackedTriggerEmission(driver, eventTrigger, "value").Emission;
        CSharpCompilation edited = initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(
            source.Replace("Value(", "Renamed(", StringComparison.Ordinal), path: "Module.cs", cancellationToken: context.CancellationToken));
        driver = RunModule(driver, edited, out Compilation output);
        (FunctionEmission emission, IncrementalStepRunReason reason) = TrackedTriggerEmission(driver, eventTrigger, "value");

        Assert.AreEqual(IncrementalStepRunReason.Modified, reason);
        Assert.AreEqual(IncrementalStepRunReason.Modified, TrackedTriggerSql(driver, eventTrigger, "value").Reason);
        Assert.Contains("global::Functions.@Renamed(context);", emission.Managed);
        Assert.AreNotEqual(previous.NativeName, emission.NativeName);
        Assert.DoesNotContain(previous.NativeName, ManifestValue(output, "Ankus.Exports"));
        Assert.Contains("'" + emission.NativeName + "'", InstallationBody(output));
        Assert.Contains("FUNCTION \"value\"()", InstallationBody(output));
    }

    /// <summary>
    /// Inherited schema edits invalidate only SQL while retaining the callback boundary and current graph order.
    /// </summary>
    /// <param name="eventTrigger">Whether to declare an event callback.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void TriggerSqlTracksInheritedSchema(bool eventTrigger)
    {
        string source = TriggerCacheSource(eventTrigger).Replace("public static partial class", "[Ankus.PgSchema(\"original\")] public static partial class", StringComparison.Ordinal);
        CSharpCompilation initial = ModuleCompilation(source);
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        CSharpCompilation edited = initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(
            source.Replace("\"original\"", "\"current\"", StringComparison.Ordinal), path: "Module.cs", cancellationToken: context.CancellationToken));
        driver = RunModule(driver, edited, out Compilation second);

        Assert.AreEqual(IncrementalStepRunReason.Cached, TrackedTriggerEmission(driver, eventTrigger, "value").Reason);
        Assert.AreEqual(IncrementalStepRunReason.Modified, TrackedTriggerSql(driver, eventTrigger, "value").Reason);
        Assert.Contains("FUNCTION \"original\".\"value\"()", InstallationBody(first));
        Assert.StartsWith("CREATE SCHEMA IF NOT EXISTS \"current\";\nCREATE FUNCTION \"current\".\"value\"()", InstallationBody(second).ReplaceLineEndings("\n"));
        Assert.DoesNotContain("\"original\"", InstallationBody(second));
        Assert.AreEqual(ManifestValue(first, "Ankus.NativeSource"), ManifestValue(second, "Ankus.NativeSource"));
    }

    /// <summary>
    /// Current initialization is inserted before trigger backend work after an unchanged boundary cache hit.
    /// </summary>
    /// <param name="eventTrigger">Whether to declare an event callback.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void TriggerBoundariesComposeCurrentInitialization(bool eventTrigger)
    {
        CSharpCompilation initial = ModuleCompilation(TriggerCacheSource(eventTrigger));
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        FunctionEmission original = TrackedTriggerEmission(driver, eventTrigger, "value").Emission;
        CSharpCompilation edited = initial.AddSyntaxTrees(CSharpSyntaxTree.ParseText(
            "public static class Startup { [Ankus.PgInitialize] public static void Initialize() { } }",
            path: "Startup.cs", cancellationToken: context.CancellationToken));
        driver = RunModule(driver, edited, out Compilation second);

        Assert.AreEqual(IncrementalStepRunReason.Cached, TrackedTriggerEmission(driver, eventTrigger, "value").Reason);
        Assert.AreEqual(IncrementalStepRunReason.Cached, TrackedTriggerSql(driver, eventTrigger, "value").Reason);
        string call = eventTrigger ? "ankus_event_trigger_call" : "ankus_trigger_call";
        string signature = $"PGDLLEXPORT Datum {original.NativeName}(PG_FUNCTION_ARGS)\n{{\n";
        Assert.Contains(signature + $"    return {call}", ManifestValue(first, "Ankus.NativeSource").ReplaceLineEndings("\n"));
        Assert.Contains(signature + $"    ankus_ensure_initialized();\n    return {call}", ManifestValue(second, "Ankus.NativeSource").ReplaceLineEndings("\n"));
        Assert.DoesNotContain("ankus_ensure_initialized", original.Native.Body);
        Assert.AreEqual(InstallationBody(first), InstallationBody(second));
    }

    /// <summary>
    /// Failed signature diagnostics use current trees and coordinates, then disappear after repair.
    /// </summary>
    /// <param name="eventTrigger">Whether to declare an event callback.</param>
    /// <param name="move">Whether diagnostics move or retain identical coordinates.</param>
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void TriggerDiagnosticsFollowCurrentTreesAndRecover(bool eventTrigger, bool move)
    {
        string valid = TriggerCacheSource(eventTrigger);
        string source = eventTrigger ? valid.Replace("static void", "static int", StringComparison.Ordinal).Replace("{ }", "=> 1;", StringComparison.Ordinal) :
            valid.Replace("Ankus.PgHeapTuple?", "string?", StringComparison.Ordinal);
        CSharpCompilation initial = ModuleCompilation(source);
        GeneratorDriver driver = ModuleDriver().RunGeneratorsAndUpdateCompilation(initial, out _, out ImmutableArray<Diagnostic> original, context.CancellationToken);
        Diagnostic previous = Assert.ContainsSingle(original);
        string replacement = move ? "\n\n" + source : source + "\n// edit";
        SyntaxTree current = CSharpSyntaxTree.ParseText(replacement, path: "Current.cs", cancellationToken: context.CancellationToken);
        driver = driver.RunGeneratorsAndUpdateCompilation(initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), current),
            out _, out ImmutableArray<Diagnostic> diagnostics, context.CancellationToken);
        Diagnostic error = Assert.ContainsSingle(diagnostics);

        Assert.AreEqual("ANKUS214", error.Id);
        Assert.AreSame(current, error.Location.SourceTree);
        Assert.AreEqual("Current.cs", error.Location.GetLineSpan().Path);
        Assert.AreEqual(move ? 3 : 1, error.Location.GetLineSpan().StartLinePosition.Line);
        Assert.AreEqual(eventTrigger ? "int" : "string?", current.GetText(context.CancellationToken).ToString(error.Location.SourceSpan));
        Assert.AreEqual(previous.GetMessage(System.Globalization.CultureInfo.InvariantCulture), error.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
        Assert.IsNull(Assert.ContainsSingle(driver.GetRunResult().Results).Exception);
        Assert.AreEqual(IncrementalStepRunReason.Cached, ModuleStep(driver, (eventTrigger ? "EventTrigger" : "Trigger") + "Emission"));

        CSharpCompilation repaired = initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(
            move ? "\n\n" + valid : valid + "\n// edit", path: "Current.cs", cancellationToken: context.CancellationToken));
        driver = RunModule(driver, repaired, out Compilation output);
        Assert.AreEqual(IncrementalStepRunReason.Modified, TrackedTriggerEmission(driver, eventTrigger, "value").Reason);
        Assert.Contains("FUNCTION \"value\"()", InstallationBody(output));
    }

    /// <summary>
    /// Removal drops deleted SQL and exports while preserving exact row/event survivor contracts across input shifts.
    /// </summary>
    /// <param name="eventTrigger">Whether to declare event callbacks.</param>
    /// <param name="removeFirst">Whether removal changes the survivor's source-table position.</param>
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void TriggerRemovalPreservesSurvivorContracts(bool eventTrigger, bool removeFirst)
    {
        CSharpCompilation initial = ModuleCompilation(TriggerCacheSource(eventTrigger)).AddSyntaxTrees(CSharpSyntaxTree.ParseText(
            TriggerCacheSource(eventTrigger).Replace("Value", "Other", StringComparison.Ordinal), path: "Other.cs", cancellationToken: context.CancellationToken));
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out _);
        string survivorName = removeFirst ? "other" : "value";
        string removedName = removeFirst ? "value" : "other";
        FunctionEmission survivor = TrackedTriggerEmission(driver, eventTrigger, survivorName).Emission;
        FunctionSqlEmission survivorSql = TrackedTriggerSql(driver, eventTrigger, survivorName).Emission;
        FunctionEmission removed = TrackedTriggerEmission(driver, eventTrigger, removedName).Emission;
        driver = RunModule(driver, initial.RemoveSyntaxTrees(removeFirst ? initial.SyntaxTrees.First() : initial.SyntaxTrees.Last()), out Compilation output);

        Assert.AreEqual(survivor, TrackedTriggerEmission(driver, eventTrigger, survivorName).Emission);
        Assert.AreEqual(survivorSql, TrackedTriggerSql(driver, eventTrigger, survivorName).Emission);
        Assert.AreEqual(removeFirst ? IncrementalStepRunReason.Modified : IncrementalStepRunReason.Cached, TrackedTriggerEmission(driver, eventTrigger, survivorName).Reason);
        Assert.DoesNotContain(removed.NativeName, ManifestValue(output, "Ankus.NativeSource"));
        Assert.DoesNotContain(removed.NativeName, ManifestValue(output, "Ankus.Exports"));
        Assert.DoesNotContain("\"" + removedName + "\"", InstallationBody(output));
        Assert.Contains("FUNCTION \"" + survivorName + "\"()", InstallationBody(output));
    }

    /// <summary>
    /// Supplies matching row/event callback declarations with optional common SQL metadata.
    /// </summary>
    /// <param name="eventTrigger">Whether to use the event callback signature.</param>
    /// <param name="attributes">The optional common function attributes.</param>
    /// <returns>A complete nullable-enabled callback declaration.</returns>
    private static string TriggerCacheSource(bool eventTrigger, string attributes = "")
        => "#nullable enable\npublic static partial class Functions { " + attributes + (eventTrigger ?
            "[Ankus.PgEventTrigger] public static void Value(Ankus.PgEventTriggerContext context) { } }" :
            "[Ankus.PgTrigger] public static Ankus.PgHeapTuple? Value(Ankus.PgTriggerContext context) => null; }");

    /// <summary>
    /// Reads the real production boundary artifact and its tracked reuse decision.
    /// </summary>
    /// <param name="driver">The executed production generator.</param>
    /// <param name="eventTrigger">Whether to inspect event callback stages.</param>
    /// <param name="name">The exact conventional SQL leaf name.</param>
    /// <returns>The rendered boundary and cache decision.</returns>
    private static (FunctionEmission Emission, IncrementalStepRunReason Reason) TrackedTriggerEmission(GeneratorDriver driver, bool eventTrigger, string name)
    {
        (object value, IncrementalStepRunReason reason) = Assert.ContainsSingle(Assert.ContainsSingle(driver.GetRunResult().Results)
            .TrackedSteps[(eventTrigger ? "EventTrigger" : "Trigger") + "Emission"].SelectMany(static step => step.Outputs).Where(output =>
                output.Reason != IncrementalStepRunReason.Removed && output.Value is FunctionEmission emission &&
                emission.NativeName.EndsWith("_" + name, StringComparison.Ordinal)));
        return (Assert.IsInstanceOfType<FunctionEmission>(value), reason);
    }

    /// <summary>
    /// Reads the real production zero-argument SQL fragments and tracked reuse decision.
    /// </summary>
    /// <param name="driver">The executed production generator.</param>
    /// <param name="eventTrigger">Whether to inspect event callback stages.</param>
    /// <param name="name">The exact conventional SQL leaf name.</param>
    /// <returns>The rendered SQL fragments and cache decision.</returns>
    private static (FunctionSqlEmission Emission, IncrementalStepRunReason Reason) TrackedTriggerSql(GeneratorDriver driver, bool eventTrigger, string name)
    {
        (object value, IncrementalStepRunReason reason) = Assert.ContainsSingle(Assert.ContainsSingle(driver.GetRunResult().Results)
            .TrackedSteps[(eventTrigger ? "EventTrigger" : "Trigger") + "SqlEmission"].SelectMany(static step => step.Outputs).Where(output =>
                output.Reason != IncrementalStepRunReason.Removed && output.Value is FunctionSqlEmission emission &&
                emission.Header.EndsWith("\"" + name + "\"(", StringComparison.Ordinal)));
        return (Assert.IsInstanceOfType<FunctionSqlEmission>(value), reason);
    }
}
