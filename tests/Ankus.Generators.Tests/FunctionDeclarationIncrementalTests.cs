using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Ankus.PgConfig;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Ankus.Generators.Tests;

public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Function SQL contracts retain cached values after implementation edits and source movement.
    /// </summary>
    /// <param name="move">Whether the function moves instead of changing its implementation.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void FunctionDeclarationModelsPreserveContractsAcrossImplementationEdits(bool move)
    {
        const string Source = "public static class Functions { [Ankus.PgFunction] public static int Value(int value = -7) => value + 1; }";
        CSharpCompilation initial = ModuleCompilation(Source);
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        string replacement = move ? "\n\n" + Source : Source.Replace("value + 1", "value + 2", StringComparison.Ordinal);
        CSharpCompilation edited = initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(
            replacement, path: "Module.cs", cancellationToken: context.CancellationToken));
        driver = RunModule(driver, edited, out Compilation second);

        Assert.AreEqual(IncrementalStepRunReason.Cached, TrackedFunctionDeclaration(driver, "value").Reason);
        Assert.Contains("FUNCTION \"value\"(\"value\" integer DEFAULT ((-7)::integer))", InstallationBody(second));
        Assert.AreEqual(InstallationBody(first), InstallationBody(second));
    }

    /// <summary>
    /// A referenced planner constant changes only its dependent SQL declaration and preserves native boundaries.
    /// </summary>
    [TestMethod]
    public void FunctionDeclarationsTrackDependentOptions()
    {
        const string Source = """
            public static class Functions
            {
                [Ankus.PgFunction(Cost = Options.Cost)] public static int Value() => 1;
                [Ankus.PgFunction] public static int Other() => 2;
            }
            """;
        CSharpCompilation initial = ModuleCompilation(Source).AddSyntaxTrees(CSharpSyntaxTree.ParseText(
            "public static class Options { public const double Cost = 1; }", path: "Options.cs", cancellationToken: context.CancellationToken));
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        CSharpCompilation edited = initial.ReplaceSyntaxTree(initial.SyntaxTrees.Last(), CSharpSyntaxTree.ParseText(
            "public static class Options { public const double Cost = 2.5; }", path: "Options.cs", cancellationToken: context.CancellationToken));
        driver = RunModule(driver, edited, out Compilation second);

        Assert.AreEqual(IncrementalStepRunReason.Modified, TrackedFunctionDeclaration(driver, "value").Reason);
        Assert.AreEqual(IncrementalStepRunReason.Cached, TrackedFunctionDeclaration(driver, "other").Reason);
        Assert.AreEqual(IncrementalStepRunReason.Cached, TrackedFunctionEmission(driver, "value").Reason);
        Assert.AreEqual(IncrementalStepRunReason.Cached, TrackedFunctionEmission(driver, "other").Reason);
        Assert.Contains("COST 1;", InstallationBody(first));
        Assert.Contains("COST 2.5;", InstallationBody(second));
        Assert.AreEqual(ManifestValue(first, "Ankus.NativeSource"), ManifestValue(second, "Ankus.NativeSource"));
    }

    /// <summary>
    /// Inherited schema changes invalidate dependent SQL while explicit overrides and native boundaries remain cached.
    /// </summary>
    [TestMethod]
    public void FunctionDeclarationsTrackInheritedSchemas()
    {
        const string Source = """
            [Ankus.PgSchema(Options.Schema)]
            public static class Functions
            {
                [Ankus.PgFunction] public static int Value() => 1;
                [Ankus.PgFunction(Schema = "fixed")] public static int Other() => 2;
            }
            """;
        CSharpCompilation initial = ModuleCompilation(Source).AddSyntaxTrees(CSharpSyntaxTree.ParseText(
            "public static class Options { public const string Schema = \"first\"; }", path: "Options.cs", cancellationToken: context.CancellationToken));
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        CSharpCompilation edited = initial.ReplaceSyntaxTree(initial.SyntaxTrees.Last(), CSharpSyntaxTree.ParseText(
            "public static class Options { public const string Schema = \"second\"; }", path: "Options.cs", cancellationToken: context.CancellationToken));
        driver = RunModule(driver, edited, out Compilation second);

        Assert.AreEqual(IncrementalStepRunReason.Modified, TrackedFunctionDeclaration(driver, "value").Reason);
        Assert.AreEqual(IncrementalStepRunReason.Cached, TrackedFunctionDeclaration(driver, "other").Reason);
        Assert.AreEqual(IncrementalStepRunReason.Cached, TrackedFunctionEmission(driver, "value").Reason);
        Assert.Contains("FUNCTION \"first\".\"value\"()", InstallationBody(first));
        Assert.Contains("FUNCTION \"second\".\"value\"()", InstallationBody(second));
        Assert.DoesNotContain("\"first\"", InstallationBody(second));
        Assert.Contains("FUNCTION \"fixed\".\"other\"()", InstallationBody(second));
        Assert.AreEqual(ManifestValue(first, "Ankus.NativeSource"), ManifestValue(second, "Ankus.NativeSource"));
    }

    /// <summary>
    /// Invalid cached declaration options report on the current tree and recover to valid SQL after repair.
    /// </summary>
    /// <param name="move">Whether the declaration moves before its diagnostic is resolved.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void FunctionDeclarationDiagnosticsFollowCurrentTreesAndRecover(bool move)
    {
        const string Source = "public static class Functions { [Ankus.PgFunction(Cost = 0)] public static int Value() => 1; }";
        CSharpCompilation initial = ModuleCompilation(Source);
        GeneratorDriver driver = ModuleDriver().RunGeneratorsAndUpdateCompilation(initial, out _, out _, context.CancellationToken);
        string replacement = move ? "\n\n" + Source : Source.Replace("=> 1", "=> 2", StringComparison.Ordinal);
        string path = move ? "Current.cs" : "Module.cs";
        SyntaxTree current = CSharpSyntaxTree.ParseText(replacement, path: path, cancellationToken: context.CancellationToken);
        CSharpCompilation edited = initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), current);
        driver = driver.RunGeneratorsAndUpdateCompilation(edited, out _, out ImmutableArray<Diagnostic> diagnostics, context.CancellationToken);
        Diagnostic diagnostic = Assert.ContainsSingle(diagnostics);

        Assert.AreEqual(move ? IncrementalStepRunReason.Unchanged : IncrementalStepRunReason.Cached, ModuleStep(driver, "FunctionDeclaration"));
        Assert.AreEqual("ANKUS004", diagnostic.Id);
        Assert.Contains("Cost must be positive", diagnostic.GetMessage(CultureInfo.InvariantCulture));
        Assert.AreSame(current, diagnostic.Location.SourceTree);
        Assert.AreEqual(path, diagnostic.Location.GetLineSpan().Path);
        Assert.AreEqual(move ? 2 : 0, diagnostic.Location.GetLineSpan().StartLinePosition.Line);
        Assert.AreEqual("Value", current.GetText(context.CancellationToken).ToString(diagnostic.Location.SourceSpan));
        Assert.IsNull(Assert.ContainsSingle(driver.GetRunResult().Results).Exception);

        CSharpCompilation repaired = edited.ReplaceSyntaxTree(current, CSharpSyntaxTree.ParseText(
            replacement.Replace("Cost = 0", "Cost = 2", StringComparison.Ordinal), path: path, cancellationToken: context.CancellationToken));
        driver = RunModule(driver, repaired, out Compilation output);
        Assert.AreEqual(IncrementalStepRunReason.Modified, TrackedFunctionDeclaration(driver, "value").Reason);
        Assert.Contains("COST 2;", InstallationBody(output));
    }

    /// <summary>
    /// Cached raw input contracts use current provider qualification and prerequisites after an assembly provider is added.
    /// </summary>
    [TestMethod]
    public void FunctionDeclarationsComposeCurrentTypeProviders()
    {
        const string Source = """
            public static class Functions
            {
                [Ankus.PgFunction] public static int Value([Ankus.PgSqlType("item")] Ankus.PgDatum value) => 1;
            }
            """;
        CSharpCompilation initial = ModuleCompilation(Source);
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        CSharpCompilation edited = initial.AddSyntaxTrees(CSharpSyntaxTree.ParseText("""
            [assembly: Ankus.PgSql("types", "CREATE TYPE item AS (number integer);", Relocatable = true)]
            [assembly: Ankus.PgSqlTypeProvider("types", "item")]
            """, path: "Provider.cs", cancellationToken: context.CancellationToken));
        driver = RunModule(driver, edited, out Compilation second);
        string before = Encoding.UTF8.GetString(Convert.FromBase64String(ManifestValue(first, "Ankus.SqlGraph")));
        string after = Encoding.UTF8.GetString(Convert.FromBase64String(ManifestValue(second, "Ankus.SqlGraph")));
        ExtensionSchemaGraph graph = ExtensionSchemaGraph.Parse(ManifestValue(second, "Ankus.SqlGraph"));

        Assert.AreEqual(IncrementalStepRunReason.Cached, TrackedFunctionDeclaration(driver, "value").Reason);
        Assert.AreEqual(IncrementalStepRunReason.Cached, TrackedFunctionEmission(driver, "value").Reason);
        Assert.DoesNotContain("\"value\" \0\"item\")", before);
        Assert.Contains("\"value\" \0\"item\")", after);
        Assert.StartsWith("CREATE TYPE item AS (number integer);\nCREATE FUNCTION ", InstallationBody(second));
        Assert.AreEqual(ManifestValue(second, "Ankus.Sql"), graph.Sql);
        Assert.AreEqual(ManifestValue(first, "Ankus.NativeSource"), ManifestValue(second, "Ankus.NativeSource"));
    }

    /// <summary>
    /// SQL generation and dependency policies compose freshly without invalidating unchanged declarations or dispatchers.
    /// </summary>
    /// <param name="policy">The changed function graph policy.</param>
    /// <param name="expected">The independently specified installation body.</param>
    [TestMethod]
    [DataRow("Sql = \"SELECT 'changed';\"", "SELECT 'changed';\nSELECT 'dependency';\n")]
    [DataRow("GenerateSql = false", "SELECT 'dependency';\n")]
    [DataRow("Requires = new[] { \"dep\" }", "SELECT 'dependency';\nSELECT 'consumer';\n")]
    public void FunctionDeclarationsTrackSqlGenerationPolicy(string policy, string expected)
    {
        const string Source = """
            [assembly: Ankus.PgSql("dep", "SELECT 'dependency';", Relocatable = true)]
            public static class Functions
            {
                [Ankus.PgFunction(Sql = "SELECT 'consumer';", SqlRelocatable = true)] public static int Value() => 1;
            }
            """;
        CSharpCompilation initial = ModuleCompilation(Source);
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        string replacement = policy.StartsWith("Sql =", StringComparison.Ordinal) || policy.StartsWith("GenerateSql", StringComparison.Ordinal)
            ? Source.Replace("Sql = \"SELECT 'consumer';\"", policy, StringComparison.Ordinal)
            : Source.Replace("SqlRelocatable = true", "SqlRelocatable = true, " + policy, StringComparison.Ordinal);
        CSharpCompilation edited = initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(
            replacement, path: "Module.cs", cancellationToken: context.CancellationToken));
        driver = RunModule(driver, edited, out Compilation second);

        Assert.AreEqual(IncrementalStepRunReason.Unchanged, TrackedFunctionDeclaration(driver, "value").Reason);
        Assert.AreEqual(IncrementalStepRunReason.Cached, TrackedFunctionEmission(driver, "value").Reason);
        Assert.AreEqual("SELECT 'consumer';\nSELECT 'dependency';\n", InstallationBody(first));
        Assert.AreEqual(expected, InstallationBody(second));
        Assert.AreEqual(ManifestValue(first, "Ankus.NativeSource"), ManifestValue(second, "Ankus.NativeSource"));
        Assert.AreEqual(ManifestValue(first, "Ankus.Exports"), ManifestValue(second, "Ankus.Exports"));
        Assert.AreEqual("true", ManifestValue(second, "Ankus.Relocatable"));
    }

    /// <summary>
    /// Reads the actual production declaration stage without inferring cache reuse from equal SQL.
    /// </summary>
    /// <param name="driver">The executed incremental driver.</param>
    /// <param name="name">The exact SQL leaf name.</param>
    /// <returns>The immutable declaration and observed cache decision.</returns>
    private static (FunctionDeclaration Declaration, IncrementalStepRunReason Reason) TrackedFunctionDeclaration(GeneratorDriver driver, string name)
    {
        (object value, IncrementalStepRunReason reason) = Assert.ContainsSingle(Assert.ContainsSingle(driver.GetRunResult().Results)
            .TrackedSteps["FunctionDeclaration"].SelectMany(static step => step.Outputs).Where(output =>
                output.Reason != IncrementalStepRunReason.Removed && output.Value is FunctionDeclaration declaration && declaration.Name == name));
        return (Assert.IsInstanceOfType<FunctionDeclaration>(value), reason);
    }
}
