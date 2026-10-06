using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Ankus.Generators.Tests;

public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Imported enum names, labels and enclosing schemas must reject exact invalid zero characters.
    /// </summary>
    /// <param name="portable">Whether the declaration is read from an emitted reference.</param>
    /// <param name="role">The exact enum identity component to invalidate.</param>
    [TestMethod]
    [DataRow(false, "name")]
    [DataRow(true, "name")]
    [DataRow(false, "schema")]
    [DataRow(true, "schema")]
    [DataRow(false, "label")]
    [DataRow(true, "label")]
    [DataRow(false, "inherited-schema")]
    [DataRow(true, "inherited-schema")]
    public void ReferencedEnumIdentitiesRejectTrailingZero(bool portable, string role)
    {
        CSharpCompilation input = ReferencedEnumInput(portable, role, "identity_é\0");
        INamedTypeSymbol? type = input.GetTypeByMetadataName("Imported.Types+Mood");
        Assert.IsNotNull(type);
        string? reason = null;
        EnumDeclaration? model = EnumDeclaration.Create(type, new GeneratorDiagnostics((descriptor, _, arguments) => reason = string.Format(System.Globalization.CultureInfo.InvariantCulture, descriptor.MessageFormat.ToString(System.Globalization.CultureInfo.InvariantCulture), arguments.Cast<object>().ToArray()), context.CancellationToken), cancellationToken: context.CancellationToken);
        Assert.IsNull(model);
        Assert.IsNotNull(reason);
        Assert.Contains(role == "label" ? "without zero characters" : "nonempty SQL", reason);

        ModuleDriver().RunGeneratorsAndUpdateCompilation(input, out Compilation output,
            out ImmutableArray<Diagnostic> diagnostics, context.CancellationToken);
        Diagnostic error = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual("ANKUS039", error.Id);
        Assert.AreSame(Assert.ContainsSingle(input.SyntaxTrees), error.Location.SourceTree);
        Assert.AreEqual("Pg_magic_func\n", ManifestValue(output, "Ankus.Exports"));
        Assert.DoesNotContain("CREATE FUNCTION", InstallationBody(output));
        Assert.IsEmpty(output.GetDiagnostics(context.CancellationToken).Where(static item => item.Severity == DiagnosticSeverity.Error));
    }

    /// <summary>
    /// Adjacent UTF-8 byte limits remain identical for source and imported enum components.
    /// </summary>
    /// <param name="portable">Whether the declaration is read from an emitted reference.</param>
    /// <param name="role">The identity component whose byte length is measured.</param>
    /// <param name="bytes">The exact UTF-8 size below, at or above PostgreSQL's limit.</param>
    [TestMethod]
    [DataRow(false, "name", 62)]
    [DataRow(true, "name", 62)]
    [DataRow(false, "name", 63)]
    [DataRow(true, "name", 63)]
    [DataRow(false, "name", 64)]
    [DataRow(true, "name", 64)]
    [DataRow(false, "schema", 62)]
    [DataRow(true, "schema", 62)]
    [DataRow(false, "schema", 63)]
    [DataRow(true, "schema", 63)]
    [DataRow(false, "schema", 64)]
    [DataRow(true, "schema", 64)]
    [DataRow(false, "label", 62)]
    [DataRow(true, "label", 62)]
    [DataRow(false, "label", 63)]
    [DataRow(true, "label", 63)]
    [DataRow(false, "label", 64)]
    [DataRow(true, "label", 64)]
    [DataRow(false, "inherited-schema", 62)]
    [DataRow(true, "inherited-schema", 62)]
    [DataRow(false, "inherited-schema", 63)]
    [DataRow(true, "inherited-schema", 63)]
    [DataRow(false, "inherited-schema", 64)]
    [DataRow(true, "inherited-schema", 64)]
    public void ReferencedEnumIdentitiesRespectUtf8Boundary(bool portable, string role, int bytes)
    {
        string value = new('é', bytes / 2);
        if (bytes % 2 != 0)
        {
            value += "x";
        }

        CSharpCompilation input = ReferencedEnumInput(portable, role, value);
        INamedTypeSymbol? type = input.GetTypeByMetadataName("Imported.Types+Mood");
        Assert.IsNotNull(type);
        string? reason = null;
        EnumDeclaration? model = EnumDeclaration.Create(type, new GeneratorDiagnostics((descriptor, _, arguments) => reason = string.Format(System.Globalization.CultureInfo.InvariantCulture, descriptor.MessageFormat.ToString(System.Globalization.CultureInfo.InvariantCulture), arguments.Cast<object>().ToArray()), context.CancellationToken), cancellationToken: context.CancellationToken);
        if (bytes > 63)
        {
            Assert.IsNull(model);
            Assert.IsNotNull(reason);
            Assert.Contains("63 UTF-8 bytes", reason);
            return;
        }

        Assert.IsNull(reason);
        Assert.IsNotNull(model);
        Assert.AreEqual(role == "name" ? value : "mood", model.Name);
        Assert.AreEqual(role is "schema" or "inherited-schema" ? value : null, model.Schema);
        EnumLabel label = Assert.ContainsSingle(model.Labels);
        Assert.AreEqual("Ready", label.Member);
        Assert.AreEqual(7, label.Value);
        Assert.AreEqual(role == "label" ? value : "Ready", label.Label);
        RunModule(ModuleDriver(), input, out Compilation output);
        Assert.IsNotEmpty(EmitDatumMappingImage(output));
    }

    /// <summary>
    /// Empty labels remain valid without changing default enum and installation-schema identity.
    /// </summary>
    /// <param name="portable">Whether the contract is imported metadata.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ReferencedEnumEmptyLabelsPreserveExactContract(bool portable)
    {
        CSharpCompilation input = ReferencedEnumInput(portable, "label", string.Empty);
        INamedTypeSymbol? type = input.GetTypeByMetadataName("Imported.Types+Mood");
        Assert.IsNotNull(type);
        EnumDeclaration? model = EnumDeclaration.Create(type, cancellationToken: context.CancellationToken);
        Assert.IsNotNull(model);
        Assert.AreEqual("mood", model.Name);
        Assert.IsNull(model.Schema);
        Assert.AreEqual(string.Empty, Assert.ContainsSingle(model.Labels).Label);
        RunModule(ModuleDriver(), input, out Compilation output);
        Assert.IsNotEmpty(EmitDatumMappingImage(output));
    }

    /// <summary>
    /// Builds a genuine source or emitted dependency with one selected enum identity component.
    /// </summary>
    /// <param name="portable">Whether to emit the reference image.</param>
    /// <param name="role">The exact declaration option under test.</param>
    /// <param name="value">The literal identity text.</param>
    /// <returns>An extension consuming the enum through its scalar result and argument.</returns>
    private CSharpCompilation ReferencedEnumInput(bool portable, string role, string value)
    {
        string literal = SymbolDisplay.FormatLiteral(value, true);
        string enumeration = "[Ankus.PgEnum" + (role is "name" or "schema" ? "(" + (role == "name" ? "Name" : "Schema") + "=" + literal + ")" :
            string.Empty) + "] public enum Mood { " + (role == "label" ? "[Ankus.PgEnumLabel(" + literal + ")] " : string.Empty) + "Ready = 7 }";
        string source = "namespace Imported { " + (role == "inherited-schema" ? "[Ankus.PgSchema(" + literal + ")] " : string.Empty) +
            "public static class Types { " + enumeration + " } }";
        CSharpCompilation library = ModuleCompilation(source).WithAssemblyName("ImportedEnumContracts");
        MetadataReference reference = portable ? MetadataReference.CreateFromImage(EmitDatumMappingImage(library)) : library.ToMetadataReference();
        return ModuleCompilation("public static class Functions { [Ankus.PgFunction] public static Imported.Types.Mood Echo(Imported.Types.Mood value) => value; }")
            .AddReferences(reference);
    }
}
