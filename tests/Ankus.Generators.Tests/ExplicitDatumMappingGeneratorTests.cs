using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Ankus.Generators.Tests;

/// <summary>
/// Verifies exact closed datum declarations with independent SQL metadata and finite discovery.
/// </summary>
public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Explicit local metadata roots closed registrations without requiring generated signatures.
    /// </summary>
    [TestMethod]
    public void ExplicitDatumMappingsRegisterIndependentRawOnlyRoots()
    {
        Compilation compilation = GenerateSqlControl(ExplicitDatumMappingSource);
        string managed = DatumMappingManaged(compilation);
        Assert.Contains("RegisterValue<global::Box<int>>(\"int4\", \"pg_catalog\"", managed);
        Assert.Contains("RegisterValue<global::Box<long>>(\"int8\", \"pg_catalog\"", managed);
        Assert.Contains("new global::Converter<int>()", managed);
        Assert.Contains("new global::Converter<long>()", managed);
        Assert.DoesNotContain("RegisterValue<global::Box<T>>", managed);
        Assert.DoesNotContain("CREATE FUNCTION", ManifestValue(compilation, "Ankus.Sql"));
        Assert.DoesNotContain("CREATE TYPE", ManifestValue(compilation, "Ankus.Sql"));
    }

    /// <summary>
    /// The selected declaration supplies exact scalar and array SQL identities in both callback directions.
    /// </summary>
    [TestMethod]
    public void ExplicitDatumMappingsPreserveIndependentSqlSignatures()
    {
        Compilation compilation = GenerateSqlControl(ExplicitDatumMappingSource + """
            public static class Functions
            {
                [Ankus.PgFunction] public static Box<int> Narrow(Box<int> value) => value;
                [Ankus.PgFunction] public static Box<long> Wide(Box<long> value) => value;
                [Ankus.PgFunction] public static Box<long>?[] Array(Box<long>?[] value) => value;
            }
            """);
        string sql = ManifestValue(compilation, "Ankus.Sql");
        Assert.Contains("\"narrow\"(\"value\" \"pg_catalog\".\"int4\")\nRETURNS \"pg_catalog\".\"int4\"", sql);
        Assert.Contains("\"wide\"(\"value\" \"pg_catalog\".\"int8\")\nRETURNS \"pg_catalog\".\"int8\"", sql);
        Assert.Contains("\"array\"(\"value\" \"pg_catalog\".\"int8\"[])\nRETURNS \"pg_catalog\".\"int8\"[]", sql);
        Assert.Contains("ReadMapped<global::Box<int>>()", DatumMappingManaged(compilation));
        Assert.Contains("FromMapped<global::Box<long>>(", DatumMappingManaged(compilation));
    }

    /// <summary>
    /// Exact metadata wins over a default declaration independently of source order.
    /// </summary>
    /// <param name="defaultFirst">Whether the fallback is written before the exact attributes.</param>
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void ExplicitDatumMappingsOverrideDefaultsIndependentlyOfOrder(bool defaultFirst)
    {
        const string fallback = "[Ankus.PgDatumType(\"numeric\", typeof(Converter<decimal>), Origin=Ankus.PgTypeOrigin.External, Schema=\"pg_catalog\")]";
        string source = defaultFirst ? fallback + ExplicitDatumMappingSource :
            ExplicitDatumMappingSource.Replace("public readonly record struct", fallback + "public readonly record struct", StringComparison.Ordinal);
        Compilation compilation = GenerateSqlControl(source + """
            public static class Functions
            {
                [Ankus.PgFunction] public static Box<decimal> Echo(Box<decimal> value) => value;
            }
            """);
        string managed = DatumMappingManaged(compilation);
        Assert.Contains("RegisterValue<global::Box<int>>(\"int4\", \"pg_catalog\"", managed);
        Assert.Contains("RegisterValue<global::Box<long>>(\"int8\", \"pg_catalog\"", managed);
        Assert.Contains("RegisterValue<global::Box<decimal>>(\"numeric\", \"pg_catalog\"", managed);
        Assert.Contains("new global::Converter<decimal>()", managed);
        Assert.Contains("RETURNS \"pg_catalog\".\"numeric\"", ManifestValue(compilation, "Ankus.Sql"));
    }

    /// <summary>
    /// Exact nested targets retain their constructed containing identity even without callbacks.
    /// </summary>
    [TestMethod]
    public void ExplicitDatumMappingsPreserveNestedTargetIdentities()
    {
        Compilation compilation = GenerateSqlControl("""
            public class Outer<T>
            {
                [Ankus.PgDatumType(typeof(Outer<int>.Value), "int4", typeof(Converter), Origin=Ankus.PgTypeOrigin.External, Schema="pg_catalog")]
                [Ankus.PgDatumType(typeof(Outer<long>.Value), "int8", typeof(Converter), Origin=Ankus.PgTypeOrigin.External, Schema="pg_catalog")]
                public readonly record struct Value(long Word);
            }
            public sealed class Converter : Ankus.IPgDatumReader<Outer<int>.Value>, Ankus.IPgDatumReader<Outer<long>.Value>
            {
                Outer<int>.Value Ankus.IPgDatumReader<Outer<int>.Value>.Read(Ankus.PgDatum value) => new(17);
                Outer<long>.Value Ankus.IPgDatumReader<Outer<long>.Value>.Read(Ankus.PgDatum value) => new(19);
            }
            """);
        string managed = DatumMappingManaged(compilation);
        Assert.Contains("RegisterValue<global::Outer<int>.Value>(\"int4\"", managed);
        Assert.Contains("RegisterValue<global::Outer<long>.Value>(\"int8\"", managed);
        Assert.DoesNotContain("RegisterValue<global::Outer<T>.Value>", managed);
    }

    /// <summary>
    /// Declaration ambiguity and invalid targets fail before producing any artifacts, including unused metadata.
    /// </summary>
    /// <param name="change">The malformed declaration or selected use.</param>
    /// <param name="reason">The expected mapping diagnostic.</param>
    [TestMethod]
    [DataRow("null", "closed construction of the annotated managed type")]
    [DataRow("open", "closed construction of the annotated managed type")]
    [DataRow("unrelated", "closed construction of the annotated managed type")]
    [DataRow("duplicate", "only one exact PgDatumType")]
    [DataRow("defaults", "only one default PgDatumType")]
    [DataRow("unlisted", "No PgDatumType declaration selects")]
    [DataRow("converter", "exact non-nullable managed type")]
    [DataRow("name", "Type and schema names")]
    public void ExplicitDatumMappingsRejectAmbiguousOrInvalidDeclarations(string change, string reason)
    {
        string source = change switch
        {
            "null" => ExplicitDatumMappingSource.Replace("typeof(Box<int>)", "null", StringComparison.Ordinal),
            "open" => ExplicitDatumMappingSource.Replace("typeof(Box<int>)", "typeof(Box<>)", StringComparison.Ordinal),
            "unrelated" => ExplicitDatumMappingSource.Replace("typeof(Box<int>)", "typeof(string)", StringComparison.Ordinal),
            "duplicate" => ExplicitDatumMappingSource.Replace("typeof(Box<long>)", "typeof(Box<int>)", StringComparison.Ordinal),
            "defaults" => """
                [Ankus.PgDatumType("int4", typeof(Converter<int>), Origin=Ankus.PgTypeOrigin.External, Schema="pg_catalog")]
                [Ankus.PgDatumType("int8", typeof(Converter<long>), Origin=Ankus.PgTypeOrigin.External, Schema="pg_catalog")]
                """ + ExplicitDatumMappingSource,
            "unlisted" => ExplicitDatumMappingSource + "public static class Functions { [Ankus.PgFunction] public static Box<double> Echo(Box<double> value) => value; }",
            "converter" => ExplicitDatumMappingSource.Replace("typeof(Converter<int>)", "typeof(Converter<long>)", StringComparison.Ordinal),
            "name" => ExplicitDatumMappingSource.Replace("\"int4\"", "\"\"", StringComparison.Ordinal),
            _ => throw new InvalidOperationException(),
        };
        AssertDatumMappingError(source, "ANKUS019", reason);
    }

    /// <summary>
    /// Referenced exact declarations select the requested SQL identity without registering every sibling.
    /// </summary>
    [TestMethod]
    public void ExplicitDatumMappingsSelectReferencedMetadata()
    {
        MetadataReference dependency = DatumMappingReference("ExplicitMappedDependency", ExplicitDatumMappingSource);
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = GenerateDatumMappingReference("""
            public static class Functions
            {
                [Ankus.PgFunction] public static Box<long> Echo(Box<long> value) => value;
            }
            """, [dependency]);
        AssertSqlControlCompilation(compilation, diagnostics);
        Assert.Contains("RegisterValue<global::Box<long>>(\"int8\"", DatumMappingManaged(compilation));
        Assert.DoesNotContain("RegisterValue<global::Box<int>>", DatumMappingManaged(compilation));
        Assert.Contains("RETURNS \"pg_catalog\".\"int8\"", ManifestValue(compilation, "Ankus.Sql"));
    }

    /// <summary>
    /// Two owned concrete identities obtain their independent completed provider dependencies.
    /// </summary>
    /// <param name="template">Whether the converter definition requires compile-time inference.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ExplicitDatumMappingsRequireIndependentOwnedProviders(bool template)
    {
        string source = """
            [assembly: Ankus.PgSql("narrow", "CREATE DOMAIN narrow_key AS integer;", Relocatable=true)]
            [assembly: Ankus.PgSql("wide", "CREATE DOMAIN wide_key AS bigint;", Relocatable=true)]
            [assembly: Ankus.PgSqlTypeProvider("narrow", typeof(Box<int>))]
            [assembly: Ankus.PgSqlTypeProvider("wide", typeof(Box<long>))]
            """ + ExplicitDatumMappingSource.Replace("\"int4\"", "\"narrow_key\"", StringComparison.Ordinal)
                .Replace("\"int8\"", "\"wide_key\"", StringComparison.Ordinal)
                .Replace(", Origin=Ankus.PgTypeOrigin.External, Schema=\"pg_catalog\"", string.Empty, StringComparison.Ordinal) + """
            public static class Functions
            {
                [Ankus.PgFunction] public static Box<int> Narrow(Box<int> value) => value;
                [Ankus.PgFunction] public static Box<long> Wide(Box<long> value) => value;
            }
            """;
        if (template)
        {
            source = source.Replace("typeof(Converter<int>)", "typeof(Converter<>)", StringComparison.Ordinal)
                .Replace("typeof(Converter<long>)", "typeof(Converter<>)", StringComparison.Ordinal);
        }

        Compilation compilation = GenerateSqlControl(source);
        string sql = ManifestValue(compilation, "Ankus.Sql");
        int narrow = sql.IndexOf("CREATE DOMAIN narrow_key AS integer;", StringComparison.Ordinal);
        int wide = sql.IndexOf("CREATE DOMAIN wide_key AS bigint;", StringComparison.Ordinal);
        Assert.IsGreaterThan(-1, narrow);
        Assert.IsGreaterThan(-1, wide);
        Assert.IsGreaterThan(narrow, sql.IndexOf("CREATE FUNCTION \"narrow\"", StringComparison.Ordinal));
        Assert.IsGreaterThan(wide, sql.IndexOf("CREATE FUNCTION \"wide\"", StringComparison.Ordinal));
        Assert.Contains("RETURNS \"narrow_key\"", sql);
        Assert.Contains("RETURNS \"wide_key\"", sql);
        AssertDatumMappingError(source.Replace("[assembly: Ankus.PgSqlTypeProvider(\"wide\", typeof(Box<long>))]", string.Empty, StringComparison.Ordinal),
            "ANKUS005", "requires a PgSqlTypeProvider");
    }

    /// <summary>
    /// Each exact declaration keeps its own schema and ownership even when a sibling uses an external built-in.
    /// </summary>
    [TestMethod]
    public void ExplicitDatumMappingsKeepPerConstructionSchemaAndOwnership()
    {
        string source = """
            [assembly: Ankus.PgSql("wide", "CREATE DOMAIN owned_schema.wide_key AS bigint;")]
            [assembly: Ankus.PgSqlTypeProvider("wide", typeof(Box<long>))]
            """ + ExplicitDatumMappingSource.Replace("\"int8\", typeof(Converter<long>), Origin=Ankus.PgTypeOrigin.External, Schema=\"pg_catalog\"",
                "\"wide_key\", typeof(Converter<long>), Schema=\"owned_schema\"", StringComparison.Ordinal) + """
            [Ankus.PgSchema("owned_schema")]
            public static class Functions
            {
                [Ankus.PgFunction] public static Box<long> Echo(Box<long> value) => value;
            }
            """;
        Compilation compilation = GenerateSqlControl(source);
        string managed = DatumMappingManaged(compilation);
        Assert.Contains("RegisterValue<global::Box<int>>(\"int4\", \"pg_catalog\", global::Ankus.PgTypeOrigin.External", managed);
        Assert.Contains("RegisterValue<global::Box<long>>(\"wide_key\", \"owned_schema\", global::Ankus.PgTypeOrigin.ThisExtension", managed);
        string sql = ManifestValue(compilation, "Ankus.Sql");
        int schema = sql.IndexOf("CREATE SCHEMA IF NOT EXISTS \"owned_schema\"", StringComparison.Ordinal);
        int provider = sql.IndexOf("CREATE DOMAIN owned_schema.wide_key", StringComparison.Ordinal);
        Assert.IsGreaterThan(-1, schema);
        Assert.IsGreaterThan(schema, provider);
        Assert.IsGreaterThan(provider, sql.IndexOf("CREATE FUNCTION \"owned_schema\".\"echo\"", StringComparison.Ordinal));
        Assert.Contains("RETURNS \"owned_schema\".\"wide_key\"", sql);
        Assert.AreEqual("false", ManifestValue(compilation, "Ankus.Relocatable"));
    }

    /// <summary>
    /// Independent SQL identities support separate closed derived helpers without a synthetic callback root.
    /// </summary>
    /// <param name="template">Whether each derived helper uses an inferred closed converter.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ExplicitDatumMappingsDeriveIndependentSqlFamilies(bool template)
    {
        string source = ExplicitDatumMappingSource.Replace("public readonly record struct Box<T>(T Number);", """
            [Ankus.PgEquality][Ankus.PgOrdering][Ankus.PgHashing]
            public readonly record struct Box<T>(long Number) : System.IComparable<Box<T>>, Ankus.IPgHashable
            {
                public int CompareTo(Box<T> other) => Number.CompareTo(other.Number);
                public int GetPostgresHashCode() => Ankus.PgHash.Compute(unchecked((ulong)Number));
            }
            """, StringComparison.Ordinal);
        if (template)
        {
            source = source.Replace("typeof(Converter<int>)", "typeof(Converter<>)", StringComparison.Ordinal)
                .Replace("typeof(Converter<long>)", "typeof(Converter<>)", StringComparison.Ordinal);
        }

        Compilation compilation = GenerateSqlControl(source);
        string sql = ManifestValue(compilation, "Ankus.Sql");
        Assert.Contains("CREATE FUNCTION \"pg_catalog\".\"int4_eq\"(\"pg_catalog\".\"int4\",\"pg_catalog\".\"int4\")", sql);
        Assert.Contains("CREATE FUNCTION \"pg_catalog\".\"int8_eq\"(\"pg_catalog\".\"int8\",\"pg_catalog\".\"int8\")", sql);
        Assert.Contains("\"int4_btree_ops\" DEFAULT FOR TYPE \"pg_catalog\".\"int4\" USING btree", sql);
        Assert.Contains("\"int8_btree_ops\" DEFAULT FOR TYPE \"pg_catalog\".\"int8\" USING btree", sql);
        Assert.Contains("\"int4_hash_ops\" DEFAULT FOR TYPE \"pg_catalog\".\"int4\" USING hash", sql);
        Assert.Contains("\"int8_hash_ops\" DEFAULT FOR TYPE \"pg_catalog\".\"int8\" USING hash", sql);
        Assert.Contains("ReadMapped<global::Box<int>>()", DatumMappingManaged(compilation));
        Assert.Contains("ReadMapped<global::Box<long>>()", DatumMappingManaged(compilation));
        Assert.Contains("ankus_read_polymorphic(fcinfo, 0", ManifestValue(compilation, "Ankus.NativeSource"));
        Assert.AreEqual("false", ManifestValue(compilation, "Ankus.Relocatable"));
    }

    /// <summary>
    /// Editing only explicit metadata invalidates the selected registration in a reused generator driver.
    /// </summary>
    [TestMethod]
    public void ExplicitDatumMappingMetadataInvalidatesIncrementalOutput()
    {
        CSharpCompilation input = CSharpCompilation.Create("ExplicitGeneratorTest",
            [CSharpSyntaxTree.ParseText(ExplicitDatumMappingSource, cancellationToken: context.CancellationToken)], s_references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, nullableContextOptions: NullableContextOptions.Enable));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new PgFunctionGenerator().AsSourceGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(input, out Compilation before, out ImmutableArray<Diagnostic> errors, context.CancellationToken);
        AssertSqlControlCompilation(before, errors);
        Assert.Contains("RegisterValue<global::Box<long>>(\"int8\"", DatumMappingManaged(before));
        input = input.ReplaceSyntaxTree(input.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(
            ExplicitDatumMappingSource.Replace("\"int8\"", "\"wide_domain\"", StringComparison.Ordinal), cancellationToken: context.CancellationToken));
        driver.RunGeneratorsAndUpdateCompilation(input, out Compilation after, out errors, context.CancellationToken);
        AssertSqlControlCompilation(after, errors);
        Assert.Contains("RegisterValue<global::Box<long>>(\"wide_domain\"", DatumMappingManaged(after));
        Assert.DoesNotContain("RegisterValue<global::Box<long>>(\"int8\"", DatumMappingManaged(after));
        Assert.Contains("RegisterValue<global::Box<int>>(\"int4\"", DatumMappingManaged(after));
    }

    /// <summary>
    /// Supplies two exact local generic roots with independently closed converters and SQL metadata.
    /// </summary>
    private const string ExplicitDatumMappingSource = """
        [Ankus.PgDatumType(typeof(Box<int>), "int4", typeof(Converter<int>), Origin=Ankus.PgTypeOrigin.External, Schema="pg_catalog")]
        [Ankus.PgDatumType(typeof(Box<long>), "int8", typeof(Converter<long>), Origin=Ankus.PgTypeOrigin.External, Schema="pg_catalog")]
        public readonly record struct Box<T>(T Number);
        public sealed class Converter<T> : Ankus.IPgDatumReader<Box<T>>, Ankus.IPgDatumWriter<Box<T>>
        {
            public Box<T> Read(Ankus.PgDatum value) => new(default!);
            public Ankus.PgDatum Write(Box<T> value, uint typeOid, Ankus.PgMemoryContext destination)
                => throw new System.InvalidOperationException();
        }
        """;
}
