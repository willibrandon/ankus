using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Ankus.Generators.Tests;

/// <summary>
/// Verifies closed datum conversions, managed provider identities and unsupported transport diagnostics.
/// </summary>
public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Required and nullable mapped roots retain the complete SQL contract of independently bound raw datums.
    /// </summary>
    /// <param name="declaration">The managed class, struct or enum root.</param>
    /// <param name="optional">Whether absent values reach the callback.</param>
    [TestMethod]
    [DataRow("public sealed class Value { }", false)]
    [DataRow("public sealed class Value { }", true)]
    [DataRow("public readonly record struct Value(int Number);", false)]
    [DataRow("public readonly record struct Value(int Number);", true)]
    [DataRow("public enum Value : ulong { First = 1, Last = ulong.MaxValue }", false)]
    [DataRow("public enum Value : ulong { First = 1, Last = ulong.MaxValue }", true)]
    public void DatumMappingsPreserveEveryScalarSetAndTableContract(string declaration, bool optional)
    {
        string suffix = optional ? "?" : string.Empty;
        const string binding = "Ankus.PgSqlType(\"int4\", Schema = \"pg_catalog\")";
        Compilation mapped = GenerateSqlControl(DatumMappingSource(declaration) + DatumMappingMethods("Value" + suffix, string.Empty));
        Compilation raw = GenerateSqlControl(DatumMappingMethods("Ankus.PgDatum" + suffix, binding));
        Assert.AreEqual(NormalizedDatumSql(raw), NormalizedDatumSql(mapped));
        string sql = ManifestValue(mapped, "Ankus.Sql");
        Assert.Contains("RETURNS TABLE (\"first\" \"pg_catalog\".\"int4\", \"second\" integer)", sql);
        Assert.Contains(optional ? " CALLED ON NULL INPUT " : " STRICT ", sql);
        Assert.AreEqual("true", ManifestValue(mapped, "Ankus.Relocatable"));
        string managed = DatumMappingManaged(mapped);
        Assert.Contains("ReadMapped<global::Value>()", managed);
        Assert.Contains("FromMapped<global::Value>(", managed);
        Assert.Contains("global::Ankus.PgTypeOrigin.External, typeof(global::Converter), static () => new global::Converter(), true, true);", managed);
        string native = ManifestValue(mapped, "Ankus.NativeSource");
        Assert.Contains("if (result.is_null && result.data == NULL)", native);
        Assert.Contains("ankus_read_polymorphic(fcinfo, 0", native);
        Assert.DoesNotContain("CREATE TYPE", sql);
    }

    /// <summary>
    /// Aggregate helper roles and manual operator/cast functions share the checked mapped datum path.
    /// </summary>
    [TestMethod]
    public void DatumMappingsCompileAggregateRolesOperatorsAndCasts()
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
                [Ankus.PgFunction, Ankus.PgCast]
                public static int Convert(INPUT TYPE value) => 7;
            }
            """;
        Compilation mapped = GenerateSqlControl(DatumMappingSource() + template.Replace("TYPE", "Value", StringComparison.Ordinal)
            .Replace("INPUT", string.Empty, StringComparison.Ordinal).Replace("OUTPUT", string.Empty, StringComparison.Ordinal));
        const string rawBinding = "Ankus.PgSqlType(\"int4\", Schema = \"pg_catalog\")";
        Compilation raw = GenerateSqlControl(template.Replace("TYPE", "Ankus.PgDatum", StringComparison.Ordinal)
            .Replace("INPUT", "[" + rawBinding + "]", StringComparison.Ordinal)
            .Replace("OUTPUT", "[return: " + rawBinding + "]", StringComparison.Ordinal));
        Assert.AreEqual(NormalizedDatumSql(raw), NormalizedDatumSql(mapped));
        string sql = ManifestValue(mapped, "Ankus.Sql");
        Assert.Contains("STYPE = \"pg_catalog\".\"int4\"", sql);
        Assert.Contains("MSTYPE = \"pg_catalog\".\"int4\"", sql);
        Assert.Contains("CREATE CAST (\"pg_catalog\".\"int4\" AS integer)", sql);
        Assert.Contains("LEFTARG = \"pg_catalog\".\"int4\", RIGHTARG = \"pg_catalog\".\"int4\"", sql);
        Assert.Contains("const bool polymorphic[2] = {true, false};", ManifestValue(mapped, "Ankus.NativeSource"));
        Assert.Contains("false, false, polymorphic, true);", ManifestValue(mapped, "Ankus.NativeSource"));
    }

    /// <summary>
    /// One-way converters are accepted precisely where their available direction is consumed.
    /// </summary>
    /// <param name="read">Whether only the reader is implemented.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void DatumMappingsRespectOneWayCapabilities(bool read)
    {
        string methods = read ? "public static int Consume(Value value) => 7;" :
            "public static System.Collections.Generic.IEnumerable<Value> Produce() => [default];";
        Compilation compilation = GenerateSqlControl(DatumMappingSource(reader: read, writer: !read) +
            "public static class Functions { [Ankus.PgFunction] " + methods + " }");
        Assert.Contains(read ? "\"value\" \"pg_catalog\".\"int4\")\nRETURNS integer" : "RETURNS SETOF \"pg_catalog\".\"int4\"",
            ManifestValue(compilation, "Ankus.Sql"));
        Assert.Contains(read ? "Converter(), true, false);" : "Converter(), false, true);", DatumMappingManaged(compilation));
    }

    /// <summary>
    /// Unsupported directions are rejected before any partial manifest or dispatcher is emitted.
    /// </summary>
    /// <param name="method">The consuming scalar or set declaration.</param>
    /// <param name="readOnly">Whether the converter lacks an output writer.</param>
    [TestMethod]
    [DataRow("public static Value Result() => default;", true)]
    [DataRow("public static System.Collections.Generic.IEnumerable<Value> Result() => [];", true)]
    [DataRow("public static System.Collections.Generic.IEnumerable<(int First, Value Second)> Result() => [];", true)]
    [DataRow("public static int Input(Value value) => 0;", false)]
    [DataRow("public static int Input(Value? value) => 0;", false)]
    public void DatumMappingsRejectUnavailableFunctionDirections(string method, bool readOnly)
        => AssertDatumMappingError(DatumMappingSource(reader: readOnly, writer: !readOnly) +
            "public static class Functions { [Ankus.PgFunction] " + method + " }", "ANKUS019",
            readOnly ? "writing SQL results" : "reading SQL arguments");

    /// <summary>
    /// Every selected aggregate helper is preflighted independently, including result-only and moving roles.
    /// </summary>
    /// <param name="role">The aggregate role containing the unavailable direction.</param>
    /// <param name="readOnly">Whether its result requires a missing writer.</param>
    [TestMethod]
    [DataRow("Transition", false)]
    [DataRow("Combine", false)]
    [DataRow("Final", true)]
    [DataRow("Serialize", false)]
    [DataRow("Deserialize", true)]
    [DataRow("MovingTransition", false)]
    [DataRow("MovingInverse", false)]
    [DataRow("MovingFinal", true)]
    public void DatumMappingsRejectUnavailableAggregateDirections(string role, bool readOnly)
    {
        string method = readOnly ? "public static Value " + role + "(int state) => default;" :
            "public static int " + role + "(Value state) => 0;";
        AssertDatumMappingError(DatumMappingSource(reader: readOnly, writer: !readOnly) +
            "[Ankus.PgAggregate] public static class Aggregate { " + method + " }", "ANKUS019",
            readOnly ? "writing SQL results" : "reading SQL arguments");
    }

    /// <summary>
    /// Nested arrays and other generic containers cannot fall through to codec or built-in element transport.
    /// </summary>
    /// <param name="method">The unsupported signature.</param>
    [TestMethod]
    [DataRow("public static int Read(Value[][] value) => 0;")]
    [DataRow("public static Value?[,] Read() => new Value?[0, 0];")]
    [DataRow("public static int Read(Ankus.PgArray<Value?[]> value) => 0;")]
    [DataRow("public static System.Collections.Generic.IEnumerable<Ankus.PgArray<Ankus.PgArray<Value>>> Read() => [];")]
    [DataRow("public static System.Collections.Generic.IEnumerable<(int First, System.Collections.Generic.List<Value> Second)> Read() => [];")]
    [DataRow("public static int Read(System.Collections.Generic.List<Value> value) => 0;")]
    public void DatumMappingsRejectUnsupportedContainers(string method)
        => AssertDatumMappingError(DatumMappingSource() + "public static class Functions { [Ankus.PgFunction] " + method + " }",
            "ANKUS019", "nested arrays and other containers are unsupported");

    /// <summary>
    /// Per-slot SQL metadata cannot override a reusable mapping, including renamed TABLE columns.
    /// </summary>
    /// <param name="method">The conflicting binding.</param>
    [TestMethod]
    [DataRow("public static int Read([Ankus.PgSqlType(\"text\")] Value value) => 0;")]
    [DataRow("[return: Ankus.PgCompositeType(\"pair\")] public static Value Read() => default;")]
    [DataRow("[return: Ankus.PgSqlType(\"text\", Column=\"renamed\"), Ankus.PgColumnNames(\"renamed\", \"number\")] public static System.Collections.Generic.IEnumerable<(Value Item, int Number)> Read() => [];")]
    public void DatumMappingsRejectSlotBindingOverrides(string method)
        => AssertDatumMappingError(DatumMappingSource() + "public static class Functions { [Ankus.PgFunction] " + method + " }",
            "ANKUS019", "cannot override their mapping");

    /// <summary>
    /// A separate raw TABLE column and opaque managed aggregate state retain their existing binding semantics.
    /// </summary>
    [TestMethod]
    public void DatumMappingsKeepUnrelatedRawColumnsAndManagedStateIndependent()
    {
        Compilation compilation = GenerateSqlControl(DatumMappingSource() + """
            public static class Functions
            {
                [Ankus.PgFunction]
                [return: Ankus.PgSqlType("text", Schema = "pg_catalog")]
                public static System.Collections.Generic.IEnumerable<(Value Mapped, Ankus.PgDatum Raw)> Rows() => [];
            }
            [Ankus.PgAggregate]
            public static class Total
            {
                public static Ankus.PgAggregateState<Value> Transition(Ankus.PgAggregateState<Value>? state, int value) => state!;
                public static int Final(Ankus.PgAggregateState<Value>? state) => 7;
            }
            """);
        Assert.Contains("RETURNS TABLE (\"mapped\" \"pg_catalog\".\"int4\", \"raw\" \"pg_catalog\".\"text\")", ManifestValue(compilation, "Ankus.Sql"));
        Assert.Contains("STYPE = internal", ManifestValue(compilation, "Ankus.Sql"));
    }

    /// <summary>
    /// Converter declarations require exact interfaces and a safely callable closed constructor.
    /// </summary>
    /// <param name="converter">The invalid converter declaration or type expression.</param>
    /// <param name="reason">The independently expected diagnostic reason.</param>
    [TestMethod]
    [DataRow("public abstract class Converter : Ankus.IPgDatumReader<Value> { public abstract Value Read(Ankus.PgDatum value); }", "accessible parameterless constructor")]
    [DataRow("public class Converter : Ankus.IPgDatumReader<Value> { private Converter() { } public Value Read(Ankus.PgDatum value) => default; }", "accessible parameterless constructor")]
    [DataRow("public class Converter(int number) : Ankus.IPgDatumReader<Value> { public Value Read(Ankus.PgDatum value) => default; }", "accessible parameterless constructor")]
    [DataRow("public class Converter { }", "exact non-nullable managed type")]
    [DataRow("public class Converter : Ankus.IPgDatumReader<int> { public int Read(Ankus.PgDatum value) => 0; }", "exact non-nullable managed type")]
    [DataRow("public class Parent { public required string Prefix { get; init; } } public class Converter : Parent, Ankus.IPgDatumReader<Value> { public Value Read(Ankus.PgDatum value) => default; }", "SetsRequiredMembers")]
    public void DatumMappingsRejectInvalidConverterDeclarations(string converter, string reason)
        => AssertDatumMappingError("[Ankus.PgDatumType(\"int4\", typeof(Converter), Origin = Ankus.PgTypeOrigin.External, Schema = \"pg_catalog\")] public struct Value { } " +
            converter, "ANKUS019", reason);

    /// <summary>
    /// Unsupported wrapper shapes, conflicting metadata and non-named/open converter selections fail at their declarations.
    /// </summary>
    /// <param name="declaration">The invalid root declaration.</param>
    /// <param name="converterType">The attribute's converter type expression.</param>
    [TestMethod]
    [DataRow("public abstract class Value { }", "Converter")]
    [DataRow("public ref struct Value { }", "Converter")]
    [DataRow("[Ankus.PgType] public struct Value { public int Number; }", "Converter")]
    [DataRow("[Ankus.PgEnum] public enum Value { First }", "Converter")]
    [DataRow("public struct Value { }", "Converter[]")]
    [DataRow("public struct Value { }", "GenericConverter<>")]
    public void DatumMappingsRejectUnsupportedRootContracts(string declaration, string converterType)
    {
        string source = "[Ankus.PgDatumType(\"item\", typeof(" + converterType + "))] " + declaration;
        source += " public class Converter { } public class GenericConverter<T> { }";
        AssertDatumMappingError(source, "ANKUS019", converterType is "Converter[]" or "GenericConverter<>" ?
            "accessible parameterless constructor" : "closed, concrete");
    }

    /// <summary>
    /// Invalid identifiers and origin choices are independent of converter and SQL provider construction.
    /// </summary>
    /// <param name="options">The invalid mapping metadata.</param>
    /// <param name="reason">The expected diagnostic reason.</param>
    [TestMethod]
    [DataRow("null, typeof(Converter)", "valid Unicode")]
    [DataRow("\"\", typeof(Converter)", "valid Unicode")]
    [DataRow("\"bad\\0name\", typeof(Converter)", "valid Unicode")]
    [DataRow("\"bad\\uD800name\", typeof(Converter)", "valid Unicode")]
    [DataRow("\"item\", typeof(Converter), Schema=\"\"", "valid Unicode")]
    [DataRow("\"item\", typeof(Converter), Origin=(Ankus.PgTypeOrigin)17", "Origin must be")]
    [DataRow("\"item\", typeof(Converter), Origin=Ankus.PgTypeOrigin.External", "explicit Schema")]
    public void DatumMappingsRejectInvalidMetadata(string options, string reason)
        => AssertDatumMappingError("[Ankus.PgDatumType(" + options + ")] public struct Value { } " + DatumConverter(), "ANKUS019", reason);

    /// <summary>
    /// Managed and catalog providers are aliases only for the same block, regardless of attribute order.
    /// </summary>
    /// <param name="reverse">Whether claims appear in reverse declaration order.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void DatumMappingProvidersShareOneCatalogDeclaration(bool reverse)
    {
        string[] providers =
        [
            "[assembly: Ankus.PgSqlTypeProvider(\"types\", typeof(Value))]",
            "[assembly: Ankus.PgSqlTypeProvider(\"types\", typeof(Other))]",
            "[assembly: Ankus.PgSqlTypeProvider(\"types\", \"item\")]",
        ];
        Compilation compilation = GenerateSqlControl("[assembly: Ankus.PgSql(\"types\", \"SELECT 'provider';\", Relocatable=true)]" +
            string.Concat(reverse ? providers.Reverse() : providers) + DatumMappingSource(external: false, name: "item") +
            DatumMappingSource("public struct Other { }", external: false, name: "item", type: "Other") + """
            public static class Functions
            {
                [Ankus.PgFunction(Sql = "SELECT 'first';", SqlRelocatable = true)] public static Value A() => default;
                [Ankus.PgFunction(Sql = "SELECT 'second';", SqlRelocatable = true)] public static Other B() => default;
                [Ankus.PgFunction(Sql = "SELECT 'raw';", SqlRelocatable = true)] public static int C([Ankus.PgSqlType("item")] Ankus.PgDatum value) => 7;
            }
            """);
        Assert.AreEqual("SELECT 'provider';\nSELECT 'first';\nSELECT 'second';\nSELECT 'raw';\n", ManifestValue(compilation, "Ankus.Sql"));
        Assert.AreEqual("true", ManifestValue(compilation, "Ankus.Relocatable"));
        Assert.Contains("RegisterValue<global::Value>(\"item\", null", DatumMappingManaged(compilation));
        Assert.Contains("RegisterValue<global::Other>(\"item\", null", DatumMappingManaged(compilation));
    }

    /// <summary>
    /// Ownership requires an exact managed provider even when a catalog name is already declared or built in.
    /// </summary>
    /// <param name="claims">The invalid provider inventory.</param>
    /// <param name="external">Whether the mapping belongs outside this extension.</param>
    /// <param name="reason">The expected graph diagnostic reason.</param>
    [TestMethod]
    [DataRow("", false, "requires a PgSqlTypeProvider naming its managed identity")]
    [DataRow("[assembly: Ankus.PgSqlTypeProvider(\"one\", \"int4\", Schema=\"pg_catalog\")]", false, "requires a PgSqlTypeProvider naming its managed identity")]
    [DataRow("[assembly: Ankus.PgSqlTypeProvider(\"one\", typeof(Value))]", true, "External datum mappings cannot")]
    [DataRow("[assembly: Ankus.PgSqlTypeProvider(\"one\", typeof(Value))] [assembly: Ankus.PgSqlTypeProvider(\"one\", typeof(Value))]", false, "more than one provider")]
    [DataRow("[assembly: Ankus.PgSqlTypeProvider(\"one\", typeof(Value))] [assembly: Ankus.PgSqlTypeProvider(\"two\", \"int4\", Schema=\"pg_catalog\")]", false, "more than one provider")]
    [DataRow("[assembly: Ankus.PgSqlTypeProvider(\"one\", typeof(Value), Schema=\"pg_catalog\")]", false, "cannot specify Schema")]
    [DataRow("[assembly: Ankus.PgSqlTypeProvider(\"absent\", typeof(Value))]", false, "must name a PgSql or PgSqlFile block")]
    [DataRow("[assembly: Ankus.PgSqlTypeProvider(\"one\", typeof(int))]", true, "registered PgDatumType")]
    [DataRow("[assembly: Ankus.PgSqlTypeProvider(\"one\", (System.Type)null!)]", true, "registered PgDatumType")]
    public void DatumMappingProvidersRejectInvalidOwnership(string claims, bool external, string reason)
        => AssertDatumMappingError("[assembly: Ankus.PgSql(\"one\", \"SELECT 1;\")] [assembly: Ankus.PgSql(\"two\", \"SELECT 2;\")]" +
            claims + DatumMappingSource(external: external, schema: "pg_catalog"), "ANKUS005", reason);

    /// <summary>
    /// Generated type identities stay reserved under every SQL policy when a mapped provider claims the same catalog name.
    /// </summary>
    /// <param name="enumType">Whether the existing declaration is a native enum.</param>
    /// <param name="options">The existing declaration's SQL generation policy.</param>
    [TestMethod]
    [DataRow(false, "")]
    [DataRow(false, ", GenerateSql=false")]
    [DataRow(false, ", Sql=\"SELECT 0;\"")]
    [DataRow(true, "")]
    [DataRow(true, ", GenerateSql=false")]
    [DataRow(true, ", Sql=\"SELECT 0;\"")]
    public void DatumMappingProvidersPreserveGeneratedReservations(bool enumType, string options)
    {
        string generated = enumType ? "[Ankus.PgEnum(Name=\"item\"" + options + ")] public enum Existing { A }" :
            "[Ankus.PgType(Name=\"item\"" + options + ")] public record Existing(int Number);";
        AssertDatumMappingError("[assembly: Ankus.PgSql(\"types\", \"SELECT 1;\")] [assembly: Ankus.PgSqlTypeProvider(\"types\", typeof(Value))]" +
            DatumMappingSource(external: false, name: "item") + generated, "ANKUS005", "including generated type or enum declarations");
    }

    /// <summary>
    /// Owned schemas order their provider after actual creation prerequisites while external schemas remain relocation neutral.
    /// </summary>
    [TestMethod]
    public void DatumMappingSchemasAndExternalOriginsRemainIndependent()
    {
        Compilation owned = GenerateSqlControl("""
            [assembly: Ankus.PgSql("z", "SELECT 'prerequisite';", Relocatable=true)]
            [assembly: Ankus.PgSql("types", "SELECT 'provider';", Relocatable=true)]
            [assembly: Ankus.PgSqlTypeProvider("types", typeof(Value))]
            [Ankus.PgSchema("placed", Requires=["z"])] public static class Placement { }
            """ + DatumMappingSource(external: false, schema: "placed") +
            "public static class Functions { [Ankus.PgFunction(Sql=\"SELECT 'consumer';\", SqlRelocatable=true)] public static Value Read()=>default; }");
        Assert.AreEqual("SELECT 'prerequisite';\nCREATE SCHEMA IF NOT EXISTS \"placed\";\nSELECT 'provider';\nSELECT 'consumer';\n",
            ManifestValue(owned, "Ankus.Sql"));
        Assert.AreEqual("false", ManifestValue(owned, "Ankus.Relocatable"));
        Compilation external = GenerateSqlControl("[assembly: Ankus.PgSql(\"unused\", \"SELECT 'provider';\", Relocatable=true)]" +
            "[assembly: Ankus.PgSqlTypeProvider(\"unused\", \"int4\", Schema=\"pg_catalog\")]" + DatumMappingSource() +
            "public static class Functions { [Ankus.PgFunction(Sql=\"SELECT 'external';\", SqlRelocatable=true)] public static Value Read()=>default; }");
        Assert.AreEqual("SELECT 'external';\nSELECT 'provider';\n", ManifestValue(external, "Ankus.Sql"));
    }

    /// <summary>
    /// Managed providers retain precise shell/completion deferral without accepting a hard explicit cycle.
    /// </summary>
    /// <param name="cycle">Whether the explicit requirements themselves form a cycle.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void DatumMappingProvidersPreserveExplicitShellOrdering(bool cycle)
    {
        string source = "[assembly: Ankus.PgSql(\"shell\", \"SELECT 'shell';\", Relocatable=true" +
            (cycle ? ", Requires=[\"complete\"]" : string.Empty) + ")]" + """
            [assembly: Ankus.PgSql("complete", "SELECT 'complete';", Requires=["input"], Relocatable=true)]
            [assembly: Ankus.PgSqlTypeProvider("complete", typeof(Value))]
            """ + DatumMappingSource(external: false, name: "item") + """
            public static class Functions
            {
                [Ankus.PgFunction(Id="input", Requires=["shell"], Sql="SELECT 'input';", SqlRelocatable=true)] public static Value Input() => default;
                [Ankus.PgFunction(Sql="SELECT 'consumer';", SqlRelocatable=true)] public static int Read(Value value) => 7;
            }
            """;
        if (cycle)
        {
            AssertDatumMappingError(source, "ANKUS005", "cycle");
        }
        else
        {
            Assert.AreEqual("SELECT 'shell';\nSELECT 'input';\nSELECT 'complete';\nSELECT 'consumer';\n",
                ManifestValue(GenerateSqlControl(source), "Ankus.Sql"));
        }
    }

    /// <summary>
    /// Distinct result-only providers cannot be masked by an input or another TABLE column using the same mapping.
    /// </summary>
    [TestMethod]
    public void DatumMappingProvidersVisitIndependentTableAndAggregateResults()
    {
        Compilation compilation = GenerateSqlControl("""
            [assembly: Ankus.PgSql("one", "SELECT 'one';", Relocatable=true)]
            [assembly: Ankus.PgSql("two", "SELECT 'two';", Relocatable=true)]
            [assembly: Ankus.PgSqlTypeProvider("one", typeof(Value))]
            [assembly: Ankus.PgSqlTypeProvider("two", typeof(Other))]
            """ + DatumMappingSource(external: false, name: "first") +
            DatumMappingSource("public struct Other { }", external: false, name: "second", type: "Other") + """
            public static class Functions
            {
                [Ankus.PgFunction(Sql="SELECT 'table';", SqlRelocatable=true)]
                public static System.Collections.Generic.IEnumerable<(Value First, Other Second)> Rows() => [];
            }
            [Ankus.PgAggregate(InitialCondition="0")]
            public static class Total
            {
                public static int Transition(int state, int input) => state + input;
                public static Other Final(int state) => default;
            }
            """);
        string sql = ManifestValue(compilation, "Ankus.Sql");
        AssertSqlControlBefore(sql, "SELECT 'one';", "SELECT 'table';");
        AssertSqlControlBefore(sql, "SELECT 'two';", "SELECT 'table';");
        AssertSqlControlBefore(sql, "SELECT 'two';", "CREATE FUNCTION \"total_final\"");
        Assert.Contains("RETURNS \"second\" AS", sql);
        Assert.Contains("STYPE = integer", sql);
    }

    /// <summary>
    /// Module registration executes without constructing a user converter or requiring a backend, even for SPI-only roots.
    /// </summary>
    [TestMethod]
    public void DatumMappingRegistrationRemainsLazyForSpiOnlyRoots()
    {
        Compilation compilation = GenerateSqlControl("""
            [Ankus.PgDatumType("text", typeof(Converter), Origin=Ankus.PgTypeOrigin.External, Schema="pg_catalog")]
            public sealed class Value { }
            public abstract class Parent : Ankus.IPgDatumReader<Value>
            {
                public required string Prefix { get; init; }
                Value Ankus.IPgDatumReader<Value>.Read(Ankus.PgDatum value) => new();
            }
            public sealed class Converter : Parent
            {
                public static int Constructions;
                [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
                internal Converter() { Constructions++; Prefix="initialized"; throw new System.InvalidOperationException("factory"); }
            }
            public static class Probe { public static int Run() => Converter.Constructions; }
            """);
        Assert.AreEqual("-- No installable objects declared.\n", ManifestValue(compilation, "Ankus.Sql"));
        Assert.AreEqual("Pg_magic_func\n", ManifestValue(compilation, "Ankus.Exports"));
        Assert.Contains("Converter(), true, false);", DatumMappingManaged(compilation));
        using var stream = new MemoryStream();
        Microsoft.CodeAnalysis.Emit.EmitResult emitted = compilation.Emit(stream, cancellationToken: context.CancellationToken);
        Assert.IsTrue(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        stream.Position = 0;
        var load = new AssemblyLoadContext("DatumMappingRegistrationProbe", isCollectible: true);
        try
        {
            Assembly assembly = load.LoadFromStream(stream);
            MethodInfo run = assembly.GetType("Probe", throwOnError: true)!.GetMethod("Run")!;
            Assert.AreEqual(0, Assert.IsInstanceOfType<int>(run.Invoke(null, null)));
        }
        finally
        {
            load.Unload();
        }
    }

    /// <summary>
    /// Closed generic and value-type converters may implement inherited or explicit exact contracts.
    /// </summary>
    /// <param name="valueConverter">Whether the converter is a boxed struct instead of a closed generic class.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void DatumMappingsAcceptClosedAndExplicitConverterContracts(bool valueConverter)
    {
        string converter = valueConverter ? "Converter" : "Converter<Value>";
        string declaration = valueConverter ? "public struct Converter : Ankus.IPgDatumReader<Value> { Value Ankus.IPgDatumReader<Value>.Read(Ankus.PgDatum value) => default; }" :
            "public abstract class Base<T> : Ankus.IPgDatumReader<T> { T Ankus.IPgDatumReader<T>.Read(Ankus.PgDatum value) => default!; } public sealed class Converter<T> : Base<T> { }";
        Compilation compilation = GenerateSqlControl("[Ankus.PgDatumType(\"int4\", typeof(" + converter +
            "), Origin=Ankus.PgTypeOrigin.External, Schema=\"pg_catalog\")] public struct Value { } " + declaration +
            "public static class Functions { [Ankus.PgFunction] public static int Read(Value value) => 7; }");
        Assert.Contains("static () => new global::" + (valueConverter ? "Converter" : "Converter<global::Value>") + "(), true, false);",
            DatumMappingManaged(compilation));
    }

    /// <summary>
    /// Nullable reference interfaces cannot claim to read or write a present non-null mapped value.
    /// </summary>
    /// <param name="reader">Whether the nullable contract is the read direction.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void DatumMappingsRejectNullableConverterContracts(bool reader)
        => AssertDatumMappingError("[Ankus.PgDatumType(\"text\", typeof(Converter), Origin=Ankus.PgTypeOrigin.External, Schema=\"pg_catalog\")] public class Value { } " +
            DatumConverter("Value?", reader: reader, writer: !reader), "ANKUS019", "exact non-nullable managed type");

    /// <summary>
    /// A shared converter's interfaces for another wrapper do not supply the missing direction for the requested wrapper.
    /// </summary>
    /// <param name="invalidDirection">Zero accepts both supported paths; one reads the writer-only root; two writes the reader-only root.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public void DatumMappingsSelectExactInterfacesFromSharedConverters(int invalidDirection)
    {
        const string declarations = """
            [Ankus.PgDatumType("int4", typeof(Converter), Origin=Ankus.PgTypeOrigin.External, Schema="pg_catalog")]
            public struct First { }
            [Ankus.PgDatumType("int4", typeof(Converter), Origin=Ankus.PgTypeOrigin.External, Schema="pg_catalog")]
            public struct Second { }
            public sealed class Converter : Ankus.IPgDatumReader<First>, Ankus.IPgDatumWriter<Second>
            {
                First Ankus.IPgDatumReader<First>.Read(Ankus.PgDatum value) => default;
                Ankus.PgDatum Ankus.IPgDatumWriter<Second>.Write(Second value, uint oid, Ankus.PgMemoryContext destination)
                    => throw new System.InvalidOperationException();
            }
            """;
        string method = invalidDirection switch
        {
            0 => "public static Second Echo(First value) => default;",
            1 => "public static int Read(Second value) => 0;",
            _ => "public static First Write() => default;",
        };
        string source = declarations + "public static class Functions { [Ankus.PgFunction] " + method + " }";
        if (invalidDirection != 0)
        {
            AssertDatumMappingError(source, "ANKUS019", invalidDirection == 1 ? "reading SQL arguments" : "writing SQL results");
        }
        else
        {
            Compilation compilation = GenerateSqlControl(source);
            Assert.Contains("RegisterValue<global::First>(\"int4\", \"pg_catalog\", global::Ankus.PgTypeOrigin.External, typeof(global::Converter), static () => new global::Converter(), true, false);", DatumMappingManaged(compilation));
            Assert.Contains("RegisterValue<global::Second>(\"int4\", \"pg_catalog\", global::Ankus.PgTypeOrigin.External, typeof(global::Converter), static () => new global::Converter(), false, true);", DatumMappingManaged(compilation));
            Assert.Contains("\"value\" \"pg_catalog\".\"int4\")\nRETURNS \"pg_catalog\".\"int4\"", ManifestValue(compilation, "Ankus.Sql"));
        }
    }

    /// <summary>
    /// Catalog identifier limits count UTF-8 bytes and preserve quoted case and Unicode exactly.
    /// </summary>
    /// <param name="schema">Whether the boundary is tested on the schema identifier.</param>
    /// <param name="valid">Whether the identifier fits in exactly 63 UTF-8 bytes.</param>
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void DatumMappingIdentifiersUseExactUtf8Boundaries(bool schema, bool valid)
    {
        string identifier = new string('é', 31) + (valid ? "X" : "é");
        string source = DatumMappingSource(name: schema ? "Mixed \" Name" : identifier, schema: schema ? identifier : "pg_catalog") +
            "public static class Functions { [Ankus.PgFunction] public static Value Read() => default; }";
        if (!valid)
        {
            AssertDatumMappingError(source, "ANKUS019", "63 UTF-8 bytes");
        }
        else
        {
            Compilation compilation = GenerateSqlControl(source);
            Assert.Contains("RETURNS " + (schema ? "\"" + identifier + "\".\"Mixed \"\" Name\"" : "\"pg_catalog\".\"" + identifier + "\"") + " AS",
                ManifestValue(compilation, "Ankus.Sql"));
            Assert.AreEqual("true", ManifestValue(compilation, "Ankus.Relocatable"));
        }
    }

    /// <summary>
    /// A referenced annotation is registered only when a signature or managed provider explicitly selects it.
    /// </summary>
    /// <param name="providerOnly">Whether only a managed provider selects the referenced root.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void DatumMappingsDiscoverReferencedClosedRoots(bool providerOnly)
    {
        string dependencySource = DatumMappingSource(external: !providerOnly, name: "item");
        MetadataReference dependency = DatumMappingReference("MappedDependency", dependencySource);
        string source = providerOnly ? "[assembly: Ankus.PgSql(\"types\", \"SELECT 'provider';\", Relocatable=true)] [assembly: Ankus.PgSqlTypeProvider(\"types\", typeof(Value))]" :
            "public static class Functions { [Ankus.PgFunction] public static Value Read(Value value) => value; }";
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = GenerateDatumMappingReference(source, [dependency]);
        AssertSqlControlCompilation(compilation, diagnostics);
        Assert.Contains("RegisterValue<global::Value>(", DatumMappingManaged(compilation));
        if (providerOnly)
        {
            Assert.AreEqual("SELECT 'provider';\n", ManifestValue(compilation, "Ankus.Sql"));
        }
        else
        {
            Assert.Contains("\"value\" \"pg_catalog\".\"item\")\nRETURNS \"pg_catalog\".\"item\"", ManifestValue(compilation, "Ankus.Sql"));
        }

        (Compilation unrelated, ImmutableArray<Diagnostic> unrelatedErrors) = GenerateDatumMappingReference(
            "public static class Functions { [Ankus.PgFunction] public static int Read() => 7; }", [dependency]);
        AssertSqlControlCompilation(unrelated, unrelatedErrors);
        Assert.DoesNotContain("PgDatumRegistry.Register", DatumMappingManaged(unrelated));
    }

    /// <summary>
    /// Two generated assemblies may register one referenced mapping without constructing or replacing its shared converter.
    /// </summary>
    [TestMethod]
    public void DatumMappingsInitializeGeneratedDependenciesAndConsumersTogether()
    {
        Compilation dependency = GenerateSqlControl("""
            [Ankus.PgDatumType("int4", typeof(Converter), Origin=Ankus.PgTypeOrigin.External, Schema="pg_catalog")]
            public struct Value { }
            public sealed class Converter : Ankus.IPgDatumReader<Value>
            {
                public static int Constructions;
                public Converter() { Constructions++; throw new System.InvalidOperationException("must stay lazy"); }
                public Value Read(Ankus.PgDatum value) => default;
            }
            """).WithAssemblyName("GeneratedDatumDependency");
        byte[] dependencyImage = EmitDatumMappingImage(dependency);
        (Compilation consumer, ImmutableArray<Diagnostic> diagnostics) = GenerateDatumMappingReference("""
            public static class Functions { [Ankus.PgFunction] public static int Read(Value value) => 7; }
            public static class Probe
            {
                public static int Run()
                {
                    try
                    {
                        Ankus.PgDatumRegistry.RegisterValue<Value>("conflicting", "pg_catalog", Ankus.PgTypeOrigin.External,
                            typeof(Converter), static () => new Converter(), true, false);
                        return -1;
                    }
                    catch (System.InvalidOperationException)
                    {
                        return Converter.Constructions;
                    }
                }
            }
            """, [MetadataReference.CreateFromImage(dependencyImage)]);
        AssertSqlControlCompilation(consumer, diagnostics);
        Assert.Contains("RegisterValue<global::Value>(", DatumMappingManaged(dependency));
        Assert.Contains("RegisterValue<global::Value>(", DatumMappingManaged(consumer));
        var load = new AssemblyLoadContext("GeneratedDatumDependencies", isCollectible: true);
        try
        {
            using var dependencyStream = new MemoryStream(dependencyImage);
            using var consumerStream = new MemoryStream(EmitDatumMappingImage(consumer));
            Assembly dependencyAssembly = load.LoadFromStream(dependencyStream);
            Assembly consumerAssembly = load.LoadFromStream(consumerStream);
            System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(dependencyAssembly.ManifestModule.ModuleHandle);
            System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(consumerAssembly.ManifestModule.ModuleHandle);
            MethodInfo run = consumerAssembly.GetType("Probe", throwOnError: true)!.GetMethod("Run")!;
            Assert.AreEqual(0, Assert.IsInstanceOfType<int>(run.Invoke(null, null)));
        }
        finally
        {
            load.Unload();
        }
    }

    /// <summary>
    /// Accessibility is checked from the consuming extension, including friend-only converter constructors and arguments.
    /// </summary>
    /// <param name="boundary">The inaccessible part of the closed converter.</param>
    /// <param name="friend">Whether the dependency grants access to the consuming assembly.</param>
    [TestMethod]
    [DataRow("type", false)]
    [DataRow("type", true)]
    [DataRow("constructor", false)]
    [DataRow("constructor", true)]
    [DataRow("argument", false)]
    [DataRow("argument", true)]
    public void DatumMappingsHonorReferencedFriendAccessibility(string boundary, bool friend)
    {
        string friendship = friend ? "[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(\"GeneratorTest\")]" : string.Empty;
        MetadataReference dependency = DatumMappingReference("MappedFriends", friendship + """
            [Ankus.PgDatumType("int4", typeof(Converter<Value, Marker>), Origin=Ankus.PgTypeOrigin.External, Schema="pg_catalog")]
            public struct Value { }
            """ + (boundary == "argument" ? "internal" : "public") + " class Marker { } " +
            (boundary == "type" ? "internal" : "public") + " class Converter<T, TMarker> : Ankus.IPgDatumReader<T> { " +
            (boundary == "constructor" ? "internal" : "public") + " Converter() { } public T Read(Ankus.PgDatum value) => default!; }");
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = GenerateDatumMappingReference(
            "public static class Functions { [Ankus.PgFunction] public static int Read(Value value) => 7; }", [dependency]);
        if (friend)
        {
            AssertSqlControlCompilation(compilation, diagnostics);
            Assert.Contains("new global::Converter<global::Value, global::Marker>()", DatumMappingManaged(compilation));
        }
        else
        {
            Diagnostic diagnostic = Assert.ContainsSingle(diagnostics);
            Assert.AreEqual("ANKUS019", diagnostic.Id);
            Assert.Contains("accessible parameterless constructor", diagnostic.GetMessage(CultureInfo.InvariantCulture));
            Assert.IsNull(compilation.GetTypeByMetadataName("Ankus.Generated.ExtensionDispatchers"));
            Assert.IsEmpty(compilation.GetDiagnostics(context.CancellationToken).Where(static item => item.Severity == DiagnosticSeverity.Error));
        }
    }

    /// <summary>
    /// Extern-alias-only roots cannot be emitted as an ambiguous global qualified managed name.
    /// </summary>
    [TestMethod]
    public void DatumMappingsRejectExternAliasOnlyContracts()
    {
        PortableExecutableReference dependency = DatumMappingReference("AliasedMapping", DatumMappingSource());
        dependency = dependency.WithAliases(["mapped"]);
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = GenerateDatumMappingReference("""
            extern alias mapped;
            public static class Functions { [Ankus.PgFunction] public static int Read(mapped::Value value) => 7; }
            """, [dependency]);
        Diagnostic diagnostic = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual("ANKUS019", diagnostic.Id);
        Assert.Contains("extern-alias-only contracts are unsupported", diagnostic.GetMessage(CultureInfo.InvariantCulture));
        Assert.IsNull(compilation.GetTypeByMetadataName("Ankus.Generated.ExtensionDispatchers"));
    }

    /// <summary>
    /// A nested converter's closed containing arguments obey the same global-name rules as direct arguments.
    /// </summary>
    /// <param name="aliasOnly">Whether the containing generic argument is visible only through an extern alias.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void DatumMappingsValidateContainingConverterArguments(bool aliasOnly)
    {
        PortableExecutableReference dependency = DatumMappingReference("ConverterMarker", "public sealed class Marker { }");
        if (aliasOnly)
        {
            dependency = dependency.WithAliases(["hidden"]);
        }

        string source = (aliasOnly ? "extern alias hidden;" : string.Empty) +
            "[Ankus.PgDatumType(\"int4\", typeof(Outer<" + (aliasOnly ? "hidden::Marker" : "Marker") +
            ">.Converter), Origin=Ankus.PgTypeOrigin.External, Schema=\"pg_catalog\")] public struct Value { } " + """
            public class Outer<T>
            {
                public sealed class Converter : Ankus.IPgDatumReader<Value>
                {
                    public Value Read(Ankus.PgDatum value) => default;
                }
            }
            public static class Functions { [Ankus.PgFunction] public static int Read(Value value) => 7; }
            """;
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = GenerateDatumMappingReference(source, [dependency]);
        if (aliasOnly)
        {
            Diagnostic diagnostic = Assert.ContainsSingle(diagnostics);
            Assert.AreEqual("ANKUS019", diagnostic.Id);
            Assert.Contains("extern-alias-only contracts are unsupported", diagnostic.GetMessage(CultureInfo.InvariantCulture));
            Assert.IsNull(compilation.GetTypeByMetadataName("Ankus.Generated.ExtensionDispatchers"));
        }
        else
        {
            AssertSqlControlCompilation(compilation, diagnostics);
            Assert.Contains("new global::Outer<global::Marker>.Converter()", DatumMappingManaged(compilation));
        }
    }

    /// <summary>
    /// A tracked SQL file and changed mapping metadata invalidate one reused driver independently.
    /// </summary>
    [TestMethod]
    public void DatumMappingFilesAndMetadataInvalidateIncrementalOutput()
    {
        string project = Path.Combine(AppContext.BaseDirectory, "mapping-project");
        var original = new SqlInput(Path.Combine(project, "types.sql"), "SELECT 'original';");
        var changed = new SqlInput(original.Path, "SELECT 'changed';");
        string Source(string name, string? schema) => "[assembly: Ankus.PgSqlFile(\"types\", \"types.sql\", Relocatable=true)]" +
            "[assembly: Ankus.PgSqlTypeProvider(\"types\", typeof(Value))]" + DatumMappingSource(external: false, name: name, schema: schema) +
            "public static class Functions { [Ankus.PgFunction] public static Value Read() => default; }";
        CSharpCompilation input = CSharpCompilation.Create("GeneratorTest", [CSharpSyntaxTree.ParseText(Source("first", null), cancellationToken: context.CancellationToken)], s_references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, nullableContextOptions: NullableContextOptions.Enable));
        GeneratorDriver driver = CSharpGeneratorDriver.Create([new PgFunctionGenerator().AsSourceGenerator()], [original], optionsProvider: new SqlOptions(project));
        driver = driver.RunGeneratorsAndUpdateCompilation(input, out Compilation before, out ImmutableArray<Diagnostic> errors, context.CancellationToken);
        AssertSqlControlCompilation(before, errors);
        Assert.StartsWith("SELECT 'original';\n", ManifestValue(before, "Ankus.Sql"));
        Assert.Contains("RETURNS \"first\" AS", ManifestValue(before, "Ankus.Sql"));
        Assert.AreEqual("true", ManifestValue(before, "Ankus.Relocatable"));
        input = input.ReplaceSyntaxTree(input.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(Source("Second", "placed"), cancellationToken: context.CancellationToken));
        driver = driver.RunGeneratorsAndUpdateCompilation(input, out Compilation metadata, out errors, context.CancellationToken);
        AssertSqlControlCompilation(metadata, errors);
        Assert.Contains("RETURNS \"placed\".\"Second\" AS", ManifestValue(metadata, "Ankus.Sql"));
        Assert.Contains("RegisterValue<global::Value>(\"Second\", \"placed\"", DatumMappingManaged(metadata));
        Assert.AreEqual("false", ManifestValue(metadata, "Ankus.Relocatable"));
        driver.ReplaceAdditionalText(original, changed).RunGeneratorsAndUpdateCompilation(input, out Compilation file, out errors, context.CancellationToken);
        AssertSqlControlCompilation(file, errors);
        Assert.AreEqual(ManifestValue(metadata, "Ankus.Sql").Replace("SELECT 'original';", "SELECT 'changed';", StringComparison.Ordinal),
            ManifestValue(file, "Ankus.Sql"));
        AssertSqlControlBoundary(metadata, file);
    }

    /// <summary>
    /// Emits a dependency without running its generator to model a referenced annotation discovered by the consumer.
    /// </summary>
    private PortableExecutableReference DatumMappingReference(string name, string source)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(name, [CSharpSyntaxTree.ParseText(source, cancellationToken: context.CancellationToken)], s_references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        return MetadataReference.CreateFromImage(EmitDatumMappingImage(compilation));
    }

    /// <summary>
    /// Emits exact compilation bytes for reference and executed-module tests.
    /// </summary>
    private byte[] EmitDatumMappingImage(Compilation compilation)
    {
        using var stream = new MemoryStream();
        Microsoft.CodeAnalysis.Emit.EmitResult result = compilation.Emit(stream, cancellationToken: context.CancellationToken);
        Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        return stream.ToArray();
    }

    /// <summary>
    /// Runs the generator with explicit additional metadata references and preserves its diagnostics for either outcome.
    /// </summary>
    private (Compilation Compilation, ImmutableArray<Diagnostic> Diagnostics) GenerateDatumMappingReference(string source, MetadataReference[] references)
    {
        CSharpCompilation input = CSharpCompilation.Create("GeneratorTest", [CSharpSyntaxTree.ParseText(source, cancellationToken: context.CancellationToken)], s_references.AddRange(references),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, nullableContextOptions: NullableContextOptions.Enable));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new PgFunctionGenerator().AsSourceGenerator());
        driver.RunGeneratorsAndUpdateCompilation(input, out Compilation output, out ImmutableArray<Diagnostic> diagnostics, context.CancellationToken);
        return (output, diagnostics);
    }

    /// <summary>
    /// Builds a mapped root with independent structural reader/writer interfaces.
    /// </summary>
    private static string DatumMappingSource(string declaration = "public struct Value { }", bool reader = true, bool writer = true,
        bool external = true, string name = "int4", string? schema = null, string type = "Value")
    {
        schema ??= external ? "pg_catalog" : null;
        string converter = type == "Value" ? "Converter" : type + "Converter";
        return "[Ankus.PgDatumType(" + SymbolDisplay.FormatLiteral(name, true) + ", typeof(" + converter + ")" +
            (external ? ", Origin=Ankus.PgTypeOrigin.External" : string.Empty) +
            (schema is null ? string.Empty : ", Schema=" + SymbolDisplay.FormatLiteral(schema, true)) + ")] " + declaration +
            DatumConverter(type, converter, reader, writer);
    }

    /// <summary>
    /// Supplies a converter whose bodies are irrelevant to signature and graph compilation.
    /// </summary>
    private static string DatumConverter(string type = "Value", string name = "Converter", bool reader = true, bool writer = true)
        => "public sealed class " + name + " : " + string.Join(", ", new[]
        {
            reader ? "Ankus.IPgDatumReader<" + type + ">" : string.Empty,
            writer ? "Ankus.IPgDatumWriter<" + type + ">" : string.Empty,
        }.Where(static value => value.Length != 0)) + " { " +
            (reader ? "public " + type + " Read(Ankus.PgDatum value) => default!;" : string.Empty) +
            (writer ? "public Ankus.PgDatum Write(" + type + " value, uint typeOid, Ankus.PgMemoryContext destination) => throw new System.InvalidOperationException();" : string.Empty) + " }";

    /// <summary>
    /// Produces equivalent typed and raw SQL signatures without sharing their conversion implementation.
    /// </summary>
    private static string DatumMappingMethods(string type, string binding)
    {
        string input = binding.Length == 0 ? string.Empty : "[" + binding + "] ";
        string output = binding.Length == 0 ? string.Empty : "[return: " + binding + "] ";
        return "public static class Functions { [Ankus.PgFunction] " + output + "public static " + type + " Identity(" + input + type + " value) => value; " +
            "[Ankus.PgFunction] " + output + "public static System.Collections.Generic.IEnumerable<" + type + "> Rows(" + input + type + " value) => [value]; " +
            "[Ankus.PgFunction] " + output + "public static System.Collections.Generic.IEnumerable<(" + type + " First, int Second)> Table(" + input + type + " value) => [(value, 7)]; }";
    }

    /// <summary>
    /// Normalizes only exact native callback identifiers for full SQL comparison across managed representations.
    /// </summary>
    private static string NormalizedDatumSql(Compilation compilation)
    {
        string sql = ManifestValue(compilation, "Ankus.Sql");
        string[] exports = SqlControlExports(compilation);
        for (int index = 0; index < exports.Length; index++)
        {
            sql = sql.Replace(exports[index], "entry" + index.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        }

        return sql;
    }

    /// <summary>
    /// Reads the complete generated dispatcher for structural registration and native-boundary assertions.
    /// </summary>
    private static string DatumMappingManaged(Compilation compilation)
        => compilation.GetTypeByMetadataName("Ankus.Generated.ExtensionDispatchers")!.DeclaringSyntaxReferences.Single().GetSyntax().ToString();

    /// <summary>
    /// Requires a matching precise diagnostic and the absence of all generated artifacts.
    /// </summary>
    private void AssertDatumMappingError(string source, string id, string reason)
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate(source);
        Assert.IsNotEmpty(diagnostics);
        Assert.IsTrue(diagnostics.All(error => error.Id == id), string.Join(Environment.NewLine, diagnostics));
        Assert.IsTrue(diagnostics.Any(error => error.GetMessage(CultureInfo.InvariantCulture).Contains(reason, StringComparison.Ordinal)),
            string.Join(Environment.NewLine, diagnostics));
        Assert.IsNull(compilation.GetTypeByMetadataName("Ankus.Generated.ExtensionDispatchers"));
        Assert.IsEmpty(compilation.Assembly.GetAttributes().Where(static attribute =>
            attribute.AttributeClass?.ToDisplayString() == "System.Reflection.AssemblyMetadataAttribute"));
        Assert.IsEmpty(compilation.GetDiagnostics(context.CancellationToken).Where(static error => error.Severity == DiagnosticSeverity.Error));
    }
}
