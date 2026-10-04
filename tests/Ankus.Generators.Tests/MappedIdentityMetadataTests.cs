using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Ankus.Generators.Tests;

public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Source and portable datum mappings reject exact invalid names or schemas at the consuming signature.
    /// </summary>
    /// <param name="portable">Whether the dependency is emitted metadata.</param>
    /// <param name="schema">Whether the invalid value is the schema rather than the type name.</param>
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public void ReferencedDatumIdentitiesRejectTrailingZeroWithoutNormalization(bool portable, bool schema)
    {
        string source = DatumMappingSource(name: schema ? "int4" : "int4\0", schema: schema ? "pg_catalog\0" : "pg_catalog");
        MetadataReference dependency = ExactMappingReference(source, portable);
        (Compilation output, ImmutableArray<Diagnostic> errors) = GenerateDatumMappingReference(
            "public static class Functions { [Ankus.PgFunction] public static int Read(Value value) => 42; }", [dependency]);

        AssertDeclarationDiagnosticLocation(Assert.ContainsSingle(errors), schema ? "ANKUS139" : "ANKUS138",
            "Value", "raw-values/#mapping-diagnostics");
        Assert.IsNull(output.GetTypeByMetadataName("Ankus.Generated.ExtensionDispatchers"));
        Assert.IsEmpty(output.Assembly.GetAttributes().Where(static attribute =>
            attribute.AttributeClass?.ToDisplayString() == "System.Reflection.AssemblyMetadataAttribute"));
        Assert.IsEmpty(output.GetDiagnostics(context.CancellationToken).Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
    }

    /// <summary>
    /// Source and portable range mappings retain invalid identity bytes through preflight.
    /// </summary>
    /// <param name="portable">Whether the dependency is emitted metadata.</param>
    /// <param name="schema">Whether the invalid value is the schema rather than the range name.</param>
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public void ReferencedRangeIdentitiesRejectTrailingZeroWithoutNormalization(bool portable, bool schema)
    {
        string name = schema ? "int4range" : "int4range\0";
        string space = schema ? "pg_catalog\0" : "pg_catalog";
        string source = DatumRangeSource(SymbolDisplay.FormatLiteral(name, true) +
            ", Origin=Ankus.PgTypeOrigin.External, Schema=" + SymbolDisplay.FormatLiteral(space, true));
        MetadataReference dependency = ExactMappingReference(source, portable);
        (Compilation output, ImmutableArray<Diagnostic> errors) = GenerateDatumMappingReference(
            "public static class Functions { [Ankus.PgFunction] public static int Read(Ankus.PgRange<Value> value) => 42; }", [dependency]);

        AssertDeclarationDiagnosticLocation(Assert.ContainsSingle(errors), schema ? "ANKUS163" : "ANKUS162",
            "Ankus.PgRange<Value>", "ranges/#mapping-diagnostics");
        Assert.IsNull(output.GetTypeByMetadataName("Ankus.Generated.ExtensionDispatchers"));
        Assert.IsEmpty(output.Assembly.GetAttributes().Where(static attribute =>
            attribute.AttributeClass?.ToDisplayString() == "System.Reflection.AssemblyMetadataAttribute"));
        Assert.IsEmpty(output.GetDiagnostics(context.CancellationToken).Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
    }

    /// <summary>
    /// Malformed UTF-8 cannot become replacement characters accepted as a different native identity.
    /// </summary>
    /// <param name="range">Whether the corrupt declaration selects a range.</param>
    /// <param name="schema">Whether corruption is in the named schema.</param>
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void ReferencedMappedIdentitiesRejectMalformedUtf8(bool range, bool schema)
    {
        const string Marker = "identity_marker";
        string source = range ? DatumRangeSource(SymbolDisplay.FormatLiteral(schema ? "int4range" : Marker, true) +
            ", Origin=Ankus.PgTypeOrigin.External, Schema=" + SymbolDisplay.FormatLiteral(schema ? Marker : "pg_catalog", true)) :
            DatumMappingSource(name: schema ? "int4" : Marker, schema: schema ? Marker : "pg_catalog");
        CSharpCompilation library = ModuleCompilation(source).WithAssemblyName("MalformedIdentityDependency");
        byte[] image = EmitDatumMappingImage(library);
        byte[] marker = Encoding.UTF8.GetBytes(Marker);
        int offset = Assert.ContainsSingle(Enumerable.Range(0, image.Length - marker.Length + 1)
            .Where(index => image.AsSpan(index, marker.Length).SequenceEqual(marker)));
        image[offset] = byte.MaxValue;
        MetadataReference dependency = MetadataReference.CreateFromImage(image);
        string carrier = range ? "Ankus.PgRange<Value>" : "Value";
        (Compilation output, ImmutableArray<Diagnostic> errors) = GenerateDatumMappingReference(
            "public static class Functions { [Ankus.PgFunction] public static int Read(" + carrier + " value) => 42; }", [dependency]);

        AssertDeclarationDiagnosticLocation(Assert.ContainsSingle(errors), range ? "ANKUS205" : "ANKUS204", carrier,
            range ? "ranges/#mapping-diagnostics" : "raw-values/#mapping-diagnostics");
        Assert.IsNull(output.GetTypeByMetadataName("Ankus.Generated.ExtensionDispatchers"));
        Assert.IsEmpty(output.Assembly.GetAttributes().Where(static attribute =>
            attribute.AttributeClass?.ToDisplayString() == "System.Reflection.AssemblyMetadataAttribute"));
        Assert.IsEmpty(output.GetDiagnostics(context.CancellationToken).Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
    }

    /// <summary>
    /// A referenced nested generic carrier selects its exact mapping instead of another attribute's strings.
    /// </summary>
    /// <param name="portable">Whether the dependency is emitted metadata.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ReferencedGenericMappingsRetainExactSelectedIdentityAndCapabilities(bool portable)
    {
        const string Source = """
            namespace Shared
            {
                public class Container
                {
                    [Ankus.PgDatumType("unused\0", typeof(Converter<>), Origin=Ankus.PgTypeOrigin.External, Schema="other")]
                    [Ankus.PgDatumType(typeof(Box<int>), "é", typeof(Converter<int>), Origin=Ankus.PgTypeOrigin.External, Schema="Δ")]
                    [Ankus.PgDatumType(typeof(Box<long>), "int8", typeof(Converter<long>), Origin=Ankus.PgTypeOrigin.External, Schema="pg_catalog")]
                    [Ankus.PgRangeType("unused_range\0", Origin=Ankus.PgTypeOrigin.External, Schema="other")]
                    [Ankus.PgRangeType(typeof(Box<int>), "é_range", Origin=Ankus.PgTypeOrigin.External, Schema="Δ")]
                    [Ankus.PgRangeType(typeof(Box<long>), "int8range", Origin=Ankus.PgTypeOrigin.External, Schema="pg_catalog")]
                    public struct Box<T> { }
                }
                public sealed class Converter<T> : Ankus.IPgDatumReader<Container.Box<T>>, Ankus.IPgDatumWriter<Container.Box<T>>
                {
                    public Container.Box<T> Read(Ankus.PgDatum value) => default;
                    public Ankus.PgDatum Write(Container.Box<T> value, uint typeOid, Ankus.PgMemoryContext destination)
                        => throw new System.InvalidOperationException();
                }
            }
            """;
        MetadataReference dependency = ExactMappingReference(Source, portable);
        CSharpCompilation input = ModuleCompilation("""
            public static class Functions
            {
                [Ankus.PgFunction] public static Shared.Container.Box<int> Echo(Shared.Container.Box<int> value) => value;
                [Ankus.PgFunction] public static Ankus.PgRange<Shared.Container.Box<long>> EchoRange(Ankus.PgRange<Shared.Container.Box<long>> value) => value;
            }
            """).AddReferences(dependency);
        GeneratorDriver driver = RunModule(ModuleDriver(), input, out Compilation output);
        DatumRegistrationModel[] models = [.. Assert.ContainsSingle(driver.GetRunResult().Results).TrackedSteps["DatumRegistrationModel"]
            .SelectMany(static step => step.Outputs).Select(static item => item.Value).OfType<DatumRegistrationModel>()];
        DatumRegistrationModel scalar = Assert.ContainsSingle(models.Where(static model => model.Managed == "global::Shared.Container.Box<int>"));
        Assert.AreEqual("é", scalar.Name);
        Assert.AreEqual("Δ", scalar.Schema);
        Assert.AreEqual("global::Shared.Converter<int>", scalar.Converter);
        Assert.IsTrue(scalar.CanRead);
        Assert.IsTrue(scalar.CanWrite);
        Assert.IsTrue(scalar.External);
        DatumRegistrationModel mappedRange = Assert.ContainsSingle(models.Where(static model => model.Bound == "global::Shared.Container.Box<long>"));
        Assert.AreEqual("int8range", mappedRange.Name);
        Assert.AreEqual("pg_catalog", mappedRange.Schema);
        Assert.IsTrue(mappedRange.CanRead);
        Assert.IsTrue(mappedRange.CanWrite);
        Assert.IsTrue(mappedRange.External);
        Assert.IsNotNull(output.GetTypeByMetadataName("Ankus.Generated.ExtensionDispatchers"));
        using var image = new MemoryStream();
        Assert.IsTrue(output.Emit(image, cancellationToken: context.CancellationToken).Success);
    }

    /// <summary>
    /// Optional schema and enum properties retain their complete serialized values and declaration order.
    /// </summary>
    /// <param name="options">The authored named properties, including their leading separator.</param>
    /// <param name="schema">The exact expected schema.</param>
    [TestMethod]
    [DataRow("", null)]
    [DataRow(", Schema=null", null)]
    [DataRow(", Schema=\"Δ\"", "Δ")]
    [DataRow(", Origin=Ankus.PgTypeOrigin.External, Schema=\"pg_catalog\"", "pg_catalog")]
    [DataRow(", Schema=\"pg_catalog\", Origin=Ankus.PgTypeOrigin.External", "pg_catalog")]
    public void MappedIdentityMetadataPreservesOptionalProperties(string options, string? schema)
    {
        bool[] modes = [false, true];
        foreach (bool portable in modes)
        {
            string source = "[Ankus.PgDatumType(\"datum_identity\", typeof(Converter)" + options + ")]" +
                "[Ankus.PgRangeType(\"range_identity\"" + options + ")] public struct Value { }" + DatumConverter();
            MetadataReference dependency = ExactMappingReference(source, portable);
            CSharpCompilation input = ModuleCompilation(string.Empty).AddReferences(dependency);
            INamedTypeSymbol? type = input.GetTypeByMetadataName("Value");
            Assert.IsNotNull(type);
            Assert.HasCount(2, type.GetAttributes());
            foreach (AttributeData attribute in type.GetAttributes())
            {
                Assert.IsTrue(MappedIdentityMetadata.TryRead(type, attribute, input, context.CancellationToken,
                    out string? actualName, out string? actualSchema));
                Assert.AreEqual(attribute.AttributeClass?.Name == "PgDatumTypeAttribute" ? "datum_identity" : "range_identity", actualName);
                Assert.AreEqual(schema, actualSchema);
            }
        }
    }

    /// <summary>
    /// Identifier validation uses exact UTF-8 byte limits through both reference kinds and both mapping kinds.
    /// </summary>
    /// <param name="range">Whether the invalid identity belongs to a range.</param>
    /// <param name="portable">Whether the declaration comes from an emitted image.</param>
    /// <param name="schema">Whether the constrained identifier is the schema.</param>
    /// <param name="byteCount">The exact number of identifier bytes.</param>
    [TestMethod]
    [DynamicData(nameof(MappedIdentityBoundaryCases))]
    public void ReferencedMappedIdentitiesRespectUtf8Boundary(bool range, bool portable, bool schema, int byteCount)
    {
        string identifier = new string('é', byteCount / 2) + (byteCount % 2 == 0 ? string.Empty : "a");
        string name = schema ? range ? "int4range" : "int4" : identifier;
        string space = schema ? identifier : "pg_catalog";
        string source = range ? DatumRangeSource(SymbolDisplay.FormatLiteral(name, true) +
            ", Origin=Ankus.PgTypeOrigin.External, Schema=" + SymbolDisplay.FormatLiteral(space, true)) :
            DatumMappingSource(name: name, schema: space);
        MetadataReference dependency = ExactMappingReference(source, portable);
        string carrier = range ? "Ankus.PgRange<Value>" : "Value";
        CSharpCompilation input = ModuleCompilation(
            "public static class Functions { [Ankus.PgFunction] public static int Read(" + carrier + " value) => 42; }").AddReferences(dependency);
        GeneratorDriver driver = ModuleDriver().RunGeneratorsAndUpdateCompilation(input, out Compilation output,
            out ImmutableArray<Diagnostic> errors, context.CancellationToken);

        if (byteCount > 63)
        {
            string id = range ? schema ? "ANKUS163" : "ANKUS162" : schema ? "ANKUS139" : "ANKUS138";
            AssertDeclarationDiagnosticLocation(Assert.ContainsSingle(errors), id, carrier,
                range ? "ranges/#mapping-diagnostics" : "raw-values/#mapping-diagnostics");
            Assert.IsNull(output.GetTypeByMetadataName("Ankus.Generated.ExtensionDispatchers"));
            Assert.IsEmpty(output.Assembly.GetAttributes().Where(static attribute =>
                attribute.AttributeClass?.ToDisplayString() == "System.Reflection.AssemblyMetadataAttribute"));
            Assert.IsEmpty(output.GetDiagnostics(context.CancellationToken).Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
            return;
        }

        AssertGucCompilation(output, errors);
        string managed = range ? "global::Ankus.PgRange<global::Value>" : "global::Value";
        DatumRegistrationModel model = Assert.ContainsSingle(Assert.ContainsSingle(driver.GetRunResult().Results).TrackedSteps["DatumRegistrationModel"]
            .SelectMany(static step => step.Outputs).Select(static item => item.Value).OfType<DatumRegistrationModel>()
            .Where(item => item.Managed == managed));
        Assert.AreEqual(name, model.Name);
        Assert.AreEqual(space, model.Schema);
        Assert.IsTrue(model.External);
    }

    /// <summary>
    /// Covers the immediately adjacent limits for each independently serialized identifier.
    /// </summary>
    /// <returns>Typed reference, mapping and identifier boundary cases.</returns>
    public static IEnumerable<(bool Range, bool Portable, bool Schema, int ByteCount)> MappedIdentityBoundaryCases()
    {
        bool[] modes = [false, true];
        int[] limits = [62, 63, 64];
        foreach (bool range in modes)
        {
            foreach (bool portable in modes)
            {
                foreach (bool schema in modes)
                {
                    foreach (int byteCount in limits)
                    {
                        yield return (range, portable, schema, byteCount);
                    }
                }
            }
        }
    }

    /// <summary>
    /// An image from another compilation cannot be read through an unrelated compiler's references.
    /// </summary>
    /// <param name="range">Whether the selected attribute is a range declaration.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void MappedIdentityMetadataRejectsUnownedReferences(bool range)
    {
        MetadataReference dependency = ExactMappingReference(DatumRangeSource(), portable: true);
        CSharpCompilation input = ModuleCompilation(string.Empty).AddReferences(dependency);
        INamedTypeSymbol? type = input.GetTypeByMetadataName("Value");
        Assert.IsNotNull(type);
        AttributeData attribute = Assert.ContainsSingle(type.GetAttributes().Where(item =>
            item.AttributeClass?.Name == (range ? "PgRangeTypeAttribute" : "PgDatumTypeAttribute")));

        Assert.IsFalse(MappedIdentityMetadata.TryRead(type, attribute, input.RemoveReferences(dependency), context.CancellationToken,
            out _, out _));
    }

    /// <summary>
    /// Metadata traversal propagates cancellation instead of turning it into a mapping error.
    /// </summary>
    /// <param name="range">Whether the selected attribute is a range declaration.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void MappedIdentityMetadataPropagatesCancellation(bool range)
    {
        MetadataReference dependency = ExactMappingReference(DatumRangeSource(), portable: true);
        CSharpCompilation input = ModuleCompilation(string.Empty).AddReferences(dependency);
        INamedTypeSymbol? type = input.GetTypeByMetadataName("Value");
        Assert.IsNotNull(type);
        AttributeData attribute = Assert.ContainsSingle(type.GetAttributes().Where(item =>
            item.AttributeClass?.Name == (range ? "PgRangeTypeAttribute" : "PgDatumTypeAttribute")));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsExactly<OperationCanceledException>(() =>
            MappedIdentityMetadata.TryRead(type, attribute, input, cancellation.Token, out _, out _));
    }

    /// <summary>
    /// Linked modules use their own exact definition table even though their types belong to the consumer assembly.
    /// </summary>
    /// <param name="zero">Whether the identity includes a trailing zero that must remain visible to validation.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void MappedIdentityMetadataPreservesLinkedModuleStrings(bool zero)
    {
        string name = zero ? "é\0" : "é";
        string schema = zero ? "Δ\0" : "Δ";
        string source = "[Ankus.PgRangeType(" + SymbolDisplay.FormatLiteral(name, true) +
            ", Schema=" + SymbolDisplay.FormatLiteral(schema, true) + ")]" + DatumMappingSource(name: name, schema: schema);
        CSharpCompilation library = ModuleCompilation(source);
        library = library.WithOptions(library.Options.WithOutputKind(OutputKind.NetModule).WithModuleName("Mapping.netmodule"));
        MetadataReference dependency = MetadataReference.CreateFromImage(EmitDatumMappingImage(library), MetadataReferenceProperties.Module);
        CSharpCompilation input = ModuleCompilation(string.Empty).AddReferences(dependency);
        INamedTypeSymbol? type = input.GetTypeByMetadataName("Value");
        Assert.IsNotNull(type);
        Assert.IsTrue(SymbolEqualityComparer.Default.Equals(input.Assembly, type.ContainingAssembly));
        Assert.AreEqual("Mapping.netmodule", type.ContainingModule.MetadataName);
        Assert.HasCount(2, type.GetAttributes());
        foreach (AttributeData attribute in type.GetAttributes())
        {
            Assert.IsTrue(MappedIdentityMetadata.TryRead(type, attribute, input, context.CancellationToken,
                out string? actualName, out string? actualSchema));
            Assert.AreEqual(name, actualName);
            Assert.AreEqual(schema, actualSchema);
        }
    }

    /// <summary>
    /// Null, empty and interior-zero identities remain independently invalid in both reference representations.
    /// </summary>
    /// <param name="range">Whether the selected declaration is a range.</param>
    /// <param name="schema">Whether the invalid text belongs to the schema.</param>
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void ReferencedMappedIdentitiesRejectInvalidText(bool range, bool schema)
    {
        bool[] modes = [false, true];
        string?[] invalid = [null, string.Empty, "int4\0tail"];
        foreach (bool portable in modes)
        {
            foreach (string? value in invalid)
            {
                string literal = value is null ? "null!" : SymbolDisplay.FormatLiteral(value, true);
                string name = schema ? range ? "\"int4range\"" : "\"int4\"" : literal;
                string space = schema ? literal : "\"pg_catalog\"";
                string options = name + (range ? string.Empty : ", typeof(Converter)") +
                    ", Origin=Ankus.PgTypeOrigin.External, Schema=" + space;
                string source = range ? DatumRangeSource(options) :
                    "[Ankus.PgDatumType(" + options + ")] public struct Value { }" + DatumConverter();
                MetadataReference dependency = ExactMappingReference(source, portable);
                string carrier = range ? "Ankus.PgRange<Value>" : "Value";
                (Compilation output, ImmutableArray<Diagnostic> errors) = GenerateDatumMappingReference(
                    "public static class Functions { [Ankus.PgFunction] public static int Read(" + carrier + " value) => 42; }", [dependency]);
                string id = schema && value is null ? range ? "ANKUS165" : "ANKUS141" :
                    range ? schema ? "ANKUS163" : "ANKUS162" : schema ? "ANKUS139" : "ANKUS138";

                AssertDeclarationDiagnosticLocation(Assert.ContainsSingle(errors), id, carrier,
                    range ? "ranges/#mapping-diagnostics" : "raw-values/#mapping-diagnostics");
                Assert.IsNull(output.GetTypeByMetadataName("Ankus.Generated.ExtensionDispatchers"));
                Assert.IsEmpty(output.Assembly.GetAttributes().Where(static attribute =>
                    attribute.AttributeClass?.ToDisplayString() == "System.Reflection.AssemblyMetadataAttribute"));
                Assert.IsEmpty(output.GetDiagnostics(context.CancellationToken).Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
            }
        }
    }

    /// <summary>
    /// Truncated named properties and trailing blob data cannot be accepted as complete mapping attributes.
    /// </summary>
    /// <param name="range">Whether the corrupt attribute declares a range.</param>
    /// <param name="trailing">Whether corruption adds trailing data rather than a missing named property.</param>
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void ReferencedMappedIdentitiesRejectIncompleteMetadata(bool range, bool trailing)
    {
        const string Marker = "complete_identity";
        string source = range ? DatumRangeSource("\"" + Marker + "\", Origin=Ankus.PgTypeOrigin.External, Schema=\"pg_catalog\"") :
            DatumMappingSource(name: Marker, schema: "pg_catalog");
        byte[] image = EmitDatumMappingImage(ModuleCompilation(source).WithAssemblyName("IncompleteMappingDependency"));
        using var file = new MemoryStream(image, writable: false);
        using var pe = new PEReader(file);
        MetadataReader reader = pe.GetMetadataReader();
        byte[] marker = Encoding.UTF8.GetBytes(Marker);
        CustomAttribute encoded = Assert.ContainsSingle(reader.CustomAttributes.Select(reader.GetCustomAttribute)
            .Where(attribute => reader.GetBlobBytes(attribute.Value).AsSpan().IndexOf(marker) >= 0));
        byte[] blob = reader.GetBlobBytes(encoded.Value);
        int offset = Assert.ContainsSingle(Enumerable.Range(0, image.Length - blob.Length + 1)
            .Where(index => image.AsSpan(index, blob.Length).SequenceEqual(blob)));
        if (trailing)
        {
            var originalLength = new BlobBuilder();
            originalLength.WriteCompressedInteger(blob.Length);
            byte[] prefix = originalLength.ToArray();
            Assert.AreSequenceEqual(prefix, image.AsSpan(offset - prefix.Length, prefix.Length).ToArray());
            var longer = new BlobBuilder();
            longer.WriteCompressedInteger(blob.Length + 1);
            Assert.AreEqual(prefix.Length, longer.Count);
            longer.ToArray().CopyTo(image, offset - prefix.Length);
        }
        else
        {
            BlobReader value = reader.GetBlobReader(encoded.Value);
            Assert.AreEqual(1, value.ReadUInt16());
            _ = value.ReadSerializedString();
            if (!range)
            {
                _ = value.ReadSerializedString();
            }

            int namedOffset = value.Offset;
            Assert.AreEqual(2, value.ReadUInt16());
            image[offset + namedOffset] = 3;
        }

        MetadataReference dependency = MetadataReference.CreateFromImage(image);
        string carrier = range ? "Ankus.PgRange<Value>" : "Value";
        (Compilation output, ImmutableArray<Diagnostic> errors) = GenerateDatumMappingReference(
            "public static class Functions { [Ankus.PgFunction] public static int Read(" + carrier + " value) => 42; }", [dependency]);
        AssertDeclarationDiagnosticLocation(Assert.ContainsSingle(errors), range ? "ANKUS205" : "ANKUS204", carrier,
            range ? "ranges/#mapping-diagnostics" : "raw-values/#mapping-diagnostics");
        Assert.IsNull(output.GetTypeByMetadataName("Ankus.Generated.ExtensionDispatchers"));
        Assert.IsEmpty(output.Assembly.GetAttributes().Where(static attribute =>
            attribute.AttributeClass?.ToDisplayString() == "System.Reflection.AssemblyMetadataAttribute"));
    }

    /// <summary>
    /// Supplies the same authored mapping through a source reference or its actual emitted image.
    /// </summary>
    private MetadataReference ExactMappingReference(string source, bool portable)
    {
        CSharpCompilation library = ModuleCompilation(source).WithAssemblyName("ExactIdentityDependency");
        Assert.IsEmpty(library.GetDiagnostics(context.CancellationToken).Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        return portable ? MetadataReference.CreateFromImage(EmitDatumMappingImage(library)) : library.ToMetadataReference();
    }
}
