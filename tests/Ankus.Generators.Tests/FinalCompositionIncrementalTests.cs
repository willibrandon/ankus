using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Ankus.Generators.Tests;

/// <summary>
/// Verifies actual final composition and rendering caches with current compiled consumers and diagnostic trees.
/// </summary>
public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Body edits preserve final source plans and execute the current managed implementation.
    /// </summary>
    /// <param name="longer">Whether the edit changes the method's source span length.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void FinalCompositionCachesBodyEdits(bool longer)
    {
        const string Source = "public static class Functions { [Ankus.PgFunction] public static int Answer() => 42; }";
        CSharpCompilation initial = ModuleCompilation(Source);
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        driver = RunModule(driver, initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(
            Source.Replace("=> 42;", longer ? "=> 43 + 0;" : "=> 43;", StringComparison.Ordinal),
            path: "Module.cs", cancellationToken: context.CancellationToken)), out Compilation second);

        Assert.AreEqual(IncrementalStepRunReason.Cached, ModuleStep(driver, "ExtensionComposition"));
        AssertFinalRenderingCached(driver);
        Assert.AreEqual(ManifestValue(first, "Ankus.NativeSource"), ManifestValue(second, "Ankus.NativeSource"));
        Assert.AreEqual(ManifestValue(first, "Ankus.SqlGraph"), ManifestValue(second, "Ankus.SqlGraph"));
        Assert.AreEqual(42, InvokeSqlReferenceAnswer(first));
        Assert.AreEqual(43, InvokeSqlReferenceAnswer(second));
    }

    /// <summary>
    /// Source movement changes faithful SQL attribution while native and dispatcher renderers remain cached.
    /// </summary>
    /// <param name="mapped">Whether the moved source includes a mapped file and line directive.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void FinalCompositionMovesSqlWithoutNativeRendering(bool mapped)
    {
        const string Source = "public static class Functions { [Ankus.PgFunction] public static int Answer() => 42; }";
        CSharpCompilation initial = ModuleCompilation(Source);
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        SyntaxTree moved = CSharpSyntaxTree.ParseText((mapped ? "#line 76 \"Mapped.cs\"\n" : "\n\n") + Source,
            path: "Moved.cs", cancellationToken: context.CancellationToken);
        driver = RunModule(driver, initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), moved), out Compilation second);

        Assert.AreEqual(IncrementalStepRunReason.Modified, ModuleStep(driver, "ExtensionComposition"));
        AssertFinalRenderingCached(driver, manifest: false);
        Assert.AreEqual(IncrementalStepRunReason.Modified, ModuleStep(driver, "ExtensionManifestEmission"));
        Assert.Contains(mapped ? "-- Moved.cs:2\n" : "-- Moved.cs:3\n", ManifestValue(second, "Ankus.Sql"));
        Assert.Contains("-- Module.cs:1\n", ManifestValue(first, "Ankus.Sql"));
        Assert.AreEqual(InstallationBody(first), InstallationBody(second));
        Assert.AreEqual(42, InvokeSqlReferenceAnswer(second));
    }

    /// <summary>
    /// An unrelated source edit does not recompose or render an unchanged extension.
    /// </summary>
    [TestMethod]
    public void FinalCompositionIgnoresUnrelatedSourceEdits()
    {
        CSharpCompilation initial = ModuleCompilation("public static class Functions { [Ankus.PgFunction] public static int Answer() => 42; }")
            .AddSyntaxTrees(CSharpSyntaxTree.ParseText("internal static class Other { internal const int Value = 1; }", path: "Other.cs", cancellationToken: context.CancellationToken));
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        SyntaxTree other = initial.SyntaxTrees.Last();
        driver = RunModule(driver, initial.ReplaceSyntaxTree(other, CSharpSyntaxTree.ParseText(
            "internal static class Other { internal const int Value = 123456; }", path: "Other.cs", cancellationToken: context.CancellationToken)), out Compilation second);

        Assert.AreEqual(IncrementalStepRunReason.Unchanged, ModuleStep(driver, "ExtensionSourceMap"));
        Assert.AreEqual(IncrementalStepRunReason.Cached, ModuleStep(driver, "ExtensionComposition"));
        AssertFinalRenderingCached(driver);
        Assert.AreEqual(ManifestValue(first, "Ankus.SqlGraph"), ManifestValue(second, "Ankus.SqlGraph"));
        Assert.AreEqual(42, InvokeSqlReferenceAnswer(second));
    }

    /// <summary>
    /// SQL execution policy changes update installation metadata without joining unchanged native source fragments.
    /// </summary>
    [TestMethod]
    public void FinalCompositionSqlPolicyDoesNotRerenderNative()
    {
        const string Source = "public static class Functions { [Ankus.PgFunction(Volatility = Ankus.PgVolatility.Volatile)] public static int Answer() => 42; }";
        CSharpCompilation initial = ModuleCompilation(Source);
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        driver = RunModule(driver, initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(
            Source.Replace("PgVolatility.Volatile", "PgVolatility.Immutable", StringComparison.Ordinal), path: "Module.cs", cancellationToken: context.CancellationToken)), out Compilation second);

        Assert.AreEqual(IncrementalStepRunReason.Modified, ModuleStep(driver, "ExtensionComposition"));
        AssertFinalRenderingCached(driver, manifest: false);
        Assert.Contains(" VOLATILE", ManifestValue(first, "Ankus.Sql"));
        Assert.Contains(" IMMUTABLE", ManifestValue(second, "Ankus.Sql"));
        Assert.AreNotEqual(ManifestValue(first, "Ankus.SqlGraph"), ManifestValue(second, "Ankus.SqlGraph"));
        Assert.AreEqual(42, InvokeSqlReferenceAnswer(second));
    }

    /// <summary>
    /// Measured native ABI changes leave SQL provenance, installation and encoded graph rendering cached.
    /// </summary>
    /// <param name="layouts">Whether the changed ABI constant describes node layouts instead of binding identity.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void FinalCompositionNativeAbiDoesNotRerenderSql(bool layouts)
    {
        string source = NativeCompilationSource();
        CSharpCompilation initial = ModuleCompilation(source);
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        string changed = layouts ? source.Replace("7:8:4", "7:12:4", StringComparison.Ordinal) :
            source.Replace("0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF", new string('F', 64), StringComparison.Ordinal);
        driver = RunModule(driver, initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(
            changed, path: "Module.cs", cancellationToken: context.CancellationToken)), out Compilation second);

        Assert.AreEqual(IncrementalStepRunReason.Modified, ModuleStep(driver, "ExtensionNativeEmission"));
        Assert.AreEqual(IncrementalStepRunReason.Cached, ModuleStep(driver, "ExtensionArtifactEmission"));
        Assert.AreEqual(IncrementalStepRunReason.Cached, ModuleStep(driver, "ExtensionExportEmission"));
        Assert.AreEqual(IncrementalStepRunReason.Cached, ModuleStep(driver, "ExtensionSqlComponentEmission"));
        Assert.AreEqual(IncrementalStepRunReason.Cached, ModuleStep(driver, "ExtensionInstallationEmission"));
        Assert.AreEqual(IncrementalStepRunReason.Cached, ModuleStep(driver, "ExtensionGraphEmission"));
        Assert.AreEqual(ManifestValue(first, "Ankus.Sql"), ManifestValue(second, "Ankus.Sql"));
        Assert.AreEqual(ManifestValue(first, "Ankus.SqlGraph"), ManifestValue(second, "Ankus.SqlGraph"));
        Assert.AreNotEqual(ManifestValue(first, "Ankus.NativeSource"), ManifestValue(second, "Ankus.NativeSource"));
        Assert.AreEqual(42, InvokeSqlReferenceAnswer(second));
    }

    /// <summary>
    /// Cached diagnostics attach to current trees and disappear after repair.
    /// </summary>
    [TestMethod]
    public void FinalCompositionDiagnosticsBindCurrentTreesAndRecover()
    {
        const string Source = "#line 91 \"Mapped.cs\"\npublic static class Functions { [Ankus.PgFunction] public static Missing Answer() => default!; }";
        CSharpCompilation initial = ModuleCompilation(Source);
        GeneratorDriver driver = ModuleDriver().RunGeneratorsAndUpdateCompilation(initial, out _, out ImmutableArray<Diagnostic> first, context.CancellationToken);
        Diagnostic previous = Assert.ContainsSingle(first);
        Assert.AreEqual("ANKUS001", previous.Id);
        SyntaxTree edited = CSharpSyntaxTree.ParseText(Source + "\n// independent edit", path: "Module.cs", cancellationToken: context.CancellationToken);
        driver = driver.RunGeneratorsAndUpdateCompilation(initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), edited), out _, out ImmutableArray<Diagnostic> errors, context.CancellationToken);
        Diagnostic current = Assert.ContainsSingle(errors);

        Assert.AreEqual(IncrementalStepRunReason.Cached, ModuleStep(driver, "ExtensionComposition"));
        Assert.AreEqual(IncrementalStepRunReason.Cached, ModuleStep(driver, "ExtensionProblems"));
        Assert.AreEqual(previous.Id, current.Id);
        Assert.AreEqual(previous.GetMessage(CultureInfo.InvariantCulture), current.GetMessage(CultureInfo.InvariantCulture));
        Assert.AreSame(edited, current.Location.SourceTree);
        Assert.AreEqual("Mapped.cs", current.Location.GetMappedLineSpan().Path);
        Assert.AreEqual(90, current.Location.GetMappedLineSpan().StartLinePosition.Line);

        driver = RunModule(driver, initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(
            "public static class Functions { [Ankus.PgFunction] public static int Answer() => 42; }", path: "Module.cs", cancellationToken: context.CancellationToken)), out Compilation repaired);
        Assert.AreEqual(IncrementalStepRunReason.Modified, ModuleStep(driver, "ExtensionComposition"));
        Assert.AreEqual(42, InvokeSqlReferenceAnswer(repaired));
    }

    /// <summary>
    /// Removing the final declaration removes previously cached sources and metadata.
    /// </summary>
    [TestMethod]
    public void FinalCompositionRemovalDropsCachedArtifacts()
    {
        CSharpCompilation initial = ModuleCompilation("public static class Functions { [Ankus.PgFunction] public static int Answer() => 42; }");
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        Assert.IsNotEmpty(ManifestValue(first, "Ankus.NativeSource"));
        driver = RunModule(driver, initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(
            "public static class Functions { public static int Answer() => 43; }", path: "Module.cs", cancellationToken: context.CancellationToken)), out Compilation second);

        Assert.IsEmpty(Assert.ContainsSingle(driver.GetRunResult().Results).GeneratedSources);
        Assert.IsEmpty(second.Assembly.GetAttributes().Where(static attribute => attribute.AttributeClass?.ToDisplayString() == "System.Reflection.AssemblyMetadataAttribute"));
        Assert.AreEqual(43, InvokeSqlReferenceAnswer(second));
    }

    /// <summary>
    /// Duplicate physical paths retain distinct tree ordinals and mapped attribution without retaining compiler trees.
    /// </summary>
    [TestMethod]
    public void FinalCompositionSourceMapPreservesDuplicatePaths()
    {
        const string Source = "#line 76 \"Mapped.cs\"\npublic static class Functions { public static int Answer() => 42; }";
        CSharpCompilation compilation = ModuleCompilation(Source).AddSyntaxTrees(CSharpSyntaxTree.ParseText(Source,
            path: "Module.cs", cancellationToken: context.CancellationToken));
        var span = new TextSpan(Source.IndexOf("Answer", StringComparison.Ordinal), "Answer".Length);
        var first = new GeneratorLocation(0, span);
        var second = new GeneratorLocation(1, span);
        GeneratorSourceMap map = GeneratorSourceMap.Create([second, first, null, first], compilation, context.CancellationToken);
        Assert.AreSequenceEqual([first, second], map.Entries.Select(static entry => entry.Coordinates));
        Assert.AreEqual("Module.cs", map.Entries[0].Physical.Path);
        Assert.AreEqual(1, map.Entries[0].Physical.StartLinePosition.Line);
        Assert.AreEqual("Mapped.cs", map.Entries[0].Mapped.Path);
        Assert.AreEqual(75, map.Entries[0].Mapped.StartLinePosition.Line);

        var resolver = new GeneratorSourceResolver(map);
        Location firstLocation = resolver.Resolve(first);
        Location secondLocation = resolver.Resolve(second);
        Assert.IsNull(firstLocation.SourceTree);
        Assert.IsNull(secondLocation.SourceTree);
        Assert.AreEqual(first, resolver.Coordinates(firstLocation));
        Assert.AreEqual(second, resolver.Coordinates(secondLocation));
        Assert.AreSame(firstLocation, resolver.Resolve(first));
        Assert.IsNull(resolver.Coordinates(Location.None));
        Assert.IsNull(resolver.Coordinates(null));
    }

    /// <summary>
    /// Detached plans freeze their source order and normalize all supported newline spellings at rendering.
    /// </summary>
    [TestMethod]
    public void FinalCompositionSourcePlanPreservesFrozenOrderAndNewlines()
    {
        var source = new GeneratorSourceBuilder("a\r\nb\r");
        source.Append(null).Append(string.Empty).AppendLine("c");
        GeneratorSourcePlan first = source.Freeze();
        source.Append("d");
        Assert.AreEqual("a\nb\nc\n", first.Render());
        Assert.AreEqual("a\nb\nc\nd", source.Freeze().Render());
        Assert.AreEqual(string.Empty, new GeneratorSourceBuilder().Freeze().Render());
    }

    /// <summary>
    /// Requires actual cache decisions at final render stages rather than equal final source text.
    /// </summary>
    /// <param name="driver">The current production generator run.</param>
    /// <param name="manifest">Whether installation metadata must also remain cached.</param>
    private static void AssertFinalRenderingCached(GeneratorDriver driver, bool manifest = true)
    {
        foreach (string stage in new[] { "ExtensionNativeEmission", "ExtensionExportEmission", "ExtensionArtifactEmission" })
        {
            Assert.AreEqual(IncrementalStepRunReason.Cached, ModuleStep(driver, stage), stage);
        }

        if (manifest)
        {
            Assert.AreEqual(IncrementalStepRunReason.Cached, ModuleStep(driver, "ExtensionManifestEmission"));
            Assert.AreEqual(IncrementalStepRunReason.Cached, ModuleStep(driver, "ExtensionSqlComponentEmission"));
            Assert.AreEqual(IncrementalStepRunReason.Cached, ModuleStep(driver, "ExtensionInstallationEmission"));
            Assert.AreEqual(IncrementalStepRunReason.Cached, ModuleStep(driver, "ExtensionGraphEmission"));
        }
    }
}
