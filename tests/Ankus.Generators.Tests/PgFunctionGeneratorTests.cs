using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Ankus.Generators.Tests;

/// <summary>
/// Verifies generated code compiles and unsupported extension contracts fail during compilation.
/// </summary>
/// <param name="context">The per-test context.</param>
[TestClass]
public sealed partial class PgFunctionGeneratorTests(TestContext context)
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
    [DataRow("[Ankus.PgFunction] public static bool Echo(bool value) => value;", "echo")]
    [DataRow("[Ankus.PgFunction] public static sbyte Echo(sbyte value) => value;", "echo")]
    [DataRow("[Ankus.PgFunction] public static short Echo(short value) => value;", "echo")]
    [DataRow("[Ankus.PgFunction] public static long Echo(long value) => value;", "echo")]
    [DataRow("[Ankus.PgFunction] public static uint Echo(uint value) => value;", "echo")]
    [DataRow("[Ankus.PgFunction] public static float Echo(float value) => value;", "echo")]
    [DataRow("[Ankus.PgFunction] public static double Echo(double value) => value;", "echo")]
    [DataRow("[Ankus.PgFunction] public static int? Echo(int? value) => value;", "echo")]
    [DataRow("[Ankus.PgFunction] public static bool? Echo(bool? value) => value;", "echo")]
    [DataRow("[Ankus.PgFunction] public static sbyte? Echo(sbyte? value) => value;", "echo")]
    [DataRow("[Ankus.PgFunction] public static short? Echo(short? value) => value;", "echo")]
    [DataRow("[Ankus.PgFunction] public static long? Echo(long? value) => value;", "echo")]
    [DataRow("[Ankus.PgFunction] public static uint? Echo(uint? value) => value;", "echo")]
    [DataRow("[Ankus.PgFunction] public static float? Echo(float? value) => value;", "echo")]
    [DataRow("[Ankus.PgFunction] public static double? Echo(double? value) => value;", "echo")]
    [DataRow("[Ankus.PgFunction] public static string Echo(string value) => value;", "echo")]
    [DataRow("[Ankus.PgFunction] public static string? Echo(string? value) => value;", "echo")]
    [DataRow("[Ankus.PgFunction] public static byte[] Echo(byte[] value) => value;", "echo")]
    [DataRow("[Ankus.PgFunction] public static byte[]? Echo(byte[]? value) => value;", "echo")]
    [DataRow("[Ankus.PgFunction] public static int[] Echo(int[] value) => value;", "echo")]
    [DataRow("[Ankus.PgFunction] public static int[] Echo(params int[] value) => value;", "echo")]
    [DataRow("[Ankus.PgFunction] public static string?[]? Echo(params string?[]? value) => value;", "echo")]
    [DataRow("[Ankus.PgFunction] public static int?[]? Echo(int?[]? value) => value;", "echo")]
    [DataRow("[Ankus.PgFunction] public static string?[]? Echo(string?[]? value) => value;", "echo")]
    [DataRow("[Ankus.PgFunction] public static byte[]?[]? Echo(byte[]?[]? value) => value;", "echo")]
    [DataRow("[Ankus.PgFunction] public static Ankus.PgArray<int?>? Echo(Ankus.PgArray<int?>? value) => value;", "echo")]
    [DataRow("[Ankus.PgFunction] public static Ankus.PgArray<string?> Echo(Ankus.PgArray<string?> value) => value;", "echo")]
    [DataRow("[Ankus.PgFunction] public static decimal?[] Echo(decimal?[] value) => value;", "echo")]
    [DataRow("[Ankus.PgFunction] public static System.DateOnly?[] Echo(System.DateOnly?[] value) => value;", "echo")]
    [DataRow("[Ankus.PgFunction] public static System.Guid Echo(System.Guid value) => value;", "echo")]
    [DataRow("[Ankus.PgFunction] public static System.Guid? Echo(System.Guid? value) => value;", "echo")]
    [DataRow("[Ankus.PgFunction] public static Ankus.PgJson Echo(Ankus.PgJson value) => value;", "echo")]
    [DataRow("[Ankus.PgFunction] public static Ankus.PgJson? Echo(Ankus.PgJson? value) => value;", "echo")]
    [DataRow("[Ankus.PgFunction] public static Ankus.PgJsonb Echo(Ankus.PgJsonb value) => value;", "echo")]
    [DataRow("[Ankus.PgFunction] public static Ankus.PgJsonb? Echo(Ankus.PgJsonb? value) => value;", "echo")]
    [DataRow("[Ankus.PgFunction] public static Ankus.PgNumeric Echo(Ankus.PgNumeric value) => value;", "echo")]
    [DataRow("[Ankus.PgFunction] public static Ankus.PgNumeric? Echo(Ankus.PgNumeric? value) => value;", "echo")]
    [DataRow("[Ankus.PgFunction] public static decimal Echo(decimal value) => value;", "echo")]
    [DataRow("[Ankus.PgFunction] public static decimal? Echo(decimal? value) => value;", "echo")]
    [DataRow("[Ankus.PgFunction] [return: Ankus.PgNumericPrecision(5, 2)] public static Ankus.PgNumeric Echo([Ankus.PgNumericPrecision(6, 3)] Ankus.PgNumeric value) => value;", "echo")]
    [DataRow("[Ankus.PgFunction] [return: Ankus.PgNumericPrecision(5)] public static decimal? Echo([Ankus.PgNumericPrecision(8, 4)] decimal? value) => value;", "echo")]
    [DataRow("[Ankus.PgFunction] [return: Ankus.PgNumericPrecision(1000, -1000)] public static Ankus.PgNumeric? Echo([Ankus.PgNumericPrecision(1, 1000)] Ankus.PgNumeric? value) => value;", "echo")]
    [DataRow("[Ankus.PgFunction] public static Ankus.PgDate Echo(Ankus.PgDate value) => value;", "echo")]
    [DataRow("[Ankus.PgFunction] public static Ankus.PgTime Echo(Ankus.PgTime value) => value;", "echo")]
    [DataRow("[Ankus.PgFunction] public static Ankus.PgTimeTz Echo(Ankus.PgTimeTz value) => value;", "echo")]
    [DataRow("[Ankus.PgFunction] public static Ankus.PgTimestamp Echo(Ankus.PgTimestamp value) => value;", "echo")]
    [DataRow("[Ankus.PgFunction] public static Ankus.PgTimestampTz Echo(Ankus.PgTimestampTz value) => value;", "echo")]
    [DataRow("[Ankus.PgFunction] public static Ankus.PgInterval Echo(Ankus.PgInterval value) => value;", "echo")]
    [DataRow("[Ankus.PgFunction] public static System.DateOnly Echo(System.DateOnly value) => value;", "echo")]
    [DataRow("[Ankus.PgFunction] public static System.TimeOnly Echo(System.TimeOnly value) => value;", "echo")]
    [DataRow("[Ankus.PgFunction] public static System.DateTime Echo(System.DateTime value) => value;", "echo")]
    [DataRow("[Ankus.PgFunction] public static System.DateTimeOffset Echo(System.DateTimeOffset value) => value;", "echo")]
    [DataRow("[Ankus.PgFunction] public static System.TimeSpan Echo(System.TimeSpan value) => value;", "echo")]
    [DataRow("[Ankus.PgFunction] public static void Nothing() { }", "nothing")]
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
    [DataRow("public static System.Uri WrongResult() => new(\"https://example.com\");")]
    [DataRow("public static int WrongArgument(System.Uri value) => 1;")]
    [DataRow("public static int[][] Nested(int[][] value) => value;")]
    [DataRow("public static int[,] Rectangular(int[,] value) => value;")]
    [DataRow("public static Ankus.PgArray<Ankus.PgArray<int>> Nested(Ankus.PgArray<Ankus.PgArray<int>> value) => value;")]
    [DataRow("public static Ankus.PgArray<byte> ByteElements(Ankus.PgArray<byte> value) => value;")]
    [DataRow("public static byte[] ScalarParams(params byte[] value) => value;")]
    [DataRow("public static int SpanParams(params System.ReadOnlySpan<int> value) => value.Length;")]
    [DataRow("public static int ByReference(ref int value) => value;")]
    [DataRow("public static int Generic<T>() => 1;")]
    [DataRow("public static async void Unobserved() { await System.Threading.Tasks.Task.Yield(); }")]
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
    /// Verifies SQL overloads have distinct native dispatchers while nullable and non-nullable identical SQL signatures collide.
    /// </summary>
    /// <param name="secondType">The second overload's parameter and result type.</param>
    /// <param name="expectedDiagnosticCount">The number of duplicate SQL signature diagnostics.</param>
    [TestMethod]
    [DataRow("long", 0)]
    [DataRow("int?", 1)]
    public void SqlSignaturesDetermineOverloadUniqueness(string secondType, int expectedDiagnosticCount)
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate($$"""
            public static class Functions
            {
                [Ankus.PgFunction] public static int Echo(int value) => value;
                [Ankus.PgFunction] public static {{secondType}} Echo({{secondType}} value) => value;
            }
            """);

        Assert.HasCount(expectedDiagnosticCount, diagnostics);
        Assert.IsEmpty(compilation.GetDiagnostics(context.CancellationToken)
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
    }

    /// <summary>
    /// Verifies .NET aliases cannot silently replace full-range overloads with the same SQL signature.
    /// </summary>
    /// <param name="clrType">The ordinary .NET type.</param>
    /// <param name="pgType">The full-range PostgreSQL type.</param>
    [TestMethod]
    [DataRow("System.DateOnly", "Ankus.PgDate")]
    [DataRow("System.TimeOnly", "Ankus.PgTime")]
    [DataRow("System.DateTime", "Ankus.PgTimestamp")]
    [DataRow("System.DateTimeOffset", "Ankus.PgTimestampTz")]
    [DataRow("System.TimeSpan", "Ankus.PgInterval")]
    [DataRow("decimal", "Ankus.PgNumeric")]
    [DataRow("int[]", "Ankus.PgArray<int?>")]
    [DataRow("byte[][]", "Ankus.PgArray<byte[]>")]
    [DataRow("System.DateOnly[]", "Ankus.PgArray<Ankus.PgDate>")]
    [DataRow("decimal[]", "Ankus.PgArray<Ankus.PgNumeric>")]
    public void ClrAliasesShareSqlSignatures(string clrType, string pgType)
    {
        (_, ImmutableArray<Diagnostic> diagnostics) = Generate($$"""
            public static class Functions
            {
                [Ankus.PgFunction] public static {{clrType}} Echo({{clrType}} value) => value;
                [Ankus.PgFunction] public static {{pgType}} Echo({{pgType}} value) => value;
            }
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

    /// <summary>
    /// Rejects misplaced or out-of-range numeric constraints before any native compilation.
    /// </summary>
    /// <param name="method">The invalid constrained method.</param>
    [TestMethod]
    [DataRow("public static int Echo([Ankus.PgNumericPrecision(5, 2)] int value) => value;")]
    [DataRow("[return: Ankus.PgNumericPrecision(5, 2)] public static string Echo(string value) => value;")]
    [DataRow("[return: Ankus.PgNumericPrecision(0)] public static decimal Echo(decimal value) => value;")]
    [DataRow("[return: Ankus.PgNumericPrecision(1001)] public static decimal Echo(decimal value) => value;")]
    [DataRow("public static decimal Echo([Ankus.PgNumericPrecision(5, -1001)] decimal value) => value;")]
    [DataRow("public static decimal Echo([Ankus.PgNumericPrecision(5, 1001)] decimal value) => value;")]
    public void InvalidNumericConstraintsAreRejected(string method)
    {
        (_, ImmutableArray<Diagnostic> diagnostics) = Generate("public static class Functions { [Ankus.PgFunction] " + method + " }");
        Diagnostic diagnostic = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual("ANKUS003", diagnostic.Id);
        Assert.AreEqual(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.StartsWith("Ankus.PgNumericPrecision", diagnostic.Location.SourceTree!.GetText(context.CancellationToken)
            .ToString(diagnostic.Location.SourceSpan));
    }

    /// <summary>
    /// Type modifiers do not distinguish SQL overloads, even when CLR types and constraints differ.
    /// </summary>
    [TestMethod]
    public void NumericConstraintsDoNotCreateSqlOverloads()
    {
        (_, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            public static class Functions
            {
                [Ankus.PgFunction] public static decimal Echo([Ankus.PgNumericPrecision(5, 2)] decimal value) => value;
                [Ankus.PgFunction] public static Ankus.PgNumeric Echo([Ankus.PgNumericPrecision(6, 3)] Ankus.PgNumeric value) => value;
            }
            """);
        Assert.AreEqual("ANKUS002", Assert.ContainsSingle(diagnostics).Id);
    }

    private (Compilation Compilation, ImmutableArray<Diagnostic> Diagnostics) Generate(string source)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            "GeneratorTest",
            [CSharpSyntaxTree.ParseText(source, cancellationToken: context.CancellationToken)],
            s_references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                allowUnsafe: true, nullableContextOptions: NullableContextOptions.Enable));
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
