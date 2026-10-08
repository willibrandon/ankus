using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using Ankus.PgConfig;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Ankus.Generators.Tests;

/// <summary>
/// Verifies exact semantic dependency selection and current graph binding after immutable analysis reuse.
/// </summary>
public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Body edits preserve exact references and current generated compilations execute the edited managed implementation.
    /// </summary>
    /// <param name="support">Whether the reference binds planner support instead of a prerequisite.</param>
    /// <param name="move">Whether to move the declaration instead of changing its body.</param>
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void SqlReferenceModelsPreserveExactContractsAcrossIndependentEdits(bool support, bool move)
    {
        string source = SqlReferenceSource(support);
        CSharpCompilation initial = ModuleCompilation(source);
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        SqlReferenceModel previous = Assert.ContainsSingle(SqlReferences(driver));
        driver = RunModule(driver, initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(
            move ? "\n\n" + source : source.Replace("=> 42;", "=> 43;", StringComparison.Ordinal),
            path: move ? "Changed.cs" : "Module.cs", cancellationToken: context.CancellationToken)), out Compilation second);
        SqlReferenceModel current = Assert.ContainsSingle(SqlReferences(driver));

        Assert.AreEqual(move ? IncrementalStepRunReason.Modified : IncrementalStepRunReason.Unchanged, ModuleStep(driver, "SqlReferenceAnalysis"));
        SqlReferenceModel previousContract = previous with { Location = null, TargetLocation = null, DeclarationIdLocation = null };
        SqlReferenceModel currentContract = current with { Location = null, TargetLocation = null, DeclarationIdLocation = null };
        Assert.AreEqual(previousContract, currentContract);
        Assert.AreEqual(previousContract.GetHashCode(), currentContract.GetHashCode());
        Assert.AreEqual(InstallationBody(first), InstallationBody(second));
        Assert.AreEqual(ManifestValue(first, "Ankus.NativeSource"), ManifestValue(second, "Ankus.NativeSource"));
        Assert.AreEqual(42, InvokeSqlReferenceAnswer(first));
        Assert.AreEqual(move ? 42 : 43, InvokeSqlReferenceAnswer(second));
        Assert.IsNotNull(current.Target);
        Assert.AreEqual("Functions.Target", current.Target.Display.Split('(')[0].Split(' ').Last());
    }

    /// <summary>
    /// A target's catalog rename binds current support SQL without changing compiler dependency selection.
    /// </summary>
    [TestMethod]
    public void SqlReferenceModelsBindCurrentPlannerCatalogAfterReuse()
    {
        string source = SqlReferenceSource(support: true).Replace("[Ankus.PgFunction]\n    public static Ankus.PgInternal? Target",
            "[Ankus.PgFunction(Name=\"first\")]\n    public static Ankus.PgInternal? Target", StringComparison.Ordinal);
        CSharpCompilation initial = ModuleCompilation(source);
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        driver = RunModule(driver, initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(
            source.Replace("Name=\"first\"", "Name=\"other\"", StringComparison.Ordinal), path: "Module.cs", cancellationToken: context.CancellationToken)),
            out Compilation second);

        Assert.AreEqual(IncrementalStepRunReason.Unchanged, ModuleStep(driver, "SqlReferenceAnalysis"));
        Assert.Contains("SUPPORT \"first\"", InstallationBody(first));
        Assert.Contains("SUPPORT \"other\"", InstallationBody(second));
        Assert.DoesNotContain("SUPPORT \"first\"", InstallationBody(second));
        ExtensionSchemaGraph graph = ExtensionSchemaGraph.Parse(ManifestValue(second, "Ankus.SqlGraph"));
        ExtensionSchemaItem sourceItem = Assert.ContainsSingle(graph.Items.Where(static item => item.Names.Contains("answer")));
        ExtensionSchemaItem targetItem = Assert.ContainsSingle(graph.Items.Where(static item => item.Names.Contains("other")));
        Assert.Contains(targetItem.Id, sourceItem.Dependencies);
        Assert.AreEqual(42, InvokeSqlReferenceAnswer(second));
    }

    /// <summary>
    /// Cached missing-target diagnostics attach to the current tree and a repaired target restores a complete graph.
    /// </summary>
    /// <param name="support">Whether the missing method is selected through planner support instead of a prerequisite.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void SqlReferenceDiagnosticsFollowCurrentTreesAndRecover(bool support)
    {
        string source = SqlReferenceSource(support).Replace("nameof(Target)", "\"Missing\"", StringComparison.Ordinal);
        CSharpCompilation initial = ModuleCompilation(source);
        GeneratorDriver driver = ModuleDriver().RunGeneratorsAndUpdateCompilation(initial, out _,
            out ImmutableArray<Diagnostic> previous, context.CancellationToken);
        const string id = "ANKUS475";
        Assert.AreEqual(id, Assert.ContainsSingle(previous).Id);
        SyntaxTree tree = CSharpSyntaxTree.ParseText(source + "\n// independent edit", path: "Module.cs", cancellationToken: context.CancellationToken);
        CSharpCompilation edited = initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), tree);
        driver = driver.RunGeneratorsAndUpdateCompilation(edited, out Compilation failed, out ImmutableArray<Diagnostic> errors, context.CancellationToken);
        Diagnostic error = Assert.ContainsSingle(errors);

        Assert.AreEqual(id, error.Id);
        Assert.AreSame(tree, error.Location.SourceTree);
        Assert.AreEqual(Assert.ContainsSingle(previous).Location.SourceSpan, error.Location.SourceSpan);
        Assert.Contains("was not found", error.GetMessage(CultureInfo.InvariantCulture));
        Assert.AreEqual(IncrementalStepRunReason.Unchanged, ModuleStep(driver, "SqlReferenceAnalysis"));
        Assert.IsNull(failed.GetTypeByMetadataName("Ankus.Generated.ExtensionManifest"));
        driver = RunModule(driver, edited.ReplaceSyntaxTree(tree, CSharpSyntaxTree.ParseText(SqlReferenceSource(support),
            path: "Module.cs", cancellationToken: context.CancellationToken)), out Compilation repaired);
        Assert.IsNull(Assert.ContainsSingle(SqlReferences(driver)).TargetError);
        Assert.Contains(support ? "SUPPORT \"target\"" : "CREATE FUNCTION \"target\"", InstallationBody(repaired));
        Assert.AreEqual(42, InvokeSqlReferenceAnswer(repaired));
    }

    /// <summary>
    /// Changing explicit parameter types selects the exact overload and updates the encoded graph dependency.
    /// </summary>
    [TestMethod]
    public void SqlReferenceModelsTrackExactSelectedOverloads()
    {
        const string Source = """
            [assembly: Ankus.PgSql("consumer", "SELECT 'consumer';", Relocatable=true)]
            [assembly: Ankus.PgRequires(typeof(Functions), nameof(Functions.Target), ParameterTypes=new[]{typeof(int)}, DeclarationId="consumer")]
            public static class Functions
            {
                [Ankus.PgFunction(Name="first")] public static int Target(int value) => value;
                [Ankus.PgFunction(Name="other")] public static long Target(long value) => value;
            }
            """;
        CSharpCompilation initial = ModuleCompilation(Source);
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        SqlReferenceModel before = Assert.ContainsSingle(SqlReferences(driver));
        driver = RunModule(driver, initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(
            Source.Replace("new[]{typeof(int)}", "new[]{typeof(long)}", StringComparison.Ordinal), path: "Module.cs", cancellationToken: context.CancellationToken)),
            out Compilation second);
        SqlReferenceModel after = Assert.ContainsSingle(SqlReferences(driver));

        Assert.AreEqual(IncrementalStepRunReason.Modified, ModuleStep(driver, "SqlReferenceAnalysis"));
        Assert.IsNotNull(before.Target);
        Assert.IsNotNull(after.Target);
        Assert.AreNotEqual(before.Target.Identity, after.Target.Identity);
        Assert.Contains("int", before.Target.Display);
        Assert.Contains("long", after.Target.Display);
        AssertSqlReferenceDependency(first, "consumer", "first");
        AssertSqlReferenceDependency(second, "consumer", "other");
        Assert.AreEqual(ManifestValue(first, "Ankus.NativeSource"), ManifestValue(second, "Ankus.NativeSource"));
    }

    /// <summary>
    /// Assembly declaration selection updates only the chosen source SQL node while retaining the exact managed target.
    /// </summary>
    [TestMethod]
    public void SqlReferenceModelsTrackAssemblyDeclarationSelection()
    {
        const string Source = """
            [assembly: Ankus.PgSql("first", "SELECT 'first';", Relocatable=true)]
            [assembly: Ankus.PgSql("other", "SELECT 'other';", Relocatable=true)]
            [assembly: Ankus.PgRequires(typeof(Functions), nameof(Functions.Target), DeclarationId="first")]
            public static class Functions { [Ankus.PgFunction] public static int Target() => 7; }
            """;
        CSharpCompilation initial = ModuleCompilation(Source);
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        SqlReferenceModel before = Assert.ContainsSingle(SqlReferences(driver));
        driver = RunModule(driver, initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(
            Source.Replace("DeclarationId=\"first\"", "DeclarationId=\"other\"", StringComparison.Ordinal), path: "Module.cs", cancellationToken: context.CancellationToken)),
            out Compilation second);
        SqlReferenceModel after = Assert.ContainsSingle(SqlReferences(driver));

        Assert.AreEqual(IncrementalStepRunReason.Modified, ModuleStep(driver, "SqlReferenceAnalysis"));
        Assert.IsTrue(before.Source.Assembly);
        Assert.AreEqual(before.Source, after.Source);
        Assert.AreEqual(before.Target, after.Target);
        AssertSqlReferenceDependency(first, "first", "target");
        AssertSqlReferenceDependency(second, "other", "target");
        ExtensionSchemaGraph graph = ExtensionSchemaGraph.Parse(ManifestValue(second, "Ankus.SqlGraph"));
        Assert.DoesNotContain(Assert.ContainsSingle(graph.Items.Where(static item => item.Names.Contains("target"))).Id,
            Assert.ContainsSingle(graph.Items.Where(static item => item.Names.Contains("first"))).Dependencies);
    }

    /// <summary>
    /// Requires the selected edge in the actual serialized installation graph.
    /// </summary>
    private static void AssertSqlReferenceDependency(Compilation compilation, string source, string target)
    {
        ExtensionSchemaGraph graph = ExtensionSchemaGraph.Parse(ManifestValue(compilation, "Ankus.SqlGraph"));
        ExtensionSchemaItem sourceItem = Assert.ContainsSingle(graph.Items.Where(item => item.Names.Contains(source)));
        ExtensionSchemaItem targetItem = Assert.ContainsSingle(graph.Items.Where(item => item.Names.Contains(target)));
        Assert.Contains(targetItem.Id, sourceItem.Dependencies);
    }

    /// <summary>
    /// Reads the original production semantic result rather than reconstructing references in the test.
    /// </summary>
    private static EquatableArray<SqlReferenceModel> SqlReferences(GeneratorDriver driver)
        => Assert.IsInstanceOfType<EquatableArray<SqlReferenceModel>>(Assert.ContainsSingle(Assert.ContainsSingle(
            Assert.ContainsSingle(driver.GetRunResult().Results).TrackedSteps["SqlReferenceAnalysis"]).Outputs).Value);

    /// <summary>
    /// Executes the current managed method from the actual generated compilation.
    /// </summary>
    private int InvokeSqlReferenceAnswer(Compilation compilation)
    {
        using var bytes = new MemoryStream();
        Microsoft.CodeAnalysis.Emit.EmitResult emitted = compilation.Emit(bytes, cancellationToken: context.CancellationToken);
        Assert.IsTrue(emitted.Success, string.Join("\n", emitted.Diagnostics));
        bytes.Position = 0;
        var load = new AssemblyLoadContext("SqlReferenceProbe", isCollectible: true);
        try
        {
            Assembly assembly = load.LoadFromStream(bytes);
            Type? functions = assembly.GetType("Functions");
            Assert.IsNotNull(functions);
            MethodInfo? answer = functions.GetMethod("Answer");
            Assert.IsNotNull(answer);
            return Assert.IsInstanceOfType<int>(answer.Invoke(null, null));
        }
        finally
        {
            load.Unload();
        }
    }

    /// <summary>
    /// Supplies one exact reference with a callable ordinary consumer and the corresponding typed target.
    /// </summary>
    private static string SqlReferenceSource(bool support)
        => "public static class Functions\n{\n    [Ankus.PgFunction]\n    [Ankus." +
            (support ? "PgSupportFunction" : "PgRequires") + "(typeof(Functions), nameof(Target))]\n" +
            "    public static int Answer() => 42;\n    [Ankus.PgFunction]\n    public static " +
            (support ? "Ankus.PgInternal? Target(Ankus.PgInternal value) => null;" : "int Target() => 7;") + "\n}";
}
