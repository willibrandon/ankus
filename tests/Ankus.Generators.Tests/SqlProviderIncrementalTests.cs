using System.Collections.Immutable;
using System.Globalization;
using Ankus.PgConfig;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Ankus.Generators.Tests;

/// <summary>
/// Verifies detached provider inventory against current graph ownership and exact compiled managed behavior.
/// </summary>
public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Body edits and movement preserve provider values while the generated compilation executes the current method.
    /// </summary>
    /// <param name="kind">The named type, managed type or function inventory.</param>
    /// <param name="move">Whether to move source instead of editing the implementation.</param>
    [TestMethod]
    [DataRow("named", false)]
    [DataRow("named", true)]
    [DataRow("managed", false)]
    [DataRow("managed", true)]
    [DataRow("function", false)]
    [DataRow("function", true)]
    public void SqlProviderModelsPreserveContractsAcrossIndependentEdits(string kind, bool move)
    {
        string source = ProviderInventorySource(kind);
        CSharpCompilation initial = ModuleCompilation(source);
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        SqlProviderModel before = Assert.ContainsSingle(SqlProviders(driver));
        driver = RunModule(driver, initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(
            move ? "\n\n" + source : source.Replace("=> 42;", "=> 43;", StringComparison.Ordinal),
            path: "Changed.cs", cancellationToken: context.CancellationToken)), out Compilation second);
        SqlProviderModel after = Assert.ContainsSingle(SqlProviders(driver));

        Assert.AreEqual(move ? IncrementalStepRunReason.Modified : IncrementalStepRunReason.Unchanged, ModuleStep(driver, "SqlProviderAnalysis"));
        Assert.AreEqual(before with { Location = null }, after with { Location = null });
        Assert.AreEqual((before with { Location = null }).GetHashCode(), (after with { Location = null }).GetHashCode());
        Assert.AreEqual(kind == "managed", after.Managed);
        Assert.AreEqual(kind == "function", after.Function);
        Assert.AreEqual(InstallationBody(first), InstallationBody(second));
        Assert.AreEqual(ManifestValue(first, "Ankus.NativeSource"), ManifestValue(second, "Ankus.NativeSource"));
        Assert.AreEqual(42, InvokeSqlReferenceAnswer(first));
        Assert.AreEqual(move ? 42 : 43, InvokeSqlReferenceAnswer(second));
        ExtensionSchemaGraph graph = ExtensionSchemaGraph.Parse(ManifestValue(second, "Ankus.SqlGraph"));
        ExtensionSchemaItem provider = Assert.ContainsSingle(graph.Items.Where(static item => item.Names.Contains("a_definition")));
        Assert.Contains(kind == "function" ? "FUNCTION provided()" : "TYPE \"item\"", provider.Attachments);
        if (kind != "function")
        {
            AssertSqlReferenceDependency(second, "get", "a_definition");
        }
    }

    /// <summary>
    /// Exact schema changes add a current inferred dependency without changing the bound function's native contract.
    /// </summary>
    [TestMethod]
    public void SqlProviderModelsTrackExactSchemaOwnership()
    {
        const string Source = """
            [assembly: Ankus.PgSql("a_definition", "CREATE DOMAIN b.item AS integer;", Relocatable=false)]
            [assembly: Ankus.PgSqlTypeProvider("a_definition", "item", Schema="a")]
            public static class Functions
            {
                [Ankus.PgFunction] public static int Answer() => 42;
                [Ankus.PgFunction][return: Ankus.PgSqlType("item", Schema="b")]
                public static Ankus.PgDatum Get() => default;
            }
            """;
        CSharpCompilation initial = ModuleCompilation(Source);
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        driver = RunModule(driver, initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(
            Source.Replace("Schema=\"a\"", "Schema=\"b\"", StringComparison.Ordinal), path: "Module.cs", cancellationToken: context.CancellationToken)),
            out Compilation second);

        Assert.AreEqual(IncrementalStepRunReason.Modified, ModuleStep(driver, "SqlProviderAnalysis"));
        Assert.AreEqual("b", Assert.ContainsSingle(SqlProviders(driver)).Schema);
        ExtensionSchemaGraph previous = ExtensionSchemaGraph.Parse(ManifestValue(first, "Ankus.SqlGraph"));
        string provider = Assert.ContainsSingle(previous.Items.Where(static item => item.Names.Contains("a_definition"))).Id;
        Assert.DoesNotContain(provider, Assert.ContainsSingle(previous.Items.Where(static item => item.Names.Contains("get"))).Dependencies);
        AssertSqlReferenceDependency(second, "get", "a_definition");
        Assert.AreEqual(ManifestValue(first, "Ankus.NativeSource"), ManifestValue(second, "Ankus.NativeSource"));
        Assert.AreEqual(42, InvokeSqlReferenceAnswer(second));
    }

    /// <summary>
    /// Authored function signature edits update current selection and attachments while leaving SQL verbatim.
    /// </summary>
    [TestMethod]
    public void SqlProviderModelsTrackFunctionAttachmentIdentity()
    {
        string source = ProviderInventorySource("function");
        CSharpCompilation initial = ModuleCompilation(source);
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        driver = RunModule(driver, initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(
            source.Replace("\"provided()\"", "\"changed()\"", StringComparison.Ordinal), path: "Module.cs", cancellationToken: context.CancellationToken)),
            out Compilation second);

        Assert.AreEqual(IncrementalStepRunReason.Modified, ModuleStep(driver, "SqlProviderAnalysis"));
        Assert.AreEqual("changed()", Assert.ContainsSingle(SqlProviders(driver)).Name);
        Assert.AreEqual(InstallationBody(first), InstallationBody(second));
        ExtensionSchemaItem block = Assert.ContainsSingle(ExtensionSchemaGraph.Parse(ManifestValue(second, "Ankus.SqlGraph")).Items
            .Where(static item => item.Names.Contains("a_definition")));
        Assert.Contains("changed()", block.Names);
        Assert.DoesNotContain("provided()", block.Names);
        Assert.AreEqual("FUNCTION changed()", Assert.ContainsSingle(block.Attachments));
        Assert.AreEqual(ManifestValue(first, "Ankus.NativeSource"), ManifestValue(second, "Ankus.NativeSource"));
    }

    /// <summary>
    /// Unchanged invalid metadata reports on the current tree and repaired block selection restores complete output.
    /// </summary>
    /// <param name="function">Whether to validate a function instead of a named type provider.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void SqlProviderDiagnosticsFollowCurrentTreesAndRecover(bool function)
    {
        string source = ProviderInventorySource(function ? "function" : "named").Replace(
            "Provider(\"a_definition\",", "Provider(\"missing\",", StringComparison.Ordinal);
        CSharpCompilation initial = ModuleCompilation(source);
        GeneratorDriver driver = ModuleDriver().RunGeneratorsAndUpdateCompilation(initial, out _,
            out ImmutableArray<Diagnostic> previous, context.CancellationToken);
        Assert.AreEqual("ANKUS005", Assert.ContainsSingle(previous).Id);
        SyntaxTree tree = CSharpSyntaxTree.ParseText(source + "\n// independent edit", path: "Module.cs", cancellationToken: context.CancellationToken);
        CSharpCompilation edited = initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), tree);
        driver = driver.RunGeneratorsAndUpdateCompilation(edited, out Compilation failed, out ImmutableArray<Diagnostic> errors, context.CancellationToken);
        Diagnostic error = Assert.ContainsSingle(errors);

        Assert.AreEqual("ANKUS005", error.Id);
        Assert.AreSame(tree, error.Location.SourceTree);
        Assert.AreEqual(Assert.ContainsSingle(previous).Location.SourceSpan, error.Location.SourceSpan);
        Assert.Contains(function ? "existing PgSql" : "must name a PgSql", error.GetMessage(CultureInfo.InvariantCulture));
        Assert.AreEqual(IncrementalStepRunReason.Unchanged, ModuleStep(driver, "SqlProviderAnalysis"));
        Assert.IsNull(failed.GetTypeByMetadataName("Ankus.Generated.ExtensionManifest"));
        driver = RunModule(driver, edited.ReplaceSyntaxTree(tree, CSharpSyntaxTree.ParseText(ProviderInventorySource(function ? "function" : "named"),
            path: "Module.cs", cancellationToken: context.CancellationToken)), out Compilation repaired);
        Assert.AreEqual("a_definition", Assert.ContainsSingle(SqlProviders(driver)).BlockId);
        Assert.AreEqual(42, InvokeSqlReferenceAnswer(repaired));
    }

    /// <summary>
    /// Explicit null schema remains an authored override and is rejected for the managed constructor overload.
    /// </summary>
    [TestMethod]
    public void SqlProviderModelsPreserveExplicitNullSchema()
    {
        string source = ProviderInventorySource("managed").Replace("typeof(Value))]", "typeof(Value), Schema=null)]", StringComparison.Ordinal);
        CSharpCompilation initial = ModuleCompilation(source);
        GeneratorDriver driver = ModuleDriver().RunGeneratorsAndUpdateCompilation(initial, out _,
            out ImmutableArray<Diagnostic> errors, context.CancellationToken);
        SqlProviderModel provider = Assert.ContainsSingle(SqlProviders(driver));

        Assert.IsTrue(provider.Managed);
        Assert.IsTrue(provider.SchemaAuthored);
        Assert.IsNull(provider.Schema);
        Assert.IsNotNull(provider.Type);
        Assert.AreEqual("ANKUS005", Assert.ContainsSingle(errors.Where(static error =>
            error.GetMessage(CultureInfo.InvariantCulture).Contains("cannot specify Schema", StringComparison.Ordinal))).Id);
        driver = RunModule(driver, initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(
            ProviderInventorySource("managed"), path: "Module.cs", cancellationToken: context.CancellationToken)), out Compilation repaired);
        Assert.IsFalse(Assert.ContainsSingle(SqlProviders(driver)).SchemaAuthored);
        AssertSqlReferenceDependency(repaired, "get", "a_definition");
    }

    /// <summary>
    /// Removing the final function inventory leaves its SQL verbatim and removes all obsolete attachments and selection names.
    /// </summary>
    [TestMethod]
    public void SqlProviderRemovalDropsOnlyItsInventory()
    {
        string source = ProviderInventorySource("function");
        CSharpCompilation initial = ModuleCompilation(source);
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        driver = RunModule(driver, initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(
            source.Replace("[assembly: Ankus.PgSqlFunctionProvider(\"a_definition\", \"provided()\")]", string.Empty, StringComparison.Ordinal),
            path: "Module.cs", cancellationToken: context.CancellationToken)), out Compilation second);

        Assert.IsEmpty(SqlProviders(driver));
        Assert.AreEqual(InstallationBody(first), InstallationBody(second));
        ExtensionSchemaItem block = Assert.ContainsSingle(ExtensionSchemaGraph.Parse(ManifestValue(second, "Ankus.SqlGraph")).Items
            .Where(static item => item.Names.Contains("a_definition")));
        Assert.IsEmpty(block.Attachments);
        Assert.DoesNotContain("provided()", block.Names);
        Assert.AreEqual(ManifestValue(first, "Ankus.NativeSource"), ManifestValue(second, "Ankus.NativeSource"));
        Assert.AreEqual(42, InvokeSqlReferenceAnswer(second));
    }

    /// <summary>
    /// Reads the actual production provider analysis rather than reconstructing metadata in the fixture.
    /// </summary>
    private static EquatableArray<SqlProviderModel> SqlProviders(GeneratorDriver driver)
        => Assert.IsInstanceOfType<EquatableArray<SqlProviderModel>>(Assert.ContainsSingle(Assert.ContainsSingle(
            Assert.ContainsSingle(driver.GetRunResult().Results).TrackedSteps["SqlProviderAnalysis"]).Outputs).Value);

    /// <summary>
    /// Supplies independent named, managed and function declarations with a callable ordinary consumer.
    /// </summary>
    private static string ProviderInventorySource(string kind)
    {
        string prefix = kind == "function" ? """
            [assembly: Ankus.PgSql("a_definition", "CREATE FUNCTION provided() RETURNS integer LANGUAGE SQL AS 'SELECT 7';", Relocatable=true)]
            [assembly: Ankus.PgSqlFunctionProvider("a_definition", "provided()")]
            """ : """
            [assembly: Ankus.PgSql("a_definition", "CREATE DOMAIN item AS integer;", Relocatable=true)]
            """ + (kind == "managed"
                ? "[assembly: Ankus.PgSqlTypeProvider(\"a_definition\", typeof(Value))]"
                : "[assembly: Ankus.PgSqlTypeProvider(\"a_definition\", \"item\")]");
        string get = kind switch
        {
            "managed" => "[Ankus.PgFunction] public static Value Get(Value value) => value;",
            "named" => "[Ankus.PgFunction][return: Ankus.PgSqlType(\"item\")] public static Ankus.PgDatum Get() => default;",
            _ => string.Empty,
        };
        return prefix + (kind == "managed" ? DatumMappingSource(external: false, name: "item") : string.Empty) +
            "public static class Functions { [Ankus.PgFunction] public static int Answer() => 42; " + get + " }";
    }
}
