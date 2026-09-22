using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Ankus.Generators.Tests;

public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Verifies declaration options, exact defaults, SQL argument names, and fixed-schema metadata are emitted together.
    /// </summary>
    [TestMethod]
    public void FunctionDeclarationsPreserveOptionsAndConstants()
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            [Ankus.PgSchema("Mixed \" schema")]
            public static class Functions
            {
                [Ankus.PgFunction(Volatility = Ankus.PgVolatility.Immutable, ParallelSafety = Ankus.PgParallelSafety.Safe,
                    SecurityDefiner = true, Leakproof = true, CreateOrReplace = true, Cost = 2.5,
                    SearchPath = new[] { "pg_catalog", "Mixed \" schema", "pg_temp" }, SupportFunction = "pg_catalog.text_starts_with_support")]
                public static decimal Value([Ankus.PgParameter(Name = "input count")] int inputCount = -12,
                    decimal amount = 12345678901234567890.123456789m) => amount;
            }
            """);
        Assert.IsEmpty(diagnostics);
        Assert.IsEmpty(compilation.GetDiagnostics(context.CancellationToken).Where(static error => error.Severity == DiagnosticSeverity.Error));
        string sql = ManifestValue(compilation, "Ankus.Sql");
        Assert.StartsWith("CREATE SCHEMA IF NOT EXISTS \"Mixed \"\" schema\";", sql);
        Assert.Contains("CREATE OR REPLACE FUNCTION \"Mixed \"\" schema\".\"value\"(\"input count\" integer DEFAULT ((-12)::integer), \"amount\" numeric DEFAULT ((12345678901234567890.123456789)::numeric))", sql);
        Assert.Contains("IMMUTABLE PARALLEL SAFE STRICT SECURITY DEFINER LEAKPROOF COST 2.5", sql);
        Assert.Contains("SUPPORT \"pg_catalog\".\"text_starts_with_support\" SET search_path TO \"pg_catalog\", \"Mixed \"\" schema\", \"pg_temp\"", sql);
        Assert.AreEqual("false", ManifestValue(compilation, "Ankus.Relocatable"));
    }

    /// <summary>
    /// Rejects invalid option values and declaration contracts before native publishing.
    /// </summary>
    /// <param name="method">The invalid function declaration.</param>
    [TestMethod]
    [DataRow("[Ankus.PgFunction(Cost = 0)] public static int F() => 1;")]
    [DataRow("[Ankus.PgFunction(Cost = double.NaN)] public static int F() => 1;")]
    [DataRow("[Ankus.PgFunction(Cost = double.PositiveInfinity)] public static int F() => 1;")]
    [DataRow("[Ankus.PgFunction(Cost = double.Epsilon)] public static int F() => 1;")]
    [DataRow("[Ankus.PgFunction(Cost = double.MaxValue)] public static int F() => 1;")]
    [DataRow("[Ankus.PgFunction(Volatility = (Ankus.PgVolatility)3)] public static int F() => 1;")]
    [DataRow("[Ankus.PgFunction(ParallelSafety = (Ankus.PgParallelSafety)(-1))] public static int F() => 1;")]
    [DataRow("[Ankus.PgFunction(NullInput = (Ankus.PgNullInput)3)] public static int F() => 1;")]
    [DataRow("[Ankus.PgFunction(NullInput = Ankus.PgNullInput.CalledOnNull)] public static int F(int value) => value;")]
    [DataRow("[Ankus.PgFunction(Schema = \"\")] public static int F() => 1;")]
    [DataRow("[Ankus.PgFunction(Schema = \"a\\0b\")] public static int F() => 1;")]
    [DataRow("[Ankus.PgFunction(SearchPath = new[] { \"\" })] public static int F() => 1;")]
    [DataRow("[Ankus.PgFunction(SearchPath = new string[] { null! })] public static int F() => 1;")]
    [DataRow("[Ankus.PgFunction(SupportFunction = \"a.b.c\")] public static int F() => 1;")]
    [DataRow("[Ankus.PgFunction] public static int F(int URL, int Url) => URL;")]
    [DataRow("[Ankus.PgFunction] public static int F([Ankus.PgParameter(Default = \" \" )] int value) => value;")]
    [DataRow("[Ankus.PgFunction] public static int F([Ankus.PgParameter(Default = \"42\")] int value, int other) => value;")]
    [DataRow("[Ankus.PgFunction] public static string F(string value = \"\\0\") => value;")]
    [DataRow("[Ankus.PgFunction] public static string F(string value = \"\\ud800\") => value;")]
    public void InvalidDeclarationOptionsAreRejected(string method)
    {
        (_, ImmutableArray<Diagnostic> diagnostics) = Generate("public static class Functions { " + method + " }");
        Assert.AreEqual("ANKUS004", Assert.ContainsSingle(diagnostics).Id);
    }

    /// <summary>
    /// Uses the nearest containing schema, supports per-function overrides, and keeps same-name functions in different schemas distinct.
    /// </summary>
    [TestMethod]
    public void SchemaInheritanceAndOverridesUseQualifiedSignatures()
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            [Ankus.PgSchema("outer")]
            public static class Outer
            {
                public static class Nested { [Ankus.PgFunction] public static int F() => 1; }
                [Ankus.PgSchema("inner")]
                public static class Inner { [Ankus.PgFunction] public static int F() => 2; }
                [Ankus.PgFunction(Schema = "override")] public static int F() => 3;
            }
            [Ankus.PgSchema("empty")]
            public static class Empty;
            """);
        Assert.IsEmpty(diagnostics);
        string sql = ManifestValue(compilation, "Ankus.Sql");
        Assert.Contains("FUNCTION \"outer\".\"f\"()", sql);
        Assert.Contains("FUNCTION \"inner\".\"f\"()", sql);
        Assert.Contains("FUNCTION \"override\".\"f\"()", sql);
        Assert.Contains("CREATE SCHEMA IF NOT EXISTS \"empty\";", sql);
    }

    /// <summary>
    /// Generates a manifest for an empty declared schema and validates its identifier by UTF-8 byte length.
    /// </summary>
    /// <param name="name">The schema identifier.</param>
    /// <param name="valid">Whether the identifier is valid.</param>
    [TestMethod]
    [DataRow("empty", true)]
    [DataRow("", false)]
    [DataRow(null, false)]
    [DataRow("pg_reserved", false)]
    [DataRow("🐘🐘🐘🐘🐘🐘🐘🐘🐘🐘🐘🐘🐘🐘🐘", true)]
    [DataRow("🐘🐘🐘🐘🐘🐘🐘🐘🐘🐘🐘🐘🐘🐘🐘🐘", false)]
    public void EmptySchemasHaveValidatedStandaloneMetadata(string? name, bool valid)
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate(
            "[Ankus.PgSchema(" + (name is null ? "null!" : SymbolDisplay.FormatLiteral(name, true)) + ")] public static class Empty;");
        if (valid)
        {
            Assert.IsEmpty(diagnostics);
            Assert.AreEqual("CREATE SCHEMA IF NOT EXISTS \"" + name + "\";\n", ManifestValue(compilation, "Ankus.Sql"));
            Assert.AreEqual("false", ManifestValue(compilation, "Ankus.Relocatable"));
        }
        else
        {
            Assert.AreEqual("ANKUS004", Assert.ContainsSingle(diagnostics).Id);
        }
    }

    /// <summary>
    /// Fixed placement does not implicitly create or adopt an existing schema.
    /// </summary>
    [TestMethod]
    public void ExistingSchemasHaveNoCreationStatement()
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            [Ankus.PgSchema("public", Create = false)]
            public static class Functions
            {
                [Ankus.PgFunction] public static int F() => 1;
                [Ankus.PgFunction(Schema = "another")] public static int G() => 2;
            }
            """);
        Assert.IsEmpty(diagnostics);
        string sql = ManifestValue(compilation, "Ankus.Sql");
        Assert.DoesNotContain("CREATE SCHEMA", sql);
        Assert.Contains("FUNCTION \"public\".\"f\"()", sql);
        Assert.Contains("FUNCTION \"another\".\"g\"()", sql);
        Assert.AreEqual("false", ManifestValue(compilation, "Ankus.Relocatable"));
    }

    private static string ManifestValue(Compilation compilation, string key)
        => (string)compilation.Assembly.GetAttributes().Single(attribute =>
            attribute.AttributeClass?.ToDisplayString() == "System.Reflection.AssemblyMetadataAttribute" &&
            (string?)attribute.ConstructorArguments[0].Value == key).ConstructorArguments[1].Value!;
}
