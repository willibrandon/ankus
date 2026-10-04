using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Ankus.Generators.Tests;

public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Equal metadata names from different assemblies cannot substitute another attribute's persisted text.
    /// </summary>
    /// <param name="portable">Whether the attributed declaration is read from an emitted assembly.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ExactAttributeStringsPreserveDefiningAttributeAssembly(bool portable)
    {
        const string Definition = """
            namespace Contracts
            {
                [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true)]
                public sealed class NameAttribute : System.Attribute
                {
                    public NameAttribute(string name)
                    {
                    }
                }
            }
            """;
        PortableExecutableReference left = MetadataReference.CreateFromImage(EmitDatumMappingImage(ModuleCompilation(Definition)
            .WithAssemblyName("LeftAttribute")), MetadataReferenceProperties.Assembly.WithAliases(["Left"]));
        PortableExecutableReference right = MetadataReference.CreateFromImage(EmitDatumMappingImage(ModuleCompilation(Definition)
            .WithAssemblyName("RightAttribute")), MetadataReferenceProperties.Assembly.WithAliases(["Right"]));
        CSharpCompilation library = ModuleCompilation("""
            extern alias Left;
            extern alias Right;
            [Left::Contracts.Name("left\0")]
            [Right::Contracts.Name("right\0")]
            public sealed class Payload { }
            """).WithAssemblyName("AttributeIdentityDependency").AddReferences(left, right);
        MetadataReference reference = portable ? MetadataReference.CreateFromImage(EmitDatumMappingImage(library)) : library.ToMetadataReference();
        CSharpCompilation input = ModuleCompilation(string.Empty).AddReferences(left, right, reference);
        INamedTypeSymbol? owner = input.GetTypeByMetadataName("Payload");
        Assert.IsNotNull(owner);
        AttributeData[] attributes = [.. owner.GetAttributes()];
        Assert.HasCount(2, attributes);
        Assert.IsFalse(SymbolEqualityComparer.Default.Equals(attributes[0].AttributeClass, attributes[1].AttributeClass));
        foreach (AttributeData attribute in attributes)
        {
            Assert.IsTrue(ExactAttributeStrings.TryRead(owner, attribute, context.CancellationToken, out AttributeStrings? strings));
            Assert.IsNotNull(strings);
            string expected = attribute.AttributeClass!.ContainingAssembly.Name == "LeftAttribute" ? "left\0" : "right\0";
            Assert.AreEqual(expected, strings.Arguments[0]);
        }
    }

    /// <summary>
    /// A legitimate assembly forwarder resolves to the compiler-selected defining attribute class.
    /// </summary>
    [TestMethod]
    public void ExactAttributeStringsResolveForwardedAttributeAssembly()
    {
        PortableExecutableReference origin = MetadataReference.CreateFromImage(EmitDatumMappingImage(ModuleCompilation("""
            namespace Contracts
            {
                public sealed class NameAttribute : System.Attribute
                {
                    public NameAttribute(string name)
                    {
                    }
                }
            }
            """).WithAssemblyName("AttributeOrigin")));
        PortableExecutableReference facade = MetadataReference.CreateFromImage(EmitDatumMappingImage(ModuleCompilation(
            "[assembly: System.Runtime.CompilerServices.TypeForwardedTo(typeof(Contracts.NameAttribute))]")
            .WithAssemblyName("AttributeFacade").AddReferences(origin)));
        byte[] image = EmitDatumMappingImage(ModuleCompilation("[Contracts.Name(\"forwarded\\0\")] public sealed class Payload { }")
            .WithAssemblyName("ForwardedAttributeConsumer").AddReferences(origin));
        byte[] originalName = Encoding.UTF8.GetBytes("AttributeOrigin");
        byte[] forwardedName = Encoding.UTF8.GetBytes("AttributeFacade");
        Assert.HasCount(originalName.Length, forwardedName);
        int offset = Assert.ContainsSingle(Enumerable.Range(0, image.Length - originalName.Length + 1)
            .Where(index => image.AsSpan(index, originalName.Length).SequenceEqual(originalName)));
        forwardedName.CopyTo(image, offset);
        CSharpCompilation input = ModuleCompilation(string.Empty).AddReferences(origin, facade, MetadataReference.CreateFromImage(image));
        INamedTypeSymbol? owner = input.GetTypeByMetadataName("Payload");
        Assert.IsNotNull(owner);
        AttributeData attribute = Assert.ContainsSingle(owner.GetAttributes());
        Assert.AreEqual("AttributeOrigin", attribute.AttributeClass!.ContainingAssembly.Name);
        Assert.Contains("AttributeFacade", owner.ContainingModule.ReferencedAssemblies.Select(static assembly => assembly.Name));
        Assert.IsTrue(ExactAttributeStrings.TryRead(owner, attribute, context.CancellationToken, out AttributeStrings? strings));
        Assert.IsNotNull(strings);
        Assert.AreEqual("forwarded\0", strings.Arguments[0]);
    }

    /// <summary>
    /// Same-module attribute constructors preserve exact values for global and nested metadata identities.
    /// </summary>
    /// <param name="portable">Whether the declaring module is an emitted reference.</param>
    /// <param name="nested">Whether the attribute class is nested rather than in the global namespace.</param>
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public void ExactAttributeStringsReadSameModuleConstructors(bool portable, bool nested)
    {
        const string Attribute = "public sealed class NameAttribute : System.Attribute { public NameAttribute(string name) { } }";
        string source = (nested ? "public static class Outer { " + Attribute + " } [Outer.Name(\"owned\\0\")] " :
            Attribute + " [Name(\"owned\\0\")] ") + "public sealed class Payload { }";
        CSharpCompilation library = ModuleCompilation(source).WithAssemblyName("SameModuleAttributeDependency");
        MetadataReference reference = portable ? MetadataReference.CreateFromImage(EmitDatumMappingImage(library)) : library.ToMetadataReference();
        CSharpCompilation input = ModuleCompilation(string.Empty).AddReferences(reference);
        INamedTypeSymbol? owner = input.GetTypeByMetadataName("Payload");
        Assert.IsNotNull(owner);
        AttributeData attribute = Assert.ContainsSingle(owner.GetAttributes());
        Assert.IsTrue(SymbolEqualityComparer.Default.Equals(owner.ContainingModule, attribute.AttributeClass!.ContainingModule));
        Assert.IsTrue(ExactAttributeStrings.TryRead(owner, attribute, context.CancellationToken, out AttributeStrings? strings));
        Assert.IsNotNull(strings);
        Assert.AreEqual("owned\0", strings.Arguments[0]);
    }
}
