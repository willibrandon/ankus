using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Ankus.Generators.Tests;

public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Imported contracts preserve exact persisted strings in detached models, JSON and independent CBOR fixtures.
    /// </summary>
    /// <param name="portable">Whether the contract is read from an emitted reference.</param>
    /// <param name="family">The string-bearing serialization attribute.</param>
    /// <param name="value">The exact persisted text, including valid empty and zero-containing strings.</param>
    [TestMethod]
    [DataRow(false, "member", "é")]
    [DataRow(true, "member", "é")]
    [DataRow(false, "member", "é\0")]
    [DataRow(true, "member", "é\0")]
    [DataRow(false, "member", "")]
    [DataRow(true, "member", "")]
    [DataRow(false, "overridden-member", "é")]
    [DataRow(true, "overridden-member", "é")]
    [DataRow(false, "overridden-member", "é\0")]
    [DataRow(true, "overridden-member", "é\0")]
    [DataRow(false, "overridden-member", "")]
    [DataRow(true, "overridden-member", "")]
    [DataRow(false, "enum", "é")]
    [DataRow(true, "enum", "é")]
    [DataRow(false, "enum", "é\0")]
    [DataRow(true, "enum", "é\0")]
    [DataRow(false, "enum", "")]
    [DataRow(true, "enum", "")]
    [DataRow(false, "discriminator-property", "é")]
    [DataRow(true, "discriminator-property", "é")]
    [DataRow(false, "discriminator-property", "é\0")]
    [DataRow(true, "discriminator-property", "é\0")]
    [DataRow(false, "discriminator-property", "")]
    [DataRow(true, "discriminator-property", "")]
    [DataRow(false, "discriminator-value", "é")]
    [DataRow(true, "discriminator-value", "é")]
    [DataRow(false, "discriminator-value", "é\0")]
    [DataRow(true, "discriminator-value", "é\0")]
    [DataRow(false, "discriminator-value", "")]
    [DataRow(true, "discriminator-value", "")]
    public void ReferencedSerializationStringsPreserveExactStorage(bool portable, string family, string value)
    {
        string literal = SymbolDisplay.FormatLiteral(value, true);
        string declaration = family switch
        {
            "member" => "public sealed class Payload { [System.Text.Json.Serialization.JsonPropertyName(" +
                literal + ")] public int Value { get; set; } = 7; }",
            "overridden-member" => "public class Parent { [System.Text.Json.Serialization.JsonPropertyName(" +
                literal + ")] public virtual int Value { get; set; } } " +
                "public sealed class Payload : Parent { public override int Value { get; set; } = 7; }",
            "enum" => "public enum Payload { [System.Text.Json.Serialization.JsonStringEnumMemberName(" +
                literal + ")] Ready = 7 }",
            "discriminator-property" => "[System.Text.Json.Serialization.JsonPolymorphic(TypeDiscriminatorPropertyName=" +
                literal + ")] [System.Text.Json.Serialization.JsonDerivedType(typeof(Child), \"child\")] public abstract class Payload { } " +
                "public sealed class Child : Payload { public int Value { get; set; } = 7; }",
            _ => "[System.Text.Json.Serialization.JsonDerivedType(typeof(Child), " + literal +
                ")] public abstract class Payload { } public sealed class Child : Payload { public int Value { get; set; } = 7; }",
        };
        CSharpCompilation library = ModuleCompilation("namespace Imported { " + declaration + " }")
            .WithAssemblyName("ImportedSerializationContracts");
        byte[] image = EmitDatumMappingImage(library);
        MetadataReference reference = portable ? MetadataReference.CreateFromImage(image) : library.ToMetadataReference();
        string initializer = family switch
        {
            "member" or "overridden-member" => "new Imported.Payload()",
            "enum" => "Imported.Payload.Ready",
            _ => "new Imported.Child()",
        };
        CSharpCompilation input = ModuleCompilation("[Ankus.PgType] public sealed class Envelope { public Imported.Payload Item { get; set; } = " +
            initializer + "; }").AddReferences(reference);
        INamedTypeSymbol? root = input.GetTypeByMetadataName("Envelope");
        Assert.IsNotNull(root);
        SerializationModel? model = DefaultTypeSerializer.Create(root, out string? error, cancellationToken: context.CancellationToken);
        Assert.IsNull(error);
        Assert.IsNotNull(model);
        SerializationModel.Node payload = Assert.ContainsSingle(model.Nodes.Where(node =>
            node.ConstructionType == "global::Imported.Payload" && node.Kind == (family == "enum" ? "enum" :
                family is "member" or "overridden-member" ? "object" : "polymorphic")));
        switch (family)
        {
            case "member":
            case "overridden-member":
                Assert.AreEqual(value, Assert.ContainsSingle(payload.Members).SerializedName);
                break;
            case "enum":
                Assert.AreEqual(value, Assert.ContainsSingle(payload.EnumMembers).Name);
                break;
            case "discriminator-property":
                Assert.AreEqual(value, payload.DiscriminatorName);
                Assert.AreEqual("child", Assert.ContainsSingle(payload.Variants).Text);
                break;
            default:
                Assert.AreEqual(value, Assert.ContainsSingle(payload.Variants).Text);
                Assert.IsNull(Assert.ContainsSingle(payload.Variants).Number);
                break;
        }

        string[] actual = RunReferencedSerializationCodec(input, image);
        Assert.HasCount(4, actual);
        Assert.AreEqual(actual[0], actual[1], "The JSON parser must preserve its exact contract.");
        Assert.AreEqual(actual[0], actual[2], "The CBOR reader must preserve its exact contract.");
        using JsonDocument json = JsonDocument.Parse(actual[0]);
        JsonElement item = json.RootElement.GetProperty("Item");
        if (family == "enum")
        {
            Assert.AreEqual(value, item.GetString());
        }
        else if (family is "member" or "overridden-member")
        {
            JsonProperty member = Assert.ContainsSingle(item.EnumerateObject());
            Assert.AreEqual(value, member.Name);
            Assert.AreEqual(7, member.Value.GetInt32());
        }
        else
        {
            Assert.HasCount(2, item.EnumerateObject());
            Assert.AreEqual(family == "discriminator-value" ? value : "child",
                item.GetProperty(family == "discriminator-property" ? value : "$type").GetString());
            Assert.AreEqual(7, item.GetProperty("Value").GetInt32());
        }

        string expectedItem = family switch
        {
            "member" or "overridden-member" => "A1" + SmallCborString(value) + "07",
            "enum" => SmallCborString(value),
            "discriminator-property" => "A2" + SmallCborString(value) + "656368696C646556616C756507",
            _ => "A2652474797065" + SmallCborString(value) + "6556616C756507",
        };
        Assert.AreEqual("A1644974656D" + expectedItem, actual[3], "The persisted CBOR must match an independent definite-map fixture.");
    }

    /// <summary>
    /// Constructs one small definite-length CBOR text fixture without using the generated codec.
    /// </summary>
    /// <param name="value">The exact fixture text.</param>
    /// <returns>The standard text header and unchanged UTF-8 contents.</returns>
    private static string SmallCborString(string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        Assert.IsLessThan(24, bytes.Length);
        return ((byte)(0x60 + bytes.Length)).ToString("X2", System.Globalization.CultureInfo.InvariantCulture) + Convert.ToHexString(bytes);
    }

    /// <summary>
    /// Executes a generated codec with its actual dependency image and a statically typed JSON/CBOR probe.
    /// </summary>
    /// <param name="input">The extension consuming the imported contract.</param>
    /// <param name="dependency">The same contract's emitted image for managed execution.</param>
    /// <returns>Original JSON, parsed JSON, stored JSON and the exact CBOR bytes.</returns>
    private string[] RunReferencedSerializationCodec(CSharpCompilation input, byte[] dependency)
    {
        input = input.AddSyntaxTrees(CSharpSyntaxTree.ParseText("""
            internal static class ImportedSerializationProbe
            {
                public static string[] Run(Ankus.PgTypeCodec<Envelope> codec)
                {
                    var value = new Envelope();
                    string json = codec.Format(value);
                    var bytes = new System.Buffers.ArrayBufferWriter<byte>();
                    codec.Write(value, bytes);
                    return new[] { json, codec.Format(codec.Parse(json)), codec.Format(codec.Read(bytes.WrittenSpan)),
                        System.Convert.ToHexString(bytes.WrittenSpan) };
                }
            }
            """, path: "Probe.cs", cancellationToken: context.CancellationToken));
        ModuleDriver().RunGeneratorsAndUpdateCompilation(input, out Compilation output,
            out ImmutableArray<Diagnostic> diagnostics, context.CancellationToken);
        Assert.IsEmpty(diagnostics);
        AssertAggregateCompilation(output, diagnostics);
        using var image = new MemoryStream(EmitDatumMappingImage(output));
        using var referenced = new MemoryStream(dependency);
        var loadContext = new AssemblyLoadContext("ImportedSerializationContractsProbe", isCollectible: true);
        try
        {
            loadContext.LoadFromStream(referenced);
            Assembly assembly = loadContext.LoadFromStream(image);
            Type value = assembly.GetType("Envelope", throwOnError: true)!;
            Type codecBase = typeof(PgTypeCodec<>).MakeGenericType(value);
            Type codecType = Assert.ContainsSingle(assembly.GetTypes().Where(type => !type.IsAbstract && codecBase.IsAssignableFrom(type)));
            object? codec = Activator.CreateInstance(codecType, nonPublic: true);
            Assert.IsNotNull(codec);
            Type probe = assembly.GetType("ImportedSerializationProbe", throwOnError: true)!;
            MethodInfo? method = probe.GetMethod("Run", BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(method);
            return Assert.IsInstanceOfType<string[]>(method.Invoke(null, [codec]));
        }
        finally
        {
            loadContext.Unload();
        }
    }
}
