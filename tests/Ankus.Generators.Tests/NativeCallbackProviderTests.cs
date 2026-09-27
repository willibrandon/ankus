using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Ankus.Generators.Tests;

public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// A consumer receives callback dispatch support from its provider without importing provider initialization methods.
    /// </summary>
    /// <param name="intermediary">Whether an ordinary class library forwards the callback requirement.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void NativeCallbacksInReferencedProvidersRetainConsumerDispatch(bool intermediary)
    {
        (Compilation provider, ImmutableArray<Diagnostic> diagnostics) = Generate(CallbackTypeSource + """
            public static partial class CallbackProvider
            {
                [Ankus.PgNativeCallback(nameof(Handle))]
                public static partial Hook Target { get; }
                private static long Handle(int first, long second) => second + first;
                [Ankus.PgModuleLoad]
                public static void RegisterProvider() { }
            }
            """);
        AssertInitializationCompilationSucceeds(provider, diagnostics);
        Assert.AreEqual("1", ManifestValue(provider, "Ankus.NativeCallbacks"));
        string registration = Assert.ContainsSingle(provider.GetTypeByMetadataName("Ankus.Generated.ExtensionDispatchers")!.GetMembers()).Name;
        using var providerBytes = new MemoryStream();
        Assert.IsTrue(provider.Emit(providerBytes, cancellationToken: context.CancellationToken).Success);
        MetadataReference reference = MetadataReference.CreateFromImage(providerBytes.ToArray());
        if (intermediary)
        {
            Compilation facade = GenerateWithReference("CallbackFacade", """
                public static class CallbackFacade
                {
                    public static nint Address() => CallbackProvider.Target.Address;
                }
                """, reference);
            Assert.AreEqual("1", ManifestValue(facade, "Ankus.NativeCallbacks"));
            using var facadeBytes = new MemoryStream();
            Assert.IsTrue(facade.Emit(facadeBytes, cancellationToken: context.CancellationToken).Success);
            reference = MetadataReference.CreateFromImage(facadeBytes.ToArray());
        }

        Compilation consumer = GenerateWithReference("CallbackConsumer",
            "public static class Consumer { [Ankus.PgFunction] public static int Answer() => 42; }", reference);
        Assert.AreEqual("1", ManifestValue(consumer, "Ankus.NativeCallbacks"));
        string native = ManifestValue(consumer, "Ankus.NativeSource");
        Assert.Contains("ankus_dispatch_native_callback(", native);
        Assert.Contains("ankus_fork_host_enter();", native);
        Assert.Contains("RhEnableForkSupport();", native);
        Assert.DoesNotContain(registration, native);
        Assert.DoesNotContain("ankus_ensure_module_loaded", native);
        Assert.Contains("_PG_init", ManifestValue(consumer, "Ankus.Exports"));
        Assert.DoesNotContain("NativeCallbackProperties.g.cs", consumer.SyntaxTrees.Select(static tree => Path.GetFileName(tree.FilePath)));
    }

    /// <summary>
    /// Compiles an extension with a real emitted metadata reference and verifies its complete generated C#.
    /// </summary>
    private Compilation GenerateWithReference(string name, string source, MetadataReference reference)
    {
        CSharpCompilation input = CSharpCompilation.Create(name,
            [CSharpSyntaxTree.ParseText(source, cancellationToken: context.CancellationToken)], s_references.Add(reference),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, nullableContextOptions: NullableContextOptions.Enable));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new PgFunctionGenerator().AsSourceGenerator());
        driver.RunGeneratorsAndUpdateCompilation(input, out Compilation output, out ImmutableArray<Diagnostic> diagnostics, context.CancellationToken);
        AssertInitializationCompilationSucceeds(output, diagnostics);
        return output;
    }
}
