using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Ankus.Generators.Tests;

public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Literal edits to either authored field invalidate the identity and preserve their exact replacement bytes.
    /// </summary>
    /// <param name="property">The authored identity property.</param>
    /// <param name="field">The corresponding native metadata field.</param>
    [TestMethod]
    [DataRow("Name", "name")]
    [DataRow("Version", "version")]
    public void ModuleEmissionTracksAuthoredLiteralChanges(string property, string field)
    {
        string source = "[assembly: Ankus.PgModule(" + property + " = \"A\")]";
        CSharpCompilation initial = ModuleCompilation(source);
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        CSharpCompilation edited = initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(),
            CSharpSyntaxTree.ParseText(source.Replace("\"A\"", "\"B\"", StringComparison.Ordinal), path: "Module.cs", cancellationToken: context.CancellationToken));
        driver = RunModule(driver, edited, out Compilation second);

        Assert.AreEqual(IncrementalStepRunReason.Modified, ModuleStep(driver, "ModuleMagic"));
        Assert.Contains("." + field + " = \"\\x41\"", ModuleMagic(first));
        Assert.Contains("." + field + " = \"\\x42\"", ModuleMagic(second));
    }

    /// <summary>
    /// Source movement changes diagnostic coordinates without regenerating unchanged valid metadata.
    /// </summary>
    [TestMethod]
    public void ModuleEmissionRemainsCachedWhenAttributeMoves()
    {
        const string Source = "[assembly: Ankus.PgModule(Name = \"A\", Version = \"1\")]";
        CSharpCompilation initial = ModuleCompilation(Source);
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        CSharpCompilation edited = initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(),
            CSharpSyntaxTree.ParseText("\n\n" + Source, path: "Module.cs", cancellationToken: context.CancellationToken));
        driver = RunModule(driver, edited, out Compilation second);

        Assert.AreEqual(IncrementalStepRunReason.Modified, ModuleStep(driver, "ModuleInput"));
        Assert.AreEqual(IncrementalStepRunReason.Unchanged, ModuleStep(driver, "ModuleAnalysis"));
        Assert.AreEqual(IncrementalStepRunReason.Cached, ModuleStep(driver, "ModuleMagic"));
        Assert.AreEqual(ModuleMagic(first), ModuleMagic(second));
    }

    /// <summary>
    /// Cached diagnostics use the current tree even when an edit leaves their source coordinates unchanged.
    /// </summary>
    [TestMethod]
    public void ModuleDiagnosticsReattachCachedCoordinatesToCurrentTree()
    {
        const string Source = "[assembly: Ankus.PgModule(Name = \"a\\0b\")]";
        CSharpCompilation initial = ModuleCompilation(Source);
        GeneratorDriver driver = ModuleDriver().RunGeneratorsAndUpdateCompilation(initial, out _,
            out ImmutableArray<Diagnostic> firstErrors, context.CancellationToken);
        Assert.AreEqual("ANKUS025", Assert.ContainsSingle(firstErrors).Id);
        SyntaxTree current = CSharpSyntaxTree.ParseText(Source + "\n// unrelated edit", path: "Module.cs", cancellationToken: context.CancellationToken);
        CSharpCompilation edited = initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), current);
        driver = driver.RunGeneratorsAndUpdateCompilation(edited, out _, out ImmutableArray<Diagnostic> errors, context.CancellationToken);
        Diagnostic error = Assert.ContainsSingle(errors);

        Assert.AreEqual("ANKUS025", error.Id);
        Assert.AreSame(current, error.Location.SourceTree);
        Assert.AreEqual(Assert.ContainsSingle(firstErrors).Location.SourceSpan, error.Location.SourceSpan);
        Assert.AreEqual(IncrementalStepRunReason.Cached, ModuleStep(driver, "ModuleAnalysis"));
        Assert.AreEqual(IncrementalStepRunReason.Cached, ModuleStep(driver, "ModuleMagic"));
    }

    /// <summary>
    /// Body changes and unrelated declarations reuse the module analysis and native emission.
    /// </summary>
    /// <param name="replacement">The edited source body.</param>
    [TestMethod]
    [DataRow("public static class Functions { [Ankus.PgFunction] public static int Answer() => 43; }")]
    [DataRow("public static class Other { public static string Name => \"unrelated\"; }")]
    public void ModuleEmissionRemainsCachedAcrossUnrelatedEdits(string replacement)
    {
        const string Attribute = "[assembly: Ankus.PgModule(Name = \"A\", Version = \"1\")]\n";
        CSharpCompilation initial = ModuleCompilation(Attribute +
            "public static class Functions { [Ankus.PgFunction] public static int Answer() => 42; }");
        GeneratorDriver driver = ModuleDriver();
        driver = RunModule(driver, initial, out Compilation first);
        CSharpCompilation edited = initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(),
            CSharpSyntaxTree.ParseText(Attribute + replacement, path: "Module.cs", cancellationToken: context.CancellationToken));
        driver = RunModule(driver, edited, out Compilation second);

        Assert.AreEqual(IncrementalStepRunReason.Unchanged, ModuleStep(driver, "ModuleInput"));
        Assert.AreEqual(IncrementalStepRunReason.Cached, ModuleStep(driver, "ModuleAnalysis"));
        Assert.AreEqual(IncrementalStepRunReason.Cached, ModuleStep(driver, "ModuleMagic"));
        Assert.AreEqual(ModuleMagic(first), ModuleMagic(second));
    }

    /// <summary>
    /// Compiler-resolved attribute constants invalidate native metadata even when its attribute syntax is unchanged.
    /// </summary>
    [TestMethod]
    public void ModuleEmissionTracksReferencedConstantChanges()
    {
        CSharpCompilation initial = ModuleCompilation("[assembly: Ankus.PgModule(Name = Names.Value, Version = \"1\")]")
            .AddSyntaxTrees(CSharpSyntaxTree.ParseText("public static class Names { public const string Value = \"A\"; }", path: "Names.cs", cancellationToken: context.CancellationToken));
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        SyntaxTree constants = initial.SyntaxTrees.Last();
        CSharpCompilation edited = initial.ReplaceSyntaxTree(constants,
            CSharpSyntaxTree.ParseText("public static class Names { public const string Value = \"B\"; }", path: "Names.cs", cancellationToken: context.CancellationToken));
        driver = RunModule(driver, edited, out Compilation second);

        Assert.AreEqual(IncrementalStepRunReason.Modified, ModuleStep(driver, "ModuleMagic"));
        Assert.Contains(".name = \"\\x41\"", ModuleMagic(first));
        Assert.Contains(".name = \"\\x42\"", ModuleMagic(second));
    }

    /// <summary>
    /// Project version changes affect emission only when no authored module version overrides them.
    /// </summary>
    /// <param name="overridden">Whether the authored identity supplies its version.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ModuleEmissionTracksEffectiveProjectVersion(bool overridden)
    {
        CSharpCompilation compilation = ModuleCompilation(overridden
            ? "[assembly: Ankus.PgModule(Name = \"A\", Version = \"x\")]"
            : "[assembly: Ankus.PgModule(Name = \"A\")]");
        GeneratorDriver driver = RunModule(ModuleDriver("1"), compilation, out Compilation first);
        driver = driver.WithUpdatedAnalyzerConfigOptions(new ModuleOptions("2"));
        driver = RunModule(driver, compilation, out Compilation second);

        Assert.AreEqual(overridden ? IncrementalStepRunReason.Cached : IncrementalStepRunReason.Modified,
            ModuleStep(driver, "ModuleMagic"));
        Assert.Contains(".version = \"" + (overridden ? "\\x78" : "\\x31") + "\"", ModuleMagic(first));
        Assert.Contains(".version = \"" + (overridden ? "\\x78" : "\\x32") + "\"", ModuleMagic(second));
    }

    /// <summary>
    /// Assembly identity changes invalidate the defaults while retaining explicit overrides.
    /// </summary>
    /// <param name="overridden">Whether the authored identity overrides the assembly name.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ModuleEmissionTracksEffectiveAssemblyName(bool overridden)
    {
        CSharpCompilation initial = ModuleCompilation(overridden
            ? "[assembly: Ankus.PgModule(Name = \"A\", Version = \"1\")]"
            : "[assembly: Ankus.PgModule(Version = \"1\")]");
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        driver = RunModule(driver, initial.WithAssemblyName("B"), out Compilation second);

        Assert.AreEqual(overridden ? IncrementalStepRunReason.Cached : IncrementalStepRunReason.Modified,
            ModuleStep(driver, "ModuleMagic"));
        Assert.Contains(overridden ? ".name = \"\\x41\"" : ".name = \"\\x42\"", ModuleMagic(second));
        if (overridden)
        {
            Assert.AreEqual(ModuleMagic(first), ModuleMagic(second));
        }
        else
        {
            Assert.AreNotEqual(ModuleMagic(first), ModuleMagic(second));
        }
    }

    /// <summary>
    /// Assembly version is the fallback when the project version and authored override are absent.
    /// </summary>
    [TestMethod]
    public void ModuleEmissionTracksAssemblyVersionFallback()
    {
        const string Source = "[assembly: Ankus.PgModule]\n[assembly: System.Reflection.AssemblyVersion(\"1.0.0.0\")]";
        CSharpCompilation initial = ModuleCompilation(Source);
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        CSharpCompilation edited = initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(),
            CSharpSyntaxTree.ParseText(Source.Replace("1.0.0.0", "2.0.0.0", StringComparison.Ordinal), path: "Module.cs", cancellationToken: context.CancellationToken));
        driver = RunModule(driver, edited, out Compilation second);

        Assert.AreEqual(IncrementalStepRunReason.Modified, ModuleStep(driver, "ModuleMagic"));
        Assert.Contains(".version = \"\\x31\\x2e\\x30\\x2e\\x30\\x2e\\x30\"", ModuleMagic(first));
        Assert.Contains(".version = \"\\x32\\x2e\\x30\\x2e\\x30\\x2e\\x30\"", ModuleMagic(second));
    }

    /// <summary>
    /// Cached errors resolve against the edited tree and retain the compiler's mapped source positions.
    /// </summary>
    [TestMethod]
    public void ModuleDiagnosticsFollowEditsAndRecover()
    {
        const string Invalid = "[assembly: Ankus.PgModule(Name = \"a\\0b\") ]";
        CSharpCompilation initial = ModuleCompilation(Invalid);
        GeneratorDriver driver = ModuleDriver().RunGeneratorsAndUpdateCompilation(initial, out _,
            out ImmutableArray<Diagnostic> firstErrors, context.CancellationToken);
        Assert.AreEqual("ANKUS025", Assert.ContainsSingle(firstErrors).Id);
        SyntaxTree shifted = CSharpSyntaxTree.ParseText("#line 100 \"Mapped.cs\"\n\n" + Invalid, path: "Module.cs", cancellationToken: context.CancellationToken);
        CSharpCompilation edited = initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), shifted);
        driver = driver.RunGeneratorsAndUpdateCompilation(edited, out _, out ImmutableArray<Diagnostic> errors, context.CancellationToken);
        Diagnostic error = Assert.ContainsSingle(errors);

        Assert.AreEqual("ANKUS025", error.Id);
        Assert.AreSame(shifted, error.Location.SourceTree);
        Assert.AreEqual("\"a\\0b\"", shifted.GetText(context.CancellationToken).ToString(error.Location.SourceSpan));
        Assert.AreEqual("Mapped.cs", error.Location.GetMappedLineSpan().Path);
        Assert.AreEqual(100, error.Location.GetMappedLineSpan().StartLinePosition.Line);
        Assert.AreEqual(IncrementalStepRunReason.Cached, ModuleStep(driver, "ModuleMagic"));

        CSharpCompilation repaired = edited.ReplaceSyntaxTree(shifted,
            CSharpSyntaxTree.ParseText("[assembly: Ankus.PgModule(Name = \"A\") ]", path: "Module.cs", cancellationToken: context.CancellationToken));
        driver = RunModule(driver, repaired, out Compilation output);
        Assert.AreEqual(IncrementalStepRunReason.Modified, ModuleStep(driver, "ModuleMagic"));
        Assert.Contains(".name = \"\\x41\"", ModuleMagic(output));
    }

    /// <summary>
    /// Removing the sole extension declaration removes its generated outputs and does not diagnose unrelated assemblies.
    /// </summary>
    [TestMethod]
    public void ModuleRemovalDropsExtensionOutput()
    {
        CSharpCompilation initial = ModuleCompilation("[assembly: Ankus.PgModule]");
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        Assert.Contains("PG_MODULE_MAGIC_EXT", ModuleMagic(first));
        CSharpCompilation edited = initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText("", path: "Module.cs", cancellationToken: context.CancellationToken));
        driver = driver.WithUpdatedAnalyzerConfigOptions(new ModuleOptions("invalid\0version"));
        driver = RunModule(driver, edited, out Compilation second);

        Assert.IsEmpty(Assert.ContainsSingle(driver.GetRunResult().Results).GeneratedSources);
        Assert.DoesNotContain(static attribute => attribute.AttributeClass?.ToDisplayString() == "System.Reflection.AssemblyMetadataAttribute",
            second.Assembly.GetAttributes());
    }

    /// <summary>
    /// Creates a normal extension compilation for reuse across generator-driver edits.
    /// </summary>
    private CSharpCompilation ModuleCompilation(string source)
        => CSharpCompilation.Create("GeneratorTest",
            [CSharpSyntaxTree.ParseText(source, path: "Module.cs", cancellationToken: context.CancellationToken)], s_references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, nullableContextOptions: NullableContextOptions.Enable));

    /// <summary>
    /// Enables Roslyn's actual step tracking on the production generator.
    /// </summary>
    private static CSharpGeneratorDriver ModuleDriver(string? version = null)
        => CSharpGeneratorDriver.Create([new PgFunctionGenerator().AsSourceGenerator()], optionsProvider: new ModuleOptions(version),
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));

    /// <summary>
    /// Runs one valid edit and requires both generator and generated-code compilation success.
    /// </summary>
    private GeneratorDriver RunModule(GeneratorDriver driver, CSharpCompilation compilation, out Compilation output)
    {
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out output, out ImmutableArray<Diagnostic> diagnostics, context.CancellationToken);
        Assert.IsEmpty(diagnostics);
        ImmutableArray<Diagnostic> errors = output.GetDiagnostics(context.CancellationToken);
        Assert.IsEmpty(errors.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), string.Join("\n", errors));
        return driver;
    }

    /// <summary>
    /// Reads the production step's actual cache decision rather than inferring it from equal generated text.
    /// </summary>
    private static IncrementalStepRunReason ModuleStep(GeneratorDriver driver, string name)
        => Assert.ContainsSingle(Assert.ContainsSingle(Assert.ContainsSingle(driver.GetRunResult().Results).TrackedSteps[name]).Outputs).Reason;

    /// <summary>
    /// Isolates the native module declaration from unrelated extension source changes.
    /// </summary>
    private static string ModuleMagic(Compilation compilation)
    {
        string native = ManifestValue(compilation, "Ankus.NativeSource");
        int start = native.IndexOf("#if PG_VERSION_NUM >= 180000\nPG_MODULE_MAGIC_EXT", StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, start);
        int end = native.IndexOf("#endif", start, StringComparison.Ordinal);
        return native[start..end];
    }
}
