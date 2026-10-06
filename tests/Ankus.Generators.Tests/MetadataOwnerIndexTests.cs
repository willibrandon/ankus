using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Ankus.Generators.Tests;

public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Malformed unselected constructor references cannot crash or fabricate the selected attribute's exact strings.
    /// </summary>
    /// <param name="combined">Whether the identity has forbidden flags rather than an unsupported content type.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void OwnerIndexRejectsMalformedUnselectedAssemblyReference(bool combined)
    {
        const string Source = """
            extern alias Foreign;
            namespace Ankus
            {
                [System.AttributeUsage(System.AttributeTargets.Enum)]
                public sealed class PgEnumAttribute : System.Attribute
                {
                    public string? Name
                    {
                        get;
                        set;
                    }
                }
            }
            namespace Imported
            {
                [Foreign::Ankus.PgEnum(Name="foreign_payload")]
                [Ankus.PgEnum(Name="actual_payload")]
                public enum Payload
                {
                    Ready,
                }
            }
            """;
        CSharpCompilation library = ModuleCompilation(Source)
            .WithAssemblyName("MalformedOwnerIndexDependency");
        library = library.WithReferences(library.References.Select(reference =>
            reference.Display == typeof(Ankus.PgEnumAttribute).Assembly.Location ? reference.WithAliases(["Foreign"]) : reference));
        byte[] image = EmitDatumMappingImage(library);
        using (PEReader pe = new(new MemoryStream(image)))
        {
            MetadataReader reader = pe.GetMetadataReader();
            AssemblyReferenceHandle reference = Assert.ContainsSingle(reader.AssemblyReferences.Where(handle =>
                reader.GetString(reader.GetAssemblyReference(handle).Name) == "Ankus.Runtime"));
            int offset = pe.PEHeaders.MetadataStartOffset + reader.GetTableMetadataOffset(TableIndex.AssemblyRef) +
                (MetadataTokens.GetRowNumber(reference) - 1) * reader.GetTableRowSize(TableIndex.AssemblyRef) + 8;
            uint extra = combined ? (uint)(AssemblyFlags.Retargetable | AssemblyFlags.WindowsRuntime) : 0x400;
            BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(offset, 4), (uint)reader.GetAssemblyReference(reference).Flags | extra);
        }

        CSharpCompilation input = ModuleCompilation("""
            public static class Functions
            {
                [Ankus.PgFunction]
                public static Imported.Payload Get() => default;
            }
            """)
            .AddReferences(MetadataReference.CreateFromImage(image));
        INamedTypeSymbol? owner = input.GetTypeByMetadataName("Imported.Payload");
        Assert.IsNotNull(owner);
        AttributeData attribute = Assert.ContainsSingle(owner.GetAttributes().Where(static item =>
            item.AttributeClass?.ContainingAssembly.Name == "MalformedOwnerIndexDependency"));
        Assert.IsFalse(ExactAttributeStrings.TryRead(owner, attribute, context.CancellationToken, out AttributeStrings? strings));
        Assert.IsNull(strings);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new PgFunctionGenerator().AsSourceGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(input, out _, out ImmutableArray<Diagnostic> diagnostics, context.CancellationToken);
        Assert.Contains(static diagnostic => diagnostic.Id == "ANKUS206", diagnostics);
        Assert.DoesNotContain(static diagnostic => diagnostic.Id == "CS8785", diagnostics);
        Assert.IsNull(Assert.ContainsSingle(driver.GetRunResult().Results).Exception);
    }

    /// <summary>
    /// Distinct images with the same assembly and type names retain their own exact member strings across repeated reads.
    /// </summary>
    /// <param name="portable">Whether declarations use emitted metadata or a source compilation reference.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void OwnerIndexPreservesDistinctImageIdentity(bool portable)
    {
        (ISymbol first, AttributeData firstAttribute) = OwnerIndexPayload(64, "first\0", portable);
        (ISymbol second, AttributeData secondAttribute) = OwnerIndexPayload(64, "second\0", portable);
        for (int index = 0; index < 4; index++)
        {
            Assert.IsTrue(ExactAttributeStrings.TryRead(first, firstAttribute, context.CancellationToken, out AttributeStrings? left));
            Assert.IsNotNull(left);
            Assert.AreEqual("first\0", left.Arguments[0]);
            Assert.IsTrue(ExactAttributeStrings.TryRead(second, secondAttribute, context.CancellationToken, out AttributeStrings? right));
            Assert.IsNotNull(right);
            Assert.AreEqual("second\0", right.Arguments[0]);
        }
    }

    /// <summary>
    /// Rejected cancelled reads leave later exact reads usable for both cold and previously read images.
    /// </summary>
    /// <param name="warm">Whether the image has been read before cancellation.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void OwnerIndexCancellationDoesNotPoisonLaterReads(bool warm)
    {
        (ISymbol owner, AttributeData attribute) = OwnerIndexPayload(64, "retained\0", true);
        if (warm)
        {
            Assert.IsTrue(ExactAttributeStrings.TryRead(owner, attribute, context.CancellationToken, out _));
        }

        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() =>
            ExactAttributeStrings.TryRead(owner, attribute, cancelled.Token, out _));
        Assert.IsTrue(ExactAttributeStrings.TryRead(owner, attribute, context.CancellationToken, out AttributeStrings? strings));
        Assert.IsNotNull(strings);
        Assert.AreEqual("retained\0", strings.Arguments[0]);
    }

    /// <summary>
    /// Metadata copies share their image identity without requiring the decoder to own or retain either wrapper.
    /// </summary>
    [TestMethod]
    public void OwnerIndexAcceptsNonOwningMetadataCopies()
    {
        (ISymbol owner, AttributeData attribute) = OwnerIndexPayload(64, "copied\0", true);
        using ModuleMetadata second = owner.ContainingModule.GetMetadata()!;
        using (ModuleMetadata first = owner.ContainingModule.GetMetadata()!)
        {
            Assert.AreNotSame(first, second);
            Assert.AreSame(first.Id, second.Id);
            Assert.IsTrue(ExactAttributeStrings.TryRead(owner, attribute, context.CancellationToken, out AttributeStrings? strings));
            Assert.IsNotNull(strings);
            Assert.AreEqual("copied\0", strings.Arguments[0]);
        }

        Assert.IsTrue(ExactAttributeStrings.TryRead(owner, attribute, context.CancellationToken, out AttributeStrings? again));
        Assert.IsNotNull(again);
        Assert.AreEqual("copied\0", again.Arguments[0]);
    }

    /// <summary>
    /// Warm owner-index lookups remain independent of unrelated type-table size, with exact decoding checked separately.
    /// </summary>
    [TestMethod]
    public void OwnerIndexWarmReadsDoNotRescanUnrelatedTypes()
    {
        (ISymbol small, AttributeData smallAttribute) = OwnerIndexPayload(16, "exact\0", true);
        (ISymbol large, AttributeData largeAttribute) = OwnerIndexPayload(2048, "exact\0", true);
        long smallAllocation = OwnerIndexAllocations(small, smallAttribute);
        long largeAllocation = OwnerIndexAllocations(large, largeAttribute);
        Assert.IsInRange(0L, smallAllocation + 32768, largeAllocation,
            "64 warm owner lookups must not allocate new names for every unrelated type in the image.");
    }

    /// <summary>
    /// Measures the shared index independently of constructor decoding and verifies every lookup and exact attribute value.
    /// </summary>
    private long OwnerIndexAllocations(ISymbol owner, AttributeData attribute)
    {
        for (int index = 0; index < 32; index++)
        {
            Read();
        }

        using ModuleMetadata module = owner.ContainingModule.GetMetadata()!;
        MetadataReader reader = module.GetMetadataReader();
        string identity = MetadataTypeName.Create(owner.ContainingType!);
        TypeDefinitionHandle expected = Assert.ContainsSingle(reader.TypeDefinitions.Where(handle =>
            reader.GetString(reader.GetTypeDefinition(handle).Name) == "Target" &&
            reader.GetString(reader.GetTypeDefinition(handle).Namespace) == "Imported"));
        CancellationToken cancellationToken = context.CancellationToken;
        bool[] found = new bool[64];
        TypeDefinitionHandle[] handles = new TypeDefinitionHandle[64];
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 64; index++)
        {
            found[index] = ExactAttributeStrings.TryFindOwnerType(reader, module.Id, identity, cancellationToken, out handles[index]);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        for (int index = 0; index < 64; index++)
        {
            Assert.IsTrue(found[index]);
            Assert.AreEqual(expected, handles[index]);
            Read();
        }

        context.WriteLine(FormattableString.Invariant($"Warm owner lookup allocation: {allocated}."));
        return allocated;

        void Read()
        {
            Assert.IsTrue(ExactAttributeStrings.TryRead(owner, attribute, context.CancellationToken, out AttributeStrings? strings));
            Assert.IsNotNull(strings);
            Assert.AreEqual("exact\0", strings.Arguments[0]);
        }
    }

    /// <summary>
    /// Emits same-named dependencies containing independently selected attributes and a bounded unrelated type population.
    /// </summary>
    private (ISymbol Owner, AttributeData Attribute) OwnerIndexPayload(int padding, string label, bool portable)
    {
        StringBuilder source = new("namespace Imported\n{\n");
        for (int index = 0; index < padding; index++)
        {
            source.Append("public sealed class Padding").Append(index).Append("\n{\n}\n\n");
        }

        source.Append("public sealed class Target\n{\n[System.Text.Json.Serialization.JsonPropertyName(")
            .Append(Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(label, true))
            .Append(")]\npublic int Value\n{\nget;\nset;\n}\n}\n}\n");
        CSharpCompilation library = ModuleCompilation(source.ToString())
            .WithAssemblyName("SameOwnerIndexDependency");
        MetadataReference reference = portable ? MetadataReference.CreateFromImage(EmitDatumMappingImage(library)) : library.ToMetadataReference();
        CSharpCompilation input = ModuleCompilation(string.Empty).AddReferences(reference);
        INamedTypeSymbol? type = input.GetTypeByMetadataName("Imported.Target");
        Assert.IsNotNull(type);
        ISymbol owner = Assert.ContainsSingle(type.GetMembers("Value"));
        return (owner, Assert.ContainsSingle(owner.GetAttributes()));
    }
}
