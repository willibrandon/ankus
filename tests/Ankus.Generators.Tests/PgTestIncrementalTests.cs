using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Ankus.Generators.Tests;

public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Implementation edits and source movement reuse independently rendered discovery and native test contracts.
    /// </summary>
    /// <param name="move">Whether the declaration moves instead of changing its body.</param>
    /// <param name="included">Whether the final extension includes native test exports.</param>
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void PgTestRenderingCachesIndependentEdits(bool move, bool included)
    {
        string source = PgTestCacheSource();
        CSharpCompilation initial = ModuleCompilation(source);
        GeneratorDriver driver = RunModule(PgTestDriver(included), initial, out Compilation first);
        string catalog = PgTestCatalog(driver, "global::Extension.Checks").Emission.Source;
        FunctionEmission boundary = PgTestBoundary(driver, "global::Extension.Checks.@First").Emission;
        CSharpCompilation edited = PgTestEdit(initial, move ? "\n\n" + source :
            source.Replace("=> System.GC.KeepAlive(1);", "=> System.GC.KeepAlive(2);", StringComparison.Ordinal));
        driver = RunModule(driver, edited, out Compilation second);

        Assert.AreEqual(IncrementalStepRunReason.Cached, PgTestCatalog(driver, "global::Extension.Checks").Reason);
        Assert.AreEqual(catalog, PgTestCatalog(driver, "global::Extension.Checks").Emission.Source);
        Assert.AreEqual(IncrementalStepRunReason.Cached, PgTestBoundary(driver, "global::Extension.Checks.@First").Reason);
        Assert.AreEqual(boundary, PgTestBoundary(driver, "global::Extension.Checks.@First").Emission);
        Assert.AreEqual(ManifestValue(first, "Ankus.NativeSource"), ManifestValue(second, "Ankus.NativeSource"));
        Assert.AreEqual(ManifestValue(first, "Ankus.Exports"), ManifestValue(second, "Ankus.Exports"));
        Assert.Contains(catalog.ReplaceLineEndings("\n"), PgTestCatalogSource(driver)!);
        Assert.AreEqual(included, ManifestValue(second, "Ankus.NativeSource").Contains(boundary.NativeName, StringComparison.Ordinal));
    }

    /// <summary>
    /// Report metadata changes update one owner's discovery while both native execution layers remain cached.
    /// </summary>
    /// <param name="before">The initial discovery option.</param>
    /// <param name="after">The changed discovery option.</param>
    /// <param name="expected">The exact new managed metadata value.</param>
    [TestMethod]
    [DataRow("ExpectedError = \"first\"", "ExpectedError = \"é\\\"quoted\\nline\"", "é\"quoted\nline")]
    [DataRow("IgnoreReason = \"later\"", "IgnoreReason = \"原因\"", "原因")]
    public void PgTestRenderingSeparatesDiscoveryPolicies(string before, string after, string expected)
    {
        string source = PgTestCacheSource();
        CSharpCompilation initial = ModuleCompilation(source);
        GeneratorDriver driver = RunModule(PgTestDriver(true), initial, out Compilation first);
        string original = PgTestCatalog(driver, "global::Extension.Checks").Emission.Source;
        string sqlName = PgTestBoundary(driver, "global::Extension.Checks.@First").Emission.NativeName;
        driver = RunModule(driver, PgTestEdit(initial, source.Replace(before, after, StringComparison.Ordinal)), out Compilation second);

        (PgTestPipeline.CatalogEmission emission, IncrementalStepRunReason reason) = PgTestCatalog(driver, "global::Extension.Checks");
        Assert.AreEqual(IncrementalStepRunReason.Modified, reason);
        Assert.AreNotEqual(original, emission.Source);
        Assert.Contains(SymbolDisplay.FormatLiteral(expected, quote: true), emission.Source);
        Assert.AreEqual(IncrementalStepRunReason.Cached, PgTestCatalog(driver, "global::Extension.Other").Reason);
        Assert.AreEqual(IncrementalStepRunReason.Cached, PgTestBoundary(driver, "global::Extension.Checks.@First").Reason);
        Assert.AreEqual(IncrementalStepRunReason.Cached, PgTestSql(driver, sqlName).Reason);
        Assert.AreEqual(ManifestValue(first, "Ankus.NativeSource"), ManifestValue(second, "Ankus.NativeSource"));
        Assert.AreEqual(ManifestValue(first, "Ankus.Sql"), ManifestValue(second, "Ankus.Sql"));
    }

    /// <summary>
    /// A schema dependency changes catalog and SQL identity while leaving the invocation boundary unchanged.
    /// </summary>
    [TestMethod]
    public void PgTestRenderingTracksInheritedSchema()
    {
        string source = PgTestCacheSource();
        CSharpCompilation initial = ModuleCompilation(source);
        GeneratorDriver driver = RunModule(PgTestDriver(true), initial, out _);
        string sqlName = PgTestBoundary(driver, "global::Extension.Checks.@First").Emission.NativeName;
        driver = RunModule(driver, PgTestEdit(initial, source.Replace("Case Schema", "Changed Schema", StringComparison.Ordinal)), out Compilation output);

        Assert.AreEqual(IncrementalStepRunReason.Modified, PgTestCatalog(driver, "global::Extension.Checks").Reason);
        Assert.Contains("\"Changed Schema\"", PgTestCatalogSource(driver)!);
        Assert.AreEqual(IncrementalStepRunReason.Modified, PgTestSql(driver, sqlName).Reason);
        Assert.Contains("\"Changed Schema\".", PgTestSql(driver, sqlName).Emission.Header);
        Assert.AreEqual(IncrementalStepRunReason.Cached, PgTestBoundary(driver, "global::Extension.Checks.@First").Reason);
        Assert.AreEqual(IncrementalStepRunReason.Cached, PgTestCatalog(driver, "global::Extension.Other").Reason);
        Assert.Contains("CREATE FUNCTION \"Changed Schema\".\"ankus_test_", InstallationBody(output));
    }

    /// <summary>
    /// Changing the test's managed identity updates discovery and its assembly-specific native entry together.
    /// </summary>
    /// <param name="before">The initial lexical contract.</param>
    /// <param name="after">The replacement lexical contract.</param>
    /// <param name="owner">The resulting catalog owner.</param>
    /// <param name="target">The resulting managed invocation target.</param>
    [TestMethod]
    [DataRow("void First()", "void Renamed()", "global::Extension.Checks", "global::Extension.Checks.@Renamed")]
    [DataRow("class Checks", "class RenamedChecks", "global::Extension.RenamedChecks", "global::Extension.RenamedChecks.@First")]
    [DataRow("namespace Extension;", "namespace @event;", "global::@event.Checks", "global::@event.Checks.@First")]
    public void PgTestRenderingTracksManagedIdentity(string before, string after, string owner, string target)
    {
        string source = PgTestCacheSource();
        CSharpCompilation initial = ModuleCompilation(source);
        GeneratorDriver driver = RunModule(PgTestDriver(true), initial, out _);
        string oldNative = PgTestBoundary(driver, "global::Extension.Checks.@First").Emission.NativeName;
        driver = RunModule(driver, PgTestEdit(initial, source.Replace(before, after, StringComparison.Ordinal)), out Compilation output);

        (FunctionEmission boundary, IncrementalStepRunReason reason) = PgTestBoundary(driver, target);
        Assert.AreEqual(IncrementalStepRunReason.Modified, reason);
        Assert.AreNotEqual(oldNative, boundary.NativeName);
        Assert.AreEqual(IncrementalStepRunReason.Modified, PgTestCatalog(driver, owner).Reason);
        Assert.Contains(target + "(", boundary.Managed);
        Assert.Contains(boundary.NativeName, ManifestValue(output, "Ankus.Exports"));
        Assert.DoesNotContain(oldNative, ManifestValue(output, "Ankus.Exports"));
        Assert.Contains(PgTestCatalog(driver, owner).Emission.Source.ReplaceLineEndings("\n"), PgTestCatalogSource(driver)!);
    }

    /// <summary>
    /// Injected argument changes affect exact invocation order but never add SQL inputs to backend tests.
    /// </summary>
    /// <param name="parameters">The replacement managed injected arguments.</param>
    /// <param name="arguments">The exact generated invocation arguments.</param>
    [TestMethod]
    [DataRow("Ankus.PgMemoryContext memory", "global::Ankus.PgMemoryContext.Current")]
    [DataRow("Ankus.PgFunctionContext call", "functionContext")]
    [DataRow("Ankus.PgMemoryContext memory, Ankus.PgFunctionContext call", "global::Ankus.PgMemoryContext.Current, functionContext")]
    [DataRow("Ankus.PgFunctionContext call, Ankus.PgMemoryContext memory", "functionContext, global::Ankus.PgMemoryContext.Current")]
    public void PgTestRenderingTracksInjectedArguments(string parameters, string arguments)
    {
        string source = PgTestCacheSource();
        CSharpCompilation initial = ModuleCompilation(source);
        GeneratorDriver driver = RunModule(PgTestDriver(true), initial, out _);
        driver = RunModule(driver, PgTestEdit(initial, source.Replace("void First()", "void First(" + parameters + ")", StringComparison.Ordinal)), out Compilation output);

        (FunctionEmission emission, IncrementalStepRunReason reason) = PgTestBoundary(driver, "global::Extension.Checks.@First");
        Assert.AreEqual(IncrementalStepRunReason.Modified, reason);
        Assert.Contains("global::Extension.Checks.@First(" + arguments + ")", emission.Managed);
        Assert.AreEqual(IncrementalStepRunReason.Cached, PgTestBoundary(driver, "global::Extension.Other.@Independent").Reason);
        FunctionSqlEmission sql = PgTestSql(driver, emission.NativeName).Emission;
        Assert.IsEmpty(sql.Arguments);
        Assert.Contains("RETURNS void", InstallationBody(output));
        Assert.Contains("Extension.Checks.First(", PgTestCatalogSource(driver)!);
    }

    /// <summary>
    /// Build inclusion changes final exports without rebuilding either cached metadata or test boundaries.
    /// </summary>
    [TestMethod]
    public void PgTestRenderingSeparatesNativeInclusion()
    {
        CSharpCompilation compilation = ModuleCompilation(PgTestCacheSource());
        GeneratorDriver driver = RunModule(PgTestDriver(false), compilation, out Compilation excluded);
        string catalog = PgTestCatalogSource(driver)!;
        string nativeName = PgTestBoundary(driver, "global::Extension.Checks.@First").Emission.NativeName;
        driver = RunModule(driver.WithUpdatedAnalyzerConfigOptions(new BackendOptions("true")), compilation, out Compilation included);
        Assert.AreEqual(IncrementalStepRunReason.Cached, PgTestCatalog(driver, "global::Extension.Checks").Reason);
        Assert.AreEqual(IncrementalStepRunReason.Cached, PgTestBoundary(driver, "global::Extension.Checks.@First").Reason);
        Assert.AreEqual(IncrementalStepRunReason.Cached, PgTestSql(driver, nativeName).Reason);
        Assert.AreEqual(catalog, PgTestCatalogSource(driver));
        Assert.DoesNotContain(nativeName, ManifestValue(excluded, "Ankus.Exports"));
        Assert.Contains(nativeName, ManifestValue(included, "Ankus.Exports"));
        driver = RunModule(driver.WithUpdatedAnalyzerConfigOptions(new BackendOptions("false")), compilation, out Compilation excludedAgain);
        Assert.AreEqual(catalog, PgTestCatalogSource(driver));
        Assert.DoesNotContain(nativeName, ManifestValue(excludedAgain, "Ankus.Exports"));
        Assert.AreEqual(IncrementalStepRunReason.Cached, PgTestBoundary(driver, "global::Extension.Checks.@First").Reason);
    }

    /// <summary>
    /// Invalid cached test declarations report on the current tree and recover without stale catalogs or boundaries.
    /// </summary>
    /// <param name="invalid">The invalid replacement test shape.</param>
    [TestMethod]
    [DataRow("public static int First() => 1;")]
    [DataRow("private static void First() { }")]
    [DataRow("public static void First(int value) { }")]
    [DataRow("public static void First(ref Ankus.PgFunctionContext call) { }")]
    public void PgTestDiagnosticsUseCurrentSourceAndRecover(string invalid)
    {
        const string declaration = "public static void First() => System.GC.KeepAlive(1);";
        string valid = PgTestCacheSource();
        string source = valid.Replace(declaration, invalid, StringComparison.Ordinal);
        CSharpCompilation initial = ModuleCompilation(source);
        GeneratorDriver driver = PgTestDriver(true).RunGeneratorsAndUpdateCompilation(initial, out _, out ImmutableArray<Diagnostic> first,
            context.CancellationToken);
        Assert.AreEqual("ANKUS023", Assert.ContainsSingle(first).Id);
        SyntaxTree current = CSharpSyntaxTree.ParseText("\n\n" + source, path: "Current.cs", cancellationToken: context.CancellationToken);
        CSharpCompilation edited = initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), current);
        driver = driver.RunGeneratorsAndUpdateCompilation(edited, out _, out ImmutableArray<Diagnostic> errors, context.CancellationToken);
        Diagnostic error = Assert.ContainsSingle(errors);
        Assert.AreEqual("ANKUS023", error.Id);
        Assert.AreSame(current, error.Location.SourceTree);
        Assert.AreEqual(Assert.ContainsSingle(first).Location.SourceSpan.Start + 2, error.Location.SourceSpan.Start);
        Assert.IsNull(PgTestCatalogSource(driver));
        SyntaxTree sameCoordinates = CSharpSyntaxTree.ParseText("\n\n" + source + "\n// unrelated", path: "Current.cs", cancellationToken: context.CancellationToken);
        edited = edited.ReplaceSyntaxTree(current, sameCoordinates);
        driver = driver.RunGeneratorsAndUpdateCompilation(edited, out _, out ImmutableArray<Diagnostic> cachedErrors, context.CancellationToken);
        Diagnostic cachedError = Assert.ContainsSingle(cachedErrors);
        Assert.AreSame(sameCoordinates, cachedError.Location.SourceTree);
        Assert.AreEqual(error.Location.SourceSpan, cachedError.Location.SourceSpan);
        (object Value, IncrementalStepRunReason Reason) cached = Assert.ContainsSingle(Assert.ContainsSingle(driver.GetRunResult().Results)
            .TrackedSteps["PgTestAnalysis"].SelectMany(static step => step.Outputs).Where(static value =>
                value.Value is PgTestPipeline.Analysis analysis && !analysis.Problems.IsEmpty));
        Assert.AreEqual(IncrementalStepRunReason.Unchanged, cached.Reason);
        Assert.IsNull(Assert.IsInstanceOfType<PgTestPipeline.Analysis>(cached.Value).Model);
        (object Value, IncrementalStepRunReason Reason) cachedModel = Assert.ContainsSingle(Assert.ContainsSingle(driver.GetRunResult().Results)
            .TrackedSteps["PgTestModel"].SelectMany(static step => step.Outputs).Where(static value => value.Value is null));
        Assert.AreEqual(IncrementalStepRunReason.Cached, cachedModel.Reason);
        driver = RunModule(driver, PgTestEdit(edited, valid), out Compilation repaired);
        Assert.Contains("Extension.Checks.First()", PgTestCatalogSource(driver)!);
        Assert.Contains(PgTestBoundary(driver, "global::Extension.Checks.@First").Emission.NativeName, ManifestValue(repaired, "Ankus.Exports"));
    }

    /// <summary>
    /// Removing a case preserves sorted survivors, removes its native entry and keeps other owners cached.
    /// </summary>
    [TestMethod]
    public void PgTestCatalogTracksMembershipAndEmptyInventory()
    {
        string source = PgTestCacheSource();
        CSharpCompilation initial = ModuleCompilation(source);
        GeneratorDriver driver = RunModule(PgTestDriver(true), initial, out _);
        string ordered = PgTestCatalog(driver, "global::Extension.Checks").Emission.Source;
        Assert.IsLessThan(ordered.IndexOf("Extension.Checks.Second()", StringComparison.Ordinal),
            ordered.IndexOf("Extension.Checks.First()", StringComparison.Ordinal));
        string removedNative = PgTestBoundary(driver, "global::Extension.Checks.@First").Emission.NativeName;
        string other = PgTestCatalog(driver, "global::Extension.Other").Emission.Source;
        string replacement = source.Replace("[Ankus.PgTest(ExpectedError = \"first\", IgnoreReason = \"later\")]", "", StringComparison.Ordinal);
        driver = RunModule(driver, PgTestEdit(initial, replacement), out Compilation output);
        string survivor = PgTestCatalog(driver, "global::Extension.Checks").Emission.Source;
        Assert.DoesNotContain("Extension.Checks.First()", survivor);
        Assert.Contains("Extension.Checks.Second()", survivor);
        Assert.DoesNotContain(removedNative, ManifestValue(output, "Ankus.Exports"));
        Assert.AreEqual(other, PgTestCatalog(driver, "global::Extension.Other").Emission.Source);
        Assert.AreEqual(IncrementalStepRunReason.Cached, PgTestCatalog(driver, "global::Extension.Other").Reason);
        driver = RunModule(driver, PgTestEdit(initial, source.Replace("void Second()", "void Third()", StringComparison.Ordinal)), out Compilation added);
        string addedCatalog = PgTestCatalog(driver, "global::Extension.Checks").Emission.Source;
        Assert.Contains("Extension.Checks.First()", addedCatalog);
        Assert.Contains("Extension.Checks.Third()", addedCatalog);
        Assert.DoesNotContain("Extension.Checks.Second()", addedCatalog);
        Assert.Contains(PgTestBoundary(driver, "global::Extension.Checks.@Third").Emission.NativeName, ManifestValue(added, "Ankus.Exports"));
        Assert.AreEqual(other, PgTestCatalog(driver, "global::Extension.Other").Emission.Source);
        driver = RunModule(driver, PgTestEdit(initial, "public static partial class Checks { }"), out Compilation empty);
        Assert.IsNull(PgTestCatalogSource(driver));
        Assert.DoesNotContain(static attribute => attribute.AttributeClass?.ToDisplayString() == "System.Reflection.AssemblyMetadataAttribute",
            empty.Assembly.GetAttributes());
    }

    /// <summary>
    /// A changed assembly name updates the matching SQL catalog entry and native dispatcher identity.
    /// </summary>
    [TestMethod]
    public void PgTestRenderingTracksAssemblyIdentity()
    {
        CSharpCompilation initial = ModuleCompilation(PgTestCacheSource());
        GeneratorDriver driver = RunModule(PgTestDriver(true), initial, out _);
        string oldNative = PgTestBoundary(driver, "global::Extension.Checks.@First").Emission.NativeName;
        driver = RunModule(driver, initial.WithAssemblyName("ChangedAssembly"), out Compilation output);
        FunctionEmission changed = PgTestBoundary(driver, "global::Extension.Checks.@First").Emission;
        Assert.AreEqual(IncrementalStepRunReason.Modified, PgTestBoundary(driver, "global::Extension.Checks.@First").Reason);
        Assert.AreNotEqual(oldNative, changed.NativeName);
        Assert.DoesNotContain(oldNative, ManifestValue(output, "Ankus.Exports"));
        Assert.Contains(changed.NativeName, ManifestValue(output, "Ankus.Exports"));
        PgTestPipeline.Analysis analysis = Assert.ContainsSingle(Assert.ContainsSingle(driver.GetRunResult().Results).TrackedSteps["PgTestAnalysis"]
            .SelectMany(static step => step.Outputs).Select(static value => value.Value).OfType<PgTestPipeline.Analysis>()
            .Where(static value => value.Declaration?.Case.Name == "Extension.Checks.First()"));
        Assert.Contains(analysis.Declaration!.Case.FunctionName, PgTestCatalogSource(driver)!);
        Assert.Contains(analysis.Declaration.Case.FunctionName, changed.NativeName);
    }

    /// <summary>
    /// Catalog-only lexical policy changes preserve native invocation contracts and independent owner output.
    /// </summary>
    /// <param name="before">The initial containing declaration.</param>
    /// <param name="after">The replacement containing declaration.</param>
    /// <param name="expected">The exact new generated partial declaration.</param>
    [TestMethod]
    [DataRow("public static partial class Checks", "internal static partial class Checks", "internal static partial class @Checks")]
    [DataRow("public static partial class Checks", "public partial class Checks", "public partial class @Checks")]
    [DataRow("public static partial class Checks", "public partial record class Checks", "public partial record class @Checks")]
    public void PgTestCatalogTracksLexicalPolicy(string before, string after, string expected)
    {
        string source = PgTestCacheSource();
        CSharpCompilation initial = ModuleCompilation(source);
        GeneratorDriver driver = RunModule(PgTestDriver(true), initial, out _);
        driver = RunModule(driver, PgTestEdit(initial, source.Replace(before, after, StringComparison.Ordinal)), out _);
        Assert.AreEqual(IncrementalStepRunReason.Modified, PgTestCatalog(driver, "global::Extension.Checks").Reason);
        Assert.Contains(expected, PgTestCatalogSource(driver)!);
        Assert.AreEqual(IncrementalStepRunReason.Cached, PgTestBoundary(driver, "global::Extension.Checks.@First").Reason);
        Assert.AreEqual(IncrementalStepRunReason.Cached, PgTestCatalog(driver, "global::Extension.Other").Reason);
    }

    /// <summary>
    /// Semantically resolved metadata constants invalidate discovery without editing the attributed test tree.
    /// </summary>
    [TestMethod]
    public void PgTestCatalogTracksReferencedMetadata()
    {
        string source = PgTestCacheSource().Replace("ExpectedError = \"first\"", "ExpectedError = Reports.Message", StringComparison.Ordinal);
        CSharpCompilation initial = ModuleCompilation(source).AddSyntaxTrees(CSharpSyntaxTree.ParseText(
            "public static class Reports { public const string Message = \"first\"; }", path: "Reports.cs", cancellationToken: context.CancellationToken));
        GeneratorDriver driver = RunModule(PgTestDriver(true), initial, out _);
        CSharpCompilation edited = initial.ReplaceSyntaxTree(initial.SyntaxTrees.Last(), CSharpSyntaxTree.ParseText(
            "public static class Reports { public const string Message = \"changed\"; }", path: "Reports.cs", cancellationToken: context.CancellationToken));
        driver = RunModule(driver, edited, out _);
        Assert.AreEqual(IncrementalStepRunReason.Modified, PgTestCatalog(driver, "global::Extension.Checks").Reason);
        Assert.Contains("\"changed\"", PgTestCatalogSource(driver)!);
        Assert.AreEqual(IncrementalStepRunReason.Cached, PgTestBoundary(driver, "global::Extension.Checks.@First").Reason);
        Assert.AreEqual(IncrementalStepRunReason.Cached, PgTestCatalog(driver, "global::Extension.Other").Reason);
    }

    /// <summary>
    /// Moving cases between partial source trees preserves the owner's deterministic discovery and invocation output.
    /// </summary>
    /// <param name="lineEnding">The original source newline convention, independent of the checkout.</param>
    [TestMethod]
    [DataRow("\n")]
    [DataRow("\r\n")]
    public void PgTestCatalogCachesPartialFileMovement(string lineEnding)
    {
        string source = PgTestCacheSource().ReplaceLineEndings(lineEnding);
        CSharpCompilation initial = ModuleCompilation(source);
        GeneratorDriver driver = RunModule(PgTestDriver(true), initial, out _);
        string catalog = PgTestCatalogSource(driver)!;
        SyntaxTree tree = initial.SyntaxTrees.Single();
        SyntaxNode root = tree.GetRoot(context.CancellationToken);
        MethodDeclarationSyntax method = root.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(static declaration => declaration.Identifier.ValueText == "Second");
        SyntaxNode replacement = root.RemoveNode(method, SyntaxRemoveOptions.KeepExteriorTrivia)!;
        Assert.DoesNotContain("Second", replacement.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Select(static declaration => declaration.Identifier.ValueText));
        CSharpCompilation edited = initial.ReplaceSyntaxTree(tree, tree.WithRootAndOptions(replacement, tree.Options))
            .AddSyntaxTrees(CSharpSyntaxTree.ParseText("""
            namespace Extension;
            public static partial class Checks
            {
                [Ankus.PgTest]
                public static void Second() { }
            }
            """, path: "Moved.cs", cancellationToken: context.CancellationToken));
        driver = RunModule(driver, edited, out Compilation output);
        Assert.AreEqual(catalog, PgTestCatalogSource(driver));
        Assert.AreEqual(IncrementalStepRunReason.Cached, PgTestCatalog(driver, "global::Extension.Checks").Reason);
        Assert.AreEqual(IncrementalStepRunReason.Cached, PgTestCatalog(driver, "global::Extension.Other").Reason);
        Assert.Contains(PgTestBoundary(driver, "global::Extension.Checks.@Second").Emission.NativeName, ManifestValue(output, "Ankus.Exports"));
    }

    /// <summary>
    /// A reserved inherited catalog member invalidates unchanged attributed syntax and disappears after repair.
    /// </summary>
    [TestMethod]
    public void PgTestDiagnosticsTrackInheritedCatalogConflict()
    {
        const string source = """
            public partial class Checks : Base
            {
                [Ankus.PgTest]
                public static void First() { }
            }
            """;
        CSharpCompilation initial = ModuleCompilation(source).AddSyntaxTrees(CSharpSyntaxTree.ParseText(
            "public class Base { }", path: "Base.cs", cancellationToken: context.CancellationToken));
        GeneratorDriver driver = RunModule(PgTestDriver(true), initial, out _);
        string catalog = PgTestCatalogSource(driver)!;
        SyntaxTree inherited = CSharpSyntaxTree.ParseText("public class Base { public static int PostgresTests => 1; }",
            path: "Base.cs", cancellationToken: context.CancellationToken);
        CSharpCompilation invalid = initial.ReplaceSyntaxTree(initial.SyntaxTrees.Last(), inherited);
        driver = driver.RunGeneratorsAndUpdateCompilation(invalid, out _, out ImmutableArray<Diagnostic> diagnostics, context.CancellationToken);
        Diagnostic error = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual("ANKUS023", error.Id);
        Assert.AreSame(initial.SyntaxTrees.First(), error.Location.SourceTree);
        Assert.Contains("PostgresTests is reserved", error.GetMessage(CultureInfo.InvariantCulture));
        Assert.IsNull(PgTestCatalogSource(driver));
        driver = RunModule(driver, initial, out Compilation repaired);
        Assert.AreEqual(catalog, PgTestCatalogSource(driver));
        Assert.Contains(PgTestBoundary(driver, "global::Checks.@First").Emission.NativeName, ManifestValue(repaired, "Ankus.Exports"));
    }

    /// <summary>
    /// Detached test boundaries retain validation of incompatible scalar, column and injected-argument bindings.
    /// </summary>
    /// <param name="method">The invalid attributed test declaration.</param>
    /// <param name="id">The established conversion diagnostic.</param>
    [TestMethod]
    [DataRow("[return: Ankus.PgColumnNames(\"column\")] public static void Invalid() { }", "ANKUS008")]
    [DataRow("[return: Ankus.PgSqlType(\"uuid\")] public static void Invalid() { }", "ANKUS016")]
    [DataRow("public static void Invalid([Ankus.PgCompositeType(\"row\")] Ankus.PgMemoryContext memory) { }", "ANKUS009")]
    public void PgTestRejectsInvalidConversionPolicies(string method, string id)
    {
        (_, ImmutableArray<Diagnostic> diagnostics) = Generate("public static partial class Checks { [Ankus.PgTest] " + method + " }",
            options: new BackendOptions("true"));
        Assert.AreEqual(id, Assert.ContainsSingle(diagnostics).Id);
    }

    /// <summary>
    /// Supplies multiple owners and ordered test cases with independently changeable report metadata.
    /// </summary>
    private static string PgTestCacheSource() => """
        namespace Extension;
        [Ankus.PgSchema("Case Schema")]
        public static partial class Checks
        {
            [Ankus.PgTest]
            public static void Second() { }
            [Ankus.PgTest(ExpectedError = "first", IgnoreReason = "later")]
            public static void First() => System.GC.KeepAlive(1);
        }
        public static partial class Other
        {
            [Ankus.PgTest]
            public static void Independent() { }
        }
        """;

    /// <summary>
    /// Creates a tracked production driver with explicit native test inclusion.
    /// </summary>
    private static GeneratorDriver PgTestDriver(bool included) => ModuleDriver().WithUpdatedAnalyzerConfigOptions(new BackendOptions(included ? "true" : "false"));

    /// <summary>
    /// Replaces the authored tree while preserving the generator driver's previous cache.
    /// </summary>
    private CSharpCompilation PgTestEdit(CSharpCompilation initial, string source) => initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(),
        CSharpSyntaxTree.ParseText(source, path: "Module.cs", cancellationToken: context.CancellationToken));

    /// <summary>
    /// Reads an owner's actual production rendering and cache decision.
    /// </summary>
    private static (PgTestPipeline.CatalogEmission Emission, IncrementalStepRunReason Reason) PgTestCatalog(GeneratorDriver driver, string owner)
    {
        (object Value, IncrementalStepRunReason Reason) result = Assert.ContainsSingle(Assert.ContainsSingle(driver.GetRunResult().Results)
            .TrackedSteps["PgTestCatalogEmission"].SelectMany(static step => step.Outputs)
            .Where(value => value.Reason != IncrementalStepRunReason.Removed && value.Value is PgTestPipeline.CatalogEmission emission && emission.Owner == owner));
        return (Assert.IsInstanceOfType<PgTestPipeline.CatalogEmission>(result.Value), result.Reason);
    }

    /// <summary>
    /// Reads the actual native rendering stage for one managed invocation target.
    /// </summary>
    private static (FunctionEmission Emission, IncrementalStepRunReason Reason) PgTestBoundary(GeneratorDriver driver, string target)
    {
        (object Value, IncrementalStepRunReason Reason) result = Assert.ContainsSingle(Assert.ContainsSingle(driver.GetRunResult().Results)
            .TrackedSteps["PgTestEmission"].SelectMany(static step => step.Outputs)
            .Where(value => value.Reason != IncrementalStepRunReason.Removed && value.Value is FunctionEmission emission &&
                emission.Managed.Contains(target + "(", StringComparison.Ordinal)));
        return (Assert.IsInstanceOfType<FunctionEmission>(result.Value), result.Reason);
    }

    /// <summary>
    /// Reads the independently rendered SQL definition associated with one native test entry.
    /// </summary>
    private static (FunctionSqlEmission Emission, IncrementalStepRunReason Reason) PgTestSql(GeneratorDriver driver, string nativeName)
    {
        (object Value, IncrementalStepRunReason Reason) result = Assert.ContainsSingle(Assert.ContainsSingle(driver.GetRunResult().Results)
            .TrackedSteps["PgTestSqlEmission"].SelectMany(static step => step.Outputs)
            .Where(value => value.Reason != IncrementalStepRunReason.Removed && value.Value is FunctionSqlEmission emission &&
                emission.Tail.Contains("'" + nativeName + "'", StringComparison.Ordinal)));
        return (Assert.IsInstanceOfType<FunctionSqlEmission>(result.Value), result.Reason);
    }

    /// <summary>
    /// Gets the current composed host-discovery source, or null when no valid tests remain.
    /// </summary>
    private static string? PgTestCatalogSource(GeneratorDriver driver)
    {
        string? source = Assert.ContainsSingle(driver.GetRunResult().Results)
            .GeneratedSources.SingleOrDefault(static generated => generated.HintName == "PostgresTests.g.cs").SourceText?.ToString();
        if (source is not null)
        {
            Assert.DoesNotContain("\r", source);
        }

        return source;
    }
}
