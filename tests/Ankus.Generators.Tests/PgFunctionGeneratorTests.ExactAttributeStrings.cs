using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Ankus.Generators.Tests;

public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Nullable System.Type constructor and named arguments are identified by CLR identity rather than annotated display text.
    /// </summary>
    /// <param name="portable">Whether the options are read from actual metadata.</param>
    /// <param name="constructor">Whether the constructor specifies a type rather than null.</param>
    /// <param name="named">Whether the named type option specifies a type rather than null.</param>
    [TestMethod]
    [DataRow(false, false, false)]
    [DataRow(true, false, false)]
    [DataRow(false, true, false)]
    [DataRow(true, true, false)]
    [DataRow(false, false, true)]
    [DataRow(true, false, true)]
    [DataRow(false, true, true)]
    [DataRow(true, true, true)]
    public void ExactAttributeStringsDecodeNullableTypeArguments(bool portable, bool constructor, bool named)
    {
        string source = "[Ankus.PgType(" + (constructor ? "typeof(Codec)" : "null") + ", TextCodec=" +
            (named ? "typeof(TextCodec)" : "null") + ", Name=\"é\\0\", Alignment=Ankus.PgTypeAlignment.EightBytes, BinaryProtocol=true)] " +
            "public class Payload { } public class Codec { } public class TextCodec { }";
        CSharpCompilation library = ModuleCompilation(source).WithAssemblyName("NullableTypeAttributeDependency");
        MetadataReference reference = portable ? MetadataReference.CreateFromImage(EmitDatumMappingImage(library)) : library.ToMetadataReference();
        CSharpCompilation input = ModuleCompilation(string.Empty).AddReferences(reference);
        INamedTypeSymbol? type = input.GetTypeByMetadataName("Payload");
        Assert.IsNotNull(type);
        AttributeData attribute = Assert.ContainsSingle(type.GetAttributes());
        Assert.IsTrue(ExactAttributeStrings.TryRead(type, attribute, context.CancellationToken, out AttributeStrings? strings));
        Assert.IsNotNull(strings);
        Assert.IsEmpty(strings.Arguments);
        Assert.HasCount(1, strings.Properties);
        Assert.AreEqual("é\0", strings.Properties["Name"]);
    }

    /// <summary>
    /// Source strings with unpaired UTF-16 cannot become different persisted UTF-8 keys.
    /// </summary>
    /// <param name="value">The invalid leading or trailing surrogate partition.</param>
    [TestMethod]
    [DataRow("\ud800tail")]
    [DataRow("head\udfff")]
    public void ExactAttributeStringsPreserveInvalidUnicodeForContractValidation(string value)
    {
        CSharpCompilation input = ModuleCompilation("public class Payload { [System.Text.Json.Serialization.JsonPropertyName(" +
            SymbolDisplay.FormatLiteral(value, true) + ")] public int Value { get; set; } }");
        INamedTypeSymbol? type = input.GetTypeByMetadataName("Payload");
        Assert.IsNotNull(type);
        ISymbol property = Assert.ContainsSingle(type.GetMembers("Value"));
        AttributeData attribute = Assert.ContainsSingle(property.GetAttributes());
        Assert.AreEqual(value, attribute.ConstructorArguments[0].Value);
        Assert.IsTrue(ExactAttributeStrings.TryRead(property, attribute, context.CancellationToken, out AttributeStrings? strings));
        Assert.IsNotNull(strings);
        Assert.AreEqual(value, strings.Arguments[0]);
        Assert.IsFalse(ExactAttributeStrings.IsUnicode(strings.Arguments[0]));
        SerializationModel? model = DefaultTypeSerializer.Create(type, out string? error, cancellationToken: context.CancellationToken);
        Assert.IsNull(model);
        Assert.IsNotNull(error);
        Assert.Contains("valid Unicode", error);
    }

    /// <summary>
    /// A same-class attribute from another declaration cannot supply strings for the selected owner.
    /// </summary>
    /// <param name="portable">Whether the declarations come from emitted metadata.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ExactAttributeStringsRejectAnotherOwnersAttribute(bool portable)
    {
        CSharpCompilation library = ModuleCompilation("public class Payload { " +
            "[System.Text.Json.Serialization.JsonPropertyName(\"owned\")] public int First { get; set; } " +
            "[System.Text.Json.Serialization.JsonPropertyName(\"foreign\")] public int Second { get; set; } }")
            .WithAssemblyName("SelectedOwnerAttributeDependency");
        MetadataReference reference = portable ? MetadataReference.CreateFromImage(EmitDatumMappingImage(library)) : library.ToMetadataReference();
        CSharpCompilation input = ModuleCompilation(string.Empty).AddReferences(reference);
        INamedTypeSymbol? type = input.GetTypeByMetadataName("Payload");
        Assert.IsNotNull(type);
        ISymbol first = Assert.ContainsSingle(type.GetMembers("First"));
        ISymbol second = Assert.ContainsSingle(type.GetMembers("Second"));
        AttributeData owned = Assert.ContainsSingle(first.GetAttributes());
        AttributeData foreign = Assert.ContainsSingle(second.GetAttributes());
        Assert.IsFalse(ExactAttributeStrings.TryRead(first, foreign, context.CancellationToken, out AttributeStrings? rejected));
        Assert.IsNull(rejected);
        Assert.IsTrue(ExactAttributeStrings.TryRead(first, owned, context.CancellationToken, out AttributeStrings? strings));
        Assert.IsNotNull(strings);
        Assert.AreEqual("owned", strings.Arguments[0]);
    }

    /// <summary>
    /// Retargeted source constants remain readable when the attribute no longer carries its source syntax.
    /// </summary>
    [TestMethod]
    public void ExactAttributeStringsPreserveRetargetedSourceConstants()
    {
        CSharpCompilation library = ModuleCompilation("public class Payload { [System.Text.Json.Serialization.JsonPropertyName(\"é\\0\")] public int Value { get; set; } }")
            .WithAssemblyName("RetargetedAttributeDependency")
            .AddReferences(DatumMappingReference("UnusedAttributeDependency", "public sealed class Unused { }"));
        CSharpCompilation input = ModuleCompilation(string.Empty).AddReferences(library.ToMetadataReference());
        INamedTypeSymbol? type = input.GetTypeByMetadataName("Payload");
        Assert.IsNotNull(type);
        ISymbol property = Assert.ContainsSingle(type.GetMembers("Value"));
        AttributeData attribute = Assert.ContainsSingle(property.GetAttributes());
        Assert.IsNull(attribute.ApplicationSyntaxReference);
        Assert.HasCount(1, type.DeclaringSyntaxReferences);
        Assert.IsNull(type.ContainingModule.GetMetadata());
        Assert.IsTrue(ExactAttributeStrings.TryRead(property, attribute, context.CancellationToken, out AttributeStrings? strings));
        Assert.IsNotNull(strings);
        Assert.AreEqual("é\0", strings.Arguments[0]);
        SerializationModel? model = DefaultTypeSerializer.Create(type, out string? error, cancellationToken: context.CancellationToken);
        Assert.IsNull(error);
        Assert.IsNotNull(model);
        Assert.AreEqual("é\0", Assert.ContainsSingle(model.Nodes[0].Members).SerializedName);
    }

    /// <summary>
    /// Linked module members retain their exact strings even though the containing assembly is the consumer.
    /// </summary>
    [TestMethod]
    public void ExactAttributeStringsPreserveLinkedModuleMembers()
    {
        CSharpCompilation library = ModuleCompilation("public class Payload { [System.Text.Json.Serialization.JsonPropertyName(\"é\\0\")] public int Value { get; set; } }");
        library = library.WithOptions(library.Options.WithOutputKind(OutputKind.NetModule).WithModuleName("AttributeContract.netmodule"));
        CSharpCompilation input = ModuleCompilation(string.Empty).AddReferences(MetadataReference.CreateFromImage(
            EmitDatumMappingImage(library), MetadataReferenceProperties.Module));
        INamedTypeSymbol? type = input.GetTypeByMetadataName("Payload");
        Assert.IsNotNull(type);
        Assert.IsTrue(SymbolEqualityComparer.Default.Equals(input.Assembly, type.ContainingAssembly));
        Assert.AreEqual("AttributeContract.netmodule", type.ContainingModule.MetadataName);
        Assert.IsNotNull(type.ContainingModule.GetMetadata());
        ISymbol property = Assert.ContainsSingle(type.GetMembers("Value"));
        AttributeData attribute = Assert.ContainsSingle(property.GetAttributes());
        Assert.IsTrue(ExactAttributeStrings.TryRead(property, attribute, context.CancellationToken, out AttributeStrings? strings));
        Assert.IsNotNull(strings);
        Assert.AreEqual("é\0", strings.Arguments[0]);
        SerializationModel? model = DefaultTypeSerializer.Create(type, out string? error, cancellationToken: context.CancellationToken);
        Assert.IsNull(error);
        Assert.IsNotNull(model);
        Assert.AreEqual("é\0", Assert.ContainsSingle(model.Nodes[0].Members).SerializedName);
    }

    /// <summary>
    /// The exact reader follows raw namespace and nested generic definitions for type and member attributes.
    /// </summary>
    /// <param name="portable">Whether the symbols come from their actual reference image.</param>
    /// <param name="space">The authored namespace, including escaped and Unicode segments.</param>
    [TestMethod]
    [DataRow(false, "Imported")]
    [DataRow(true, "Imported")]
    [DataRow(false, "@class.@namespace")]
    [DataRow(true, "@class.@namespace")]
    [DataRow(false, "é.Δ")]
    [DataRow(true, "é.Δ")]
    public void ExactAttributeStringsFollowDefiningTypeAndMember(bool portable, string space)
    {
        string source = "namespace " + space + " { public class Outer<T> { public class Inner<U> { " +
            "[System.Text.Json.Serialization.JsonPropertyName(\"é\\0\")] public int Value { get; set; } } } }";
        CSharpCompilation library = ModuleCompilation(source).WithAssemblyName("NestedAttributeDependency");
        MetadataReference reference = portable ? MetadataReference.CreateFromImage(EmitDatumMappingImage(library)) : library.ToMetadataReference();
        CSharpCompilation input = ModuleCompilation(string.Empty).AddReferences(reference);
        string metadata = space.Replace("@", string.Empty, StringComparison.Ordinal) + ".Outer`1+Inner`1";
        INamedTypeSymbol? type = input.GetTypeByMetadataName(metadata);
        Assert.IsNotNull(type);
        ISymbol property = Assert.ContainsSingle(type.GetMembers("Value"));
        AttributeData attribute = Assert.ContainsSingle(property.GetAttributes());
        Assert.IsTrue(ExactAttributeStrings.TryRead(property, attribute, context.CancellationToken, out AttributeStrings? strings));
        Assert.IsNotNull(strings);
        Assert.AreEqual("é\0", strings.Arguments[0]);
        Assert.IsEmpty(strings.Properties);
        Assert.AreEqual(portable, type.ContainingModule.GetMetadata() is not null);
    }

    /// <summary>
    /// Repeated discriminator attributes retain their own string or numeric constructor overload and ordinal.
    /// </summary>
    /// <param name="portable">Whether the declarations are imported metadata.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ExactAttributeStringsPreserveSelectedConstructorAndOrdinal(bool portable)
    {
        const string Source = """
            [System.Text.Json.Serialization.JsonDerivedType(typeof(First), "é\0")]
            [System.Text.Json.Serialization.JsonDerivedType(typeof(Second), "é")]
            [System.Text.Json.Serialization.JsonDerivedType(typeof(Third), 42)]
            [System.Text.Json.Serialization.JsonDerivedType(typeof(Fourth), "42")]
            public abstract class Payload { }
            public sealed class First : Payload { }
            public sealed class Second : Payload { }
            public sealed class Third : Payload { }
            public sealed class Fourth : Payload { }
            """;
        CSharpCompilation library = ModuleCompilation(Source).WithAssemblyName("VariantAttributeDependency");
        MetadataReference reference = portable ? MetadataReference.CreateFromImage(EmitDatumMappingImage(library)) : library.ToMetadataReference();
        CSharpCompilation input = ModuleCompilation(string.Empty).AddReferences(reference);
        INamedTypeSymbol? type = input.GetTypeByMetadataName("Payload");
        Assert.IsNotNull(type);
        AttributeData[] attributes = [.. type.GetAttributes()];
        Assert.HasCount(4, attributes);
        string?[] expected = ["é\0", "é", null, "42"];
        for (int index = 0; index < attributes.Length; index++)
        {
            Assert.IsTrue(ExactAttributeStrings.TryRead(type, attributes[index], context.CancellationToken, out AttributeStrings? strings));
            Assert.IsNotNull(strings);
            Assert.AreEqual(expected[index] is not null, strings.Arguments.TryGetValue(1, out string? tag));
            Assert.AreEqual(expected[index], tag);
            Assert.IsEmpty(strings.Properties);
        }

        SerializationModel? model = DefaultTypeSerializer.Create(type, out string? error, cancellationToken: context.CancellationToken);
        Assert.IsNull(error);
        Assert.IsNotNull(model);
        Assert.AreSequenceEqual(expected, model.Nodes[0].Variants.Select(static item => item.Text));
        Assert.AreSequenceEqual([null, null, 42, null], model.Nodes[0].Variants.Select(static item => item.Number));
    }

    /// <summary>
    /// Null, empty and omitted named strings preserve existing defaults while non-null zero-containing text remains exact.
    /// </summary>
    /// <param name="portable">Whether the attribute comes from emitted metadata.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ExactAttributeStringsPreserveOptionalAndNullableProperties(bool portable)
    {
        const string Source = "[Ankus.PgEnum(Name=null, Schema=\"\", Id=\"é\\0\", Requires=new[] { \"one\", \"two\" }, GenerateSql=false)] public enum Mood { Ready }";
        CSharpCompilation library = ModuleCompilation(Source).WithAssemblyName("OptionalAttributeDependency");
        MetadataReference reference = portable ? MetadataReference.CreateFromImage(EmitDatumMappingImage(library)) : library.ToMetadataReference();
        CSharpCompilation input = ModuleCompilation(string.Empty).AddReferences(reference);
        INamedTypeSymbol? type = input.GetTypeByMetadataName("Mood");
        Assert.IsNotNull(type);
        AttributeData attribute = Assert.ContainsSingle(type.GetAttributes());
        Assert.IsTrue(ExactAttributeStrings.TryRead(type, attribute, context.CancellationToken, out AttributeStrings? strings));
        Assert.IsNotNull(strings);
        Assert.IsNull(strings.Properties["Name"]);
        Assert.AreEqual(string.Empty, strings.Properties["Schema"]);
        Assert.AreEqual("é\0", strings.Properties["Id"]);
        Assert.AreEqual("fallback", strings.Property("Name", "fallback"));
        Assert.AreEqual(string.Empty, strings.Property("Schema", "fallback"));
        Assert.AreEqual("omitted", strings.Property("Sql", "omitted"));
        Assert.IsFalse(strings.Properties.ContainsKey("Requires"));
        Assert.IsFalse(strings.Properties.ContainsKey("GenerateSql"));
    }

    /// <summary>
    /// Corrupt attribute text and incomplete blobs fail closed instead of supplying replacement or partial values.
    /// </summary>
    /// <param name="corruption">The malformed UTF-8, missing named property or trailing-byte partition.</param>
    [TestMethod]
    [DataRow("utf8")]
    [DataRow("missing-property")]
    [DataRow("trailing")]
    public void ExactAttributeStringsRejectMalformedMetadata(string corruption)
    {
        const string Marker = "attribute_marker";
        byte[] image = EmitDatumMappingImage(ModuleCompilation(
            "public class Payload { [System.Text.Json.Serialization.JsonPropertyName(\"" + Marker + "\")] public int Value { get; set; } }")
            .WithAssemblyName("CorruptAttributeDependency"));
        CorruptAttributeImage(image, Marker, corruption);

        CSharpCompilation input = ModuleCompilation(string.Empty).AddReferences(MetadataReference.CreateFromImage(image));
        INamedTypeSymbol? type = input.GetTypeByMetadataName("Payload");
        Assert.IsNotNull(type);
        ISymbol owner = Assert.ContainsSingle(type.GetMembers("Value"));
        AttributeData selected = Assert.ContainsSingle(owner.GetAttributes());
        Assert.IsFalse(ExactAttributeStrings.TryRead(owner, selected, context.CancellationToken, out AttributeStrings? strings));
        Assert.IsNull(strings);
        SerializationModel? model = DefaultTypeSerializer.Create(type, out string? error, cancellationToken: context.CancellationToken);
        Assert.IsNull(model);
        Assert.IsNotNull(error);
        Assert.Contains("exact serialization attribute metadata cannot be read", error);
    }

    /// <summary>
    /// Corrupts one exact custom-attribute blob while retaining the surrounding readable PE image.
    /// </summary>
    /// <param name="image">The defining assembly image.</param>
    /// <param name="textMarker">The unique attribute string locating the intended blob.</param>
    /// <param name="corruption">The malformed UTF-8, incomplete argument or trailing-byte partition.</param>
    /// <param name="namedCountOffset">The named-argument count offset when the marker itself is a named argument.</param>
    private static void CorruptAttributeImage(byte[] image, string textMarker, string corruption, int? namedCountOffset = null)
    {
        using var stream = new MemoryStream(image, writable: false);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        byte[] marker = Encoding.UTF8.GetBytes(textMarker);
        CustomAttribute attribute = Assert.ContainsSingle(reader.CustomAttributes.Select(reader.GetCustomAttribute)
            .Where(item => reader.GetBlobBytes(item.Value).AsSpan().IndexOf(marker) >= 0));
        byte[] blob = reader.GetBlobBytes(attribute.Value);
        int offset = Assert.ContainsSingle(Enumerable.Range(0, image.Length - blob.Length + 1)
            .Where(index => image.AsSpan(index, blob.Length).SequenceEqual(blob)));
        if (corruption == "utf8")
        {
            int text = blob.AsSpan().IndexOf(marker);
            Assert.IsGreaterThan(0, text);
            image[offset + text] = byte.MaxValue;
        }
        else if (corruption == "missing-property")
        {
            int count = namedCountOffset ?? blob.Length - 2;
            image[offset + count] = byte.MaxValue;
            image[offset + count + 1] = byte.MaxValue;
        }
        else
        {
            var prefix = new BlobBuilder();
            prefix.WriteCompressedInteger(blob.Length);
            var longer = new BlobBuilder();
            longer.WriteCompressedInteger(blob.Length + 1);
            Assert.AreEqual(prefix.Count, longer.Count);
            Assert.AreSequenceEqual(prefix.ToArray(), image.AsSpan(offset - prefix.Count, prefix.Count).ToArray());
            longer.ToArray().CopyTo(image, offset - prefix.Count);
        }
    }

    /// <summary>
    /// An already cancelled exact-reader invocation propagates cancellation without producing partial text.
    /// </summary>
    /// <param name="portable">Whether metadata traversal would otherwise be required.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ExactAttributeStringsPropagateCancellation(bool portable)
    {
        CSharpCompilation library = ModuleCompilation("[Ankus.PgEnum(Name=\"é\")] public enum Mood { Ready }")
            .WithAssemblyName("CancelledAttributeDependency");
        MetadataReference reference = portable ? MetadataReference.CreateFromImage(EmitDatumMappingImage(library)) : library.ToMetadataReference();
        CSharpCompilation input = ModuleCompilation(string.Empty).AddReferences(reference);
        INamedTypeSymbol? type = input.GetTypeByMetadataName("Mood");
        Assert.IsNotNull(type);
        AttributeData attribute = Assert.ContainsSingle(type.GetAttributes());
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => ExactAttributeStrings.TryRead(type, attribute, cancelled.Token, out _));
    }
}
