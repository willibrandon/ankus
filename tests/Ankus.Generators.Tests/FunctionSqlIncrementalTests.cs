using System.Text;
using Ankus.PgConfig;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Ankus.Generators.Tests;

public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Scalar, SETOF and TABLE SQL rendering stays cached after implementation edits or declaration movement.
    /// </summary>
    /// <param name="move">Whether the declaration moves instead of changing its implementation.</param>
    /// <param name="shape">The scalar, SETOF or TABLE result shape.</param>
    [TestMethod]
    [DataRow(false, 0)]
    [DataRow(true, 0)]
    [DataRow(false, 1)]
    [DataRow(true, 1)]
    [DataRow(false, 2)]
    [DataRow(true, 2)]
    public void FunctionSqlEmissionRemainsCachedAcrossImplementationEdits(bool move, int shape)
    {
        string source = shape == 1 ? "public static class Functions { [Ankus.PgFunction] public static System.Collections.Generic.IEnumerable<int> Value(int value = 7) => new[] { value + 1 }; }" :
            FunctionCacheSource(shape == 2);
        CSharpCompilation initial = ModuleCompilation(source);
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        string replacement = move ? "\n\n" + source : source.Replace("value + 1", "value + 2", StringComparison.Ordinal);
        CSharpCompilation edited = initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(
            replacement, path: "Module.cs", cancellationToken: context.CancellationToken));
        driver = RunModule(driver, edited, out Compilation second);

        Assert.AreEqual(IncrementalStepRunReason.Cached, TrackedFunctionSqlEmission(driver, "value").Reason);
        Assert.AreEqual(InstallationBody(first), InstallationBody(second));
        Assert.Contains("\"value\" integer DEFAULT ((7)::integer)", InstallationBody(second));
        Assert.Contains(shape switch { 0 => "RETURNS integer", 1 => "RETURNS SETOF integer", _ => "RETURNS TABLE (\"number\" integer, \"label\" text)" }, InstallationBody(second));
    }

    /// <summary>
    /// Planner constants rerender only dependent SQL while managed/native boundaries remain cached.
    /// </summary>
    [TestMethod]
    public void FunctionSqlEmissionTracksDependentOptions()
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
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out _);
        FunctionSqlEmission before = TrackedFunctionSqlEmission(driver, "value").Emission;
        CSharpCompilation edited = initial.ReplaceSyntaxTree(initial.SyntaxTrees.Last(), CSharpSyntaxTree.ParseText(
            "public static class Options { public const double Cost = 2.5; }", path: "Options.cs", cancellationToken: context.CancellationToken));
        driver = RunModule(driver, edited, out Compilation output);
        (FunctionSqlEmission emission, IncrementalStepRunReason reason) = TrackedFunctionSqlEmission(driver, "value");

        Assert.AreEqual(IncrementalStepRunReason.Modified, reason);
        Assert.AreEqual(IncrementalStepRunReason.Cached, TrackedFunctionSqlEmission(driver, "other").Reason);
        Assert.AreEqual(IncrementalStepRunReason.Cached, TrackedFunctionEmission(driver, "value").Reason);
        Assert.EndsWith("COST 1", before.Tail);
        Assert.EndsWith("COST 2.5", emission.Tail);
        Assert.Contains("COST 2.5;", InstallationBody(output));
    }

    /// <summary>
    /// A nullable return changes managed/native conversion while preserving the same SQL catalog definition.
    /// </summary>
    [TestMethod]
    public void FunctionSqlEmissionRemainsCachedAfterReturnNullabilityChange()
    {
        const string Source = "#nullable enable\npublic static class Functions { [Ankus.PgFunction] public static string Value(string value) => value; }";
        CSharpCompilation initial = ModuleCompilation(Source);
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        CSharpCompilation edited = initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(
            Source.Replace("public static string Value", "public static string? Value", StringComparison.Ordinal),
            path: "Module.cs", cancellationToken: context.CancellationToken));
        driver = RunModule(driver, edited, out Compilation second);

        Assert.AreEqual(IncrementalStepRunReason.Cached, TrackedFunctionSqlEmission(driver, "value").Reason);
        Assert.AreEqual(IncrementalStepRunReason.Modified, TrackedFunctionEmission(driver, "value").Reason);
        Assert.AreEqual(InstallationBody(first), InstallationBody(second));
        Assert.Contains("RETURNS text", InstallationBody(second));
        Assert.Contains(" STRICT ", InstallationBody(second));
    }

    /// <summary>
    /// Cached SQL fragments apply new provider ownership to scalar, array and TABLE inputs and outputs.
    /// </summary>
    /// <param name="shape">The scalar, raw array or TABLE result shape.</param>
    /// <param name="expected">The independently specified schema-aware return clause.</param>
    [TestMethod]
    [DataRow(0, "RETURNS \0\"item\" AS")]
    [DataRow(1, "RETURNS \0\"item\"[] AS")]
    [DataRow(2, "RETURNS TABLE (\"result\" \0\"item\", \"vector\" \0\"item\"[]) AS")]
    public void FunctionSqlEmissionComposesCurrentTypeProviders(int shape, string expected)
    {
        string declaration = shape switch
        {
            0 => "[return: Ankus.PgSqlType(\"item\")] [Ankus.PgFunction] public static Ankus.PgDatum Value([Ankus.PgSqlType(\"item\")] Ankus.PgDatum item) => item;",
            1 => "[return: Ankus.PgSqlType(\"item\", IsArray = true)] [Ankus.PgFunction] public static Ankus.PgDatum Value([Ankus.PgSqlType(\"item\", IsArray = true)] Ankus.PgDatum item) => item;",
            _ => "[return: Ankus.PgSqlType(\"item\", Column = \"result\")] [return: Ankus.PgSqlType(\"item\", Column = \"vector\", IsArray = true)] [Ankus.PgFunction] public static System.Collections.Generic.IEnumerable<(Ankus.PgDatum Result, Ankus.PgDatum Vector)> Value([Ankus.PgSqlType(\"item\")] Ankus.PgDatum item) => new[] { (item, item) };",
        };
        CSharpCompilation initial = ModuleCompilation("public static class Functions { " + declaration + " }");
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        FunctionSqlEmission before = TrackedFunctionSqlEmission(driver, "value").Emission;
        CSharpCompilation edited = initial.AddSyntaxTrees(CSharpSyntaxTree.ParseText("""
            [assembly: Ankus.PgSql("types", "CREATE TYPE item AS (number integer);", Relocatable = true)]
            [assembly: Ankus.PgSqlTypeProvider("types", "item")]
            """, path: "Provider.cs", cancellationToken: context.CancellationToken));
        driver = RunModule(driver, edited, out Compilation second);
        (FunctionSqlEmission emission, IncrementalStepRunReason reason) = TrackedFunctionSqlEmission(driver, "value");
        string original = Encoding.UTF8.GetString(Convert.FromBase64String(ManifestValue(first, "Ankus.SqlGraph")));
        string current = Encoding.UTF8.GetString(Convert.FromBase64String(ManifestValue(second, "Ankus.SqlGraph")));
        ExtensionSchemaGraph graph = ExtensionSchemaGraph.Parse(ManifestValue(second, "Ankus.SqlGraph"));

        Assert.AreEqual(IncrementalStepRunReason.Cached, reason);
        Assert.AreEqual(before, emission);
        Assert.DoesNotContain("\0\"item\"", original);
        Assert.Contains("\"item\" \0\"item\"" + (shape == 1 ? "[]" : string.Empty), current);
        Assert.Contains(expected, current);
        Assert.StartsWith("CREATE TYPE item AS (number integer);\nCREATE FUNCTION ", InstallationBody(second));
        Assert.AreEqual(ManifestValue(second, "Ankus.Sql"), graph.Sql);
    }

    /// <summary>
    /// Changing a typed support reference composes current support SQL and edges without rerendering consumer SQL.
    /// </summary>
    /// <param name="set">Whether the supported consumer is a SETOF function.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void FunctionSqlEmissionComposesCurrentPlannerSupport(bool set)
    {
        string consumer = set ? "public static System.Collections.Generic.IEnumerable<int> A(int value) => new[] { value };" :
            "public static int A(int value) => value;";
        string source = """
            public static class Functions
            {
                [Ankus.PgFunction, Ankus.PgSupportFunction(typeof(Functions), nameof(ZSupport))]
            """ + consumer + """
                [Ankus.PgFunction(Name = "first_support")] public static Ankus.PgInternal? ZSupport(Ankus.PgInternal request) => null;
                [Ankus.PgFunction(Name = "second_support")] public static Ankus.PgInternal? YSupport(Ankus.PgInternal request) => null;
            }
            """;
        CSharpCompilation initial = ModuleCompilation(source);
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        CSharpCompilation edited = initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(
            source.Replace("nameof(ZSupport)", "nameof(YSupport)", StringComparison.Ordinal), path: "Module.cs", cancellationToken: context.CancellationToken));
        driver = RunModule(driver, edited, out Compilation second);
        ExtensionSchemaGraph graph = ExtensionSchemaGraph.Parse(ManifestValue(second, "Ankus.SqlGraph"));
        ExtensionSchemaItem selected = Assert.ContainsSingle(graph.Items.Where(static item => item.Names.Contains("second_support", StringComparer.Ordinal)));
        ExtensionSchemaItem supported = Assert.ContainsSingle(graph.Items.Where(static item => item.Names.Contains("a", StringComparer.Ordinal)));

        Assert.AreEqual(IncrementalStepRunReason.Cached, TrackedFunctionSqlEmission(driver, "a").Reason);
        Assert.Contains("SUPPORT \"first_support\"", InstallationBody(first));
        Assert.Contains("SUPPORT \"second_support\"", supported.Sql);
        Assert.DoesNotContain("SUPPORT \"first_support\"", supported.Sql);
        Assert.AreSequenceEqual<string>([selected.Id], supported.Dependencies);
        Assert.DoesNotContain("SUPPORT", TrackedFunctionSqlEmission(driver, "a").Emission.Tail);
        Assert.AreEqual(ManifestValue(first, "Ankus.NativeSource"), ManifestValue(second, "Ankus.NativeSource"));
    }

    /// <summary>
    /// Reads an individual production SQL renderer's actual artifact and incremental decision.
    /// </summary>
    /// <param name="driver">The executed production driver.</param>
    /// <param name="name">The exact SQL leaf name.</param>
    /// <returns>The rendered fragments and observed cache reason.</returns>
    private static (FunctionSqlEmission Emission, IncrementalStepRunReason Reason) TrackedFunctionSqlEmission(GeneratorDriver driver, string name)
    {
        (object value, IncrementalStepRunReason reason) = Assert.ContainsSingle(Assert.ContainsSingle(driver.GetRunResult().Results)
            .TrackedSteps["FunctionSqlEmission"].SelectMany(static step => step.Outputs).Where(output =>
                output.Reason != IncrementalStepRunReason.Removed && output.Value is FunctionSqlEmission emission &&
                emission.Header.EndsWith("\"" + name + "\"(", StringComparison.Ordinal)));
        return (Assert.IsInstanceOfType<FunctionSqlEmission>(value), reason);
    }
}
