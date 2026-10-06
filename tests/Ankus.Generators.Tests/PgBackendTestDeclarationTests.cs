using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Ankus.Generators.Tests;

public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Managed test discovery is available while ordinary native publications omit test functions and SQL.
    /// </summary>
    [TestMethod]
    public void GeneratesBackendCatalogWithoutProductionExports()
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            namespace Extension;
            public static partial class Checks
            {
                [Ankus.PgTest]
                public static void WritesRows() { }
            }
            """);
        Assert.IsEmpty(diagnostics);
        Assert.IsEmpty(compilation.GetDiagnostics(context.CancellationToken).Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        INamedTypeSymbol checks = compilation.GetTypeByMetadataName("Extension.Checks")!;
        INamedTypeSymbol catalogType = Assert.ContainsSingle(checks.GetTypeMembers("PostgresTests"));
        IPropertySymbol catalog = Assert.IsInstanceOfType<IPropertySymbol>(Assert.ContainsSingle(catalogType.GetMembers("Cases")));
        Assert.IsTrue(catalog.IsStatic);
        Assert.IsNull(catalog.SetMethod);
        Assert.AreEqual("System.Collections.Generic.IReadOnlyList<Ankus.PgTestCase>", catalog.Type.ToDisplayString());
        Assert.AreEqual("-- No installable objects declared.\n", InstallationBody(compilation));
        Assert.DoesNotContain("ankus_test_", ManifestValue(compilation, "Ankus.Exports"));
        Assert.DoesNotContain("ankus_test_", ManifestValue(compilation, "Ankus.NativeSource"));
    }

    /// <summary>
    /// Native tests require an explicit enabled build property and retain ordinary exports in either mode.
    /// </summary>
    /// <param name="enabled">The compiler-visible build switch.</param>
    /// <param name="expected">Whether native test functions must be emitted.</param>
    [TestMethod]
    [DataRow("true", true)]
    [DataRow("TrUe", true)]
    [DataRow("false", false)]
    [DataRow("", false)]
    public void IncludesBackendExportsOnlyWhenRequested(string enabled, bool expected)
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            public static partial class Checks
            {
                [Ankus.PgTest]
                public static void Native(Ankus.PgFunctionContext context) { }
                [Ankus.PgFunction]
                public static int Ordinary() => 42;
            }
            """, options: new BackendOptions(enabled));
        Assert.IsEmpty(diagnostics);
        Assert.IsEmpty(compilation.GetDiagnostics(context.CancellationToken).Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        string sql = InstallationBody(compilation);
        Assert.Contains("CREATE FUNCTION \"ordinary\"()", sql);
        Assert.AreEqual(expected, sql.Contains("CREATE FUNCTION \"ankus_test_", StringComparison.Ordinal));
        Assert.AreEqual(expected, ManifestValue(compilation, "Ankus.Exports").Contains("ankus_test_", StringComparison.Ordinal));
    }

    /// <summary>
    /// Discovery preserves exact metadata and stable SQL names without initializing backend-only author types.
    /// </summary>
    [TestMethod]
    public void PreservesBackendCaseMetadataAndStableNames()
    {
        const string Source = """
            namespace @event;
            public partial class Outer
            {
                static Outer() => throw new System.InvalidOperationException("Host initialized outer type.");
                [Ankus.PgSchema("Case Schema")]
                public partial record class @class
                {
                    static @class() => throw new System.InvalidOperationException("Host initialized test type.");
                    [Ankus.PgTest(ExpectedError = "café\n\"expected\"", IgnoreReason = "Explicit author choice")]
                    public static void ZetaWithANameLongerThanPostgresAllowsForOneSqlIdentifierButStillValidInCSharp() { }
                    [Ankus.PgTest]
                    internal static void Alpha() { }
                }
            }
            """;
        PgTestCase[] tests = ReadBackendCatalog(Source, "event.Outer+class+PostgresTests");
        Assert.HasCount(2, tests);
        Assert.EndsWith(".Alpha()", tests[0].Name);
        Assert.IsNull(tests[0].ExpectedError);
        Assert.IsNull(tests[0].IgnoreReason);
        Assert.AreEqual("Case Schema", tests[1].Schema);
        Assert.AreEqual("café\n\"expected\"", tests[1].ExpectedError);
        Assert.AreEqual("Explicit author choice", tests[1].IgnoreReason);
        Assert.IsGreaterThan(63, tests[1].Name.Length);
        Assert.MatchesRegex("^ankus_test_[0-9a-f]{32}$", tests[1].FunctionName);
        Assert.AreNotEqual(tests[0].FunctionName, tests[1].FunctionName);
        PgTestCase[] reordered = ReadBackendCatalog(Source.Replace("public partial class Outer", "// unrelated source change\npublic partial class Outer", StringComparison.Ordinal),
            "event.Outer+class+PostgresTests");
        Assert.AreSequenceEqual(tests.Select(static test => test.FunctionName), reordered.Select(static test => test.FunctionName));
    }

    private PgTestCase[] ReadBackendCatalog(string source, string typeName)
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate(source);
        Assert.IsEmpty(diagnostics);
        return ReadBackendCatalog(compilation, typeName);
    }

    /// <summary>
    /// Executes a generated discovery catalog without invoking any native backend callback.
    /// </summary>
    /// <param name="compilation">The actual generated managed compilation.</param>
    /// <param name="typeName">The fully qualified discovery owner.</param>
    /// <returns>The immutable discovered cases.</returns>
    private PgTestCase[] ReadBackendCatalog(Compilation compilation, string typeName)
    {
        using var stream = new MemoryStream();
        Microsoft.CodeAnalysis.Emit.EmitResult emitted = compilation.Emit(stream, cancellationToken: context.CancellationToken);
        Assert.IsTrue(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        stream.Position = 0;
        var load = new AssemblyLoadContext("BackendCatalogProbe", isCollectible: true);
        try
        {
            Assembly assembly = load.LoadFromStream(stream);
            Type catalog = assembly.GetType(typeName, throwOnError: true)!;
            IReadOnlyList<PgTestCase> cases = Assert.IsInstanceOfType<IReadOnlyList<PgTestCase>>(catalog.GetProperty("Cases")!.GetValue(null));
            IList<PgTestCase> collection = Assert.IsInstanceOfType<IList<PgTestCase>>(cases);
            Assert.IsTrue(collection.IsReadOnly);
            Assert.ThrowsExactly<NotSupportedException>(() => collection.Clear());
            return [.. cases];
        }
        finally
        {
            load.Unload();
        }
    }

    /// <summary>
    /// Invalid test items retain their own diagnostic without dropping valid catalogs, ordinary exports or native artifacts.
    /// </summary>
    /// <param name="enabled">Whether valid backend tests are included in the native publication.</param>
    /// <param name="invalid">The invalid declaration placed before its valid sibling.</param>
    /// <param name="expected">The dedicated declaration error.</param>
    /// <param name="highlight">The authored cause requiring correction.</param>
    [TestMethod]
    [DataRow("true", "[Ankus.PgTest] public static int AInvalid() => 1;", "ANKUS295", "int")]
    [DataRow("false", "[Ankus.PgTest] public static int AInvalid() => 1;", "ANKUS295", "int")]
    [DataRow("true", "[Ankus.PgTest] public static void AInvalid(int value) { }", "ANKUS297", "value")]
    [DataRow("false", "[Ankus.PgTest] public static void AInvalid(int value) { }", "ANKUS297", "value")]
    [DataRow("true", "[Ankus.PgTest, Ankus.PgFunction] public static void AInvalid() { }", "ANKUS299", "Ankus.PgFunction")]
    [DataRow("false", "[Ankus.PgTest, Ankus.PgFunction] public static void AInvalid() { }", "ANKUS299", "Ankus.PgFunction")]
    public void InvalidBackendTestDoesNotDropValidDeclarations(string enabled, string invalid, string expected, string highlight)
    {
        string source = """
            namespace Extension;
            public static partial class Checks
            {
                INVALID
                [Ankus.PgTest]
                public static void ZValid() { }
                [Ankus.PgFunction]
                public static int Ordinary() => 42;
            }
            public static partial class Other
            {
                [Ankus.PgTest]
                public static void Independent() { }
            }
            """.Replace("INVALID", invalid, StringComparison.Ordinal);
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate(source, options: new BackendOptions(enabled));
        Diagnostic error = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual(expected, error.Id);
        Assert.IsTrue(error.Location.IsInSource);
        Assert.AreEqual(highlight, error.Location.SourceTree!.GetText(context.CancellationToken).ToString(error.Location.SourceSpan));
        Assert.IsEmpty(compilation.GetDiagnostics(context.CancellationToken)
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        PgTestCase sibling = Assert.ContainsSingle(ReadBackendCatalog(compilation, "Extension.Checks+PostgresTests"));
        PgTestCase independent = Assert.ContainsSingle(ReadBackendCatalog(compilation, "Extension.Other+PostgresTests"));
        Assert.EndsWith(".ZValid()", sibling.Name);
        Assert.EndsWith(".Independent()", independent.Name);
        IMethodSymbol[] callbacks = [.. compilation.GetTypeByMetadataName("Ankus.Generated.ExtensionDispatchers")!.GetMembers().OfType<IMethodSymbol>()];
        Assert.HasCount(enabled == "true" ? 3 : 1, callbacks);
        string sql = InstallationBody(compilation);
        Assert.Contains("CREATE FUNCTION \"ordinary\"()", sql);
        Assert.AreEqual(enabled == "true", sql.Contains(sibling.FunctionName, StringComparison.Ordinal));
        Assert.AreEqual(enabled == "true", sql.Contains(independent.FunctionName, StringComparison.Ordinal));
        Assert.DoesNotContain("a_invalid", sql);
        Assert.IsNotEmpty(ManifestValue(compilation, "Ankus.NativeSource"));
    }

    /// <summary>
    /// Invalid test declarations produce one actionable diagnostic before exposing a partial catalog.
    /// </summary>
    /// <param name="source">An invalid declaration.</param>
    /// <param name="expected">The dedicated declaration error.</param>
    [TestMethod]
    [DataRow("public partial class Checks { [Ankus.PgTest] public void Test() { } }", "ANKUS291")]
    [DataRow("public static partial class Checks { [Ankus.PgTest] public static int Test() => 1; }", "ANKUS295")]
    [DataRow("public static partial class Checks { [Ankus.PgTest] public static void Test(int value) { } }", "ANKUS297")]
    [DataRow("public static class Checks { [Ankus.PgTest] public static void Test() { } }", "ANKUS304")]
    [DataRow("public static partial class Checks<T> { [Ankus.PgTest] public static void Test() { } }", "ANKUS301")]
    [DataRow("public static partial class Checks { [Ankus.PgTest, Ankus.PgFunction] public static void Test() { } }", "ANKUS299")]
    [DataRow("public static partial class Checks { public static int PostgresTests => 1; [Ankus.PgTest] public static void Test() { } }", "ANKUS306")]
    [DataRow("public static partial class Checks { [Ankus.PgTest(IgnoreReason = \"\")] public static void Test() { } }", "ANKUS309")]
    [DataRow("public static partial class Checks { [Ankus.PgTest(ExpectedError = \"bad\\0text\")] public static void Test() { } }", "ANKUS307")]
    [DataRow("public static partial class Checks { [Ankus.PgTest] private static void Test() { } }", "ANKUS296")]
    [DataRow("public static partial class Checks { [Ankus.PgTest] public static void Test<T>() { } }", "ANKUS293")]
    [DataRow("public static partial class Checks { [Ankus.PgTest] public static async void Test() { await System.Threading.Tasks.Task.Yield(); } }", "ANKUS292")]
    [DataRow("public static partial class Checks { [Ankus.PgTest] public static void Test(ref Ankus.PgFunctionContext context) { } }", "ANKUS298")]
    [DataRow("public class Outer { public static partial class Checks { [Ankus.PgTest] public static void Test() { } } }", "ANKUS304")]
    [DataRow("public partial class Outer { private static partial class Checks { [Ankus.PgTest] public static void Test() { } } }", "ANKUS303")]
    [DataRow("public class Base { public static int PostgresTests => 1; } public partial class Checks : Base { [Ankus.PgTest] public static void Test() { } }", "ANKUS306")]
    [DataRow("public static partial class Checks { [Ankus.PgTest(IgnoreReason = \" \")] public static void Test() { } }", "ANKUS309")]
    [DataRow("public static partial class Checks { [Ankus.PgTest(ExpectedError = \"\\ud800\")] public static void Test() { } }", "ANKUS307")]
    [DataRow("public static partial class PostgresTests { [Ankus.PgTest] public static void Test() { } }", "ANKUS305")]
    public void RejectsInvalidBackendTestDeclarations(string source, string expected)
    {
        (_, ImmutableArray<Diagnostic> diagnostics) = Generate(source);
        Assert.AreEqual(expected, Assert.ContainsSingle(diagnostics).Id);
    }

    /// <summary>
    /// Supplies only the explicit native-test build switch to the generator under test.
    /// </summary>
    private sealed class BackendOptions(string enabled) : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new BackendGlobalOptions(enabled);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => GlobalOptions;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => GlobalOptions;
    }

    /// <summary>
    /// Exposes an independent compiler-option value without importing project state.
    /// </summary>
    private sealed class BackendGlobalOptions(string enabled) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string value)
        {
            value = enabled;
            return key == "build_property.AnkusIncludeTests";
        }
    }
}
