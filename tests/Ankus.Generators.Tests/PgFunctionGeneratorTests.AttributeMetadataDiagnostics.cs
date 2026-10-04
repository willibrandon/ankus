using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Ankus.Generators.Tests;

public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Unreadable referenced declarations identify the exact attribute and current consuming SQL slot after edits.
    /// </summary>
    /// <param name="family">The enum, base type or nested serialization contract.</param>
    /// <param name="corruption">The actual malformed custom-attribute blob.</param>
    /// <param name="parameter">Whether the rejected conversion is an argument rather than a result.</param>
    [TestMethod]
    [DataRow("enum", "utf8", false)]
    [DataRow("enum", "utf8", true)]
    [DataRow("enum", "missing-property", false)]
    [DataRow("enum", "missing-property", true)]
    [DataRow("enum", "trailing", false)]
    [DataRow("enum", "trailing", true)]
    [DataRow("type", "utf8", false)]
    [DataRow("type", "utf8", true)]
    [DataRow("type", "missing-property", false)]
    [DataRow("type", "missing-property", true)]
    [DataRow("type", "trailing", false)]
    [DataRow("type", "trailing", true)]
    [DataRow("serialization", "utf8", false)]
    [DataRow("serialization", "utf8", true)]
    [DataRow("serialization", "missing-property", false)]
    [DataRow("serialization", "missing-property", true)]
    [DataRow("serialization", "trailing", false)]
    [DataRow("serialization", "trailing", true)]
    [DataRow("variant", "utf8", false)]
    [DataRow("variant", "utf8", true)]
    [DataRow("variant", "missing-property", false)]
    [DataRow("variant", "missing-property", true)]
    [DataRow("variant", "trailing", false)]
    [DataRow("variant", "trailing", true)]
    public void ReferencedAttributeFailuresIdentifyCurrentSqlSlot(string family, string corruption, bool parameter)
    {
        MetadataReference reference = CorruptDeclarationReference(family, corruption);
        string source = parameter
            ? "public static class Functions { [Ankus.PgFunction] public static int Use(Imported.Payload value) => 1; }"
            : "public static class Functions { [Ankus.PgFunction] public static Imported.Payload Use() => default!; }";
        CSharpCompilation input = ModuleCompilation(source).AddReferences(reference);
        GeneratorDriver driver = ModuleDriver();
        foreach (string prefix in new[] { string.Empty, "\n\n" })
        {
            SyntaxTree tree = CSharpSyntaxTree.ParseText(prefix + source, path: "Consumer.cs", cancellationToken: context.CancellationToken);
            input = input.RemoveAllSyntaxTrees().AddSyntaxTrees(tree);
            driver = driver.RunGeneratorsAndUpdateCompilation(input, out Compilation output,
                out ImmutableArray<Diagnostic> diagnostics, context.CancellationToken);
            Diagnostic error = Assert.ContainsSingle(diagnostics);
            Assert.AreEqual("ANKUS206", error.Id);
            Assert.AreEqual(DiagnosticSeverity.Error, error.Severity);
            Assert.AreSame(tree, error.Location.SourceTree);
            Assert.AreEqual("Imported.Payload", tree.GetText(context.CancellationToken).ToString(error.Location.SourceSpan));
            Assert.Contains(family == "enum" ? "PgEnumAttribute" : family == "type" ? "PgTypeAttribute" :
                family == "variant" ? "JsonDerivedTypeAttribute" : "JsonPropertyNameAttribute",
                error.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
            Assert.Contains("Imported.Payload", error.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
            Assert.EndsWith("#referenced-attribute-metadata", error.Descriptor.HelpLinkUri);
            Assert.AreEqual("Pg_magic_func\n", ManifestValue(output, "Ankus.Exports"));
            Assert.DoesNotContain("CREATE FUNCTION", InstallationBody(output));
            Assert.IsEmpty(output.GetDiagnostics(context.CancellationToken).Where(static item => item.Severity == DiagnosticSeverity.Error));
        }
    }

    /// <summary>
    /// An unreadable nested referenced member diagnoses its current owning custom-type declaration.
    /// </summary>
    /// <param name="corruption">The actual malformed custom-attribute blob.</param>
    [TestMethod]
    [DataRow("utf8")]
    [DataRow("missing-property")]
    [DataRow("trailing")]
    public void ReferencedSerializerFailureIdentifiesCurrentCustomType(string corruption)
    {
        CSharpCompilation input = ModuleCompilation("[Ankus.PgType] public sealed class Envelope { public Imported.Payload Item { get; set; } = new(); }")
            .AddReferences(CorruptDeclarationReference("serialization", corruption));
        ModuleDriver().RunGeneratorsAndUpdateCompilation(input, out Compilation output,
            out ImmutableArray<Diagnostic> diagnostics, context.CancellationToken);
        Diagnostic error = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual("ANKUS206", error.Id);
        Assert.AreSame(Assert.ContainsSingle(input.SyntaxTrees), error.Location.SourceTree);
        Assert.AreEqual("Envelope", error.Location.SourceTree!.GetText(context.CancellationToken).ToString(error.Location.SourceSpan));
        Assert.Contains("JsonPropertyNameAttribute", error.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Contains("Imported.Payload.Value", error.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
        Assert.DoesNotContain("CREATE TYPE", InstallationBody(output));
        Assert.IsEmpty(output.GetDiagnostics(context.CancellationToken).Where(static item => item.Severity == DiagnosticSeverity.Error));
    }

    /// <summary>
    /// Creates an actual readable dependency containing one deliberately unreadable declaration attribute.
    /// </summary>
    private PortableExecutableReference CorruptDeclarationReference(string family, string corruption)
    {
        const string Marker = "declaration_marker";
        string source = family switch
        {
            "enum" => "[Ankus.PgEnum(Name=\"" + Marker + "\")] public enum Payload { Ready }",
            "type" => "[Ankus.PgType(Name=\"" + Marker + "\")] public sealed class Payload { public int Value { get; set; } }",
            "variant" => "[Ankus.PgType] [System.Text.Json.Serialization.JsonDerivedType(typeof(Child), \"" + Marker +
                "\")] public abstract class Payload { } public sealed class Child : Payload { public int Value { get; set; } }",
            _ => "[Ankus.PgType] public sealed class Payload { [System.Text.Json.Serialization.JsonPropertyName(\"" + Marker +
                "\")] public int Value { get; set; } }",
        };
        byte[] image = EmitDatumMappingImage(ModuleCompilation("namespace Imported { " + source + " }")
            .WithAssemblyName("CorruptDeclarationDependency"));
        CorruptAttributeImage(image, Marker, corruption, family == "type" ? 3 : family == "enum" ? 2 : null);
        return MetadataReference.CreateFromImage(image);
    }

    /// <summary>
    /// Arrays, table rows, set elements and typed aggregate inputs cannot obscure unreadable referenced declarations.
    /// </summary>
    /// <param name="family">The exact referenced enum, type or serialization contract.</param>
    /// <param name="shape">The composing signature's container or capability.</param>
    [TestMethod]
    [DataRow("enum", "array")]
    [DataRow("type", "array")]
    [DataRow("serialization", "array")]
    [DataRow("enum", "set")]
    [DataRow("type", "set")]
    [DataRow("serialization", "set")]
    [DataRow("enum", "table")]
    [DataRow("type", "table")]
    [DataRow("serialization", "table")]
    [DataRow("enum", "aggregate")]
    [DataRow("type", "aggregate")]
    [DataRow("serialization", "aggregate")]
    [DataRow("variant", "array")]
    [DataRow("variant", "set")]
    [DataRow("variant", "table")]
    [DataRow("variant", "aggregate")]
    public void ReferencedAttributeFailuresRemainPreciseThroughComposition(string family, string shape)
    {
        string source = shape switch
        {
            "array" => "public static class Functions { [Ankus.PgFunction] public static Imported.Payload[] Use() => []; }",
            "set" => "public static class Functions { [Ankus.PgFunction] public static System.Collections.Generic.IEnumerable<Imported.Payload> Use() => []; }",
            "table" => "public static class Functions { [Ankus.PgFunction] public static System.Collections.Generic.IEnumerable<(int Id, Imported.Payload Item)> Use() => []; }",
            _ => "[Ankus.PgAggregate(InitialCondition=\"0\")] public sealed class Total : Ankus.IPgAggregate<long, Imported.Payload> { " +
                "public static long Transition(Ankus.PgAggregateContext context, long state, Imported.Payload value) => state; }",
        };
        CSharpCompilation input = ModuleCompilation(source).AddReferences(CorruptDeclarationReference(family, "utf8"));
        ModuleDriver().RunGeneratorsAndUpdateCompilation(input, out Compilation output,
            out ImmutableArray<Diagnostic> diagnostics, context.CancellationToken);
        Diagnostic error = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual("ANKUS206", error.Id);
        Assert.AreSame(Assert.ContainsSingle(input.SyntaxTrees), error.Location.SourceTree);
        Assert.Contains("Imported.Payload", error.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Contains("Imported.Payload", error.Location.SourceTree!.GetText(context.CancellationToken).ToString(error.Location.SourceSpan));
        Assert.AreEqual("Pg_magic_func\n", ManifestValue(output, "Ankus.Exports"));
        Assert.DoesNotContain("CREATE FUNCTION", InstallationBody(output));
        Assert.DoesNotContain("CREATE AGGREGATE", InstallationBody(output));
        Assert.IsEmpty(output.GetDiagnostics(context.CancellationToken).Where(static item => item.Severity == DiagnosticSeverity.Error));
    }
}
