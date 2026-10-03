using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using Ankus.PgConfig;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Ankus.Generators.Tests;

public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Body edits and movement reuse production rendering while the constrained helper executes the current implementation.
    /// </summary>
    /// <param name="move">Whether to move declarations rather than edit the transition body.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void AggregateArtifactsCacheIndependentImplementationEdits(bool move)
    {
        const string Source = """
            [Ankus.PgAggregate(InitialCondition = "0")]
            public sealed class Total : Ankus.IPgAggregate<int, int>
            {
                public static int Transition(Ankus.PgAggregateContext context, int state, int value) => state + value + 1;
            }
            """;
        CSharpCompilation initial = ModuleCompilation(Source + OtherAggregateSource);
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        CSharpCompilation edited = initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(
            (move ? "\n\n" + Source : Source.Replace("value + 1", "value + 2", StringComparison.Ordinal)) + OtherAggregateSource,
            path: "Moved.cs", cancellationToken: context.CancellationToken));
        driver = RunModule(driver, edited, out Compilation second);

        AssertAggregateRenderingReason(driver, "total", IncrementalStepRunReason.Cached);
        AssertAggregateRenderingReason(driver, "other", IncrementalStepRunReason.Cached);
        Assert.AreEqual(InstallationBody(first), InstallationBody(second));
        Assert.AreEqual(ManifestValue(first, "Ankus.NativeSource"), ManifestValue(second, "Ankus.NativeSource"));
        Assert.Contains("Moved.cs:", ManifestValue(second, "Ankus.Sql"));
        Assert.AreEqual(move ? 42 : 43, InvokeAggregateTransition(second, AggregateOutput(driver, "total")));
    }

    /// <summary>
    /// Numeric rescaling edits change the selected boundary independently of support and aggregate catalog rendering.
    /// </summary>
    /// <param name="target">Whether to edit state, input or return rescaling.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public void AggregateArtifactsTrackNumericPrecisionIndependently(int target)
    {
        const string Source = """
            [Ankus.PgAggregate(InitialCondition = "0")]
            public sealed class Total : Ankus.IPgAggregate<Ankus.PgNumeric, Ankus.PgNumeric>
            {
                [return: Ankus.PgNumericPrecision(5, 2)]
                public static Ankus.PgNumeric Transition(Ankus.PgAggregateContext context,
                    [Ankus.PgNumericPrecision(5, 2)] Ankus.PgNumeric state,
                    [Ankus.PgNumericPrecision(5, 2)] Ankus.PgNumeric value) => state + value;
            }
            """;
        string changed = target switch
        {
            0 => Source.Replace("[Ankus.PgNumericPrecision(5, 2)] Ankus.PgNumeric state", "[Ankus.PgNumericPrecision(6, 3)] Ankus.PgNumeric state", StringComparison.Ordinal),
            1 => Source.Replace("[Ankus.PgNumericPrecision(5, 2)] Ankus.PgNumeric value", "[Ankus.PgNumericPrecision(6, 3)] Ankus.PgNumeric value", StringComparison.Ordinal),
            _ => Source.Replace("[return: Ankus.PgNumericPrecision(5, 2)]", "[return: Ankus.PgNumericPrecision(6, 3)]", StringComparison.Ordinal),
        };
        CSharpCompilation initial = ModuleCompilation(Source + OtherAggregateSource);
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        CSharpCompilation edited = initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(
            changed + OtherAggregateSource, cancellationToken: context.CancellationToken));
        driver = RunModule(driver, edited, out Compilation second);

        Assert.AreEqual(IncrementalStepRunReason.Modified, AggregateHelperStep(driver, "AggregateHelperBoundaryEmission", "total").Reason);
        Assert.AreEqual(IncrementalStepRunReason.Cached, AggregateHelperStep(driver, "AggregateHelperSqlEmission", "total").Reason);
        Assert.AreEqual(IncrementalStepRunReason.Cached, AggregateSqlStep(driver, "total").Reason);
        AssertAggregateRenderingReason(driver, "other", IncrementalStepRunReason.Cached);
        AggregatePipeline.HelperOutput helper = Assert.ContainsSingle(AggregateOutput(driver, "total").Helpers);
        Assert.HasCount(2, helper.Boundary.Managed.Split(".Rescale(6, 3)", StringSplitOptions.None));
        Assert.HasCount(3, helper.Boundary.Managed.Split(".Rescale(5, 2)", StringSplitOptions.None));
        Assert.AreEqual(InstallationBody(first), InstallationBody(second));
        Assert.AreEqual(ManifestValue(first, "Ankus.NativeSource"), ManifestValue(second, "Ankus.NativeSource"));
    }

    /// <summary>
    /// A final-function precision change rerenders only that helper's boundary within the same aggregate.
    /// </summary>
    [TestMethod]
    public void AggregateArtifactsTrackSelectedHelperIndependently()
    {
        const string Source = """
            [Ankus.PgAggregate(InitialCondition = "0")]
            public sealed class Total : Ankus.IPgAggregate<Ankus.PgNumeric, Ankus.PgNumeric>,
                Ankus.IPgFinalizingAggregate<Ankus.PgNumeric, System.ValueTuple, Ankus.PgNumeric>
            {
                public static Ankus.PgNumeric Transition(Ankus.PgAggregateContext context, Ankus.PgNumeric state, Ankus.PgNumeric value) => state + value;
                [return: Ankus.PgNumericPrecision(5, 2)]
                public static Ankus.PgNumeric Final(Ankus.PgAggregateContext context, Ankus.PgNumeric state, System.ValueTuple direct) => state;
            }
            """;
        CSharpCompilation initial = ModuleCompilation(Source);
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        CSharpCompilation edited = initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(
            Source.Replace("PgNumericPrecision(5, 2)", "PgNumericPrecision(6, 3)", StringComparison.Ordinal), cancellationToken: context.CancellationToken));
        driver = RunModule(driver, edited, out Compilation second);

        Assert.AreEqual(IncrementalStepRunReason.Cached, AggregateSqlStep(driver, "total").Reason);
        Assert.AreEqual(IncrementalStepRunReason.Cached, AggregateHelperStep(driver, "AggregateHelperBoundaryEmission", "total").Reason);
        Assert.AreEqual(IncrementalStepRunReason.Modified, AggregateHelperStep(driver, "AggregateHelperBoundaryEmission", "total", "Final").Reason);
        foreach (string role in new[] { "Transition", "Final" })
        {
            Assert.AreEqual(IncrementalStepRunReason.Cached, AggregateHelperStep(driver, "AggregateHelperSqlEmission", "total", role).Reason);
        }

        AggregatePipeline.Output current = AggregateOutput(driver, "total");
        Assert.Contains(".Rescale(6, 3)", Assert.ContainsSingle(current.Helpers.Where(static helper => helper.Analysis.Model.Role == "Final")).Boundary.Managed);
        Assert.DoesNotContain(".Rescale(6, 3)", Assert.ContainsSingle(current.Helpers.Where(static helper => helper.Analysis.Model.Role == "Transition")).Boundary.Managed);
        Assert.AreEqual(InstallationBody(first), InstallationBody(second));
        Assert.AreEqual(ManifestValue(first, "Ankus.NativeSource"), ManifestValue(second, "Ankus.NativeSource"));
    }

    /// <summary>
    /// SQL-only edits invalidate the actual selected DDL stage while retaining native and managed boundaries.
    /// </summary>
    /// <param name="kind">The support execution policy, aggregate initial condition or support state parameter name.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public void AggregateArtifactsTrackSqlPolicyIndependently(int kind)
    {
        const string Source = """
            [Ankus.PgAggregate(InitialCondition = "0")]
            public sealed class Total : Ankus.IPgAggregate<int, int>
            {
                [Ankus.PgFunction(Volatility = Ankus.PgVolatility.Immutable)]
                public static int Transition(Ankus.PgAggregateContext context, int state, int value) => state + value;
            }
            """;
        string changed = kind switch
        {
            0 => Source.Replace("Immutable", "Stable", StringComparison.Ordinal),
            1 => Source.Replace("InitialCondition = \"0\"", "InitialCondition = \"1\"", StringComparison.Ordinal),
            _ => Source.Replace("int state, int value) => state + value", "int accumulator, int value) => accumulator + value", StringComparison.Ordinal),
        };
        CSharpCompilation initial = ModuleCompilation(Source + OtherAggregateSource);
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        CSharpCompilation edited = initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(
            changed + OtherAggregateSource, cancellationToken: context.CancellationToken));
        driver = RunModule(driver, edited, out Compilation second);

        Assert.AreEqual(IncrementalStepRunReason.Cached, AggregateHelperStep(driver, "AggregateHelperBoundaryEmission", "total").Reason);
        Assert.AreEqual(kind == 1 ? IncrementalStepRunReason.Cached : IncrementalStepRunReason.Modified,
            AggregateHelperStep(driver, "AggregateHelperSqlEmission", "total").Reason);
        Assert.AreEqual(kind == 1 ? IncrementalStepRunReason.Modified : IncrementalStepRunReason.Cached, AggregateSqlStep(driver, "total").Reason);
        AssertAggregateRenderingReason(driver, "other", IncrementalStepRunReason.Cached);
        Assert.AreNotEqual(InstallationBody(first), InstallationBody(second));
        Assert.Contains(kind switch { 0 => "LANGUAGE c STABLE", 1 => "INITCOND = E'1'", _ => "\"accumulator\" integer" }, InstallationBody(second));
        Assert.AreEqual(ManifestValue(first, "Ankus.NativeSource"), ManifestValue(second, "Ankus.NativeSource"));
    }

    /// <summary>
    /// Cached validation failures resolve to the current source tree and repair restores complete compilable rendering.
    /// </summary>
    [TestMethod]
    public void AggregateArtifactsReportCurrentInvalidTreeAndRepair()
    {
        const string Source = """
            [Ankus.PgAggregate(ParallelSafety = (Ankus.PgParallelSafety)3)]
            public sealed class Total : Ankus.IPgAggregate<int, int>
            {
                [Ankus.PgFunction]
                public static int Transition(Ankus.PgAggregateContext context, int state, int value) => state + value;
            }
            """;
        CSharpCompilation initial = ModuleCompilation(Source);
        GeneratorDriver driver = ModuleDriver().RunGeneratorsAndUpdateCompilation(initial, out _, out ImmutableArray<Diagnostic> first,
            context.CancellationToken);
        Assert.AreEqual("ANKUS082", Assert.ContainsSingle(first).Id);
        SyntaxTree currentTree = CSharpSyntaxTree.ParseText("\n\n" + Source, path: "Current.cs", cancellationToken: context.CancellationToken);
        CSharpCompilation moved = initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), currentTree);
        driver = driver.RunGeneratorsAndUpdateCompilation(moved, out _, out ImmutableArray<Diagnostic> current, context.CancellationToken);
        Diagnostic problem = Assert.ContainsSingle(current);
        Assert.AreEqual("ANKUS082", problem.Id);
        Assert.AreSame(currentTree, problem.Location.SourceTree);
        Assert.AreEqual(2, problem.Location.GetLineSpan().StartLinePosition.Line);
        Assert.AreEqual(IncrementalStepRunReason.Cached, ModuleStep(driver, "AggregateSqlEmission"));
        CSharpCompilation repaired = moved.ReplaceSyntaxTree(currentTree, CSharpSyntaxTree.ParseText(
            Source.Replace("(Ankus.PgParallelSafety)3", "Ankus.PgParallelSafety.Safe", StringComparison.Ordinal),
            path: "Repaired.cs", cancellationToken: context.CancellationToken));
        driver = RunModule(driver, repaired, out Compilation output);
        Assert.Contains("PARALLEL = SAFE", InstallationBody(output));
        Assert.Contains("Repaired.cs:", ManifestValue(output, "Ankus.Sql"));
        Assert.HasCount(1, AggregateOutput(driver, "total").Helpers);
    }

    /// <summary>
    /// Current initialization composes into the cached native body without rerendering aggregate artifacts.
    /// </summary>
    [TestMethod]
    public void AggregateArtifactsComposeCurrentExtensionInitialization()
    {
        CSharpCompilation initial = ModuleCompilation(OtherAggregateSource);
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        CSharpCompilation edited = initial.AddSyntaxTrees(CSharpSyntaxTree.ParseText(
            "public static class Lifecycle { [Ankus.PgInitialize] public static void Initialize() { } }",
            path: "Lifecycle.cs", cancellationToken: context.CancellationToken));
        driver = RunModule(driver, edited, out Compilation second);

        AssertAggregateRenderingReason(driver, "other", IncrementalStepRunReason.Cached);
        NativeFunctionEmission boundary = Assert.ContainsSingle(AggregateOutput(driver, "other").Helpers).Boundary.Native;
        Assert.Contains((boundary.Header + boundary.Body).ReplaceLineEndings("\n"), ManifestValue(first, "Ankus.NativeSource"));
        Assert.Contains((boundary.Header + "    ankus_ensure_initialized();\n" + boundary.Body).ReplaceLineEndings("\n"),
            ManifestValue(second, "Ankus.NativeSource"));
        Assert.AreEqual(InstallationBody(first), InstallationBody(second));
    }

    /// <summary>
    /// Assembly changes alter entry identities while aggregate DDL stays independently cached.
    /// </summary>
    [TestMethod]
    public void AggregateArtifactsTrackAssemblyIdentity()
    {
        CSharpCompilation initial = ModuleCompilation(OtherAggregateSource);
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        string previous = Assert.ContainsSingle(AggregateOutput(driver, "other").Helpers).Analysis.Callback;
        driver = RunModule(driver, initial.WithAssemblyName("RenamedExtension"), out Compilation second);
        string current = Assert.ContainsSingle(AggregateOutput(driver, "other").Helpers).Analysis.Callback;

        Assert.AreNotEqual(previous, current);
        Assert.AreEqual(IncrementalStepRunReason.Cached, AggregateSqlStep(driver, "other").Reason);
        Assert.AreEqual(IncrementalStepRunReason.Modified, AggregateHelperStep(driver, "AggregateHelperBoundaryEmission", "other").Reason);
        Assert.AreEqual(IncrementalStepRunReason.Modified, AggregateHelperStep(driver, "AggregateHelperSqlEmission", "other").Reason);
        Assert.DoesNotContain(previous, ManifestValue(second, "Ankus.NativeSource"));
        Assert.Contains(current, ManifestValue(second, "Ankus.NativeSource"));
        Assert.AreNotEqual(ManifestValue(first, "Ankus.Exports"), ManifestValue(second, "Ankus.Exports"));
    }

    /// <summary>
    /// Inherited, explicit and default static capabilities retain cached boundaries and execute the new implementation.
    /// </summary>
    /// <param name="kind">The private explicit, inherited generic or default interface implementation.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public void AggregateArtifactsPreserveConstrainedImplementations(int kind)
    {
        string source = kind switch
        {
            0 => """
                [Ankus.PgAggregate(InitialCondition = "0")]
                public sealed class Total : Ankus.IPgAggregate<int, int>
                {
                    static int Ankus.IPgAggregate<int, int>.Transition(Ankus.PgAggregateContext context, int state, int value) => state + value + 1;
                }
                """,
            1 => """
                public abstract class Base<T> : Ankus.IPgAggregate<T, int> where T : System.Numerics.INumber<T>
                {
                    public static T Transition(Ankus.PgAggregateContext context, T state, int value) => state + T.CreateChecked(value + 1);
                }
                [Ankus.PgAggregate(InitialCondition = "0")] public sealed class Total : Base<int>;
                """,
            _ => """
                public interface IDefaultTotal : Ankus.IPgAggregate<int, int>
                {
                    static int Ankus.IPgAggregate<int, int>.Transition(Ankus.PgAggregateContext context, int state, int value) => state + value + 1;
                }
                [Ankus.PgAggregate(InitialCondition = "0")] public sealed class Total : IDefaultTotal;
                """,
        };
        CSharpCompilation initial = ModuleCompilation(source);
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        Assert.AreEqual(42, InvokeAggregateTransition(first, AggregateOutput(driver, "total")));
        CSharpCompilation edited = initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(
            source.Replace("value + 1", "value + 2", StringComparison.Ordinal), path: "Implementations.cs", cancellationToken: context.CancellationToken));
        driver = RunModule(driver, edited, out Compilation second);

        AssertAggregateRenderingReason(driver, "total", IncrementalStepRunReason.Cached);
        Assert.AreEqual(43, InvokeAggregateTransition(second, AggregateOutput(driver, "total")));
        Assert.AreEqual(InstallationBody(first), InstallationBody(second));
        Assert.AreEqual(ManifestValue(first, "Ankus.NativeSource"), ManifestValue(second, "Ankus.NativeSource"));
        Assert.Contains("Implementations.cs:", ManifestValue(second, "Ankus.Sql"));
    }

    /// <summary>
    /// Aggregate SQL disablement and replacement compose current policy while all production rendering stays cached.
    /// </summary>
    /// <param name="replace">Whether to replace aggregate SQL rather than disable it.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void AggregateArtifactsComposeCurrentSqlGenerationPolicy(bool replace)
    {
        CSharpCompilation initial = ModuleCompilation(OtherAggregateSource);
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        string changed = OtherAggregateSource.Replace("InitialCondition = \"0\"", "InitialCondition = \"0\", " +
            (replace ? "Sql = \"SELECT 42;\", SqlRelocatable = true" : "GenerateSql = false"), StringComparison.Ordinal);
        CSharpCompilation edited = initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(
            changed, cancellationToken: context.CancellationToken));
        driver = RunModule(driver, edited, out Compilation second);

        AssertAggregateRenderingReason(driver, "other", IncrementalStepRunReason.Cached);
        Assert.DoesNotContain("CREATE AGGREGATE", InstallationBody(second));
        Assert.Contains("CREATE FUNCTION \"other_transition\"", InstallationBody(second));
        Assert.AreEqual(replace, InstallationBody(second).Contains("SELECT 42;", StringComparison.Ordinal));
        Assert.AreEqual(ManifestValue(first, "Ankus.NativeSource"), ManifestValue(second, "Ankus.NativeSource"));
    }

    /// <summary>
    /// Current authored aggregate or support dependencies update graph edges without invalidating rendering.
    /// </summary>
    /// <param name="support">Whether to add the dependency to the support function.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void AggregateArtifactsComposeCurrentDependencies(bool support)
    {
        string source = "[assembly: Ankus.PgSql(\"seed\", \"SELECT 1;\")]\n" + OtherAggregateSource.Replace(
            "public static int Transition", "[Ankus.PgFunction] public static int Transition", StringComparison.Ordinal);
        CSharpCompilation initial = ModuleCompilation(source);
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        string changed = support
            ? source.Replace("[Ankus.PgFunction]", "[Ankus.PgFunction(Requires = new[] { \"seed\" })]", StringComparison.Ordinal)
            : source.Replace("InitialCondition = \"0\"", "InitialCondition = \"0\", Requires = new[] { \"seed\" }", StringComparison.Ordinal);
        CSharpCompilation edited = initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(
            changed, cancellationToken: context.CancellationToken));
        driver = RunModule(driver, edited, out Compilation second);

        AssertAggregateRenderingReason(driver, "other", IncrementalStepRunReason.Cached);
        ExtensionSchemaGraph previous = ExtensionSchemaGraph.Parse(ManifestValue(first, "Ankus.SqlGraph"));
        ExtensionSchemaGraph current = ExtensionSchemaGraph.Parse(ManifestValue(second, "Ankus.SqlGraph"));
        ExtensionSchemaItem before = Assert.ContainsSingle(previous.Items.Where(value => value.Kind == (support ? "function" : "aggregate")));
        ExtensionSchemaItem after = Assert.ContainsSingle(current.Items.Where(value => value.Kind == (support ? "function" : "aggregate")));
        string seed = Assert.ContainsSingle(current.Items.Where(static value => value.Kind == "sql")).Id;
        Assert.DoesNotContain(seed, before.Dependencies);
        Assert.Contains(seed, after.Dependencies);
        Assert.IsLessThan(current.Sql.IndexOf(support ? "CREATE FUNCTION" : "CREATE AGGREGATE", StringComparison.Ordinal),
            current.Sql.IndexOf("SELECT 1;", StringComparison.Ordinal));
        Assert.AreEqual(ManifestValue(first, "Ankus.NativeSource"), ManifestValue(second, "Ankus.NativeSource"));
    }

    /// <summary>
    /// Cached aggregate and support SQL apply current explicit provider ownership at typed catalog boundaries.
    /// </summary>
    [TestMethod]
    public void AggregateArtifactsComposeCurrentTypeProviders()
    {
        const string Source = """
            [Ankus.PgAggregate]
            public sealed class Total : Ankus.IPgAggregate<Ankus.PgDatum?, Ankus.PgDatum?>
            {
                [return: Ankus.PgSqlType("item")]
                public static Ankus.PgDatum? Transition(Ankus.PgAggregateContext context,
                    [Ankus.PgSqlType("item")] Ankus.PgDatum? state,
                    [Ankus.PgSqlType("item")] Ankus.PgDatum? item) => state;
            }
            """;
        CSharpCompilation initial = ModuleCompilation(Source);
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        CSharpCompilation edited = initial.AddSyntaxTrees(CSharpSyntaxTree.ParseText("""
            [assembly: Ankus.PgSql("types", "CREATE TYPE item AS (number integer);", Relocatable = true)]
            [assembly: Ankus.PgSqlTypeProvider("types", "item")]
            """, path: "Provider.cs", cancellationToken: context.CancellationToken));
        driver = RunModule(driver, edited, out Compilation second);

        AssertAggregateRenderingReason(driver, "total", IncrementalStepRunReason.Cached);
        string previous = Encoding.UTF8.GetString(Convert.FromBase64String(ManifestValue(first, "Ankus.SqlGraph")));
        string current = Encoding.UTF8.GetString(Convert.FromBase64String(ManifestValue(second, "Ankus.SqlGraph")));
        Assert.DoesNotContain("\0\"item\"", previous);
        Assert.Contains("\"item\" \0\"item\"", current);
        Assert.Contains("STYPE = \0\"item\"", current);
        ExtensionSchemaGraph graph = ExtensionSchemaGraph.Parse(ManifestValue(second, "Ankus.SqlGraph"));
        string provider = Assert.ContainsSingle(graph.Items.Where(static item => item.Kind == "sql")).Id;
        Assert.Contains(provider, Assert.ContainsSingle(graph.Items.Where(static item => item.Kind == "function")).Dependencies);
        Assert.StartsWith("CREATE TYPE item AS (number integer);\nCREATE FUNCTION", InstallationBody(second));
    }

    /// <summary>
    /// Inherited support methods shared by exact closed identity remain one SQL function and one exported native boundary.
    /// </summary>
    [TestMethod]
    public void AggregateArtifactsPreserveSharedSupportIdentity()
    {
        const string Source = """
            public abstract class Base<T> : Ankus.IPgAggregate<T, int> where T : System.Numerics.INumber<T>
            {
                [Ankus.PgFunction(Name = "shared_step")]
                public static T Transition(Ankus.PgAggregateContext context, T state, int value) => state + T.CreateChecked(value + 1);
            }
            [Ankus.PgAggregate(InitialCondition = "0")] public sealed class First : Base<int>;
            [Ankus.PgAggregate(InitialCondition = "0")] public sealed class Second : Base<int>;
            """;
        CSharpCompilation initial = ModuleCompilation(Source);
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        CSharpCompilation edited = initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(
            Source.Replace("value + 1", "value + 2", StringComparison.Ordinal), cancellationToken: context.CancellationToken));
        driver = RunModule(driver, edited, out Compilation second);

        AssertAggregateRenderingReason(driver, "first", IncrementalStepRunReason.Cached);
        AssertAggregateRenderingReason(driver, "second", IncrementalStepRunReason.Cached);
        ExtensionSchemaGraph graph = ExtensionSchemaGraph.Parse(ManifestValue(second, "Ankus.SqlGraph"));
        ExtensionSchemaItem helper = Assert.ContainsSingle(graph.Items.Where(static item => item.Kind == "function"));
        Assert.HasCount(2, graph.Items.Where(static item => item.Kind == "aggregate"));
        foreach (ExtensionSchemaItem aggregate in graph.Items.Where(static item => item.Kind == "aggregate"))
        {
            Assert.Contains(helper.Id, aggregate.Dependencies);
        }

        Assert.HasCount(3, ManifestValue(second, "Ankus.Exports").Split('\n', StringSplitOptions.RemoveEmptyEntries));
        Assert.AreEqual(43, InvokeAggregateTransition(second, AggregateOutput(driver, "first")));
        Assert.AreEqual(InstallationBody(first), InstallationBody(second));
        Assert.AreEqual(ManifestValue(first, "Ankus.NativeSource"), ManifestValue(second, "Ankus.NativeSource"));
    }

    /// <summary>
    /// Removing a trailing aggregate drops its graph and native identities while preserving the surviving cached stages.
    /// </summary>
    [TestMethod]
    public void AggregateArtifactsRemoveDeclarations()
    {
        string source = OtherAggregateSource.Replace("class Other", "class Total", StringComparison.Ordinal) + OtherAggregateSource;
        CSharpCompilation initial = ModuleCompilation(source);
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        string removed = Assert.ContainsSingle(AggregateOutput(driver, "other").Helpers).Analysis.Callback;
        CSharpCompilation edited = initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(
            OtherAggregateSource.Replace("class Other", "class Total", StringComparison.Ordinal), cancellationToken: context.CancellationToken));
        driver = RunModule(driver, edited, out Compilation second);

        AssertAggregateRenderingReason(driver, "total", IncrementalStepRunReason.Cached);
        Assert.Contains(removed, ManifestValue(first, "Ankus.NativeSource"));
        Assert.DoesNotContain(removed, ManifestValue(second, "Ankus.NativeSource"));
        Assert.DoesNotContain("CREATE AGGREGATE \"other\"", InstallationBody(second));
        Assert.DoesNotContain(removed.Replace("ankus_managed_", "ankus_fn_", StringComparison.Ordinal), ManifestValue(second, "Ankus.Exports"));
        Assert.AreEqual(41, InvokeAggregateTransition(second, AggregateOutput(driver, "total")));
    }

    /// <summary>
    /// Finds a current production aggregate output without inferring cache behavior from generated text.
    /// </summary>
    private static AggregatePipeline.Output AggregateOutput(GeneratorDriver driver, string name)
        => Assert.ContainsSingle(Assert.IsInstanceOfType<EquatableArray<AggregatePipeline.Output>>(
            Assert.ContainsSingle(Assert.ContainsSingle(Assert.ContainsSingle(driver.GetRunResult().Results).TrackedSteps["AggregateOutputs"]).Outputs).Value)
            .Where(value => value.Analysis.Model?.Name == name));

    /// <summary>
    /// Reads an actual aggregate DDL rendering cache decision.
    /// </summary>
    private static (object Value, IncrementalStepRunReason Reason) AggregateSqlStep(GeneratorDriver driver, string name)
    {
        AggregatePipeline.Output current = AggregateOutput(driver, name);
        return Assert.ContainsSingle(Assert.ContainsSingle(driver.GetRunResult().Results).TrackedSteps["AggregateSqlEmission"]
            .SelectMany(static step => step.Outputs).Where(output => output.Reason != IncrementalStepRunReason.Removed &&
                output.Value is AggregateSqlEmission emission && emission.Name == current.Sql!.Name));
    }

    /// <summary>
    /// Reads an actual support rendering cache decision by aggregate declaration and role.
    /// </summary>
    private static (object Value, IncrementalStepRunReason Reason) AggregateHelperStep(GeneratorDriver driver, string stage, string name, string role = "Transition")
    {
        DeclarationIdentity identity = AggregateOutput(driver, name).Analysis.Identity;
        return Assert.ContainsSingle(Assert.ContainsSingle(driver.GetRunResult().Results).TrackedSteps[stage]
            .SelectMany(static step => step.Outputs).Where(output => output.Reason != IncrementalStepRunReason.Removed && (output.Value switch
            {
                AggregatePipeline.BoundaryOutput boundary => boundary.Slot.Aggregate == identity && boundary.Slot.Role == role,
                AggregatePipeline.HelperSqlOutput sql => sql.Slot.Aggregate == identity && sql.Slot.Role == role,
                _ => false,
            })));
    }

    /// <summary>
    /// Requires every independent rendering stage for an ordinary aggregate to report the expected production decision.
    /// </summary>
    private static void AssertAggregateRenderingReason(GeneratorDriver driver, string name, IncrementalStepRunReason reason)
    {
        Assert.AreEqual(reason, AggregateSqlStep(driver, name).Reason, "AggregateSqlEmission");
        foreach (string stage in new[] { "AggregateHelperBoundaryEmission", "AggregateHelperSqlEmission" })
        {
            Assert.AreEqual(reason, AggregateHelperStep(driver, stage, name).Reason, stage);
        }
    }

    /// <summary>
    /// Executes the emitted constrained call in the compiled current output without entering an unmanaged backend boundary.
    /// </summary>
    private int InvokeAggregateTransition(Compilation compilation, AggregatePipeline.Output output)
    {
        using var bytes = new MemoryStream();
        Microsoft.CodeAnalysis.Emit.EmitResult emitted = compilation.Emit(bytes, cancellationToken: context.CancellationToken);
        Assert.IsTrue(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        bytes.Position = 0;
        var load = new AssemblyLoadContext("AggregateIncrementalProbe", isCollectible: true);
        try
        {
            Assembly assembly = load.LoadFromStream(bytes);
            Type dispatchers = assembly.GetType("Ankus.Generated.ExtensionDispatchers", throwOnError: true)!;
            Type aggregate = assembly.GetType(output.Analysis.MetadataName, throwOnError: true)!;
            string callback = Assert.ContainsSingle(output.Helpers).Analysis.Callback;
            MethodInfo? invocation = dispatchers.GetMethod(callback + "_invoke", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(invocation);
            return Assert.IsInstanceOfType<int>(invocation.MakeGenericMethod(aggregate).Invoke(null, [null, 41, 0]));
        }
        finally
        {
            load.Unload();
        }
    }

    /// <summary>
    /// Supplies an independent ordinary aggregate for detecting unintended cache invalidation.
    /// </summary>
    private const string OtherAggregateSource = """
        [Ankus.PgAggregate(InitialCondition = "0")]
        public sealed class Other : Ankus.IPgAggregate<int, int>
        {
            public static int Transition(Ankus.PgAggregateContext context, int state, int value) => state + value;
        }
        """;
}
