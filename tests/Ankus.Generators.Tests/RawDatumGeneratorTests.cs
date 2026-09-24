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
            public static class First
            {
                [return: Ankus.PgSqlType("custom")]
                public static Ankus.PgDatum? Transition([Ankus.PgSqlType("custom")] Ankus.PgDatum? state,
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
    [TestMethod]
    [DataRow("public static int Value(Ankus.PgDatum value) => 0;")]
    [DataRow("public static Ankus.PgDatum Value() => null!;")]
    [DataRow("public static System.Collections.Generic.IEnumerable<Ankus.PgDatum> Value() => null!;")]
    [DataRow("[return: Ankus.PgSqlType(\"x\")] public static System.Collections.Generic.IEnumerable<(Ankus.PgDatum A, Ankus.PgDatum B)> Value() => null!;")]
    [DataRow("[return: Ankus.PgSqlType(\"x\", Column=\"absent\")] public static System.Collections.Generic.IEnumerable<(Ankus.PgDatum A, int B)> Value() => null!;")]
    [DataRow("[return: Ankus.PgSqlType(\"x\", Column=\"a\")] public static Ankus.PgDatum Value() => null!;")]
    [DataRow("[return: Ankus.PgSqlType(\"x\")] public static int Value() => 0;")]
    [DataRow("[return: Ankus.PgSqlType(\"\")] public static Ankus.PgDatum Value() => null!;")]
    [DataRow("[return: Ankus.PgSqlType(\"x\", Schema=\"\")] public static Ankus.PgDatum Value() => null!;")]
    [DataRow("[return: Ankus.PgSqlType(\"x\"), Ankus.PgSqlType(\"y\")] public static Ankus.PgDatum Value() => null!;")]
    [DataRow("public static int Value([Ankus.PgSqlType(\"x\")] int value) => value;")]
    [DataRow("public static int Value([Ankus.PgSqlType(\"x\")] Ankus.PgDatum[] value) => 0;")]
    public void InvalidRawBindingsAreDiagnosed(string method)
    {
        (_, ImmutableArray<Diagnostic> diagnostics) = Generate("public static class Functions { [Ankus.PgFunction] " + method + " }");
        Assert.IsNotEmpty(diagnostics);
        Assert.IsTrue(diagnostics.All(static diagnostic => diagnostic.Id == "ANKUS016"));
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
        AssertVirtualContextDiagnostic(diagnostics, "ANKUS004");
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
