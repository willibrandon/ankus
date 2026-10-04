using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Ankus.Generators.Tests;

/// <summary>
/// Verifies semantic discovery and source attribution preserve independently cached extension contracts.
/// </summary>
public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Every configuration kind resolves aliases to the same generated property and native registration contract.
    /// </summary>
    /// <param name="kind">The runtime setting transport.</param>
    /// <param name="attribute">The supported configuration attribute suffix.</param>
    [TestMethod]
    [DataRow("boolean", "Bool")]
    [DataRow("integer", "Int")]
    [DataRow("real", "Real")]
    [DataRow("string", "String")]
    [DataRow("enum", "Enum")]
    public void ConfigurationAttributeAliasesUseSemanticIndex(string kind, string attribute)
    {
        string source = GucCacheSource(kind);
        CSharpCompilation initial = ModuleCompilation(source);
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        string propertySource = Assert.ContainsSingle(driver.GetRunResult().Results).GeneratedSources.Single(static generated =>
            generated.HintName == "GucProperties.g.cs").SourceText.ToString();
        string changed = "using Configuration = Ankus.PgGuc" + attribute + "Attribute;\n"
            + source.Replace("Ankus.PgGuc" + attribute + "(", "Configuration(", StringComparison.Ordinal);
        driver = RunModule(driver, initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(
            changed, path: "Module.cs", cancellationToken: context.CancellationToken)), out Compilation second);

        AssertGucCompilation(second, []);
        Assert.AreEqual(propertySource,
            Assert.ContainsSingle(driver.GetRunResult().Results).GeneratedSources.Single(static generated =>
                generated.HintName == "GucProperties.g.cs").SourceText.ToString());
        Assert.AreEqual(IncrementalStepRunReason.Cached, TrackedGucEmission(driver, "demo.value").Reason);
        Assert.AreEqual(ManifestValue(first, "Ankus.NativeSource"), ManifestValue(second, "Ankus.NativeSource"));
    }

    /// <summary>
    /// Aliased type capabilities retain the same compiled operators and installed SQL bodies without duplicate roots.
    /// </summary>
    /// <param name="attribute">The aliased value-operator capability.</param>
    [TestMethod]
    [DataRow("PgEquality")]
    [DataRow("PgOrdering")]
    [DataRow("PgHashing")]
    public void DerivedAttributeAliasesUseSemanticIndex(string attribute)
    {
        CSharpCompilation initial = ModuleCompilation(CustomOperatorValueSource);
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        string changed = "using Capability = Ankus." + attribute + "Attribute;\n"
            + CustomOperatorValueSource.Replace("[Ankus." + attribute + "]", "[Capability]", StringComparison.Ordinal);
        driver = RunModule(driver, initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(
            changed, path: "Module.cs", cancellationToken: context.CancellationToken)), out Compilation second);

        Assert.AreEqual(InstallationBody(first), InstallationBody(second));
        Assert.AreEqual(ManifestValue(first, "Ankus.NativeSource"), ManifestValue(second, "Ankus.NativeSource"));
        Assert.AreEqual(ManifestValue(first, "Ankus.Exports"), ManifestValue(second, "Ankus.Exports"));
        Assert.AreEqual(IncrementalStepRunReason.Cached, ModuleStep(driver, "ExtensionNativeEmission"));
        Assert.AreEqual(IncrementalStepRunReason.Cached, ModuleStep(driver, "ExtensionExportEmission"));
    }

    /// <summary>
    /// Physical line changes update SQL comments without recomposing managed declarations or native contracts.
    /// </summary>
    /// <param name="leading">Whether lines move above the first declaration instead of inside its implementation.</param>
    /// <param name="mapped">Whether a source mapping precedes both declarations.</param>
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void SourceAttributionEditsDoNotRecomposeExtension(bool leading, bool mapped)
    {
        string source = (mapped ? "#line 91 \"Mapped.cs\"\n" : string.Empty)
            + "public static class Functions { [Ankus.PgFunction] public static int First() => 41;\n"
            + "[Ankus.PgFunction] public static int Answer() => First() + 1; }";
        SyntaxTree unrelated = CSharpSyntaxTree.ParseText("internal static class Other { }", path: "Other.cs",
            cancellationToken: context.CancellationToken);
        CSharpCompilation initial = ModuleCompilation(source).AddSyntaxTrees(unrelated);
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        string changed = leading ? "\n\n" + source : source.Replace("=> 41;", "=>\n41;", StringComparison.Ordinal);
        CSharpCompilation edited = initial.ReplaceSyntaxTree(initial.SyntaxTrees.First(), CSharpSyntaxTree.ParseText(changed,
            path: "Module.cs", cancellationToken: context.CancellationToken));
        driver = RunModule(driver, edited, out Compilation second);

        Assert.AreEqual(42, InvokeSqlReferenceAnswer(first));
        Assert.AreEqual(42, InvokeSqlReferenceAnswer(second));
        Assert.AreEqual(IncrementalStepRunReason.Cached, ModuleStep(driver, "ExtensionComposition"));
        Assert.AreEqual(IncrementalStepRunReason.Cached, ModuleStep(driver, "ExtensionSemanticGraph"));
        AssertFinalRenderingCached(driver, manifest: false);
        Assert.AreEqual(ManifestValue(first, "Ankus.NativeSource"), ManifestValue(second, "Ankus.NativeSource"));
        Assert.AreEqual(InstallationBody(first), InstallationBody(second));
        Assert.AreNotEqual(ManifestValue(first, "Ankus.SqlGraph"), ManifestValue(second, "Ankus.SqlGraph"));
        int firstLine = (mapped ? 2 : 1) + (leading ? 2 : 0);
        int secondLine = firstLine + (leading ? 1 : 2);
        Assert.Contains("-- Module.cs:" + firstLine.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n",
            ManifestValue(second, "Ankus.Sql"));
        Assert.Contains("-- Module.cs:" + secondLine.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n",
            ManifestValue(second, "Ankus.Sql"));
        IncrementalGeneratorRunStep retained = Assert.ContainsSingle(Assert.ContainsSingle(driver.GetRunResult().Results)
            .TrackedSteps["ExtensionSourceTree"].Where(step => step.Inputs.Any(input =>
                input.Source.Outputs[input.OutputIndex].Value is SyntaxTree tree && tree == unrelated)));
        Assert.AreEqual(IncrementalStepRunReason.Cached, Assert.ContainsSingle(retained.Outputs).Reason);
    }

    /// <summary>
    /// Dependency capability reads stay cached after consumer body edits for both source and portable references.
    /// </summary>
    /// <param name="portable">Whether the callback dependency is emitted metadata rather than a compilation reference.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void NativeReferenceReadsCacheImplementationEdits(bool portable)
    {
        const string Dependency = "[assembly: System.Reflection.AssemblyMetadata(\"Ankus.NativeCallbacks\", \"1\")] public sealed class Provider;";
        MetadataReference reference = portable ? NativeCompilerReference("CallbackCapability", Dependency)
            : ModuleCompilation(Dependency).WithAssemblyName("CallbackCapability").ToMetadataReference();
        const string Source = "public static class Functions { [Ankus.PgFunction] public static int Answer() => 42; }";
        CSharpCompilation initial = ModuleCompilation(Source).AddReferences(reference);
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        driver = RunModule(driver, initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(
            Source.Replace("=> 42;", "=> 43;", StringComparison.Ordinal), path: "Module.cs",
            cancellationToken: context.CancellationToken)), out Compilation second);

        Assert.IsTrue(NativeCompilerModel(driver).ReferencedCallbacks);
        Assert.AreEqual("1", ManifestValue(first, "Ankus.NativeCallbacks"));
        Assert.AreEqual("1", ManifestValue(second, "Ankus.NativeCallbacks"));
        Assert.AreEqual(42, InvokeSqlReferenceAnswer(first));
        Assert.AreEqual(43, InvokeSqlReferenceAnswer(second));
        foreach (IncrementalGeneratorRunStep step in Assert.ContainsSingle(driver.GetRunResult().Results).TrackedSteps["NativeReferenceAnalysis"])
        {
            Assert.AreEqual(IncrementalStepRunReason.Cached, Assert.ContainsSingle(step.Outputs).Reason);
        }
    }
}
