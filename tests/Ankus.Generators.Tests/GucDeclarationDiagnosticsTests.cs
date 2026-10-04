using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Ankus.Generators.Tests;

public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Configuration metadata failures identify the value or property type the author must correct.
    /// </summary>
    /// <param name="attribute">The invalid setting declaration.</param>
    /// <param name="type">The managed property type.</param>
    /// <param name="id">The exact diagnostic contract.</param>
    /// <param name="span">The current authored syntax requiring correction.</param>
    [TestMethod]
    [DataRow("Ankus.PgGucBool(\"invalid\", true, \"Value\")", "bool", "ANKUS181", "\"invalid\"")]
    [DataRow("Ankus.PgGucBool(shortDescription: \"Value\", defaultValue: true, name: \"invalid\")", "bool", "ANKUS181", "\"invalid\"")]
    [DataRow("Ankus.PgGucBool(\"demo.value\", true, null!)", "bool", "ANKUS182", "null!")]
    [DataRow("Ankus.PgGucBool(\"demo.value\", true, \"bad\\0\")", "bool", "ANKUS182", "\"bad\\0\"")]
    [DataRow("Ankus.PgGucBool(\"demo.value\", true, \"Value\")", "bool?", "ANKUS183", "bool?")]
    [DataRow("Ankus.PgGucBool(\"demo.value\", true, \"Value\", LongDescription = \"bad\\0\")", "bool", "ANKUS184", "\"bad\\0\"")]
    [DataRow("Ankus.PgGucBool(\"demo.value\", true, \"Value\", Context = (Ankus.PgGucContext)7)", "bool", "ANKUS185", "(Ankus.PgGucContext)7")]
    [DataRow("Ankus.PgGucBool(\"demo.value\", true, \"Value\", Flags = (Ankus.PgGucOptions)1024)", "bool", "ANKUS186", "(Ankus.PgGucOptions)1024")]
    [DataRow("Ankus.PgGucInt(\"demo.value\", 1, \"Value\", Unit = (Ankus.PgGucUnit)9)", "int", "ANKUS187", "(Ankus.PgGucUnit)9")]
    [DataRow("Ankus.PgGucBool(\"demo.value\", true, \"Value\", Flags = Ankus.PgGucOptions.IsName)", "bool", "ANKUS188", "Ankus.PgGucOptions.IsName")]
    [DataRow("Ankus.PgGucBool(\"demo.value\", true, \"Value\", Unit = Ankus.PgGucUnit.Bytes)", "bool", "ANKUS189", "Ankus.PgGucUnit.Bytes")]
    [DataRow("Ankus.PgGucInt(\"demo.value\", 1, \"Value\", Minimum = 2, Maximum = 0)", "int", "ANKUS190", "2")]
    [DataRow("Ankus.PgGucInt(\"demo.value\", 0, \"Value\", Minimum = 1)", "int", "ANKUS191", "0")]
    [DataRow("Ankus.PgGucInt(shortDescription: \"Value\", name: \"demo.value\", defaultValue: 2, Maximum = 1)", "int", "ANKUS191", "2")]
    [DataRow("Ankus.PgGucReal(\"demo.value\", 1.0, \"Value\", Minimum = double.NaN)", "double", "ANKUS192", "double.NaN")]
    [DataRow("Ankus.PgGucReal(\"demo.value\", 1.0, \"Value\", Maximum = double.NaN)", "double", "ANKUS192", "double.NaN")]
    [DataRow("Ankus.PgGucReal(\"demo.value\", 1.0, \"Value\", Minimum = 2.0, Maximum = 0.0)", "double", "ANKUS192", "2.0")]
    [DataRow("Ankus.PgGucReal(\"demo.value\", double.NaN, \"Value\")", "double", "ANKUS193", "double.NaN")]
    [DataRow("Ankus.PgGucReal(\"demo.value\", 2.0, \"Value\", Maximum = 1.0)", "double", "ANKUS193", "2.0")]
    [DataRow("Ankus.PgGucString(\"demo.value\", \"bad\\0\", \"Value\")", "string", "ANKUS194", "\"bad\\0\"")]
    [DataRow("Ankus.PgGucString(\"demo.value\", null, \"Value\")", "string", "ANKUS195", "string")]
    [DataRow("Ankus.PgGucInt(\"demo.value\", 1, \"Value\", Check = \"Absent\")", "int", "ANKUS199", "\"Absent\"")]
    [DataRow("Ankus.PgGucInt(\"demo.value\", 1, \"Value\", Assign = \"Absent\")", "int", "ANKUS200", "\"Absent\"")]
    [DataRow("Ankus.PgGucInt(\"demo.value\", 1, \"Value\", Show = \"Absent\")", "int", "ANKUS201", "\"Absent\"")]
    public void GucDiagnosticsIdentifyAuthoredValues(string attribute, string type, string id, string span)
    {
        (Compilation output, ImmutableArray<Diagnostic> errors) = Generate(
            "public static partial class Settings { [" + attribute + "] public static partial " + type + " Value { get; } }");
        AssertDeclarationDiagnosticLocation(Assert.ContainsSingle(errors), id, span, "configuration/#declaration-diagnostics");
        Assert.DoesNotContain("ankus_guc_register(&", ManifestValue(output, "Ankus.NativeSource"));
        Assert.IsEmpty(output.SyntaxTrees.Where(static tree => tree.FilePath.EndsWith("GucProperties.g.cs", StringComparison.Ordinal)));
        Assert.IsEmpty(output.GetDiagnostics(context.CancellationToken).Where(static item =>
            item.Severity == DiagnosticSeverity.Error && item.Id != "CS9248"));
    }

    /// <summary>
    /// Property and containing-type constraints navigate to the declaration that must change.
    /// </summary>
    /// <param name="source">The invalid setting shape.</param>
    /// <param name="id">The exact diagnostic contract.</param>
    /// <param name="span">The offending declaration or accessor.</param>
    [TestMethod]
    [DataRow("public partial class Settings { [Ankus.PgGucBool(\"demo.value\", true, \"Value\")] public partial bool Value { get; } }", "ANKUS170", "Value")]
    [DataRow("public partial class Settings { [Ankus.PgGucBool(\"demo.value\", true, \"Value\")] public static partial bool Value { get; set; } }", "ANKUS171", "set")]
    [DataRow("public partial class Settings { [Ankus.PgGucBool(\"demo.value\", true, \"Value\")] public static partial bool Value { get; init; } }", "ANKUS171", "init")]
    [DataRow("public partial class Settings { [Ankus.PgGucBool(\"demo.value\", true, \"Value\")] public static ref bool Value => throw new System.Exception(); }", "ANKUS172", "ref bool")]
    [DataRow("public partial class Settings { [Ankus.PgGucBool(\"demo.value\", true, \"Value\")] public static bool Value { get; } }", "ANKUS173", "Value")]
    [DataRow("public partial class Settings { [Ankus.PgGucBool(\"demo.value\", true, \"Value\")] private static partial bool Value { get; } }", "ANKUS174", "Value")]
    [DataRow("public partial struct Settings { [Ankus.PgGucBool(\"demo.value\", true, \"Value\")] public static partial bool Value { get; } }", "ANKUS176", "Settings")]
    [DataRow("public partial class Outer<T> { public partial class Settings { [Ankus.PgGucBool(\"demo.value\", true, \"Value\")] public static partial bool Value { get; } } }", "ANKUS177", "Settings")]
    [DataRow("file partial class Settings { [Ankus.PgGucBool(\"demo.value\", true, \"Value\")] public static partial bool Value { get; } }", "ANKUS178", "Settings")]
    [DataRow("public partial class Outer { private partial class Settings { [Ankus.PgGucBool(\"demo.value\", true, \"Value\")] public static partial bool Value { get; } } }", "ANKUS179", "Settings")]
    [DataRow("public class Outer { public partial class Settings { [Ankus.PgGucBool(\"demo.value\", true, \"Value\")] public static partial bool Value { get; } } }", "ANKUS180", "Outer")]
    public void GucShapeDiagnosticsIdentifyAuthoredDeclarations(string source, string id, string span)
    {
        (Compilation output, ImmutableArray<Diagnostic> errors) = Generate(source);
        AssertDeclarationDiagnosticLocation(Assert.ContainsSingle(errors), id, span, "configuration/#declaration-diagnostics");
        Assert.DoesNotContain("ankus_guc_register(&", ManifestValue(output, "Ankus.NativeSource"));
        Assert.IsEmpty(output.SyntaxTrees.Where(static tree => tree.FilePath.EndsWith("GucProperties.g.cs", StringComparison.Ordinal)));
    }

    /// <summary>
    /// Enum errors distinguish a bad default, malformed label, and ASCII-folded label collision.
    /// </summary>
    /// <param name="members">The enum fields and label metadata.</param>
    /// <param name="boot">The proposed compiled default.</param>
    /// <param name="id">The exact diagnostic contract.</param>
    /// <param name="span">The authored syntax requiring correction.</param>
    [TestMethod]
    [DataRow("First", "(Mode)1", "ANKUS196", "(Mode)1")]
    [DataRow("First", "0", "ANKUS196", "0")]
    [DataRow("[Ankus.PgGucLabel(null!)] First", "Mode.First", "ANKUS197", "null!")]
    [DataRow("[Ankus.PgGucLabel(\"bad\\0\")] First", "Mode.First", "ANKUS197", "\"bad\\0\"")]
    [DataRow("First, first", "Mode.First", "ANKUS198", "first")]
    [DataRow("[Ankus.PgGucLabel(\"same\")] First, [Ankus.PgGucLabel(\"SAME\")] Last", "Mode.First", "ANKUS198", "\"SAME\"")]
    public void GucEnumDiagnosticsIdentifyDefaultOrLabel(string members, string boot, string id, string span)
    {
        (Compilation output, ImmutableArray<Diagnostic> errors) = Generate("public enum Mode { " + members + " } " +
            "public static partial class Settings { [Ankus.PgGucEnum(\"demo.mode\", " + boot + ", \"Mode\")] public static partial Mode Value { get; } }");
        AssertDeclarationDiagnosticLocation(Assert.ContainsSingle(errors), id, span, "configuration/#declaration-diagnostics");
        Assert.DoesNotContain("ankus_guc_register(&", ManifestValue(output, "Ankus.NativeSource"));
        Assert.IsEmpty(output.GetDiagnostics(context.CancellationToken).Where(static item =>
            item.Severity == DiagnosticSeverity.Error && item.Id != "CS9248"));
    }

    /// <summary>
    /// Invalid enum metadata in another assembly points to the consuming property type.
    /// </summary>
    /// <param name="members">The referenced enum fields.</param>
    /// <param name="id">The precise label diagnostic.</param>
    [TestMethod]
    [DataRow("[Ankus.PgGucLabel(\"bad\\0\")] First", "ANKUS197")]
    [DataRow("[Ankus.PgGucLabel(\"bad\\0tail\")] First", "ANKUS197")]
    [DataRow("[Ankus.PgGucLabel(null!)] First", "ANKUS197")]
    [DataRow("First, first", "ANKUS198")]
    public void ReferencedGucEnumDiagnosticsIdentifyConsumingProperty(string members, string id)
    {
        MetadataReference dependency = DatumMappingReference("GucEnumDiagnosticDependency", "public enum Mode { " + members + " }");
        (Compilation output, ImmutableArray<Diagnostic> errors) = GenerateDatumMappingReference(
            "public static partial class Settings { [Ankus.PgGucEnum(\"demo.mode\", Mode.First, \"Mode\")] public static partial Mode Value { get; } }",
            [dependency]);
        AssertDeclarationDiagnosticLocation(Assert.ContainsSingle(errors), id, "Mode", "configuration/#declaration-diagnostics");
        Assert.DoesNotContain("ankus_guc_register(&", ManifestValue(output, "Ankus.NativeSource"));
        Assert.IsEmpty(output.SyntaxTrees.Where(static tree => tree.FilePath.EndsWith("GucProperties.g.cs", StringComparison.Ordinal)));
        Assert.IsEmpty(output.GetDiagnostics(context.CancellationToken).Where(static item =>
            item.Severity == DiagnosticSeverity.Error && item.Id != "CS9248"));
    }

    /// <summary>
    /// Imported labels retain Unicode, hidden aliases, dense ordinals, and nested generic identities.
    /// </summary>
    [TestMethod]
    public void ReferencedGucEnumLabelsPreserveExactValidContracts()
    {
        MetadataReference dependency = DatumMappingReference("GucValidEnumDependency", """
            namespace Shared
            {
                public class Container<T>
                {
                    public enum Mode : long
                    {
                        [Ankus.PgGucLabel("é")] First = long.MinValue,
                        [Ankus.PgGucLabel("hidden-label", Hidden = true)] Hidden = long.MaxValue,
                        [Ankus.PgGucLabel("alias")] Alias = Hidden,
                    }
                }
            }
            """);
        CSharpCompilation input = ModuleCompilation("""
            public static partial class Settings
            {
                [Ankus.PgGucEnum("demo.mode", Shared.Container<int>.Mode.Hidden, "Mode")]
                public static partial Shared.Container<int>.Mode Value { get; }
            }
            """).AddReferences(dependency);
        GeneratorDriver driver = RunModule(ModuleDriver(), input, out Compilation output);
        GucModel model = Assert.ContainsSingle(Assert.ContainsSingle(driver.GetRunResult().Results).TrackedSteps["GucModel"]
            .SelectMany(static step => step.Outputs).Select(static item => item.Value).OfType<GucModel>());

        Assert.AreSequenceEqual([
            new GucModel.Label("First", "é", 0, false),
            new GucModel.Label("Hidden", "hidden-label", 1, true),
            new GucModel.Label("Alias", "alias", 1, false),
        ], model.Labels);
        Assert.AreEqual(new GucConstant(1), model.Default);
        AssertGucCompilation(output, []);
    }

    /// <summary>
    /// Malformed imported UTF-8 cannot silently become a replacement character in a native label.
    /// </summary>
    [TestMethod]
    public void ReferencedGucEnumLabelsRejectMalformedUtf8()
    {
        const string Marker = "marker_label";
        CSharpCompilation library = CSharpCompilation.Create("GucMalformedLabelDependency",
            [CSharpSyntaxTree.ParseText("public enum Mode { [Ankus.PgGucLabel(\"" + Marker + "\")] First }",
                cancellationToken: context.CancellationToken)], s_references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        byte[] image = EmitDatumMappingImage(library);
        byte[] marker = System.Text.Encoding.UTF8.GetBytes(Marker);
        int offset = Assert.ContainsSingle(Enumerable.Range(0, image.Length - marker.Length + 1)
            .Where(index => image.AsSpan(index, marker.Length).SequenceEqual(marker)));
        image[offset] = byte.MaxValue;
        MetadataReference dependency = MetadataReference.CreateFromImage(image);
        (Compilation output, ImmutableArray<Diagnostic> errors) = GenerateDatumMappingReference(
            "public static partial class Settings { [Ankus.PgGucEnum(\"demo.mode\", Mode.First, \"Mode\")] public static partial Mode Value { get; } }",
            [dependency]);

        AssertDeclarationDiagnosticLocation(Assert.ContainsSingle(errors), "ANKUS197", "Mode", "configuration/#declaration-diagnostics");
        Assert.DoesNotContain("ankus_guc_register(&", ManifestValue(output, "Ankus.NativeSource"));
        Assert.IsEmpty(output.SyntaxTrees.Where(static tree => tree.FilePath.EndsWith("GucProperties.g.cs", StringComparison.Ordinal)));
    }

    /// <summary>
    /// Source movement preserves cached invalid setting analysis and resolves its argument in the current tree.
    /// </summary>
    [TestMethod]
    public void GucValueDiagnosticsRemainCachedAcrossSourceMovement()
    {
        string source = "public static class Earlier { public static int Body() => 1; }\n" +
            "public static partial class Settings { [Ankus.PgGucInt(\"invalid\", 1, \"Value\")] public static partial int Value { get; } }";
        CSharpCompilation input = ModuleCompilation(source);
        GeneratorDriver driver = ModuleDriver().RunGeneratorsAndUpdateCompilation(input, out _,
            out ImmutableArray<Diagnostic> initial, context.CancellationToken);
        Diagnostic previous = Assert.ContainsSingle(initial);
        SyntaxTree current = CSharpSyntaxTree.ParseText(source.Replace("=> 1;", "=> 10000;", StringComparison.Ordinal),
            path: "Module.cs", cancellationToken: context.CancellationToken);
        SyntaxTree unrelated = CSharpSyntaxTree.ParseText("internal static class Unrelated;", path: "Unrelated.cs",
            cancellationToken: context.CancellationToken);
        CSharpCompilation edited = input.RemoveAllSyntaxTrees().AddSyntaxTrees(unrelated, current);
        driver = driver.RunGeneratorsAndUpdateCompilation(edited, out _, out ImmutableArray<Diagnostic> errors, context.CancellationToken);
        Diagnostic error = Assert.ContainsSingle(errors);

        AssertDeclarationDiagnosticLocation(error, "ANKUS181", "\"invalid\"", "configuration/#declaration-diagnostics");
        Assert.AreEqual(IncrementalStepRunReason.Cached, ModuleStep(driver, "GucAnalysis"));
        Assert.AreEqual(IncrementalStepRunReason.Cached, ModuleStep(driver, "GucEmission"));
        Assert.AreSame(current, error.Location.SourceTree);
        Assert.AreNotEqual(previous.Location.SourceSpan.Start, error.Location.SourceSpan.Start);
    }
}
