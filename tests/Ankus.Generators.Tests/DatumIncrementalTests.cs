using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Ankus.Generators.Tests;

/// <summary>
/// Verifies production mapping analysis, registration cache decisions and current graph composition.
/// </summary>
public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Body edits and movement retain registrations while the actual compiled reader follows current source.
    /// </summary>
    /// <param name="move">Whether declarations move instead of changing a reader body.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void DatumRegistrationEmissionRemainsCachedAcrossIndependentEdits(bool move)
    {
        string source = DatumCachedSource();
        CSharpCompilation initial = ModuleCompilation(source);
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        CSharpCompilation edited = initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(
            move ? "\n\n" + source : source.Replace("Value(42)", "Value(43)", StringComparison.Ordinal),
            path: "Changed.cs", cancellationToken: context.CancellationToken));
        driver = RunModule(driver, edited, out Compilation second);

        Assert.AreEqual(IncrementalStepRunReason.Cached, DatumRegistrationStep(driver, "global::Value").Reason);
        Assert.AreEqual(ManifestValue(first, "Ankus.NativeSource"), ManifestValue(second, "Ankus.NativeSource"));
        Assert.AreEqual(InstallationBody(first), InstallationBody(second));
        Assert.AreEqual(42, RunDatumRegistrationReader(first));
        Assert.AreEqual(move ? 42 : 43, RunDatumRegistrationReader(second));
    }

    /// <summary>
    /// Catalog edits invalidate only the selected registration and update the current typed SQL slots.
    /// </summary>
    /// <param name="schema">Whether the fixed schema changes instead of the catalog name.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void DatumRegistrationEmissionTracksCatalogChangesIndependently(bool schema)
    {
        string source = DatumCachedSource();
        CSharpCompilation initial = ModuleCompilation(source).AddSyntaxTrees(CSharpSyntaxTree.ParseText(
            DatumMappingSource("public struct Other { }", type: "Other", name: "int8"), path: "Other.cs", cancellationToken: context.CancellationToken));
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        CSharpCompilation edited = initial.ReplaceSyntaxTree(initial.SyntaxTrees.First(), CSharpSyntaxTree.ParseText(
            source.Replace(schema ? "pg_catalog" : "int4", "changed", StringComparison.Ordinal), path: "Module.cs", cancellationToken: context.CancellationToken));
        driver = RunModule(driver, edited, out Compilation second);

        Assert.AreEqual(IncrementalStepRunReason.Modified, DatumRegistrationStep(driver, "global::Value").Reason);
        Assert.AreEqual(IncrementalStepRunReason.Cached, DatumRegistrationStep(driver, "global::Other").Reason);
        DatumTypeModel model = Assert.ContainsSingle(DatumAnalysis(driver).Models.Where(static model => model.Reference.Managed == "global::Value"));
        Assert.AreEqual("changed", schema ? model.Reference.Schema : model.Reference.Name);
        Assert.Contains(schema ? "\"changed\".\"int4\"" : "\"pg_catalog\".\"changed\"", InstallationBody(second));
        Assert.DoesNotContain("\"pg_catalog\".\"int4\"", InstallationBody(second));
        Assert.AreEqual(ManifestValue(first, "Ankus.NativeSource"), ManifestValue(second, "Ankus.NativeSource"));
    }

    /// <summary>
    /// Converter implementation identity changes the lazy factory without changing SQL or native transport.
    /// </summary>
    [TestMethod]
    public void DatumRegistrationEmissionTracksClosedConverterIdentity()
    {
        string source = DatumCachedSource();
        CSharpCompilation initial = ModuleCompilation(source);
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        CSharpCompilation edited = initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(
            source.Replace("Converter", "RenamedConverter", StringComparison.Ordinal), path: "Module.cs", cancellationToken: context.CancellationToken));
        driver = RunModule(driver, edited, out Compilation second);

        Assert.AreEqual(IncrementalStepRunReason.Modified, DatumRegistrationStep(driver, "global::Value").Reason);
        Assert.Contains("typeof(global::RenamedConverter), static () => new global::RenamedConverter()", DatumMappingManaged(second));
        Assert.DoesNotContain("typeof(global::Converter)", DatumMappingManaged(second));
        Assert.AreEqual(InstallationBody(first), InstallationBody(second));
        Assert.AreEqual(ManifestValue(first, "Ankus.NativeSource"), ManifestValue(second, "Ankus.NativeSource"));
    }

    /// <summary>
    /// Direction changes update registration capability flags while preserving a valid input-only function.
    /// </summary>
    [TestMethod]
    public void DatumRegistrationEmissionTracksConversionDirections()
    {
        const string Method = "public static class Functions { [Ankus.PgFunction] public static int Consume(Value value) => 42; }";
        CSharpCompilation initial = ModuleCompilation(DatumMappingSource() + Method);
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        CSharpCompilation edited = initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(
            DatumMappingSource(writer: false) + Method, path: "Module.cs", cancellationToken: context.CancellationToken));
        driver = RunModule(driver, edited, out Compilation second);

        Assert.AreEqual(IncrementalStepRunReason.Modified, DatumRegistrationStep(driver, "global::Value").Reason);
        DatumTypeModel model = Assert.ContainsSingle(DatumAnalysis(driver).Models);
        Assert.IsTrue(model.Reference.CanRead);
        Assert.IsFalse(model.Reference.CanWrite);
        Assert.Contains("Converter(), true, false);", DatumMappingManaged(second));
        Assert.AreEqual(InstallationBody(first), InstallationBody(second));
        Assert.AreEqual(ManifestValue(first, "Ankus.NativeSource"), ManifestValue(second, "Ankus.NativeSource"));
    }

    /// <summary>
    /// Invalid direction diagnostics reattach to the current source tree and disappear after repairing the contract.
    /// </summary>
    [TestMethod]
    public void DatumDiagnosticsFollowCurrentTreesAndRecover()
    {
        string source = DatumMappingSource(writer: false) + DatumMappingMethods("Value", string.Empty);
        CSharpCompilation initial = ModuleCompilation(source);
        GeneratorDriver driver = ModuleDriver().RunGeneratorsAndUpdateCompilation(initial, out _,
            out ImmutableArray<Diagnostic> previous, context.CancellationToken);
        Assert.IsNotEmpty(previous);
        Assert.IsTrue(previous.All(static diagnostic => diagnostic.Id == "ANKUS019"));
        SyntaxTree tree = CSharpSyntaxTree.ParseText(source + "\n// independent edit", path: "Module.cs", cancellationToken: context.CancellationToken);
        CSharpCompilation edited = initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), tree);
        driver = driver.RunGeneratorsAndUpdateCompilation(edited, out Compilation failed, out ImmutableArray<Diagnostic> errors, context.CancellationToken);

        Assert.AreSequenceEqual(previous.Select(static error => error.Id), errors.Select(static error => error.Id));
        Assert.AreSequenceEqual(previous.Select(static error => error.Location.SourceSpan), errors.Select(static error => error.Location.SourceSpan));
        foreach (Diagnostic error in errors)
        {
            Assert.AreSame(tree, error.Location.SourceTree);
        }

        Assert.AreEqual(IncrementalStepRunReason.Unchanged, ModuleStep(driver, "DatumAnalysis"));
        Assert.AreEqual(IncrementalStepRunReason.Cached, ModuleStep(driver, "DatumOutputs"));
        Assert.IsNull(failed.GetTypeByMetadataName("Ankus.Generated.ExtensionDispatchers"));
        CSharpCompilation repaired = edited.ReplaceSyntaxTree(tree, CSharpSyntaxTree.ParseText(
            DatumCachedSource(), path: "Module.cs", cancellationToken: context.CancellationToken));
        driver = RunModule(driver, repaired, out Compilation output);
        Assert.IsTrue(DatumAnalysis(driver).Valid);
        Assert.Contains("RegisterValue<global::Value>", DatumMappingManaged(output));
        Assert.AreEqual(42, RunDatumRegistrationReader(output));
    }

    /// <summary>
    /// Range catalog changes preserve the scalar registration cache and update exact range SQL identity.
    /// </summary>
    [TestMethod]
    public void DatumRangeRegistrationChangesIndependentlyOfScalar()
    {
        string source = "[Ankus.PgRangeType(\"int4range\", Origin=Ankus.PgTypeOrigin.External, Schema=\"pg_catalog\")] " +
            DatumMappingSource() + DatumMappingMethods("Ankus.PgRange<Value>", string.Empty);
        CSharpCompilation initial = ModuleCompilation(source);
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        CSharpCompilation edited = initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(
            source.Replace("int4range", "changed_range", StringComparison.Ordinal), path: "Module.cs", cancellationToken: context.CancellationToken));
        driver = RunModule(driver, edited, out Compilation second);

        Assert.AreEqual(IncrementalStepRunReason.Cached, DatumRegistrationStep(driver, "global::Value").Reason);
        Assert.AreEqual(IncrementalStepRunReason.Modified, DatumRegistrationStep(driver, "global::Value", range: true).Reason);
        Assert.Contains("RegisterRange<global::Value>(\"changed_range\"", DatumMappingManaged(second));
        Assert.Contains("RETURNS \"pg_catalog\".\"changed_range\"", InstallationBody(second));
        Assert.DoesNotContain("int4range", InstallationBody(second));
        Assert.AreEqual(ManifestValue(first, "Ankus.NativeSource"), ManifestValue(second, "Ankus.NativeSource"));
    }

    /// <summary>
    /// Current provider dependencies reorder owned SQL without rerendering a cached lazy registration.
    /// </summary>
    [TestMethod]
    public void DatumProviderGraphUpdatesAfterRegistrationCacheHit()
    {
        string source = """
            [assembly: Ankus.PgSql("a_domain", "CREATE DOMAIN item AS integer;", Relocatable=true)]
            [assembly: Ankus.PgSql("z_seed", "SELECT 'seed';", Relocatable=true)]
            [assembly: Ankus.PgSqlTypeProvider("a_domain", typeof(Value))]
            """ + DatumMappingSource(external: false, name: "item") + DatumMappingMethods("Value", string.Empty);
        CSharpCompilation initial = ModuleCompilation(source);
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        CSharpCompilation edited = initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(
            source.Replace("CREATE DOMAIN item AS integer;\", Relocatable=true", "CREATE DOMAIN item AS integer;\", Requires=new[]{\"z_seed\"}, Relocatable=true", StringComparison.Ordinal),
            path: "Module.cs", cancellationToken: context.CancellationToken));
        driver = RunModule(driver, edited, out Compilation second);

        Assert.AreEqual(IncrementalStepRunReason.Cached, DatumRegistrationStep(driver, "global::Value").Reason);
        string previous = InstallationBody(first);
        string current = InstallationBody(second);
        foreach (string sql in new[] { previous, current })
        {
            Assert.Contains("SELECT 'seed';", sql);
            Assert.Contains("CREATE DOMAIN item AS integer;", sql);
            Assert.Contains("CREATE FUNCTION", sql);
        }

        Assert.IsLessThan(previous.IndexOf("SELECT 'seed';", StringComparison.Ordinal), previous.IndexOf("CREATE DOMAIN item", StringComparison.Ordinal));
        Assert.IsLessThan(current.IndexOf("CREATE DOMAIN item", StringComparison.Ordinal), current.IndexOf("SELECT 'seed';", StringComparison.Ordinal));
        Assert.IsLessThan(current.IndexOf("CREATE FUNCTION", StringComparison.Ordinal), current.IndexOf("CREATE DOMAIN item", StringComparison.Ordinal));
        Assert.AreEqual(ManifestValue(first, "Ankus.NativeSource"), ManifestValue(second, "Ankus.NativeSource"));
    }

    /// <summary>
    /// Removing a trailing closed root removes its factory and native function while preserving the surviving registration cache.
    /// </summary>
    [TestMethod]
    public void DatumClosedRootRemovalDropsOnlyItsGeneratedContracts()
    {
        const string Source = """
            [Ankus.PgDatumType("int4", typeof(Converter<>), Origin=Ankus.PgTypeOrigin.External, Schema="pg_catalog")]
            public struct Value<T> { }
            public sealed class Converter<T> : Ankus.IPgDatumReader<Value<T>>
            {
                public Value<T> Read(Ankus.PgDatum value) => default;
            }
            public static class First
            {
                [Ankus.PgFunction] public static int Read(Value<int> value) => 41;
            }
            """;
        SyntaxTree extra = CSharpSyntaxTree.ParseText("public static class Second { [Ankus.PgFunction(Name=\"read_wide\")] public static int Read(Value<long> value) => 42; }",
            path: "Other.cs", cancellationToken: context.CancellationToken);
        CSharpCompilation initial = ModuleCompilation(Source).AddSyntaxTrees(extra);
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        driver = RunModule(driver, initial.RemoveSyntaxTrees(extra), out Compilation second);

        Assert.HasCount(1, DatumAnalysis(driver).Models);
        Assert.AreEqual(IncrementalStepRunReason.Cached, DatumRegistrationStep(driver, "global::Value<int>").Reason);
        Assert.Contains("new global::Converter<long>()", DatumMappingManaged(first));
        Assert.DoesNotContain("global::Value<long>", DatumMappingManaged(second));
        Assert.DoesNotContain("Converter<long>", DatumMappingManaged(second));
        Assert.Contains("Converter<int>", DatumMappingManaged(second));
        Assert.HasCount(1, SqlControlExports(second));
    }

    /// <summary>
    /// Creates a concrete reader with observable current implementation and an ordinary mapped function.
    /// </summary>
    /// <returns>The valid mapped extension source.</returns>
    private static string DatumCachedSource()
        => DatumMappingSource("public readonly record struct Value(int Number);").Replace("=> default!;", "=> new Value(42);", StringComparison.Ordinal)
            + "public static class Functions { [Ankus.PgFunction] public static Value Echo(Value value) => value; }";

    /// <summary>
    /// Reads the actual production analysis from Roslyn's tracked result.
    /// </summary>
    /// <param name="driver">The production driver with step tracking enabled.</param>
    /// <returns>The current detached semantic analysis.</returns>
    private static DatumPipeline.Analysis DatumAnalysis(GeneratorDriver driver)
        => Assert.IsInstanceOfType<DatumPipeline.Analysis>(Assert.ContainsSingle(Assert.ContainsSingle(
            Assert.ContainsSingle(driver.GetRunResult().Results).TrackedSteps["DatumAnalysis"]).Outputs).Value);

    /// <summary>
    /// Selects a scalar or range registration's actual cache decision by its exact managed bound.
    /// </summary>
    /// <param name="driver">The current production driver.</param>
    /// <param name="managed">The scalar or bound's exact managed spelling.</param>
    /// <param name="range">Whether to select the range registration instead of its scalar bound.</param>
    /// <returns>The actual cached rendering value and step reason.</returns>
    private static (object Value, IncrementalStepRunReason Reason) DatumRegistrationStep(GeneratorDriver driver, string managed, bool range = false)
    {
        DatumRegistrationModel model = Assert.ContainsSingle(DatumAnalysis(driver).Models.Where(value => range
            ? value.Registration.Bound == managed : value.Reference.Managed == managed)).Registration;
        string prefix = (range ? "RegisterRange<" : model.IsValueType ? "RegisterValue<" : "RegisterReference<") + managed + ">";
        return Assert.ContainsSingle(Assert.ContainsSingle(driver.GetRunResult().Results).TrackedSteps["DatumRegistrationEmission"]
            .SelectMany(static step => step.Outputs).Where(output => output.Reason != IncrementalStepRunReason.Removed &&
                output.Value is string source && source.Contains(prefix, StringComparison.Ordinal)));
    }
}
