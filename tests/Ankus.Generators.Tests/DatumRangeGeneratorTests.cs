using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Ankus.Generators.Tests;

/// <summary>
/// Verifies finite mapped range identities, inherited conversion directions and SQL provider ordering.
/// </summary>
public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Range arguments and every result shape have the same complete SQL contract as explicitly typed raw datums.
    /// </summary>
    /// <param name="type">The range or range-array representation.</param>
    /// <param name="sqlType">The independent raw SQL binding.</param>
    [TestMethod]
    [DataRow("Ankus.PgRange<Value>", "int4range")]
    [DataRow("Ankus.PgRange<Value>?", "int4range")]
    [DataRow("Ankus.PgRange<Value>?[]", "_int4range")]
    [DataRow("Ankus.PgArray<Ankus.PgRange<Value>?>?", "_int4range")]
    public void DatumRangesPreserveScalarSetTableAndArrayContracts(string type, string sqlType)
    {
        Compilation mapped = GenerateSqlControl(DatumRangeSource() + DatumMappingMethods(type, string.Empty));
        string optional = type.EndsWith('?') ? "?" : string.Empty;
        Compilation raw = GenerateSqlControl(DatumMappingMethods("Ankus.PgDatum" + optional,
            "Ankus.PgSqlType(\"" + sqlType + "\", Schema=\"pg_catalog\")"));
        Assert.AreEqual(NormalizedDatumSql(raw).Replace("\"_int4range\"", "\"int4range\"[]", StringComparison.Ordinal),
            NormalizedDatumSql(mapped));
        string managed = DatumMappingManaged(mapped);
        int scalar = managed.IndexOf("RegisterValue<global::Value>", StringComparison.Ordinal);
        int range = managed.IndexOf("RegisterRange<global::Value>(\"int4range\", \"pg_catalog\", global::Ankus.PgTypeOrigin.External);", StringComparison.Ordinal);
        Assert.IsGreaterThan(-1, scalar);
        Assert.IsGreaterThan(scalar, range);
        Assert.Contains("ReadMapped<", managed);
        Assert.DoesNotContain("ReadRange<", managed);
        Assert.DoesNotContain("CREATE TYPE", ManifestValue(mapped, "Ankus.Sql"));
    }

    /// <summary>
    /// Exact range declarations discover raw-only closed roots and override the generic default independently of scalar metadata.
    /// </summary>
    [TestMethod]
    public void DatumRangesSelectExactGenericRootsAndConverterTemplates()
    {
        const string source = """
            [Ankus.PgDatumType("int4", typeof(Converter<>), Origin=Ankus.PgTypeOrigin.External, Schema="pg_catalog")]
            [Ankus.PgRangeType("int4range", Origin=Ankus.PgTypeOrigin.External, Schema="pg_catalog")]
            [Ankus.PgRangeType(typeof(Value<long>), "wide_range", Origin=Ankus.PgTypeOrigin.External, Schema="custom")]
            public struct Value<T> { }
            public sealed class Converter<T> : Ankus.IPgDatumReader<Value<T>>, Ankus.IPgDatumWriter<Value<T>>
            {
                public Value<T> Read(Ankus.PgDatum value) => default;
                public Ankus.PgDatum Write(Value<T> value, uint oid, Ankus.PgMemoryContext owner) => throw new System.InvalidOperationException();
            }
            public static class Functions
            {
                [Ankus.PgFunction] public static Ankus.PgRange<Value<int>> Echo(Ankus.PgRange<Value<int>> value) => value;
            }
            """;
        Compilation compilation = GenerateSqlControl(source);
        string managed = DatumMappingManaged(compilation);
        Assert.Contains("RegisterRange<global::Value<int>>(\"int4range\", \"pg_catalog\"", managed);
        Assert.Contains("RegisterRange<global::Value<long>>(\"wide_range\", \"custom\"", managed);
        Assert.Contains("new global::Converter<int>()", managed);
        Assert.Contains("new global::Converter<long>()", managed);
        Assert.Contains("RETURNS \"pg_catalog\".\"int4range\"", ManifestValue(compilation, "Ankus.Sql"));
        Assert.DoesNotContain("RETURNS \"custom\".\"wide_range\"", ManifestValue(compilation, "Ankus.Sql"));
    }

    /// <summary>
    /// Provider completion orders owned subtypes before their ranges even when lexical SQL ordering would reverse them.
    /// </summary>
    /// <param name="shared">Whether one block completes both types.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void DatumRangesOrderIndependentOwnedProviders(bool shared)
    {
        string providers = shared ? """
            [assembly: Ankus.PgSql("types", "CREATE DOMAIN bound AS integer; CREATE TYPE bounds AS RANGE (subtype=bound);", Relocatable=true)]
            [assembly: Ankus.PgSqlTypeProvider("types", typeof(Value))]
            [assembly: Ankus.PgSqlTypeProvider("types", typeof(Ankus.PgRange<Value>))]
            """ : """
            [assembly: Ankus.PgSql("a_range", "CREATE TYPE bounds AS RANGE (subtype=bound);", Relocatable=true)]
            [assembly: Ankus.PgSql("z_bound", "CREATE DOMAIN bound AS integer;", Relocatable=true)]
            [assembly: Ankus.PgSqlTypeProvider("a_range", typeof(Ankus.PgRange<Value>))]
            [assembly: Ankus.PgSqlTypeProvider("z_bound", typeof(Value))]
            """;
        string source = providers + DatumRangeSource("\"bounds\"", scalarOwned: true) +
            DatumMappingMethods("Ankus.PgRange<Value>?", string.Empty);
        Compilation compilation = GenerateSqlControl(source);
        string sql = ManifestValue(compilation, "Ankus.Sql");
        int scalar = sql.IndexOf("CREATE DOMAIN bound AS integer;", StringComparison.Ordinal);
        int range = sql.IndexOf("CREATE TYPE bounds AS RANGE (subtype=bound);", StringComparison.Ordinal);
        Assert.IsGreaterThan(-1, scalar);
        Assert.IsGreaterThan(scalar, range);
        Assert.IsGreaterThan(range, sql.IndexOf("CREATE FUNCTION \"identity\"", StringComparison.Ordinal));
        Assert.AreEqual("true", ManifestValue(compilation, "Ankus.Relocatable"));
        Assert.Contains("RegisterRange<global::Value>(\"bounds\", null, global::Ankus.PgTypeOrigin.ThisExtension);", DatumMappingManaged(compilation));
    }

    /// <summary>
    /// Owned range metadata cannot borrow its scalar's provider implicitly.
    /// </summary>
    [TestMethod]
    public void DatumRangesRequireTheirOwnProvider()
        => AssertDatumMappingError("""
            [assembly: Ankus.PgSql("bound", "CREATE DOMAIN bound AS integer;", Relocatable=true)]
            [assembly: Ankus.PgSqlTypeProvider("bound", typeof(Value))]
            """ + DatumRangeSource("\"bounds\"", scalarOwned: true), "ANKUS005", "requires a PgSqlTypeProvider");

    /// <summary>
    /// A hard subtype dependency rejects a reverse dependency instead of emitting an unusable installation order.
    /// </summary>
    [TestMethod]
    public void DatumRangesRejectProviderCompletionCycles()
        => AssertDatumMappingError("""
            [assembly: Ankus.PgSql("ranges", "SELECT 'range';", Relocatable=true)]
            [assembly: Ankus.PgSql("bounds", "SELECT 'bound';", Requires=new[] { "ranges" }, Relocatable=true)]
            [assembly: Ankus.PgSqlTypeProvider("ranges", typeof(Ankus.PgRange<Value>))]
            [assembly: Ankus.PgSqlTypeProvider("bounds", typeof(Value))]
            """ + DatumRangeSource("\"bounds\"", scalarOwned: true), "ANKUS005", "cycle");

    /// <summary>
    /// Each range consumes only the scalar conversion direction required by its generated slot.
    /// </summary>
    /// <param name="read">Whether only input conversion exists.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void DatumRangesInheritIndependentDirections(bool read)
    {
        string prefix = DatumRangeSource(reader: read, writer: !read);
        string valid = read ? "public static int Consume(Ankus.PgRange<Value> value) => 1;" :
            "public static Ankus.PgRange<Value> Produce() => new();";
        Compilation compilation = GenerateSqlControl(prefix + "public static class Functions { [Ankus.PgFunction] " + valid + " }");
        Assert.Contains(read ? "RETURNS integer" : "RETURNS \"pg_catalog\".\"int4range\"", ManifestValue(compilation, "Ankus.Sql"));
        string invalid = read ? "public static Ankus.PgRange<Value>? Produce() => null;" :
            "public static int Consume(Ankus.PgArray<Ankus.PgRange<Value>?>? value) => 1;";
        AssertDatumMappingError(prefix + "public static class Functions { [Ankus.PgFunction] " + invalid + " }", "ANKUS019",
            read ? "writing SQL results" : "reading SQL arguments");
    }

    /// <summary>
    /// Invalid range identities and origins fail before generating any callback or manifest.
    /// </summary>
    /// <param name="options">The malformed range attribute arguments.</param>
    /// <param name="reason">The actionable diagnostic reason.</param>
    [TestMethod]
    [DataRow("\"\"", "63 UTF-8 bytes")]
    [DataRow("\"name\", Schema=\"\"", "63 UTF-8 bytes")]
    [DataRow("\"name\", Origin=(Ankus.PgTypeOrigin)2", "Origin")]
    [DataRow("\"name\", Origin=Ankus.PgTypeOrigin.External", "explicit Schema")]
    [DataRow("typeof(int), \"name\"", "closed construction")]
    public void DatumRangesRejectInvalidMetadata(string options, string reason)
        => AssertDatumMappingError(DatumRangeSource(options), "ANKUS020", reason);

    /// <summary>
    /// Duplicate defaults, duplicate exact targets and an absent range selection have precise declaration diagnostics.
    /// </summary>
    /// <param name="extra">The additional range declaration.</param>
    [TestMethod]
    [DataRow("[Ankus.PgRangeType(\"second\")]")]
    [DataRow("[Ankus.PgRangeType(typeof(Value), \"one\")][Ankus.PgRangeType(typeof(Value), \"two\")]")]
    public void DatumRangesRejectDuplicateSelections(string extra)
        => AssertDatumMappingError(extra + DatumRangeSource(), "ANKUS020", "only one");

    /// <summary>
    /// A scalar mapping alone does not imply a matching catalog range or permit a built-in fallback.
    /// </summary>
    [TestMethod]
    public void DatumRangesRejectMissingRangeSelection()
        => AssertDatumMappingError(DatumMappingSource() + DatumMappingMethods("Ankus.PgRange<Value>", string.Empty),
            "ANKUS020", "No valid PgRangeType declaration");

    /// <summary>
    /// Range attributes cannot silently decorate unsupported scalar declarations even without a consuming function.
    /// </summary>
    [TestMethod]
    public void DatumRangesRequireMappedValueBounds()
    {
        AssertDatumMappingError("[Ankus.PgRangeType(\"range\")] public struct Value { }", "ANKUS020", "carrying PgDatumType");
        AssertDatumMappingError("[Ankus.PgRangeType(\"range\")] " + DatumMappingSource("public sealed class Value { }"),
            "ANKUS020", "value type");
    }

    /// <summary>
    /// Referenced range metadata selects only the consumed closed root and retains exact scalar conversion.
    /// </summary>
    [TestMethod]
    public void DatumRangesSelectReferencedMetadata()
    {
        MetadataReference dependency = DatumMappingReference("RangeDependency", DatumRangeSource());
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = GenerateDatumMappingReference(
            DatumMappingMethods("Ankus.PgRange<Value>?", string.Empty), [dependency]);
        AssertSqlControlCompilation(compilation, diagnostics);
        Assert.Contains("RETURNS \"pg_catalog\".\"int4range\"", ManifestValue(compilation, "Ankus.Sql"));
        Assert.Contains("RegisterValue<global::Value>(\"int4\"", DatumMappingManaged(compilation));
        Assert.Contains("RegisterRange<global::Value>(\"int4range\"", DatumMappingManaged(compilation));
    }

    /// <summary>
    /// A metadata-only range edit updates both registration and function SQL in a reused incremental driver.
    /// </summary>
    [TestMethod]
    public void DatumRangesInvalidateMetadataInReusedDriver()
    {
        string source = DatumRangeSource() + DatumMappingMethods("Ankus.PgRange<Value>", string.Empty);
        CSharpCompilation input = CSharpCompilation.Create("RangeGeneratorTest",
            [CSharpSyntaxTree.ParseText(source, cancellationToken: context.CancellationToken)], s_references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, nullableContextOptions: NullableContextOptions.Enable));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new PgFunctionGenerator().AsSourceGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(input, out Compilation before, out ImmutableArray<Diagnostic> errors, context.CancellationToken);
        AssertSqlControlCompilation(before, errors);
        Assert.Contains("RETURNS \"pg_catalog\".\"int4range\"", ManifestValue(before, "Ankus.Sql"));
        input = input.ReplaceSyntaxTree(input.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(
            source.Replace("\"int4range\"", "\"new_range\"", StringComparison.Ordinal), cancellationToken: context.CancellationToken));
        driver.RunGeneratorsAndUpdateCompilation(input, out Compilation after, out errors, context.CancellationToken);
        AssertSqlControlCompilation(after, errors);
        Assert.Contains("RegisterRange<global::Value>(\"new_range\"", DatumMappingManaged(after));
        Assert.Contains("RETURNS \"pg_catalog\".\"new_range\"", ManifestValue(after, "Ankus.Sql"));
        Assert.DoesNotContain("\"int4range\"", ManifestValue(after, "Ankus.Sql"));
    }

    /// <summary>
    /// Range aggregate states and comparison operators preserve complete raw-equivalent SQL contracts.
    /// </summary>
    [TestMethod]
    public void DatumRangesCompileAggregateRolesAndOperators()
    {
        const string template = """
            [Ankus.PgAggregate]
            public static class First
            {
                OUTPUT public static TYPE? Transition(INPUT TYPE? state, int value) => state;
                OUTPUT public static TYPE? Combine(INPUT TYPE? left, INPUT TYPE? right) => left ?? right;
                OUTPUT public static TYPE? Final(INPUT TYPE? state) => state;
                OUTPUT public static TYPE? MovingTransition(INPUT TYPE? state, int value) => state;
                OUTPUT public static TYPE? MovingInverse(INPUT TYPE? state, int value) => state;
                OUTPUT public static TYPE? MovingFinal(INPUT TYPE? state) => state;
            }
            public static class Functions
            {
                [Ankus.PgFunction, Ankus.PgOperator("===")]
                public static bool Equal(INPUT TYPE left, INPUT TYPE right) => true;
            }
            """;
        Compilation mapped = GenerateSqlControl(DatumRangeSource() + template.Replace("TYPE", "Ankus.PgRange<Value>", StringComparison.Ordinal)
            .Replace("INPUT", string.Empty, StringComparison.Ordinal).Replace("OUTPUT", string.Empty, StringComparison.Ordinal));
        const string binding = "Ankus.PgSqlType(\"int4range\", Schema=\"pg_catalog\")";
        Compilation raw = GenerateSqlControl(template.Replace("TYPE", "Ankus.PgDatum", StringComparison.Ordinal)
            .Replace("INPUT", "[" + binding + "]", StringComparison.Ordinal).Replace("OUTPUT", "[return: " + binding + "]", StringComparison.Ordinal));
        Assert.AreEqual(NormalizedDatumSql(raw), NormalizedDatumSql(mapped));
        Assert.Contains("STYPE = \"pg_catalog\".\"int4range\"", ManifestValue(mapped, "Ankus.Sql"));
        Assert.Contains("MSTYPE = \"pg_catalog\".\"int4range\"", ManifestValue(mapped, "Ankus.Sql"));
    }

    /// <summary>
    /// Every aggregate helper checks its required range direction independently of transition discovery.
    /// </summary>
    /// <param name="role">The aggregate helper role.</param>
    /// <param name="readOnly">Whether the helper needs a missing writer.</param>
    [TestMethod]
    [DataRow("Transition", false)]
    [DataRow("Combine", false)]
    [DataRow("Final", true)]
    [DataRow("Serialize", false)]
    [DataRow("Deserialize", true)]
    [DataRow("MovingTransition", false)]
    [DataRow("MovingInverse", false)]
    [DataRow("MovingFinal", true)]
    public void DatumRangesRejectUnavailableAggregateDirections(string role, bool readOnly)
    {
        string method = readOnly ? "public static Ankus.PgRange<Value>? " + role + "(int state) => null;" :
            "public static int " + role + "(Ankus.PgRange<Value>? state) => 0;";
        AssertDatumMappingError(DatumRangeSource(reader: readOnly, writer: !readOnly) +
            "[Ankus.PgAggregate] public static class Aggregate { " + method + " }", "ANKUS019",
            readOnly ? "writing SQL results" : "reading SQL arguments");
    }

    /// <summary>
    /// A function cannot override either a direct range or its array's declared SQL identity.
    /// </summary>
    /// <param name="type">The mapped container in the signature.</param>
    [TestMethod]
    [DataRow("Ankus.PgRange<Value>")]
    [DataRow("Ankus.PgArray<Ankus.PgRange<Value>?>")]
    public void DatumRangesRejectSlotOverrides(string type)
        => AssertDatumMappingError(DatumRangeSource() + DatumMappingMethods(type, "Ankus.PgSqlType(\"text\", Schema=\"pg_catalog\")"),
            "ANKUS019", "cannot override their mapping");

    /// <summary>
    /// Supplies independent scalar and range declarations without relying on generated SQL creation.
    /// </summary>
    private static string DatumRangeSource(string options = "\"int4range\", Origin=Ankus.PgTypeOrigin.External, Schema=\"pg_catalog\"",
        bool reader = true, bool writer = true, bool scalarOwned = false)
        => "[Ankus.PgRangeType(" + options + ")] " + DatumMappingSource(reader: reader, writer: writer,
            name: scalarOwned ? "bound" : "int4", external: !scalarOwned);
}
