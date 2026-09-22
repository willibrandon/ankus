using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Ankus.Generators.Tests;

/// <summary>
/// Verifies generated code compiles and unsupported extension contracts fail during compilation.
/// </summary>
/// <param name="context">The per-test context.</param>
[TestClass]
public sealed class PgFunctionGeneratorTests(TestContext context)
{
    private static readonly ImmutableArray<MetadataReference> s_references = GetReferences();

    /// <summary>
    /// Verifies ordinary functions, aliases, escaped identifiers, and zero-argument functions produce compilable dispatchers.
    /// </summary>
    /// <param name="method">The attributed method declaration.</param>
    /// <param name="sqlName">The expected SQL name suffix on the assembly-specific unmanaged callback.</param>
    [TestMethod]
    [DataRow("[Ankus.PgFunction] public static int Add(int a, int b) => a + b;", "add")]
    [DataRow("[Ankus.PgFunction] internal static int HTTPCode() => 200;", "http_code")]
    [DataRow("[Ankus.PgFunction(Name = \"answer\")] public static int @return() => 42;", "answer")]
    public void SupportedFunctionsCompile(string method, string sqlName)
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("public static class Functions { " + method + " }");

        Assert.IsEmpty(diagnostics);
        Assert.IsEmpty(compilation.GetDiagnostics(context.CancellationToken)
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        using var assembly = new MemoryStream();
        Assert.IsTrue(compilation.Emit(assembly, cancellationToken: context.CancellationToken).Success);
        INamedTypeSymbol? dispatchers = compilation.GetTypeByMetadataName("Ankus.Generated.ExtensionDispatchers");
        Assert.IsNotNull(dispatchers);
        IMethodSymbol generated = Assert.IsInstanceOfType<IMethodSymbol>(Assert.ContainsSingle(dispatchers.GetMembers()));
        AttributeData entryPoint = Assert.ContainsSingle(generated.GetAttributes());
        string export = Assert.IsInstanceOfType<string>(
            entryPoint.NamedArguments.Single(static argument => argument.Key == "EntryPoint").Value.Value);
        Assert.StartsWith("ankus_managed_", export);
        Assert.EndsWith("_" + sqlName, export);
    }

    /// <summary>
    /// Verifies unsupported CLR signatures produce an actionable diagnostic instead of unsafe or uncompilable wrappers.
    /// </summary>
    /// <param name="method">The unsupported attributed method declaration.</param>
    [TestMethod]
    [DataRow("public int Instance() => 1;")]
    [DataRow("private static int Hidden() => 1;")]
    [DataRow("public static long WrongResult() => 1;")]
    [DataRow("public static int WrongArgument(string value) => 1;")]
    [DataRow("public static int ByReference(ref int value) => value;")]
    [DataRow("public static int Generic<T>() => 1;")]
    public void UnsupportedSignaturesAreRejected(string method)
    {
        (_, ImmutableArray<Diagnostic> diagnostics) = Generate("public class Functions { [Ankus.PgFunction] " + method + " }");

        Diagnostic error = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual("ANKUS001", error.Id);
        Assert.AreEqual(DiagnosticSeverity.Error, error.Severity);
        Assert.IsTrue(error.Location.IsInSource);
    }

    /// <summary>
    /// Verifies invalid SQL names cannot inject SQL, C identifiers, or unmanaged entry point names.
    /// </summary>
    /// <param name="name">An invalid SQL name.</param>
    [TestMethod]
    [DataRow("")]
    [DataRow("9starts_with_digit")]
    [DataRow("has-dash")]
    [DataRow("has'quote")]
    [DataRow("UPPERCASE")]
    public void InvalidSqlNamesAreRejected(string name)
    {
        string source = "public static class Functions { [Ankus.PgFunction(Name = " +
            SymbolDisplay.FormatLiteral(name, quote: true) + ")] public static int Value() => 1; }";
        (_, ImmutableArray<Diagnostic> diagnostics) = Generate(source);

        Assert.AreEqual("ANKUS002", Assert.ContainsSingle(diagnostics).Id);
    }

    /// <summary>
    /// Verifies name collisions across distinct declaring types are rejected before linker symbol collisions occur.
    /// </summary>
    [TestMethod]
    public void DuplicateSqlNamesAreRejected()
    {
        (_, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            public static class First { [Ankus.PgFunction] public static int Add() => 1; }
            public static class Second { [Ankus.PgFunction] public static int Add() => 2; }
            """);

        Assert.AreEqual("ANKUS002", Assert.ContainsSingle(diagnostics).Id);
    }

    /// <summary>
    /// Verifies generics and inaccessible containing types do not produce invalid method references.
    /// </summary>
    /// <param name="source">A function inside an unsupported containing type.</param>
    [TestMethod]
    [DataRow("public class Container<T> { [Ankus.PgFunction] public static int Add() => 1; }")]
    [DataRow("file class Container { [Ankus.PgFunction] public static int Add() => 1; }")]
    [DataRow("public class Outer { private class Inner { [Ankus.PgFunction] public static int Add() => 1; } }")]
    public void UnsupportedContainersAreRejected(string source)
    {
        (_, ImmutableArray<Diagnostic> diagnostics) = Generate(source);

        Assert.AreEqual("ANKUS001", Assert.ContainsSingle(diagnostics).Id);
    }

    private (Compilation Compilation, ImmutableArray<Diagnostic> Diagnostics) Generate(string source)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            "GeneratorTest",
            [CSharpSyntaxTree.ParseText(source, cancellationToken: context.CancellationToken)],
            s_references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new PgFunctionGenerator().AsSourceGenerator());
        driver.RunGeneratorsAndUpdateCompilation(
            compilation, out Compilation output, out ImmutableArray<Diagnostic> diagnostics, context.CancellationToken);
        return (output, diagnostics);
    }

    private static ImmutableArray<MetadataReference> GetReferences()
    {
        string assemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
            ?? throw new InvalidOperationException("The test runtime did not provide its assembly reference list.");
        return [.. assemblies.Split(Path.PathSeparator).Append(typeof(PgFunctionAttribute).Assembly.Location)
            .Distinct(StringComparer.Ordinal).Select(static path => MetadataReference.CreateFromFile(path))];
    }
}
