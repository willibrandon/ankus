using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Ankus.Generators.Tests;

/// <summary>
/// Verifies cached derived helper boundaries against current compiled value semantics and graph policy.
/// </summary>
public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Body edits and movement preserve both roots' helpers while direct generated calls use current implementations.
    /// </summary>
    /// <param name="mapped">Whether the root uses a mapped datum instead of generated binary storage.</param>
    /// <param name="move">Whether source moves instead of changing its hash implementation.</param>
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void DerivedHelperEmissionRemainsCachedAcrossIndependentEdits(bool mapped, bool move)
    {
        string source = mapped ? MappedOperatorSource() : CustomOperatorValueSource;
        string other = OtherDerivedSource(source)
            .Replace("\"key\"", "\"other\"", StringComparison.Ordinal);
        CSharpCompilation initial = ModuleCompilation(source).AddSyntaxTrees(CSharpSyntaxTree.ParseText(other, path: "Other.cs", cancellationToken: context.CancellationToken));
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        CSharpCompilation edited = initial.ReplaceSyntaxTree(initial.SyntaxTrees.First(), CSharpSyntaxTree.ParseText(
            move ? "\n\n" + source : source.Replace("992", "991", StringComparison.Ordinal), path: "Changed.cs", cancellationToken: context.CancellationToken));
        driver = RunModule(driver, edited, out Compilation second);

        AssertDerivedHelperReasons(driver, "global::Value", IncrementalStepRunReason.Cached);
        AssertDerivedHelperReasons(driver, "global::Other", IncrementalStepRunReason.Cached);
        AssertDerivedSqlReason(driver, "global::Value", IncrementalStepRunReason.Cached);
        AssertDerivedSqlReason(driver, "global::Other", IncrementalStepRunReason.Cached);
        Assert.AreEqual(ManifestValue(first, "Ankus.NativeSource"), ManifestValue(second, "Ankus.NativeSource"));
        Assert.AreEqual(InstallationBody(first), InstallationBody(second));
        DerivedOperatorModel model = Assert.ContainsSingle(DatumAnalysis(driver).Derived.Where(static model => model.Managed == "global::Value"));
        Assert.AreEqual(-989, RunDerivedHash(first, model.Symbol));
        Assert.AreEqual(move ? -989 : -988, RunDerivedHash(second, model.Symbol));
    }

    /// <summary>
    /// Authored SQL disablement updates current graph policy without invalidating any helper boundary.
    /// </summary>
    [TestMethod]
    public void DerivedHelperEmissionRemainsCachedAcrossSqlPolicyChanges()
    {
        string source = CustomOperatorValueSource.Replace("[Ankus.PgOrdering]", "[Ankus.PgOrdering(GenerateSql=true)]", StringComparison.Ordinal);
        CSharpCompilation initial = ModuleCompilation(source);
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        CSharpCompilation edited = initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(
            source.Replace("GenerateSql=true", "GenerateSql=false", StringComparison.Ordinal), path: "Module.cs", cancellationToken: context.CancellationToken));
        driver = RunModule(driver, edited, out Compilation second);

        AssertDerivedHelperReasons(driver, "global::Value", IncrementalStepRunReason.Cached);
        AssertDerivedSqlReason(driver, "global::Value", IncrementalStepRunReason.Cached);
        Assert.Contains("USING btree", InstallationBody(first));
        Assert.DoesNotContain("USING btree", InstallationBody(second));
        Assert.Contains("USING hash", InstallationBody(second));
        Assert.AreEqual(ManifestValue(first, "Ankus.NativeSource"), ManifestValue(second, "Ankus.NativeSource"));
    }

    /// <summary>
    /// Current extension initialization is inserted before cached derived helper native work.
    /// </summary>
    [TestMethod]
    public void DerivedHelperEmissionComposesCurrentInitialization()
    {
        CSharpCompilation initial = ModuleCompilation(MappedOperatorSource());
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        CSharpCompilation edited = initial.AddSyntaxTrees(CSharpSyntaxTree.ParseText(
            "public static class Lifecycle { [Ankus.PgInitialize] public static void Initialize() { } }", path: "Lifecycle.cs", cancellationToken: context.CancellationToken));
        driver = RunModule(driver, edited, out Compilation second);

        AssertDerivedHelperReasons(driver, "global::Value", IncrementalStepRunReason.Cached);
        AssertDerivedSqlReason(driver, "global::Value", IncrementalStepRunReason.Cached);
        foreach (DatumPipeline.HelperOutput helper in DatumHelpers(driver, "global::Value"))
        {
            string header = helper.Emission.Native.Header.Replace("\r\n", "\n", StringComparison.Ordinal);
            string body = helper.Emission.Native.Body.Replace("\r\n", "\n", StringComparison.Ordinal);
            Assert.Contains(header + body, ManifestValue(first, "Ankus.NativeSource"));
            Assert.Contains(header + "    ankus_ensure_initialized();\n" + body, ManifestValue(second, "Ankus.NativeSource"));
        }

        Assert.AreEqual(InstallationBody(first), InstallationBody(second));
    }

    /// <summary>
    /// Assembly identity invalidates every helper symbol and removes all previous symbols from the current native source.
    /// </summary>
    [TestMethod]
    public void DerivedHelperEmissionTracksAssemblyIdentity()
    {
        CSharpCompilation initial = ModuleCompilation(MappedOperatorSource());
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        string previous = Assert.ContainsSingle(DatumAnalysis(driver).Derived).Symbol;
        driver = RunModule(driver, initial.WithAssemblyName("RenamedExtension"), out Compilation second);
        string current = Assert.ContainsSingle(DatumAnalysis(driver).Derived).Symbol;

        Assert.AreNotEqual(previous, current);
        AssertDerivedHelperReasons(driver, "global::Value", IncrementalStepRunReason.Modified);
        AssertDerivedSqlReason(driver, "global::Value", IncrementalStepRunReason.Modified);
        Assert.DoesNotContain(previous, ManifestValue(second, "Ankus.NativeSource"));
        Assert.Contains(current, ManifestValue(second, "Ankus.NativeSource"));
        Assert.AreNotEqual(InstallationBody(first), InstallationBody(second));
        Assert.AreEqual(-989, RunDerivedHash(second, current));
    }

    /// <summary>
    /// Invalid interface contracts report on current trees and recover into complete typed helper output.
    /// </summary>
    [TestMethod]
    public void DerivedDiagnosticsFollowCurrentTreesAndRecover()
    {
        const string Source = "[Ankus.PgType][Ankus.PgEquality][Ankus.PgOrdering] public readonly record struct Value(int Number);";
        CSharpCompilation initial = ModuleCompilation(Source);
        GeneratorDriver driver = ModuleDriver().RunGeneratorsAndUpdateCompilation(initial, out _,
            out ImmutableArray<Diagnostic> previous, context.CancellationToken);
        Assert.AreEqual("ANKUS018", Assert.ContainsSingle(previous).Id);
        SyntaxTree tree = CSharpSyntaxTree.ParseText(Source + "\n// independent edit", path: "Module.cs", cancellationToken: context.CancellationToken);
        CSharpCompilation edited = initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), tree);
        driver = driver.RunGeneratorsAndUpdateCompilation(edited, out Compilation failed, out ImmutableArray<Diagnostic> errors, context.CancellationToken);
        Diagnostic error = Assert.ContainsSingle(errors);

        Assert.AreEqual("ANKUS018", error.Id);
        Assert.AreSame(tree, error.Location.SourceTree);
        Assert.AreEqual(Assert.ContainsSingle(previous).Location.SourceSpan, error.Location.SourceSpan);
        Assert.AreEqual(IncrementalStepRunReason.Unchanged, ModuleStep(driver, "DatumAnalysis"));
        Assert.AreEqual(IncrementalStepRunReason.Cached, ModuleStep(driver, "DatumOutputs"));
        Assert.IsNull(failed.GetTypeByMetadataName("Ankus.Generated.ExtensionDispatchers"));
        CSharpCompilation repaired = edited.ReplaceSyntaxTree(tree, CSharpSyntaxTree.ParseText(Source.Replace(";",
            " : System.IComparable<Value> { public int CompareTo(Value other) => Number.CompareTo(other.Number); }", StringComparison.Ordinal),
            path: "Module.cs", cancellationToken: context.CancellationToken));
        driver = RunModule(driver, repaired, out Compilation output);

        Assert.HasCount(7, DatumHelpers(driver, "global::Value"));
        Assert.Contains("USING btree", InstallationBody(output));
        Assert.IsNull(Assert.ContainsSingle(DatumAnalysis(driver).Derived).Error);
    }

    /// <summary>
    /// Removing a trailing derived root removes its SQL and boundaries while preserving the complete surviving helper cache.
    /// </summary>
    [TestMethod]
    public void DerivedRemovalDropsOnlyItsGeneratedContracts()
    {
        SyntaxTree extra = CSharpSyntaxTree.ParseText(CustomOperatorValueSource,
            path: "Value.cs", cancellationToken: context.CancellationToken);
        CSharpCompilation initial = ModuleCompilation(OtherDerivedSource(CustomOperatorValueSource)).AddSyntaxTrees(extra);
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        string removed = Assert.ContainsSingle(DatumAnalysis(driver).Derived.Where(static model => model.Managed == "global::Value")).Symbol;
        driver = RunModule(driver, initial.RemoveSyntaxTrees(extra), out Compilation second);

        Assert.HasCount(1, DatumAnalysis(driver).Derived);
        AssertDerivedHelperReasons(driver, "global::Other", IncrementalStepRunReason.Cached);
        AssertDerivedSqlReason(driver, "global::Other", IncrementalStepRunReason.Cached);
        Assert.Contains(removed, ManifestValue(first, "Ankus.NativeSource"));
        Assert.DoesNotContain(removed, ManifestValue(second, "Ankus.NativeSource"));
        Assert.DoesNotContain("\"value", InstallationBody(second));
        Assert.Contains("\"other_btree_ops\"", InstallationBody(second));
    }

    /// <summary>
    /// Catalog changes regenerate one root's SQL without disturbing another root or native calling contracts.
    /// </summary>
    /// <param name="schema">Whether to change the explicit schema instead of the scalar catalog name.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void DerivedSqlEmissionTracksIndependentCatalogIdentity(bool schema)
    {
        string source = MappedOperatorSource();
        string other = OtherDerivedSource(source).Replace("\"key\"", "\"other\"", StringComparison.Ordinal);
        CSharpCompilation initial = ModuleCompilation(source).AddSyntaxTrees(CSharpSyntaxTree.ParseText(other,
            path: "Other.cs", cancellationToken: context.CancellationToken));
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        string changed = source.Replace(schema ? "Schema=\"mapped\"" : "\"key\"", schema ? "Schema=\"changed\"" : "\"changed\"", StringComparison.Ordinal);
        driver = RunModule(driver, initial.ReplaceSyntaxTree(initial.SyntaxTrees.First(), CSharpSyntaxTree.ParseText(changed,
            path: "Module.cs", cancellationToken: context.CancellationToken)), out Compilation second);

        AssertDerivedSqlReason(driver, "global::Value", IncrementalStepRunReason.Modified);
        AssertDerivedSqlReason(driver, "global::Other", IncrementalStepRunReason.Cached);
        AssertDerivedHelperReasons(driver, "global::Other", IncrementalStepRunReason.Cached);
        Assert.AreEqual(ManifestValue(first, "Ankus.NativeSource"), ManifestValue(second, "Ankus.NativeSource"));
        Assert.Contains("CREATE FUNCTION \"mapped\".\"key_cmp\"(\"mapped\".\"key\",\"mapped\".\"key\") RETURNS integer", InstallationBody(first));
        string current = InstallationBody(second);
        Assert.DoesNotContain("\"mapped\".\"key_cmp\"", current);
        Assert.Contains(schema
            ? "CREATE FUNCTION \"changed\".\"key_cmp\"(\"changed\".\"key\",\"changed\".\"key\") RETURNS integer"
            : "CREATE FUNCTION \"mapped\".\"changed_cmp\"(\"mapped\".\"changed\",\"mapped\".\"changed\") RETURNS integer", current);
        Assert.Contains("CREATE FUNCTION \"mapped\".\"other_cmp\"", current);
        string symbol = Assert.ContainsSingle(DatumAnalysis(driver).Derived.Where(static model => model.Managed == "global::Value")).Symbol;
        Assert.AreEqual(-989, RunDerivedHash(second, symbol));
    }

    /// <summary>
    /// Changing the selected reader updates lazy registration while retaining SQL and the registry-based native boundary.
    /// </summary>
    [TestMethod]
    public void DerivedSqlEmissionRemainsCachedAcrossConverterIdentity()
    {
        string source = MappedOperatorSource();
        CSharpCompilation initial = ModuleCompilation(source);
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        driver = RunModule(driver, initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(
            source.Replace("Converter", "ChangedConverter", StringComparison.Ordinal), path: "Module.cs", cancellationToken: context.CancellationToken)),
            out Compilation second);

        AssertDerivedSqlReason(driver, "global::Value", IncrementalStepRunReason.Cached);
        AssertDerivedHelperReasons(driver, "global::Value", IncrementalStepRunReason.Cached);
        Assert.AreEqual(InstallationBody(first), InstallationBody(second));
        Assert.AreEqual(ManifestValue(first, "Ankus.NativeSource"), ManifestValue(second, "Ankus.NativeSource"));
        Assert.Contains("typeof(global::ChangedConverter)", DatumMappingManaged(second));
        Assert.DoesNotContain("typeof(global::Converter)", DatumMappingManaged(second));
        Assert.AreEqual(-989, RunDerivedHash(second, Assert.ContainsSingle(DatumAnalysis(driver).Derived).Symbol));
    }

    /// <summary>
    /// Current dependencies reorder a derived family after cache hits without altering its boundary or catalog grammar.
    /// </summary>
    [TestMethod]
    public void DerivedSqlEmissionComposesCurrentDependencies()
    {
        const string Prefix = "[assembly: Ankus.PgSql(\"z_seed\", \"SELECT 'seed';\", Before=new[]{\"ordering\"}, Relocatable=true)]";
        string source = Prefix + CustomOperatorValueSource.Replace("[Ankus.PgOrdering]", "[Ankus.PgOrdering(Id=\"ordering\")]", StringComparison.Ordinal);
        CSharpCompilation initial = ModuleCompilation(source);
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        driver = RunModule(driver, initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(
            source.Replace("Before=new[]{\"ordering\"}", "Requires=new[]{\"ordering\"}", StringComparison.Ordinal),
            path: "Module.cs", cancellationToken: context.CancellationToken)), out Compilation second);

        AssertDerivedSqlReason(driver, "global::Value", IncrementalStepRunReason.Cached);
        AssertDerivedHelperReasons(driver, "global::Value", IncrementalStepRunReason.Cached);
        string previous = InstallationBody(first);
        string current = InstallationBody(second);
        Assert.Contains("SELECT 'seed';", previous);
        Assert.Contains("SELECT 'seed';", current);
        Assert.Contains("CREATE OPERATOR FAMILY \"value_btree_ops\"", previous);
        Assert.Contains("CREATE OPERATOR FAMILY \"value_btree_ops\"", current);
        Assert.IsLessThan(previous.IndexOf("CREATE OPERATOR FAMILY \"value_btree_ops\"", StringComparison.Ordinal), previous.IndexOf("SELECT 'seed';", StringComparison.Ordinal));
        Assert.IsLessThan(current.IndexOf("SELECT 'seed';", StringComparison.Ordinal), current.IndexOf("CREATE OPERATOR FAMILY \"value_btree_ops\"", StringComparison.Ordinal));
        Assert.AreEqual(ManifestValue(first, "Ankus.NativeSource"), ManifestValue(second, "Ankus.NativeSource"));
    }

    /// <summary>
    /// Reads the actual production SQL renderer's decision for one exact root.
    /// </summary>
    /// <param name="driver">The tracked production driver.</param>
    /// <param name="managed">The globally qualified managed root.</param>
    /// <param name="reason">The required rendering cache decision.</param>
    private static void AssertDerivedSqlReason(GeneratorDriver driver, string managed, IncrementalStepRunReason reason)
    {
        (object _, IncrementalStepRunReason actual) = Assert.ContainsSingle(Assert.ContainsSingle(driver.GetRunResult().Results)
            .TrackedSteps["DerivedSqlEmission"].SelectMany(static step => step.Outputs).Where(output =>
                output.Reason != IncrementalStepRunReason.Removed && output.Value is DatumPipeline.SqlOutput sql && sql.Managed == managed));
        Assert.AreEqual(reason, actual);
    }

    /// <summary>
    /// Requires the actual production rendering reason for every expected operation on one selected root.
    /// </summary>
    /// <param name="driver">The tracked production driver.</param>
    /// <param name="managed">The exact managed root spelling.</param>
    /// <param name="reason">The expected cache decision.</param>
    private static void AssertDerivedHelperReasons(GeneratorDriver driver, string managed, IncrementalStepRunReason reason)
    {
        IEnumerable<(object Value, IncrementalStepRunReason Reason)> outputs = Assert.ContainsSingle(driver.GetRunResult().Results)
            .TrackedSteps["DerivedHelperEmission"].SelectMany(static step => step.Outputs).Where(output =>
                output.Reason != IncrementalStepRunReason.Removed && output.Value is DatumPipeline.HelperOutput helper && helper.Slot.Managed == managed);
        Assert.HasCount(8, outputs);
        foreach ((object _, IncrementalStepRunReason actual) in outputs)
        {
            Assert.AreEqual(reason, actual);
        }
    }

    /// <summary>
    /// Reads all current generated helper boundaries for one exact managed root.
    /// </summary>
    /// <param name="driver">The production driver.</param>
    /// <param name="managed">The selected globally qualified type.</param>
    /// <returns>The original per-role boundary inventory.</returns>
    private static DatumPipeline.HelperOutput[] DatumHelpers(GeneratorDriver driver, string managed)
        => [.. Assert.ContainsSingle(driver.GetRunResult().Results).TrackedSteps["DerivedHelperEmission"].SelectMany(static step => step.Outputs)
            .Where(static output => output.Reason != IncrementalStepRunReason.Removed).Select(static output => output.Value)
            .OfType<DatumPipeline.HelperOutput>().Where(value => value.Slot.Managed == managed)];

    /// <summary>
    /// Renames only the two fixture type identifiers while preserving numeric member names and literal contents.
    /// </summary>
    /// <param name="source">One of the fixed valid derived-operator fixture declarations.</param>
    /// <returns>The independent second root's source.</returns>
    private string OtherDerivedSource(string source)
    {
        SyntaxNode root = CSharpSyntaxTree.ParseText(source, cancellationToken: context.CancellationToken).GetRoot(context.CancellationToken);
        return root.ReplaceTokens(root.DescendantTokens().Where(static token => token.IsKind(SyntaxKind.IdentifierToken) &&
            token.ValueText is "Value" or "Converter"), static (token, _) => SyntaxFactory.Identifier(token.LeadingTrivia,
                token.ValueText == "Value" ? "Other" : "OtherConverter", token.TrailingTrivia)).ToFullString();
    }

    /// <summary>
    /// Executes a direct generated helper from the actual tracked compilation without entering PostgreSQL.
    /// </summary>
    /// <param name="compilation">The current generated compilation.</param>
    /// <param name="symbol">The selected root's original native identity.</param>
    /// <returns>The current implementation's exact hash value.</returns>
    private int RunDerivedHash(Compilation compilation, string symbol)
    {
        using var bytes = new MemoryStream();
        Microsoft.CodeAnalysis.Emit.EmitResult emitted = compilation.Emit(bytes, cancellationToken: context.CancellationToken);
        Assert.IsTrue(emitted.Success, string.Join("\n", emitted.Diagnostics));
        bytes.Position = 0;
        var load = new AssemblyLoadContext("DerivedCacheProbe", isCollectible: true);
        try
        {
            Assembly assembly = load.LoadFromStream(bytes);
            Type? type = assembly.GetType("Value");
            Assert.IsNotNull(type);
            Type? dispatchers = assembly.GetType("Ankus.Generated.ExtensionDispatchers");
            Assert.IsNotNull(dispatchers);
            MethodInfo? method = dispatchers.GetMethod("ankus_operator_" + symbol + "_hash", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(method);
            object? value = Activator.CreateInstance(type, [3, 11]);
            Assert.IsNotNull(value);
            return Assert.IsInstanceOfType<int>(method.Invoke(null, [value]));
        }
        finally
        {
            load.Unload();
        }
    }
}
