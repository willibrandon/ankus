using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Ankus.Generators.Tests;

public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Valid source enum labels survive retargeting without requiring an unavailable portable image.
    /// </summary>
    /// <param name="retarget">Whether an unused reference forces source-symbol retargeting.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void SourceReferencedGucLabelsPreserveExactContractsWhenRetargeted(bool retarget)
    {
        const string Source = """
            public enum Mode : long
            {
                [Ankus.PgGucLabel("é")] First = long.MinValue,
                [Ankus.PgGucLabel("hidden", Hidden = true)] Hidden = long.MaxValue,
                [Ankus.PgGucLabel("alias")] Alias = Hidden,
            }
            """;
        MetadataReference dependency = GucSourceReference(Source, retarget);
        CSharpCompilation input = ModuleCompilation("""
            public static partial class Settings
            {
                [Ankus.PgGucEnum("demo.mode", Mode.Hidden, "Mode")]
                public static partial Mode Value { get; }
            }
            """).AddReferences(dependency);
        INamedTypeSymbol? enumType = input.GetTypeByMetadataName("Mode");
        Assert.IsNotNull(enumType);
        IFieldSymbol first = Assert.ContainsSingle(enumType.GetMembers("First").OfType<IFieldSymbol>());
        AttributeData label = Assert.ContainsSingle(first.GetAttributes());
        Assert.HasCount(1, enumType.DeclaringSyntaxReferences);
        Assert.AreEqual("é", label.ConstructorArguments[0].Value);
        if (retarget)
        {
            Assert.IsNull(label.ApplicationSyntaxReference);
        }

        GeneratorDriver driver = RunModule(ModuleDriver(), input, out Compilation output);
        GucModel model = Assert.ContainsSingle(Assert.ContainsSingle(driver.GetRunResult().Results).TrackedSteps["GucModel"]
            .SelectMany(static step => step.Outputs).Select(static item => item.Value).OfType<GucModel>());

        Assert.AreSequenceEqual([
            new GucModel.Label("First", "é", 0, false),
            new GucModel.Label("Hidden", "hidden", 1, true),
            new GucModel.Label("Alias", "alias", 1, false),
        ], model.Labels);
        Assert.AreEqual(new GucConstant(1), model.Default);
        AssertGucCompilation(output, []);
    }

    /// <summary>
    /// Invalid labels remain errors and navigate to the current property for both kinds of source reference.
    /// </summary>
    /// <param name="retarget">Whether the source assembly's symbols are retargeted.</param>
    /// <param name="members">The exact enum declaration's invalid labels.</param>
    /// <param name="id">The required label diagnostic.</param>
    [TestMethod]
    [DataRow(false, "[Ankus.PgGucLabel(\"bad\\0\")] First", "ANKUS197")]
    [DataRow(true, "[Ankus.PgGucLabel(\"bad\\0\")] First", "ANKUS197")]
    [DataRow(false, "[Ankus.PgGucLabel(null!)] First", "ANKUS197")]
    [DataRow(true, "[Ankus.PgGucLabel(null!)] First", "ANKUS197")]
    [DataRow(false, "First, first", "ANKUS198")]
    [DataRow(true, "First, first", "ANKUS198")]
    public void SourceReferencedGucErrorsIdentifyCurrentProperty(bool retarget, string members, string id)
    {
        MetadataReference dependency = GucSourceReference("public enum Mode { " + members + " }", retarget);
        (Compilation output, ImmutableArray<Diagnostic> errors) = GenerateDatumMappingReference(
            "public static partial class Settings { [Ankus.PgGucEnum(\"demo.mode\", Mode.First, \"Mode\")] public static partial Mode Value { get; } }",
            [dependency]);

        Diagnostic error = Assert.ContainsSingle(errors);
        Assert.AreSame(output.SyntaxTrees.First(), error.Location.SourceTree);
        AssertDeclarationDiagnosticLocation(error, id, "Mode", "configuration/#declaration-diagnostics");
        Assert.DoesNotContain("ankus_guc_register(&", ManifestValue(output, "Ankus.NativeSource"));
        Assert.IsEmpty(output.SyntaxTrees.Where(static tree => tree.FilePath.EndsWith("GucProperties.g.cs", StringComparison.Ordinal)));
        Assert.IsEmpty(output.GetDiagnostics(context.CancellationToken).Where(static diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error && diagnostic.Id != "CS9248"));
    }

    /// <summary>
    /// Forces genuine Roslyn retargeting by omitting an unused source-project reference from the consumer.
    /// </summary>
    private CompilationReference GucSourceReference(string source, bool retarget)
    {
        CSharpCompilation library = ModuleCompilation(source).WithAssemblyName("SourceGucDependency");
        if (retarget)
        {
            library = library.AddReferences(DatumMappingReference("UnusedGucDependency", "public sealed class Unused { }"));
        }

        Assert.IsEmpty(library.GetDiagnostics(context.CancellationToken).Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        return library.ToMetadataReference();
    }
}
