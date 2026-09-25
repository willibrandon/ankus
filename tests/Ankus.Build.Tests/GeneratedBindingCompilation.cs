using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace Ankus.Build.Tests;

/// <summary>
/// Compiles and executes generated native binding declarations inside a collectible test assembly.
/// </summary>
internal static class GeneratedBindingCompilation
{
    private static readonly ImmutableArray<MetadataReference> s_references = GetReferences();

    /// <summary>
    /// Executes a test harness compiled together with the emitted declarations, requiring clean compiler diagnostics.
    /// </summary>
    /// <param name="binding">The complete generated declarations.</param>
    /// <param name="harness">A BindingAssertions.Run method returning observed values.</param>
    /// <param name="cancellationToken">Cancels parsing and compilation.</param>
    /// <returns>The actual values read from the compiled unmanaged representations.</returns>
    internal static long[] Run(NativeBindingSource binding, string harness, CancellationToken cancellationToken)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(binding.AssemblyName,
            [CSharpSyntaxTree.ParseText(binding.Source, cancellationToken: cancellationToken),
                CSharpSyntaxTree.ParseText(harness, cancellationToken: cancellationToken)],
            s_references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                allowUnsafe: true, nullableContextOptions: NullableContextOptions.Enable,
                generalDiagnosticOption: ReportDiagnostic.Error));
        using var output = new MemoryStream();
        EmitResult result = compilation.Emit(output, cancellationToken: cancellationToken);
        Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        output.Position = 0;
        var loader = new AssemblyLoadContext(binding.AssemblyName, isCollectible: true);
        try
        {
            Assembly assembly = loader.LoadFromStream(output);
            Type? assertions = assembly.GetType("BindingAssertions");
            Assert.IsNotNull(assertions);
            MethodInfo? method = assertions.GetMethod("Run", BindingFlags.Static | BindingFlags.Public);
            Assert.IsNotNull(method);
            return method.CreateDelegate<Func<long[]>>()();
        }
        finally
        {
            loader.Unload();
        }
    }

    private static ImmutableArray<MetadataReference> GetReferences()
    {
        string assemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
            ?? throw new InvalidOperationException("The test runtime did not provide its assembly reference list.");
        return [.. assemblies.Split(Path.PathSeparator).Append(typeof(IPgNativeType).Assembly.Location)
            .Distinct(StringComparer.Ordinal).Select(static path => MetadataReference.CreateFromFile(path))];
    }
}
