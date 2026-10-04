using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Ankus.Generators.Tests;

public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Datum and range readers retain the selected attribute's defining assembly rather than its equal metadata name.
    /// </summary>
    /// <param name="portable">Whether the carrier is an emitted assembly rather than a source reference.</param>
    /// <param name="range">Whether the selected attribute declares a range rather than a datum.</param>
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public void RemainingReaderMappingsPreserveAttributeAssembly(bool portable, bool range)
    {
        (PortableExecutableReference left, PortableExecutableReference right) = RemainingReaderAttributeReferences();
        CSharpCompilation library = ModuleCompilation("""
            extern alias Left;
            extern alias Right;
            [Left::Ankus.PgDatumType("left\0", typeof(int), Schema = "left_schema\0")]
            [Right::Ankus.PgDatumType("right\0", typeof(int), Schema = "right_schema\0")]
            [Left::Ankus.PgRangeType("left\0", Schema = "left_schema\0")]
            [Right::Ankus.PgRangeType("right\0", Schema = "right_schema\0")]
            public struct Payload { }
            """).WithAssemblyName("RemainingReaderMappingDependency").AddReferences(left, right);
        MetadataReference reference = portable ? MetadataReference.CreateFromImage(EmitDatumMappingImage(library)) : library.ToMetadataReference();
        CSharpCompilation input = ModuleCompilation(string.Empty).AddReferences(left, right, reference);
        INamedTypeSymbol? owner = input.GetTypeByMetadataName("Payload");
        Assert.IsNotNull(owner);
        AttributeData[] attributes = [.. owner.GetAttributes().Where(attribute => attribute.AttributeClass?.Name ==
            (range ? "PgRangeTypeAttribute" : "PgDatumTypeAttribute"))];
        Assert.HasCount(2, attributes);
        Assert.IsFalse(SymbolEqualityComparer.Default.Equals(attributes[0].AttributeClass, attributes[1].AttributeClass));
        foreach (AttributeData attribute in attributes)
        {
            string prefix = attribute.AttributeClass!.ContainingAssembly.Name == "LeftAttribute" ? "left" : "right";
            Assert.IsTrue(MappedIdentityMetadata.TryRead(owner, attribute, input, context.CancellationToken,
                out string? name, out string? schema));
            Assert.AreEqual(prefix + "\0", name);
            Assert.AreEqual(prefix + "_schema\0", schema);
        }
    }

    /// <summary>
    /// Equal label metadata names from distinct assemblies do not fabricate a duplicate of the selected semantic attribute.
    /// </summary>
    [TestMethod]
    public void RemainingReaderGucLabelsPreserveSelectedAttributeAssembly()
    {
        (PortableExecutableReference left, PortableExecutableReference right) = RemainingReaderAttributeReferences();
        CSharpCompilation library = ModuleCompilation("""
            extern alias Left;
            extern alias Right;
            public enum Mode
            {
                [Left::Ankus.PgGucLabel("left\0")]
                [Right::Ankus.PgGucLabel("right\0")]
                First,
                Plain,
            }
            """).WithAssemblyName("RemainingReaderGucDependency").AddReferences(left, right);
        MetadataReference reference = MetadataReference.CreateFromImage(EmitDatumMappingImage(library));
        CSharpCompilation input = ModuleCompilation(string.Empty).AddReferences(left, right, reference);
        INamedTypeSymbol? mode = input.GetTypeByMetadataName("Mode");
        Assert.IsNotNull(mode);
        IFieldSymbol first = Assert.ContainsSingle(mode.GetMembers("First").OfType<IFieldSymbol>());
        AttributeData selected = first.GetAttributes().First(static attribute => attribute.AttributeClass?.ToDisplayString() ==
            "Ankus.PgGucLabelAttribute");
        Assert.AreEqual("LeftAttribute", selected.AttributeClass!.ContainingAssembly.Name);
        Dictionary<string, string?>? labels = GucEnumMetadata.ReadLabels(mode, input, context.CancellationToken);
        Assert.IsNotNull(labels);
        Assert.HasCount(1, labels);
        Assert.AreEqual("left\0", labels["First"]);
        Assert.IsFalse(labels.ContainsKey("Plain"));
    }

    /// <summary>
    /// Label metadata is accepted only after the complete named-property payload is consumed.
    /// </summary>
    /// <param name="corruption">The incomplete-property or trailing-byte boundary.</param>
    [TestMethod]
    [DataRow("missing-property")]
    [DataRow("trailing")]
    public void RemainingReaderGucLabelsRejectIncompleteMetadata(string corruption)
    {
        const string Marker = "remaining_reader_marker";
        byte[] image = EmitDatumMappingImage(ModuleCompilation(
            "public enum Mode { [Ankus.PgGucLabel(\"" + Marker + "\")] First }")
            .WithAssemblyName("IncompleteGucReaderDependency"));
        CorruptAttributeImage(image, Marker, corruption);
        CSharpCompilation input = ModuleCompilation(string.Empty).AddReferences(MetadataReference.CreateFromImage(image));
        INamedTypeSymbol? mode = input.GetTypeByMetadataName("Mode");
        Assert.IsNotNull(mode);
        Assert.IsNull(GucEnumMetadata.ReadLabels(mode, input, context.CancellationToken));
    }

    /// <summary>
    /// Incomplete imported labels diagnose the current consuming setting without registering a partial native definition.
    /// </summary>
    /// <param name="corruption">The incomplete-property or trailing-byte boundary.</param>
    [TestMethod]
    [DataRow("missing-property")]
    [DataRow("trailing")]
    public void RemainingReaderGucMetadataErrorsIdentifyCurrentSetting(string corruption)
    {
        const string Marker = "remaining_setting_marker";
        byte[] image = EmitDatumMappingImage(ModuleCompilation(
            "public enum Mode { [Ankus.PgGucLabel(\"" + Marker + "\")] First }")
            .WithAssemblyName("IncompleteSettingReaderDependency"));
        CorruptAttributeImage(image, Marker, corruption);
        (Compilation output, ImmutableArray<Diagnostic> errors) = GenerateDatumMappingReference(
            "public static partial class Settings { [Ankus.PgGucEnum(\"demo.mode\", Mode.First, \"Mode\")] public static partial Mode Value { get; } }",
            [MetadataReference.CreateFromImage(image)]);
        AssertDeclarationDiagnosticLocation(Assert.ContainsSingle(errors), "ANKUS197", "Mode", "configuration/#declaration-diagnostics");
        Assert.DoesNotContain("ankus_guc_register(&", ManifestValue(output, "Ankus.NativeSource"));
        Assert.IsEmpty(output.SyntaxTrees.Where(static tree => tree.FilePath.EndsWith("GucProperties.g.cs", StringComparison.Ordinal)));
        Assert.IsEmpty(output.GetDiagnostics(context.CancellationToken).Where(static diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error && diagnostic.Id != "CS9248"));
    }

    /// <summary>
    /// Linked enum modules retain exact field labels from their own definition table.
    /// </summary>
    /// <param name="zero">Whether the independently specified label ends in a zero character.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void RemainingReaderGucLabelsPreserveLinkedModules(bool zero)
    {
        string expected = zero ? "é\0" : "é";
        CSharpCompilation library = ModuleCompilation("public enum Mode { [Ankus.PgGucLabel(" +
            SymbolDisplay.FormatLiteral(expected, true) + ", Hidden=true)] First, Plain }");
        library = library.WithOptions(library.Options.WithOutputKind(OutputKind.NetModule).WithModuleName("GucReader.netmodule"));
        MetadataReference reference = MetadataReference.CreateFromImage(EmitDatumMappingImage(library), MetadataReferenceProperties.Module);
        CSharpCompilation input = ModuleCompilation(string.Empty).AddReferences(reference);
        INamedTypeSymbol? mode = input.GetTypeByMetadataName("Mode");
        Assert.IsNotNull(mode);
        Assert.IsTrue(SymbolEqualityComparer.Default.Equals(mode.ContainingAssembly, input.Assembly));
        Assert.AreEqual("GucReader.netmodule", mode.ContainingModule.MetadataName);
        Dictionary<string, string?>? labels = GucEnumMetadata.ReadLabels(mode, input, context.CancellationToken);
        Assert.IsNotNull(labels);
        Assert.HasCount(1, labels);
        Assert.AreEqual(expected, labels["First"]);
    }

    /// <summary>
    /// The supplied consumer must actually own the portable enum reference.
    /// </summary>
    [TestMethod]
    public void RemainingReaderGucLabelsRejectUnownedImages()
    {
        MetadataReference reference = MetadataReference.CreateFromImage(EmitDatumMappingImage(ModuleCompilation(
            "public enum Mode { [Ankus.PgGucLabel(\"label\")] First }").WithAssemblyName("OwnedGucReaderDependency")));
        CSharpCompilation input = ModuleCompilation(string.Empty).AddReferences(reference);
        INamedTypeSymbol? mode = input.GetTypeByMetadataName("Mode");
        Assert.IsNotNull(mode);
        Assert.IsNull(GucEnumMetadata.ReadLabels(mode, input.RemoveReferences(reference), context.CancellationToken));
    }

    /// <summary>
    /// Cancellation propagates for both source and portable enum inputs without partial label results.
    /// </summary>
    /// <param name="portable">Whether the declaration is a portable reference rather than a source reference.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void RemainingReaderGucLabelsPropagateCancellation(bool portable)
    {
        CSharpCompilation library = ModuleCompilation("public enum Mode { [Ankus.PgGucLabel(\"label\")] First }")
            .WithAssemblyName("CancelledGucReaderDependency");
        MetadataReference reference = portable ? MetadataReference.CreateFromImage(EmitDatumMappingImage(library)) : library.ToMetadataReference();
        CSharpCompilation input = ModuleCompilation(string.Empty).AddReferences(reference);
        INamedTypeSymbol? mode = input.GetTypeByMetadataName("Mode");
        Assert.IsNotNull(mode);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => GucEnumMetadata.ReadLabels(mode, input, cancelled.Token));
    }

    /// <summary>
    /// Unlabelled enums have an empty portable label table and retain the source-reference fallback.
    /// </summary>
    /// <param name="portable">Whether the enum is supplied by an emitted dependency.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void RemainingReaderGucLabelsPreserveUnlabelledEnums(bool portable)
    {
        CSharpCompilation library = ModuleCompilation("public enum Mode { First, Last }").WithAssemblyName("PlainGucReaderDependency");
        MetadataReference reference = portable ? MetadataReference.CreateFromImage(EmitDatumMappingImage(library)) : library.ToMetadataReference();
        CSharpCompilation input = ModuleCompilation(string.Empty).AddReferences(reference);
        INamedTypeSymbol? mode = input.GetTypeByMetadataName("Mode");
        Assert.IsNotNull(mode);
        Dictionary<string, string?>? labels = GucEnumMetadata.ReadLabels(mode, input, context.CancellationToken);
        if (portable)
        {
            Assert.IsNotNull(labels);
            Assert.IsEmpty(labels);
        }
        else
        {
            Assert.IsNull(labels);
        }
    }

    /// <summary>
    /// Produces two independently defined equal-name attribute classes reachable only through explicit aliases.
    /// </summary>
    private (PortableExecutableReference Left, PortableExecutableReference Right) RemainingReaderAttributeReferences()
    {
        const string Definitions = """
            namespace Ankus
            {
                [System.AttributeUsage(System.AttributeTargets.Struct)]
                public sealed class PgDatumTypeAttribute : System.Attribute
                {
                    public PgDatumTypeAttribute(string name, System.Type converter) { }
                    public string? Schema { get; set; }
                }
                [System.AttributeUsage(System.AttributeTargets.Struct)]
                public sealed class PgRangeTypeAttribute : System.Attribute
                {
                    public PgRangeTypeAttribute(string name) { }
                    public string? Schema { get; set; }
                }
                [System.AttributeUsage(System.AttributeTargets.Field)]
                public sealed class PgGucLabelAttribute : System.Attribute
                {
                    public PgGucLabelAttribute(string name) { }
                }
            }
            """;
        return (MetadataReference.CreateFromImage(EmitDatumMappingImage(ModuleCompilation(Definitions).WithAssemblyName("LeftAttribute")))
            .WithAliases(["Left"]),
            MetadataReference.CreateFromImage(EmitDatumMappingImage(ModuleCompilation(Definitions).WithAssemblyName("RightAttribute")))
            .WithAliases(["Right"]));
    }
}
