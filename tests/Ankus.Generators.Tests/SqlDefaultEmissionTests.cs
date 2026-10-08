using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;

namespace Ankus.Generators.Tests;

/// <summary>
/// Verifies authored SQL defaults are emitted so their own text cannot consume the generated declaration.
/// </summary>
public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// A default that contains a line-comment marker closes its clause on the next line, while other defaults keep their
    /// existing single-line text. Removing comments as PostgreSQL's scanner does leaves the complete argument list.
    /// </summary>
    /// <param name="parameters">The C# parameter list.</param>
    /// <param name="arguments">The exact generated SQL argument list.</param>
    /// <param name="scanned">The argument list after PostgreSQL removes line comments through each newline.</param>
    [TestMethod]
    [DataRow("[Ankus.PgParameter(Default = \"42 -- x\")] int value",
        "\"value\" integer DEFAULT (42 -- x\n)", "\"value\" integer DEFAULT (42 \n)")]
    [DataRow("[Ankus.PgParameter(Default = \"1 -- first\")] int first, [Ankus.PgParameter(Default = \"2--second\")] int second",
        "\"first\" integer DEFAULT (1 -- first\n), \"second\" integer DEFAULT (2--second\n)",
        "\"first\" integer DEFAULT (1 \n), \"second\" integer DEFAULT (2\n)")]
    [DataRow("[Ankus.PgParameter(Default = \"1 -- one\\n + 2\")] int value",
        "\"value\" integer DEFAULT (1 -- one\n + 2\n)", "\"value\" integer DEFAULT (1 \n + 2\n)")]
    [DataRow("[Ankus.PgParameter(Default = \"42 -- x\")] int first, int second = 7",
        "\"first\" integer DEFAULT (42 -- x\n), \"second\" integer DEFAULT ((7)::integer)",
        "\"first\" integer DEFAULT (42 \n), \"second\" integer DEFAULT ((7)::integer)")]
    [DataRow("[Ankus.PgParameter(Default = \"current_date\")] Ankus.PgDate value",
        "\"value\" date DEFAULT (current_date)", "\"value\" date DEFAULT (current_date)")]
    [DataRow("[Ankus.PgParameter(Default = \"6 - -1\")] int value",
        "\"value\" integer DEFAULT (6 - -1)", "\"value\" integer DEFAULT (6 - -1)")]
    [DataRow("int value = -7", "\"value\" integer DEFAULT ((-7)::integer)", "\"value\" integer DEFAULT ((-7)::integer)")]
    public void SqlDefaultsCannotCommentOutFollowingDeclarationText(string parameters, string arguments, string scanned)
    {
        (Compilation output, ImmutableArray<Diagnostic> diagnostics) = Generate(
            "public static class Functions { [Ankus.PgFunction] public static int Value(" + parameters + ") => 0; }");
        string sql = InstallationBody(output);
        string expected = "CREATE FUNCTION \"value\"(" + arguments + ")\nRETURNS integer AS 'MODULE_PATHNAME', '";

        Assert.IsEmpty(diagnostics);
        Assert.IsEmpty(output.GetDiagnostics(context.CancellationToken).Where(static value => value.Severity == DiagnosticSeverity.Error));
        Assert.Contains(expected, sql);
        Assert.Contains("CREATE FUNCTION \"value\"(" + scanned + ")\nRETURNS integer AS 'MODULE_PATHNAME', '",
            SqlLineComment().Replace(sql, string.Empty));
    }

    /// <summary>
    /// Matches PostgreSQL's scanner rule for a line comment, which continues until a carriage return or line feed.
    /// </summary>
    /// <returns>The comment pattern for test inputs without quoted text.</returns>
    [GeneratedRegex("--[^\r\n]*", RegexOptions.CultureInvariant)]
    private static partial Regex SqlLineComment();
}
