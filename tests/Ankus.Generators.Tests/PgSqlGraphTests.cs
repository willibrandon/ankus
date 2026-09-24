using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Ankus.Generators.Tests;

public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Resolves schema/function dependencies, before constraints, and first/last SQL independently of declaration order.
    /// </summary>
    [TestMethod]
    public void SqlGraphOrdersAllDeclarationKinds()
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            [assembly: Ankus.PgSql("last", "SELECT 'last';", Order = Ankus.PgSqlOrder.Finalize)]
            [assembly: Ankus.PgSql("view", "CREATE VIEW s.v AS SELECT s.f();", Requires = new[] { "function" })]
            [assembly: Ankus.PgSql("table", "CREATE TABLE s.t(n int);", Requires = new[] { "schema" }, Before = new[] { "function" })]
            [assembly: Ankus.PgSql("first", "SELECT 'first';", Order = Ankus.PgSqlOrder.Bootstrap)]
            [Ankus.PgSchema("s", Id = "schema")]
            public static class Functions
            {
                [Ankus.PgFunction(Id = "function", Requires = new[] { "table" })] public static int F() => 1;
            }
            """);
        Assert.IsEmpty(diagnostics);
        Assert.IsEmpty(compilation.GetDiagnostics(context.CancellationToken).Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        string sql = ManifestValue(compilation, "Ankus.Sql");
        Assert.StartsWith("SELECT 'first';\nCREATE SCHEMA IF NOT EXISTS \"s\";\nCREATE TABLE s.t(n int);\nCREATE FUNCTION \"s\".\"f\"()", sql);
        Assert.EndsWith("CREATE VIEW s.v AS SELECT s.f();\nSELECT 'last';\n", sql);
        Assert.AreEqual("false", ManifestValue(compilation, "Ankus.Relocatable"));
    }

    /// <summary>
    /// Independent blocks have stable output even when source declarations change order; SQL is preserved verbatim.
    /// </summary>
    [TestMethod]
    public void CustomSqlIsDeterministicAndPreservesStatementText()
    {
        const string first = """[assembly: Ankus.PgSql("a", "SELECT $$quotes' ; \\ café$$; -- tail", Relocatable = true)]""";
        const string second = """[assembly: Ankus.PgSql("b", "SELECT 2;\n", Relocatable = true)]""";
        (Compilation left, ImmutableArray<Diagnostic> diagnostics) = Generate(first + second);
        (Compilation right, ImmutableArray<Diagnostic> reordered) = Generate(second + first);
        Assert.IsEmpty(diagnostics);
        Assert.IsEmpty(reordered);
        Assert.AreEqual("SELECT $$quotes' ; \\ café$$; -- tail\nSELECT 2;\n", ManifestValue(left, "Ankus.Sql"));
        Assert.AreEqual(ManifestValue(left, "Ankus.Sql"), ManifestValue(right, "Ankus.Sql"));
        Assert.AreEqual("true", ManifestValue(left, "Ankus.Relocatable"));
        Assert.AreEqual("Pg_magic_func\n", ManifestValue(left, "Ankus.Exports"));
    }

    /// <summary>
    /// Invalid references, cycles, identifiers and raw SQL fail compilation with a declaration diagnostic.
    /// </summary>
    /// <param name="source">The invalid declaration source.</param>
    /// <param name="message">The diagnostic fragment identifying the failure.</param>
    [TestMethod]
    [DataRow("[assembly: Ankus.PgSql(\"a\", \"SELECT 1;\", Requires = new[] { \"missing\" })]", "missing dependency 'missing'")]
    [DataRow("[assembly: Ankus.PgSql(\"a\", \"SELECT 1;\", Before = new[] { \"missing\" })]", "missing dependency 'missing'")]
    [DataRow("[assembly: Ankus.PgSql(\"a\", \"SELECT 1;\", Requires = new[] { \"a\" })]", "cycle blocks: a")]
    [DataRow("[assembly: Ankus.PgSql(\"a\", \"SELECT 1;\", Requires = new[] { \"b\" })] [assembly: Ankus.PgSql(\"b\", \"SELECT 2;\", Requires = new[] { \"a\" })]", "cycle blocks: a, b")]
    [DataRow("[assembly: Ankus.PgSql(\"a\", \"SELECT 1;\")] [assembly: Ankus.PgSql(\"a\", \"SELECT 2;\")]", "declared more than once")]
    [DataRow("[assembly: Ankus.PgSql(\"a\", \"SELECT 1;\", Order = Ankus.PgSqlOrder.Bootstrap)] [assembly: Ankus.PgSql(\"b\", \"SELECT 2;\", Order = Ankus.PgSqlOrder.Bootstrap)]", "Only one bootstrap")]
    [DataRow("[assembly: Ankus.PgSql(\"a\", \"SELECT 1;\", Order = Ankus.PgSqlOrder.Finalize)] [assembly: Ankus.PgSql(\"b\", \"SELECT 2;\", Order = Ankus.PgSqlOrder.Finalize)]", "Only one final")]
    [DataRow("[assembly: Ankus.PgSql(\"a\", \"SELECT 1;\", Order = Ankus.PgSqlOrder.Bootstrap, Requires = new[] { \"b\" })] [assembly: Ankus.PgSql(\"b\", \"SELECT 2;\")]", "cycle")]
    [DataRow("[assembly: Ankus.PgSql(\"a\", \"SELECT 1;\", Order = Ankus.PgSqlOrder.Finalize, Before = new[] { \"b\" })] [assembly: Ankus.PgSql(\"b\", \"SELECT 2;\")]", "cycle")]
    [DataRow("[assembly: Ankus.PgSql(\"\", \"SELECT 1;\")]", "nonempty dependency name")]
    [DataRow("[assembly: Ankus.PgSql(null!, \"SELECT 1;\")]", "nonempty dependency name")]
    [DataRow("[assembly: Ankus.PgSql(\"a\", \" \")]", "nonempty SQL")]
    [DataRow("[assembly: Ankus.PgSql(\"a\", \"SELECT '\\0';\")]", "nonempty SQL")]
    [DataRow("[assembly: Ankus.PgSql(\"a\", \"SELECT '\\ud800';\")]", "nonempty SQL")]
    [DataRow("[assembly: Ankus.PgSql(\"a\", \"SELECT 1;\", Order = (Ankus.PgSqlOrder)3)]", "undefined PgSqlOrder")]
    [DataRow("[assembly: Ankus.PgSql(\"a\", \"SELECT 1;\", Requires = null!)]", "invalid Requires")]
    [DataRow("[assembly: Ankus.PgSql(\"a\", \"SELECT 1;\", Before = new[] { \"\" })]", "invalid Before")]
    [DataRow("[assembly: Ankus.PgSql(\"a\", \"SELECT 1;\", Requires = new[] { \"A\" })]", "missing dependency 'A'")]
    [DataRow("public static class C { [Ankus.PgFunction(Id = \"x\")] public static int F() => 1; [Ankus.PgFunction(Id = \"x\")] public static int G() => 2; }", "declared more than once")]
    [DataRow("[Ankus.PgSchema(\"s\", Id = \"schema\", Requires = new[] { \"function\" })] public static class C { [Ankus.PgFunction(Id = \"function\")] public static int F() => 1; }", "cycle")]
    public void InvalidSqlDependenciesAreDiagnosed(string source, string message)
    {
        (_, ImmutableArray<Diagnostic> diagnostics) = Generate(source);
        Diagnostic diagnostic = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual("ANKUS005", diagnostic.Id);
        Assert.Contains(message, diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// All aliases for a shared schema resolve to one creation node without losing dependencies.
    /// </summary>
    [TestMethod]
    public void RepeatedSchemaDeclarationsShareOneGraphNode()
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            [assembly: Ankus.PgSql("table", "CREATE TABLE s.t(n int);", Requires = new[] { "one", "two" })]
            [Ankus.PgSchema("s", Id = "one")] public static class A;
            [Ankus.PgSchema("s", Id = "two", Create = false)] public static class B;
            """);
        Assert.IsEmpty(diagnostics);
        Assert.AreEqual("CREATE SCHEMA IF NOT EXISTS \"s\";\nCREATE TABLE s.t(n int);\n", ManifestValue(compilation, "Ankus.Sql"));
    }

    /// <summary>
    /// File paths resolve relative to the project, normalize separators and parent segments, and retain file content.
    /// </summary>
    [TestMethod]
    public void SqlFilesUseTrackedProjectRelativeInputs()
    {
        string project = Path.Combine(AppContext.BaseDirectory, "virtual-project");
        var options = new SqlOptions(project);
        var file = new SqlInput(Path.Combine(project, "sql", "seed.sql"), "SELECT 'file'; -- exact\n");
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            [assembly: Ankus.PgSqlFile("seed", "sql\\nested/../seed.sql", Relocatable = true)]
            """, [file], options);
        Assert.IsEmpty(diagnostics);
        Assert.AreEqual("SELECT 'file'; -- exact\n", ManifestValue(compilation, "Ankus.Sql"));
        Assert.AreEqual("true", ManifestValue(compilation, "Ankus.Relocatable"));
    }

    /// <summary>
    /// A compiler-resolved project-root alias still identifies the unique tracked relative SQL input.
    /// </summary>
    [TestMethod]
    public void SqlFilesUseUniqueRelativeInputAcrossProjectRootAliases()
    {
        string root = Path.GetPathRoot(AppContext.BaseDirectory)!;
        string project = Path.Combine(root, "declared-root", "project alias");
        string resolved = Path.Combine(root, "resolved-root", "project alias", "sql setup", "seed.sql");
        var options = new SqlOptions(project);
        var file = new SqlInput(resolved, "SELECT 'aliased';\n");
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            [assembly: Ankus.PgSqlFile("seed", "sql setup/seed.sql", Relocatable = true)]
            """, [file], options);
        Assert.IsEmpty(diagnostics);
        Assert.AreEqual("SELECT 'aliased';\n", ManifestValue(compilation, "Ankus.Sql"));
    }

    /// <summary>
    /// Missing, duplicated, and unreadable file inputs cannot silently produce an incomplete installation script.
    /// </summary>
    /// <param name="kind">The invalid file-input scenario.</param>
    [TestMethod]
    [DataRow("missing")]
    [DataRow("duplicate")]
    [DataRow("unreadable")]
    [DataRow("empty")]
    [DataRow("no project directory")]
    public void InvalidSqlFilesAreDiagnosed(string kind)
    {
        string project = Path.Combine(AppContext.BaseDirectory, "virtual-project");
        var file = new SqlInput(Path.Combine(project, "seed.sql"), kind == "unreadable" ? null : kind == "empty" ? "" : "SELECT 1;");
        AdditionalText[] files = kind == "missing" ? [] : kind == "duplicate"
            ? [file, new SqlInput(Path.Combine(project, "nested", "..", "seed.sql"), "SELECT 2;")] : [file];
        (_, ImmutableArray<Diagnostic> diagnostics) = Generate("""[assembly: Ankus.PgSqlFile("seed", "seed.sql")]""",
            files, kind == "no project directory" ? null : new SqlOptions(project));
        Assert.AreEqual("ANKUS005", Assert.ContainsSingle(diagnostics).Id);
    }

    /// <summary>
    /// Replacing an AdditionalFiles input invalidates cached generation without changing C# source.
    /// </summary>
    [TestMethod]
    public void SqlFileChangesInvalidateIncrementalOutput()
    {
        string project = Path.Combine(AppContext.BaseDirectory, "virtual-project");
        var original = new SqlInput(Path.Combine(project, "seed.sql"), "SELECT 1;");
        var replacement = new SqlInput(original.Path, "SELECT 2;");
        CSharpCompilation input = CSharpCompilation.Create("SqlFileTest",
            [CSharpSyntaxTree.ParseText("""[assembly: Ankus.PgSqlFile("seed", "seed.sql")]""", cancellationToken: context.CancellationToken)],
            s_references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
        GeneratorDriver driver = CSharpGeneratorDriver.Create([new PgFunctionGenerator().AsSourceGenerator()],
            [original], optionsProvider: new SqlOptions(project));
        driver = driver.RunGeneratorsAndUpdateCompilation(input, out Compilation first, out ImmutableArray<Diagnostic> initialErrors, context.CancellationToken);
        Assert.IsEmpty(initialErrors);
        Assert.AreEqual("SELECT 1;\n", ManifestValue(first, "Ankus.Sql"));
        driver.ReplaceAdditionalText(original, replacement).RunGeneratorsAndUpdateCompilation(input, out Compilation second,
            out ImmutableArray<Diagnostic> updatedErrors, context.CancellationToken);
        Assert.IsEmpty(updatedErrors);
        Assert.AreEqual("SELECT 2;\n", ManifestValue(second, "Ankus.Sql"));
    }

    private sealed class SqlInput(string path, string? content) : AdditionalText
    {
        public override string Path => path;
        public override SourceText? GetText(CancellationToken cancellationToken = default) => content is null ? null : SourceText.From(content);
    }

    private sealed class SqlOptions(string project) : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new ProjectOptions(project);
        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => GlobalOptions;
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => GlobalOptions;
    }

    private sealed class ProjectOptions(string project) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string value)
        {
            value = project;
            return key == "build_property.MSBuildProjectDirectory";
        }
    }
}
