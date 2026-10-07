using System.Collections.Immutable;
using Ankus.PgConfig;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Ankus.Generators.Tests;

/// <summary>
/// Verifies detached method selection and native capability metadata against current compiled and graph behavior.
/// </summary>
public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Body edits and source movement retain method contracts and rendered boundaries while compiled code stays current.
    /// </summary>
    /// <param name="move">Whether to move source instead of editing its implementation.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void MethodInventoryPreservesContractsAcrossIndependentEdits(bool move)
    {
        const string Source = "public static class Functions { [Ankus.PgFunction] public static int Answer() => 42; }";
        CSharpCompilation initial = ModuleCompilation(Source);
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        MethodInventoryModel before = Assert.ContainsSingle(MethodInventory(driver));
        driver = RunModule(driver, initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(
            move ? "\n\n" + Source : Source.Replace("=> 42;", "=> 43;", StringComparison.Ordinal),
            path: move ? "Changed.cs" : "Module.cs", cancellationToken: context.CancellationToken)), out Compilation second);
        MethodInventoryModel after = Assert.ContainsSingle(MethodInventory(driver));

        Assert.AreEqual(before with { Location = null }, after with { Location = null });
        Assert.AreEqual((before with { Location = null }).GetHashCode(), (after with { Location = null }).GetHashCode());
        Assert.AreEqual(move ? IncrementalStepRunReason.Modified : IncrementalStepRunReason.Unchanged, ModuleStep(driver, "MethodInventoryAnalysis"));
        Assert.AreEqual(IncrementalStepRunReason.Cached, TrackedFunctionEmission(driver, "answer").Reason);
        Assert.AreEqual(InstallationBody(first), InstallationBody(second));
        Assert.AreEqual(ManifestValue(first, "Ankus.NativeSource"), ManifestValue(second, "Ankus.NativeSource"));
        Assert.AreEqual(42, InvokeSqlReferenceAnswer(first));
        Assert.AreEqual(move ? 42 : 43, InvokeSqlReferenceAnswer(second));
        Assert.Contains("Functions.Answer()", Assert.ContainsSingle(ExtensionSchemaGraph.Parse(ManifestValue(second, "Ankus.SqlGraph")).Items).Names);
    }

    /// <summary>
    /// Specialized roles retain exact native support and SQL exclusion while backend discovery stays available in either publication mode.
    /// </summary>
    /// <param name="includeTests">Whether native test functions are enabled.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void MethodInventoryPreservesSpecializedRoles(bool includeTests)
    {
        CSharpCompilation initial = ModuleCompilation("""
            #nullable enable
            public static partial class Functions
            {
                [Ankus.PgFunction] public static int Answer() => 42;
                [Ankus.PgFunction] public static System.Collections.Generic.IEnumerable<int> Series() => [1, 2];
                [Ankus.PgFunction][return: Ankus.PgSqlType("integer")]
                public static Ankus.PgDatum Raw([Ankus.PgSqlType("integer")] Ankus.PgDatum value) => value;
                [Ankus.PgTrigger] public static Ankus.PgHeapTuple? Row(Ankus.PgTriggerContext context) => null;
                [Ankus.PgEventTrigger] public static void Event(Ankus.PgEventTriggerContext context) { }
                [Ankus.PgInitialize] public static void Start() { }
                [Ankus.PgModuleLoad] public static void Load() { }
                [Ankus.PgBackgroundWorker(EntryPoint="InventoryWorker")] public static void Worker(nuint argument) { }
                [Ankus.PgTest] public static void Backend() { }
            }
            """);
        GeneratorDriver driver = RunModule(PgTestDriver(includeTests), initial, out Compilation output);
        EquatableArray<MethodInventoryModel> methods = MethodInventory(driver);
        Assert.HasCount(6, methods);
        Assert.IsTrue(Assert.ContainsSingle(methods.Where(static method => method.Name == "Series")).Sequence);
        Assert.IsTrue(Assert.ContainsSingle(methods.Where(static method => method.Name == "Raw")).RawTransport);
        Assert.IsTrue(Assert.ContainsSingle(methods.Where(static method => method.Name == "Row")).Trigger);
        Assert.IsTrue(Assert.ContainsSingle(methods.Where(static method => method.Name == "Event")).EventTrigger);
        Assert.DoesNotContain("Start", methods.Select(static method => method.Name));
        Assert.DoesNotContain("Load", methods.Select(static method => method.Name));
        Assert.DoesNotContain("Worker", methods.Select(static method => method.Name));
        Assert.IsTrue(Assert.ContainsSingle(methods.Where(static method => method.Name == "Backend")).Test);
        ExtensionSchemaGraph graph = ExtensionSchemaGraph.Parse(ManifestValue(output, "Ankus.SqlGraph"));
        Assert.HasCount(includeTests ? 6 : 5, graph.Items);
        Assert.IsEmpty(graph.Items.Where(static item => item.Names.Contains("Start") || item.Names.Contains("Load") || item.Names.Contains("Worker")));
        Assert.AreEqual(includeTests ? 1 : 0, graph.Items.Count(static item => item.Names.Contains("Backend")));
        Assert.IsNotNull(output.GetTypeByMetadataName("Functions+PostgresTests"));
        Assert.Contains("InventoryWorker", ManifestValue(output, "Ankus.Exports"));
        Assert.Contains("RETURNS SETOF integer", InstallationBody(output));
        Assert.Contains("RETURNS trigger", InstallationBody(output));
        Assert.Contains("RETURNS event_trigger", InstallationBody(output));
        Assert.AreEqual(42, InvokeSqlReferenceAnswer(output));
    }

    /// <summary>
    /// Conflicting ordinary and host callback roles retain their existing diagnostics and do not become SQL exports after repair.
    /// </summary>
    /// <param name="role">The conflicting initialization or worker role.</param>
    [TestMethod]
    [DataRow("initialize")]
    [DataRow("module")]
    [DataRow("worker")]
    public void MethodInventoryConflictingHostRolesReportAndRecover(string role)
    {
        string marker = role switch
        {
            "initialize" => "[Ankus.PgInitialize]",
            "module" => "[Ankus.PgModuleLoad]",
            _ => "[Ankus.PgBackgroundWorker]",
        };
        string conflict = "[Ankus.PgFunction]" + marker + " public static void Host(" + (role == "worker" ? "nuint argument" : string.Empty) + ") { }";
        string source = "public static class Functions { [Ankus.PgFunction] public static int Answer() => 42; " + conflict + " }";
        CSharpCompilation initial = ModuleCompilation(source);
        GeneratorDriver driver = ModuleDriver().RunGeneratorsAndUpdateCompilation(initial, out _, out ImmutableArray<Diagnostic> errors, context.CancellationToken);
        Assert.AreEqual(role == "worker" ? "ANKUS269" : "ANKUS247", Assert.ContainsSingle(errors).Id);
        MethodInventoryModel selected = Assert.ContainsSingle(MethodInventory(driver).Where(static method => method.Name == "Host"));
        Assert.AreEqual(role == "worker", selected.Worker);
        Assert.AreEqual(role != "worker", selected.Initializer);

        driver = RunModule(driver, initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(
            source.Replace(conflict, conflict.Replace("[Ankus.PgFunction]", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal),
            path: "Module.cs", cancellationToken: context.CancellationToken)), out Compilation repaired);
        Assert.AreEqual("Answer", Assert.ContainsSingle(MethodInventory(driver)).Name);
        Assert.DoesNotContain("CREATE FUNCTION \"host\"", InstallationBody(repaired));
        Assert.AreEqual(42, InvokeSqlReferenceAnswer(repaired));
    }

    /// <summary>
    /// Overload identities bind exact typed references and removal retains the surviving graph and managed execution.
    /// </summary>
    [TestMethod]
    public void MethodInventoryOverloadsKeepExactGraphIdentityAfterRemoval()
    {
        const string Source = """
            public static class Functions
            {
                [Ankus.PgFunction] public static int Echo(int value) => value;
                [Ankus.PgFunction] public static long Echo(long value) => value;
                [Ankus.PgFunction][Ankus.PgRequires(typeof(Functions), nameof(Echo), ParameterTypes=[typeof(int)])]
                public static int Answer() => 42;
            }
            """;
        CSharpCompilation initial = ModuleCompilation(Source);
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        MethodInventoryModel[] overloads = [.. MethodInventory(driver).Where(static method => method.Name == "Echo")];
        Assert.HasCount(2, overloads);
        Assert.AreNotEqual(overloads[0].Identity, overloads[1].Identity);
        ExtensionSchemaGraph graph = ExtensionSchemaGraph.Parse(ManifestValue(first, "Ankus.SqlGraph"));
        ExtensionSchemaItem selected = Assert.ContainsSingle(graph.Items.Where(static item => item.Names.Contains("Functions.Echo(int)")));
        Assert.AreEqual(selected.Id, Assert.ContainsSingle(Assert.ContainsSingle(graph.Items.Where(static item => item.Names.Contains("answer"))).Dependencies));
        driver = RunModule(driver, initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(
            Source.Replace("[Ankus.PgFunction] public static long Echo(long value) => value;", string.Empty, StringComparison.Ordinal),
            path: "Module.cs", cancellationToken: context.CancellationToken)), out Compilation second);

        Assert.HasCount(2, MethodInventory(driver));
        ExtensionSchemaGraph surviving = ExtensionSchemaGraph.Parse(ManifestValue(second, "Ankus.SqlGraph"));
        Assert.HasCount(2, surviving.Items);
        Assert.AreEqual(selected.Id, Assert.ContainsSingle(Assert.ContainsSingle(surviving.Items.Where(static item => item.Names.Contains("answer"))).Dependencies));
        Assert.DoesNotContain("RETURNS bigint", InstallationBody(second));
        Assert.AreEqual(42, InvokeSqlReferenceAnswer(second));
    }

    /// <summary>
    /// Invalid scalar and sequence signatures retain current diagnostics and compiled recovery after the legacy fallback is removed.
    /// </summary>
    /// <param name="sequence">Whether the unsupported result is a sequence.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void MethodInventoryInvalidSignaturesFollowCurrentTreesAndRecover(bool sequence)
    {
        string source = "public static class Functions { [Ankus.PgFunction] public static " +
            (sequence ? "System.Collections.Generic.IEnumerable<Missing>" : "Missing") + " Answer() => default!; }";
        CSharpCompilation initial = ModuleCompilation(source);
        GeneratorDriver driver = ModuleDriver().RunGeneratorsAndUpdateCompilation(initial, out _, out ImmutableArray<Diagnostic> previous, context.CancellationToken);
        Assert.AreEqual(sequence ? "ANKUS397" : "ANKUS039", Assert.ContainsSingle(previous).Id);
        SyntaxTree tree = CSharpSyntaxTree.ParseText(source + "\n// independent edit", path: "Module.cs", cancellationToken: context.CancellationToken);
        CSharpCompilation edited = initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), tree);
        driver = driver.RunGeneratorsAndUpdateCompilation(edited, out _, out ImmutableArray<Diagnostic> errors, context.CancellationToken);
        Assert.AreSequenceEqual(previous.Select(static diagnostic => diagnostic.Id), errors.Select(static diagnostic => diagnostic.Id));
        Assert.AreSame(tree, Assert.ContainsSingle(errors).Location.SourceTree);
        Assert.AreEqual(IncrementalStepRunReason.Unchanged, ModuleStep(driver, "MethodInventoryAnalysis"));

        driver = RunModule(driver, edited.ReplaceSyntaxTree(tree, CSharpSyntaxTree.ParseText(
            "public static class Functions { [Ankus.PgFunction] public static int Answer() => 42; }",
            path: "Module.cs", cancellationToken: context.CancellationToken)), out Compilation repaired);
        Assert.IsFalse(Assert.ContainsSingle(MethodInventory(driver)).Sequence);
        Assert.AreEqual(42, InvokeSqlReferenceAnswer(repaired));
    }

    /// <summary>
    /// Removing the final raw signature removes its native capability without disturbing the ordinary consumer boundary.
    /// </summary>
    [TestMethod]
    public void MethodInventoryRemovalUpdatesNativeCapabilities()
    {
        const string Raw = "[Ankus.PgFunction][return: Ankus.PgSqlType(\"integer\")] public static Ankus.PgDatum Raw([Ankus.PgSqlType(\"integer\")] Ankus.PgDatum value) => value;";
        string source = "public static class Functions { [Ankus.PgFunction] public static int Answer() => 42; " + Raw + " }";
        CSharpCompilation initial = ModuleCompilation(source);
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        Assert.IsTrue(Assert.ContainsSingle(MethodInventory(driver).Where(static method => method.Name == "Raw")).RawTransport);
        driver = RunModule(driver, initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(
            source.Replace(Raw, string.Empty, StringComparison.Ordinal), path: "Module.cs", cancellationToken: context.CancellationToken)), out Compilation second);

        Assert.IsFalse(Assert.ContainsSingle(MethodInventory(driver)).RawTransport);
        Assert.AreEqual(IncrementalStepRunReason.Cached, TrackedFunctionEmission(driver, "answer").Reason);
        Assert.AreNotEqual(ManifestValue(first, "Ankus.NativeSource"), ManifestValue(second, "Ankus.NativeSource"));
        Assert.DoesNotContain("CREATE FUNCTION \"raw\"", InstallationBody(second));
        Assert.AreEqual(42, InvokeSqlReferenceAnswer(second));
    }

    /// <summary>
    /// Reads the actual production immutable method inventory.
    /// </summary>
    private static EquatableArray<MethodInventoryModel> MethodInventory(GeneratorDriver driver)
        => Assert.IsInstanceOfType<EquatableArray<MethodInventoryModel>>(Assert.ContainsSingle(Assert.ContainsSingle(
            Assert.ContainsSingle(driver.GetRunResult().Results).TrackedSteps["MethodInventoryAnalysis"]).Outputs).Value);
}
