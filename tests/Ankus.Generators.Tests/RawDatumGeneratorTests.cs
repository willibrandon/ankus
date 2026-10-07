using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Ankus.Generators.Tests;

/// <summary>
/// Verifies explicit raw SQL bindings, compiled dispatch, and declaration diagnostics.
/// </summary>
public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Compiles exact raw scalar, SETOF, and TABLE identities with independent NULL policies.
    /// </summary>
    /// <param name="array">Whether the raw datum represents a whole SQL array.</param>
    /// <param name="optional">Whether SQL NULL reaches the managed callback.</param>
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void RawSignaturesCompileWithExactSqlBindings(bool array, bool optional)
    {
        string binding = $"Ankus.PgSqlType(\"custom\", Schema=\"types\", IsArray={array.ToString().ToLowerInvariant()})";
        string type = "Ankus.PgDatum" + (optional ? "?" : string.Empty);
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate($$"""
            public static class Functions
            {
                [Ankus.PgFunction]
                [return: {{binding}}]
                public static {{type}} Identity([{{binding}}] {{type}} value) => value;
                [Ankus.PgFunction]
                [return: {{binding}}]
                public static System.Collections.Generic.IEnumerable<{{type}}> Rows([{{binding}}] {{type}} value) => [value];
                [Ankus.PgFunction]
                [return: {{binding}}]
                [return: Ankus.PgCompositeType("pair", Column="pair")]
                public static System.Collections.Generic.IEnumerable<({{type}} Raw, Ankus.PgHeapTuple? Pair)> Table([{{binding}}] {{type}} value)
                    => [(value, null)];
            }
            """);
        AssertAggregateCompilation(compilation, diagnostics);
        string sql = ManifestValue(compilation, "Ankus.Sql");
        string sqlType = "\"types\".\"custom\"" + (array ? "[]" : string.Empty);
        Assert.Contains("\"value\" " + sqlType, sql);
        Assert.Contains("RETURNS " + sqlType + " AS", sql);
        Assert.Contains("RETURNS SETOF " + sqlType + " AS", sql);
        Assert.Contains("RETURNS TABLE (\"raw\" " + sqlType + ", \"pair\" \"pair\")", sql);
        Assert.Contains(optional ? " CALLED ON NULL INPUT " : " STRICT ", sql);
    }

    /// <summary>
    /// Shares exact raw bindings across aggregate support functions, operators, and casts.
    /// </summary>
    [TestMethod]
    public void RawAggregateOperatorAndCastBindingsCompile()
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            [Ankus.PgAggregate]
            public sealed class First : Ankus.IPgAggregate<Ankus.PgDatum?,Ankus.PgDatum?>
            {
                [return: Ankus.PgSqlType("custom")]
                public static Ankus.PgDatum? Transition(Ankus.PgAggregateContext context,[Ankus.PgSqlType("custom")] Ankus.PgDatum? state,
                    [Ankus.PgSqlType("custom")] Ankus.PgDatum? value) => state ?? value;
            }
            public static class Functions
            {
                [Ankus.PgFunction, Ankus.PgOperator("===")]
                public static bool Equal([Ankus.PgSqlType("custom")] Ankus.PgDatum left,
                    [Ankus.PgSqlType("custom")] Ankus.PgDatum right) => left.DangerousGetBits() == right.DangerousGetBits();
                [Ankus.PgFunction, Ankus.PgCast]
                public static int Convert([Ankus.PgSqlType("custom")] Ankus.PgDatum value) => value.Read<int>();
            }
            """);
        AssertAggregateCompilation(compilation, diagnostics);
        string sql = ManifestValue(compilation, "Ankus.Sql");
        Assert.Contains("STYPE = \"custom\"", sql);
        Assert.Contains("LEFTARG = \"custom\"", sql);
        Assert.Contains("CREATE CAST (\"custom\" AS integer)", sql);
    }

    /// <summary>
    /// Rejects missing, ambiguous, malformed, or incompatible raw type bindings before SQL emission.
    /// </summary>
    /// <param name="method">The invalid declaration.</param>
    /// <param name="expected">The precise binding diagnostics in report order.</param>
    [TestMethod]
    [DataRow("public static int Value(Ankus.PgDatum value) => 0;", "ANKUS410")]
    [DataRow("public static Ankus.PgDatum Value() => null!;", "ANKUS410")]
    [DataRow("public static System.Collections.Generic.IEnumerable<Ankus.PgDatum> Value() => null!;", "ANKUS408")]
    [DataRow("[return: Ankus.PgSqlType(\"x\")] public static System.Collections.Generic.IEnumerable<(Ankus.PgDatum A, Ankus.PgDatum B)> Value() => null!;", "ANKUS404,ANKUS408,ANKUS408")]
    [DataRow("[return: Ankus.PgSqlType(\"x\", Column=\"absent\")] public static System.Collections.Generic.IEnumerable<(Ankus.PgDatum A, int B)> Value() => null!;", "ANKUS403,ANKUS408")]
    [DataRow("[return: Ankus.PgSqlType(\"x\", Column=\"a\")] public static Ankus.PgDatum Value() => null!;", "ANKUS411")]
    [DataRow("[return: Ankus.PgSqlType(\"x\")] public static int Value() => 0;", "ANKUS405")]
    [DataRow("[return: Ankus.PgSqlType(\"\")] public static Ankus.PgDatum Value() => null!;", "ANKUS401")]
    [DataRow("[return: Ankus.PgSqlType(\"x\", Schema=\"\")] public static Ankus.PgDatum Value() => null!;", "ANKUS401")]
    [DataRow("[return: Ankus.PgSqlType(\"x\"), Ankus.PgSqlType(\"y\")] public static Ankus.PgDatum Value() => null!;", "ANKUS409")]
    [DataRow("public static int Value([Ankus.PgSqlType(\"x\")] int value) => value;", "ANKUS405")]
    [DataRow("public static int Value([Ankus.PgSqlType(\"x\")] Ankus.PgDatum[] value) => 0;", "ANKUS405")]
    public void InvalidRawBindingsAreDiagnosed(string method, string expected)
    {
        (_, ImmutableArray<Diagnostic> diagnostics) = Generate("public static class Functions { [Ankus.PgFunction] " + method + " }");
        Assert.AreSequenceEqual(expected.Split(','), diagnostics.Select(static diagnostic => diagnostic.Id));
    }

    /// <summary>
    /// Binding diagnostics select the attribute, parameter, or result that must change.
    /// </summary>
    /// <param name="method">The invalid declaration.</param>
    /// <param name="expectedId">The precise diagnostic identifier.</param>
    /// <param name="highlight">The exact syntax selected by the diagnostic.</param>
    [TestMethod]
    [DataRow("public static int Value(Ankus.PgDatum value) => 0;", "ANKUS410", "value")]
    [DataRow("public static Ankus.PgDatum Value() => null!;", "ANKUS410", "Ankus.PgDatum")]
    [DataRow("[return: Ankus.PgSqlType(\"\")] public static Ankus.PgDatum Value() => null!;", "ANKUS401", "Ankus.PgSqlType(\"\")")]
    [DataRow("[return: Ankus.PgSqlType(\"x\")] public static int Value() => 0;", "ANKUS405", "Ankus.PgSqlType(\"x\")")]
    [DataRow("public static System.Collections.Generic.IEnumerable<Ankus.PgDatum> Value() => null!;", "ANKUS408", "System.Collections.Generic.IEnumerable<Ankus.PgDatum>")]
    public void SqlTypeBindingDiagnosticsPointAtTheCorrectSyntax(string method, string expectedId, string highlight)
    {
        (_, ImmutableArray<Diagnostic> diagnostics) = Generate("public static class Functions { [Ankus.PgFunction] " + method + " }");
        Diagnostic diagnostic = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual(expectedId, diagnostic.Id);
        Assert.AreEqual(highlight,
            diagnostic.Location.SourceTree!.GetText(context.CancellationToken).ToString(diagnostic.Location.SourceSpan));
    }

    /// <summary>
    /// PostgreSQL pseudotype safety rules apply equally to explicit raw bindings.
    /// </summary>
    /// <param name="type">The result pseudotype requiring an input witness.</param>
    [TestMethod]
    [DataRow("internal")]
    [DataRow("anyelement")]
    [DataRow("anycompatiblearray")]
    public void RawPseudotypeResultsRequireInputs(string type)
    {
        (_, ImmutableArray<Diagnostic> diagnostics) = Generate($$"""
            public static class Functions
            {
                [Ankus.PgFunction]
                [return: Ankus.PgSqlType("{{type}}", Schema="pg_catalog")]
                public static Ankus.PgDatum? Value() => null;
            }
            """);
        AssertVirtualContextDiagnostic(diagnostics, type == "internal" ? "ANKUS047" : "ANKUS048");
    }

    /// <summary>
    /// Catalog bindings remain relocatable while extension type schemas constrain installation.
    /// </summary>
    /// <param name="schema">The explicitly selected type schema.</param>
    /// <param name="relocatable">The expected extension relocation policy.</param>
    [TestMethod]
    [DataRow("pg_catalog", "true")]
    [DataRow("types", "false")]
    public void RawBindingsPreserveSchemaDependenciesAndRelocation(string schema, string relocatable)
    {
        string declaration = schema == "types" ? "[Ankus.PgSchema(\"types\")] public static class Types { }" : string.Empty;
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate($$"""
            {{declaration}}
            public static class Functions
            {
                [Ankus.PgFunction]
                [return: Ankus.PgSqlType("text", Schema="{{schema}}")]
                public static Ankus.PgDatum? Value([Ankus.PgSqlType("text", Schema="{{schema}}")] Ankus.PgDatum? value) => value;
            }
            """);
        AssertAggregateCompilation(compilation, diagnostics);
        Assert.AreEqual(relocatable, ManifestValue(compilation, "Ankus.Relocatable"));
        if (schema == "types")
        {
            string sql = ManifestValue(compilation, "Ankus.Sql");
            Assert.IsLessThan(sql.IndexOf("CREATE FUNCTION", StringComparison.Ordinal), sql.IndexOf("CREATE SCHEMA", StringComparison.Ordinal));
        }
    }
}
