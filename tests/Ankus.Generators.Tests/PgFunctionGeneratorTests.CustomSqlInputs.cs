using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Ankus.Generators.Tests;

public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Inline input failures identify the exact authored constant and the independently repairable text contract.
    /// </summary>
    /// <param name="field">The constructor field to invalidate.</param>
    /// <param name="expression">The authored C# constant expression.</param>
    /// <param name="expected">The required diagnostic identity.</param>
    [TestMethod]
    [DataRow("name", "null!", "ANKUS355")]
    [DataRow("name", "\"\"", "ANKUS355")]
    [DataRow("name", "\" \\t\"", "ANKUS355")]
    [DataRow("name", "\"a\\0b\"", "ANKUS356")]
    [DataRow("name", "\"\\uD800\"", "ANKUS357")]
    [DataRow("name", "\"\\uDC00\"", "ANKUS357")]
    [DataRow("sql", "null!", "ANKUS359")]
    [DataRow("sql", "\"SELECT '\\0';\"", "ANKUS360")]
    [DataRow("sql", "\"SELECT '\\uD800';\"", "ANKUS361")]
    [DataRow("sql", "\"SELECT '\\uDC00';\"", "ANKUS361")]
    public void CustomSqlInputFailuresIdentifyAuthoredValues(string field, string expression, string expected)
    {
        string source = field == "name" ? "[assembly: Ankus.PgSql(" + expression + ", \"SELECT 1;\")]" :
            "[assembly: Ankus.PgSql(\"seed\", " + expression + ")]";
        (Compilation output, ImmutableArray<Diagnostic> errors) = Generate(source);
        AssertCustomSqlInputError(output, errors, expression, expected);
    }

    /// <summary>
    /// Named constructor arguments retain semantic field attribution even when written in reverse order.
    /// </summary>
    /// <param name="file">Whether the declaration uses a file path.</param>
    /// <param name="name">Whether the dependency name is invalid.</param>
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void CustomSqlInputNamedArgumentsKeepSemanticLocations(bool file, bool name)
    {
        const string Invalid = "\"a\\0b\"";
        string source = "[assembly: Ankus." + (file ? "PgSqlFile" : "PgSql") + "(" + (file ? "path" : "sql") + ": " +
            (name ? file ? "\"seed.sql\"" : "\"SELECT 1;\"" : Invalid) + ", name: " + (name ? Invalid : "\"seed\"") + ")]";
        string project = Path.Combine(AppContext.BaseDirectory, "sql-input-contracts");
        var input = new SqlInput(Path.Combine(project, "seed.sql"), "SELECT 1;");
        (Compilation output, ImmutableArray<Diagnostic> errors) = Generate(source, [input], new SqlOptions(project));
        AssertCustomSqlInputError(output, errors, Invalid, name ? "ANKUS356" : file ? "ANKUS363" : "ANKUS360");
    }

    /// <summary>
    /// A named first constructor argument does not shift the semantic index of a following positional value.
    /// </summary>
    /// <param name="file">Whether the declaration selects a tracked file.</param>
    /// <param name="contentNamed">Whether the final content argument is explicitly named instead.</param>
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void CustomSqlInputMixedArgumentsKeepSemanticLocations(bool file, bool contentNamed)
    {
        const string Invalid = "\"a\\0b\"";
        string source = "[assembly: Ankus." + (file ? "PgSqlFile" : "PgSql") + "(" +
            (contentNamed ? Invalid : "name: \"seed\"") + ", " +
            (contentNamed ? (file ? "path" : "sql") + ": \"SELECT 1;\"" : Invalid) + ")]";
        (Compilation output, ImmutableArray<Diagnostic> errors) = Generate(source,
            options: new SqlOptions(Path.Combine(AppContext.BaseDirectory, "sql-input-contracts")));
        AssertCustomSqlInputError(output, errors, Invalid, contentNamed ? "ANKUS356" : file ? "ANKUS363" : "ANKUS360");
    }

    /// <summary>
    /// File selection distinguishes missing, duplicate, unreadable and text-boundary failures at the requested path.
    /// </summary>
    /// <param name="kind">The independent tracked-input failure.</param>
    /// <param name="expected">The required diagnostic identity.</param>
    [TestMethod]
    [DataRow("missing", "ANKUS367")]
    [DataRow("duplicate", "ANKUS368")]
    [DataRow("unreadable", "ANKUS369")]
    [DataRow("zero", "ANKUS360")]
    [DataRow("high", "ANKUS361")]
    [DataRow("low", "ANKUS361")]
    [DataRow("project", "ANKUS366")]
    public void CustomSqlTrackedFailuresIdentifySelectionCause(string kind, string expected)
    {
        string project = Path.Combine(AppContext.BaseDirectory, "sql-input-contracts");
        var file = new SqlInput(Path.Combine(project, "seed.sql"), kind switch
        {
            "unreadable" => null,
            "zero" => "SELECT '\0';",
            "high" => "SELECT '\uD800';",
            "low" => "SELECT '\uDC00';",
            _ => "SELECT 1;",
        });
        AdditionalText[] inputs = kind == "missing" ? [] : kind == "duplicate"
            ? [file, new SqlInput(Path.Combine(project, "child", "..", "seed.sql"), "SELECT 2;")] : [file];
        (Compilation output, ImmutableArray<Diagnostic> errors) = Generate("[assembly: Ankus.PgSqlFile(\"seed\", \"seed.sql\")]",
            inputs, kind == "project" ? null : new SqlOptions(project));
        AssertCustomSqlInputError(output, errors, "\"seed.sql\"", expected);
    }

    /// <summary>
    /// Invalid file paths are rejected before additional-file selection, without conflating them with project configuration.
    /// </summary>
    /// <param name="expression">The authored path expression.</param>
    /// <param name="expected">The required diagnostic identity.</param>
    [TestMethod]
    [DataRow("null!", "ANKUS362")]
    [DataRow("\"\"", "ANKUS362")]
    [DataRow("\" \\t\"", "ANKUS362")]
    [DataRow("\"bad\\0.sql\"", "ANKUS363")]
    [DataRow("\"\\uD800.sql\"", "ANKUS364")]
    [DataRow("\"\\uDC00.sql\"", "ANKUS364")]
    public void CustomSqlPathFailuresIdentifyAuthoredValues(string expression, string expected)
    {
        (Compilation output, ImmutableArray<Diagnostic> errors) = Generate("[assembly: Ankus.PgSqlFile(\"seed\", " + expression + ")]",
            options: new SqlOptions(Path.Combine(AppContext.BaseDirectory, "sql-input-contracts")));
        AssertCustomSqlInputError(output, errors, expression, expected);
    }

    /// <summary>
    /// Undefined ordering values point at the enum expression without changing valid SQL text.
    /// </summary>
    /// <param name="expression">The undefined authored enum value.</param>
    [TestMethod]
    [DataRow("(Ankus.PgSqlOrder)(-1)")]
    [DataRow("(Ankus.PgSqlOrder)3")]
    public void CustomSqlOrderFailuresIdentifyAuthoredValue(string expression)
    {
        (Compilation output, ImmutableArray<Diagnostic> errors) = Generate("[assembly: Ankus.PgSql(\"seed\", \"SELECT 1;\", Order = " + expression + ")]");
        AssertCustomSqlInputError(output, errors, expression, "ANKUS358");
    }

    /// <summary>
    /// Valid non-identifier names and Unicode boundaries retain exact SQL, graph identity and relocation semantics.
    /// </summary>
    /// <param name="name">The exact dependency name.</param>
    /// <param name="sql">The exact authored SQL.</param>
    [TestMethod]
    [DataRow("a name.with punctuation", "SELECT 'café'; -- exact")]
    [DataRow("\uD7FF", "SELECT '\uE000';")]
    [DataRow("\uD800\uDC00", "SELECT '\uDBFF\uDFFF';")]
    [DataRow(" a ", "SELECT 1;")]
    public void CustomSqlInputBoundariesPreserveExactValidText(string name, string sql)
    {
        string source = "[assembly: Ankus.PgSql(" + Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(name, quote: true) + ", " +
            Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(sql, quote: true) + ", Relocatable = true)]";
        (Compilation output, ImmutableArray<Diagnostic> errors) = Generate(source);
        Assert.IsEmpty(errors);
        Assert.IsEmpty(output.GetDiagnostics(context.CancellationToken).Where(static error => error.Severity >= DiagnosticSeverity.Warning));
        Assert.AreEqual(sql + "\n", InstallationBody(output));
        Assert.AreEqual("true", ManifestValue(output, "Ankus.Relocatable"));
        Ankus.PgConfig.ExtensionSchemaGraph graph = Ankus.PgConfig.ExtensionSchemaGraph.Parse(ManifestValue(output, "Ankus.SqlGraph"));
        Assert.AreEqual(name, Assert.ContainsSingle(Assert.ContainsSingle(graph.Items).Names));
    }

    /// <summary>
    /// Checks the externally visible diagnostic boundary and rejects an incomplete installation manifest.
    /// </summary>
    /// <param name="output">The actual generated compilation.</param>
    /// <param name="errors">The actual generator diagnostics.</param>
    /// <param name="expression">The exact authored expression to highlight.</param>
    /// <param name="expected">The independently required rule identity.</param>
    private void AssertCustomSqlInputError(Compilation output, ImmutableArray<Diagnostic> errors, string expression, string expected)
    {
        Diagnostic error = Assert.ContainsSingle(errors);
        Assert.AreEqual(expected, error.Id);
        Assert.AreEqual(DiagnosticSeverity.Error, error.Severity);
        Assert.AreEqual(expression, error.Location.SourceTree!.GetText(context.CancellationToken).ToString(error.Location.SourceSpan));
        Assert.AreEqual("https://willibrandon.github.io/ankus/custom-sql/#custom-sql-input-diagnostics", error.Descriptor.HelpLinkUri);
        Assert.IsEmpty(output.GetDiagnostics(context.CancellationToken).Where(static diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning));
        Assert.DoesNotContain(static attribute => attribute.AttributeClass?.ToDisplayString() == "System.Reflection.AssemblyMetadataAttribute" &&
            attribute.ConstructorArguments[0].Value is "Ankus.Sql" or "Ankus.SqlGraph", output.Assembly.GetAttributes());
    }
}
