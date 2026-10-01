using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Ankus.Generators.Tests;

public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Independent implementation edits preserve detached aggregate contracts and complete compiled dispatch.
    /// </summary>
    /// <param name="move">Whether the declaration moves rather than changing its transition body.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void DetachedAggregateModelsPreserveImplementationIndependence(bool move)
    {
        const string Source = """
            [Ankus.PgAggregate(InitialCondition = "0")]
            public sealed class Total : Ankus.IPgAggregate<int, int>
            {
                public static int Transition(Ankus.PgAggregateContext context, int state, int value) => state + value;
            }
            """;
        CSharpCompilation initial = ModuleCompilation(Source);
        AggregateModel previous = DetachedAggregateContract(initial);
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        CSharpCompilation edited = initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(
            move ? "\n\n" + Source : Source.Replace("state + value", "state + value + 1", StringComparison.Ordinal),
            path: "Moved.cs", cancellationToken: context.CancellationToken));
        AggregateModel current = DetachedAggregateContract(edited);
        RunModule(driver, edited, out Compilation second);

        Assert.AreEqual(previous, current);
        Assert.AreEqual(previous.GetHashCode(), current.GetHashCode());
        Assert.AreEqual(AggregateSqlModel.Create(previous), AggregateSqlModel.Create(current));
        Assert.AreNotSame(initial.GetTypeByMetadataName("Total"), edited.GetTypeByMetadataName("Total"));
        Assert.AreEqual("\"total\"(integer)", current.Signature);
        AggregateHelperModel helper = Assert.ContainsSingle(current.Helpers);
        Assert.AreEqual("Transition", helper.Role);
        Assert.AreSequenceEqual(["state", "value"], helper.Parameters.Select(static parameter => parameter.Name));
        Assert.AreSequenceEqual(["global::Ankus.PgAggregateContext", "int", "int"], helper.Invocation.Parameters);
        Assert.AreEqual("global::Ankus.IPgAggregate<int, int>", helper.Invocation.Interface);
        Assert.AreEqual("global::Total", helper.Invocation.Implementation);
        Assert.AreEqual(InstallationBody(first), InstallationBody(second));
        Assert.AreEqual(ManifestValue(first, "Ankus.NativeSource"), ManifestValue(second, "Ankus.NativeSource"));
        Assert.Contains("Moved.cs:", ManifestValue(second, "Ankus.Sql"));
    }

    /// <summary>
    /// Numeric conversion metadata stays detached and changes only the actual selected managed conversion.
    /// </summary>
    /// <param name="target">Whether state, input or return precision changes.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public void DetachedAggregateModelsPreserveNumericConversionMetadata(int target)
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
        string old = target switch
        {
            0 => "[Ankus.PgNumericPrecision(5, 2)] Ankus.PgNumeric state",
            1 => "[Ankus.PgNumericPrecision(5, 2)] Ankus.PgNumeric value",
            _ => "[return: Ankus.PgNumericPrecision(5, 2)]",
        };
        CSharpCompilation initial = ModuleCompilation(Source);
        AggregateModel previous = DetachedAggregateContract(initial);
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        CSharpCompilation edited = initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(
            Source.Replace(old, old.Replace("5, 2", "6, 3", StringComparison.Ordinal), StringComparison.Ordinal),
            path: "Module.cs", cancellationToken: context.CancellationToken));
        AggregateModel current = DetachedAggregateContract(edited);
        RunModule(driver, edited, out Compilation second);

        Assert.AreNotEqual(previous, current);
        AggregateSqlModel previousSql = AggregateSqlModel.Create(previous);
        AggregateSqlModel currentSql = AggregateSqlModel.Create(current);
        Assert.AreEqual(previousSql, currentSql);
        Assert.AreEqual(previousSql.GetHashCode(), currentSql.GetHashCode());
        AggregateHelperModel helper = Assert.ContainsSingle(current.Helpers);
        Assert.AreEqual(target == 0 ? new NumericPrecision(6, 3) : new NumericPrecision(5, 2), helper.Parameters[0].Precision);
        Assert.AreEqual(target == 1 ? new NumericPrecision(6, 3) : new NumericPrecision(5, 2), helper.Parameters[1].Precision);
        Assert.AreEqual(target == 2 ? new NumericPrecision(6, 3) : new NumericPrecision(5, 2), helper.ResultPrecision);
        Assert.AreEqual(InstallationBody(first), InstallationBody(second));
        Assert.AreEqual(ManifestValue(first, "Ankus.NativeSource"), ManifestValue(second, "Ankus.NativeSource"));
        string managed = string.Join("\n", second.SyntaxTrees.Skip(1).Select(tree => tree.GetText(context.CancellationToken).ToString()));
        Assert.HasCount(2, managed.Split(".Rescale(6, 3)", StringSplitOptions.None));
        Assert.HasCount(3, managed.Split(".Rescale(5, 2)", StringSplitOptions.None));
    }

    /// <summary>
    /// Support parameter renames update their function SQL without invalidating the enclosing aggregate DDL.
    /// </summary>
    /// <param name="final">Whether to rename the final state rather than the transition state.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void DetachedAggregateSqlIgnoresSupportOnlyParameterNames(bool final)
    {
        const string Source = """
            [Ankus.PgAggregate(InitialCondition = "0")]
            public sealed class Total : Ankus.IPgAggregate<int, int>,
                Ankus.IPgFinalizingAggregate<int, System.ValueTuple, int>
            {
                public static int Transition(Ankus.PgAggregateContext context, int state, int value) => state + value;
                public static int Final(Ankus.PgAggregateContext context, int state, System.ValueTuple direct) => state;
            }
            """;
        CSharpCompilation initial = ModuleCompilation(Source);
        AggregateModel previous = DetachedAggregateContract(initial);
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        string changed = final
            ? Source.Replace("int state, System.ValueTuple direct) => state", "int accumulator, System.ValueTuple direct) => accumulator", StringComparison.Ordinal)
            : Source.Replace("int state, int value) => state + value", "int accumulator, int value) => accumulator + value", StringComparison.Ordinal);
        CSharpCompilation edited = initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(changed,
            cancellationToken: context.CancellationToken));
        AggregateModel current = DetachedAggregateContract(edited);
        RunModule(driver, edited, out Compilation second);

        Assert.AreNotEqual(previous, current);
        Assert.AreEqual(AggregateSqlModel.Create(previous), AggregateSqlModel.Create(current));
        Assert.AreEqual(AggregateSqlEmission.Create(AggregateSqlModel.Create(previous)),
            AggregateSqlEmission.Create(AggregateSqlModel.Create(current)));
        string before = InstallationBody(first);
        string after = InstallationBody(second);
        Assert.AreNotEqual(before, after);
        Assert.Contains("\"accumulator\" integer", after);
        int previousStart = before.IndexOf("CREATE AGGREGATE", StringComparison.Ordinal);
        int currentStart = after.IndexOf("CREATE AGGREGATE", StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, previousStart);
        Assert.IsGreaterThanOrEqualTo(0, currentStart);
        Assert.AreEqual(before[previousStart..], after[currentStart..]);
        Assert.AreEqual(ManifestValue(first, "Ankus.NativeSource"), ManifestValue(second, "Ankus.NativeSource"));
    }

    /// <summary>
    /// Captures an actual aggregate declaration through the production semantic validators.
    /// </summary>
    private AggregateModel DetachedAggregateContract(CSharpCompilation compilation)
    {
        INamedTypeSymbol? type = compilation.GetTypeByMetadataName("Total");
        Assert.IsNotNull(type);
        var errors = new List<string>();
        var diagnostics = new GeneratorDiagnostics((descriptor, _, _) => errors.Add(descriptor.Id), context.CancellationToken);
        AggregateDeclaration? declaration = AggregateDeclaration.Create(type, diagnostics);
        Assert.IsNotNull(declaration);
        Assert.IsEmpty(errors);
        return declaration.Freeze();
    }
}
