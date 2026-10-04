using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Ankus.Generators.Tests;

public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Base-type factories validate exact imported names, schemas and native null-input messages.
    /// </summary>
    /// <param name="portable">Whether the attributed type is imported metadata.</param>
    /// <param name="role">The exact SQL identity or native error-message option.</param>
    [TestMethod]
    [DataRow(false, "name")]
    [DataRow(true, "name")]
    [DataRow(false, "schema")]
    [DataRow(true, "schema")]
    [DataRow(false, "inherited-schema")]
    [DataRow(true, "inherited-schema")]
    [DataRow(false, "null-input")]
    [DataRow(true, "null-input")]
    public void ReferencedCustomTypeStringsRejectTrailingZero(bool portable, string role)
    {
        string options = role switch
        {
            "name" => "Name=\"é\\0\"",
            "schema" => "Schema=\"é\\0\"",
            "null-input" => "NullInputErrorMessage=\"é\\0\"",
            _ => string.Empty,
        };
        string source = (role == "inherited-schema" ? "[Ankus.PgSchema(\"é\\0\")] " : string.Empty) +
            "public static class Types { [Ankus.PgType(" + options + ")] public sealed class Payload { public int Value { get; set; } } }";
        CSharpCompilation library = ModuleCompilation(source).WithAssemblyName("CustomTypeAttributeDependency");
        MetadataReference reference = portable ? MetadataReference.CreateFromImage(EmitDatumMappingImage(library)) : library.ToMetadataReference();
        CSharpCompilation input = ModuleCompilation(string.Empty).AddReferences(reference);
        INamedTypeSymbol? type = input.GetTypeByMetadataName("Types+Payload");
        Assert.IsNotNull(type);
        string? error = null;
        CustomTypeDeclaration? model = CustomTypeDeclaration.Create(type, report: value => error = value,
            cancellationToken: context.CancellationToken);
        Assert.IsNull(model);
        Assert.IsNotNull(error);
        Assert.Contains(role == "null-input" ? "NullInputErrorMessage" : "valid identifiers", error);
    }

    /// <summary>
    /// Valid imported base-type options reach the detached registration and native I/O contract unchanged.
    /// </summary>
    /// <param name="portable">Whether the exact option values are read from emitted metadata.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ReferencedCustomTypeOptionsPreserveDefaultsAndExactText(bool portable)
    {
        const string Source = """
            [Ankus.PgSchema("固定 Δ")]
            public static class Types
            {
                [Ankus.PgType(Name=null, Schema=null, NullInputErrorMessage="é ' input", BinaryProtocol=true,
                    Alignment=Ankus.PgTypeAlignment.EightBytes, Requires=new[] { "setup" })]
                public sealed class Payload
                {
                    public int Value
                    {
                        get;
                        set;
                    }
                }
            }
            """;
        CSharpCompilation library = ModuleCompilation(Source).WithAssemblyName("CustomTypeDefaultsDependency");
        MetadataReference reference = portable ? MetadataReference.CreateFromImage(EmitDatumMappingImage(library)) : library.ToMetadataReference();
        CSharpCompilation input = ModuleCompilation(string.Empty).AddReferences(reference);
        INamedTypeSymbol? type = input.GetTypeByMetadataName("Types+Payload");
        Assert.IsNotNull(type);
        string? error = null;
        CustomTypeDeclaration? declaration = CustomTypeDeclaration.Create(type, report: value => error = value,
            cancellationToken: context.CancellationToken);
        Assert.IsNull(error);
        Assert.IsNotNull(declaration);
        CustomTypeModel model = declaration.Freeze();
        Assert.AreEqual("payload", model.Name);
        Assert.AreEqual("固定 Δ", model.Schema);
        Assert.AreEqual("é ' input", model.NullInputErrorMessage);
        Assert.AreEqual("é ' input", model.Io.NullInputErrorMessage);
        Assert.AreEqual("double", model.Alignment);
        Assert.IsTrue(model.BinaryProtocol);
        Assert.IsNotNull(model.Serializer);
        Assert.AreEqual("Value", Assert.ContainsSingle(model.Serializer.Nodes[0].Members).SerializedName);
    }
}
