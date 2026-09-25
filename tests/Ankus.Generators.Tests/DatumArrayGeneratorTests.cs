using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;

namespace Ankus.Generators.Tests;

/// <summary>
/// Verifies statically composed mapped arrays, directional contracts and leaf provider dependencies.
/// </summary>
public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Arrays preserve complete scalar, SETOF and TABLE SQL contracts while selecting raw mapped transport.
    /// </summary>
    /// <param name="declaration">The mapped scalar declaration.</param>
    /// <param name="arrayType">The vector or shaped element representation.</param>
    /// <param name="optional">Whether the whole array accepts SQL NULL.</param>
    [TestMethod]
    [DataRow("public struct Value { }", "Value[]", false)]
    [DataRow("public struct Value { }", "Value[]", true)]
    [DataRow("public struct Value { }", "Value?[]", false)]
    [DataRow("public struct Value { }", "Value?[]", true)]
    [DataRow("public struct Value { }", "Ankus.PgArray<Value>", false)]
    [DataRow("public struct Value { }", "Ankus.PgArray<Value>", true)]
    [DataRow("public struct Value { }", "Ankus.PgArray<Value?>", false)]
    [DataRow("public struct Value { }", "Ankus.PgArray<Value?>", true)]
    [DataRow("public sealed class Value { }", "Value?[]", false)]
    [DataRow("public sealed class Value { }", "Ankus.PgArray<Value?>", true)]
    [DataRow("public enum Value : byte { Zero, Last = 255 }", "Value[]", false)]
    [DataRow("public enum Value : byte { Zero, Last = 255 }", "Ankus.PgArray<Value?>", true)]
    public void DatumArraysPreserveEveryScalarSetAndTableContract(string declaration, string arrayType, bool optional)
    {
        string suffix = optional ? "?" : string.Empty;
        Compilation mapped = GenerateSqlControl(DatumMappingSource(declaration) + DatumMappingMethods(arrayType + suffix, string.Empty));
        Compilation raw = GenerateSqlControl(DatumMappingMethods("Ankus.PgDatum" + suffix,
            "Ankus.PgSqlType(\"_int4\", Schema=\"pg_catalog\")"));
        Assert.AreEqual(NormalizedDatumSql(raw).Replace("\"_int4\"", "\"int4\"[]", StringComparison.Ordinal),
            NormalizedDatumSql(mapped));
        string sql = ManifestValue(mapped, "Ankus.Sql");
        Assert.Contains("RETURNS TABLE (\"first\" \"pg_catalog\".\"int4\"[], \"second\" integer)", sql);
        Assert.Contains(optional ? " CALLED ON NULL INPUT " : " STRICT ", sql);
        Assert.DoesNotContain("CREATE TYPE", sql);
        Assert.AreEqual("true", ManifestValue(mapped, "Ankus.Relocatable"));
        string managedType = arrayType.Replace("Ankus.", "global::Ankus.", StringComparison.Ordinal)
            .Replace("Value", "global::Value", StringComparison.Ordinal);
        string managed = DatumMappingManaged(mapped);
        Assert.Contains("ReadMapped<" + managedType + ">()", managed);
        Assert.Contains("FromMapped<" + managedType + ">(", managed);
        Assert.DoesNotContain("ReadArray<global::Value", managed);
        string native = ManifestValue(mapped, "Ankus.NativeSource");
        Assert.Contains("const bool polymorphic[] = { true };", native);
        Assert.Contains("false, polymorphic, true);", native);
        Assert.Contains("if (result.is_null && result.data == NULL)", native);
    }

    /// <summary>
    /// Variadic mapped elements retain their array identity, including byte-backed enums and nullable cells.
    /// </summary>
    /// <param name="declaration">The mapped element declaration.</param>
    /// <param name="element">The required or optional element type.</param>
    [TestMethod]
    [DataRow("public struct Value { }", "Value")]
    [DataRow("public struct Value { }", "Value?")]
    [DataRow("public enum Value : byte { Zero, Last = 255 }", "Value")]
    [DataRow("public sealed class Value { }", "Value?")]
    public void DatumArraysDeclareVariadicLeafIdentity(string declaration, string element)
    {
        Compilation compilation = GenerateSqlControl(DatumMappingSource(declaration, writer: false) +
            "public static class Functions { [Ankus.PgFunction] public static int Count(params " + element + "[] values) => values.Length; }");
        string sql = ManifestValue(compilation, "Ankus.Sql");
        Assert.Contains("VARIADIC \"values\" \"pg_catalog\".\"int4\"[]", sql);
        Assert.Contains("RETURNS integer", sql);
        Assert.DoesNotContain("bytea", sql);
        Assert.Contains("ReadMapped<global::" + element + "[]>()", DatumMappingManaged(compilation));
    }

    /// <summary>
    /// One-way array mappings consume only the direction required by the selected signature.
    /// </summary>
    /// <param name="shaped">Whether the array preserves dimensions.</param>
    /// <param name="read">Whether the mapped element has only a reader.</param>
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void DatumArraysAcceptIndependentDirections(bool shaped, bool read)
    {
        string type = shaped ? "Ankus.PgArray<Value?>" : "Value?[]";
        string method = read ? "public static int Consume(" + type + "? value) => 7;" :
            "public static System.Collections.Generic.IEnumerable<" + type + "?> Produce() => [null];";
        Compilation compilation = GenerateSqlControl(DatumMappingSource(reader: read, writer: !read) +
            "public static class Functions { [Ankus.PgFunction] " + method + " }");
        Assert.Contains(read ? "\"value\" \"pg_catalog\".\"int4\"[])\nRETURNS integer" : "RETURNS SETOF \"pg_catalog\".\"int4\"[]",
            ManifestValue(compilation, "Ankus.Sql"));
        Assert.Contains(read ? "Converter(), true, false);" : "Converter(), false, true);", DatumMappingManaged(compilation));
    }

    /// <summary>
    /// Missing leaf directions fail for every generated result shape and nullable or variadic input.
    /// </summary>
    /// <param name="method">The consuming signature.</param>
    /// <param name="readOnly">Whether the leaf lacks a writer.</param>
    [TestMethod]
    [DataRow("public static Value[] Result() => [];", true)]
    [DataRow("public static Ankus.PgArray<Value?>? Result() => null;", true)]
    [DataRow("public static System.Collections.Generic.IEnumerable<Value?[]> Result() => [];", true)]
    [DataRow("public static System.Collections.Generic.IEnumerable<(int First, Ankus.PgArray<Value?>? Second)> Result() => [];", true)]
    [DataRow("public static int Input(Value[] value) => 0;", false)]
    [DataRow("public static int Input(Value?[]? value) => 0;", false)]
    [DataRow("public static int Input(Ankus.PgArray<Value?>? value) => 0;", false)]
    [DataRow("public static int Input(params Value?[] value) => 0;", false)]
    public void DatumArraysRejectUnavailableFunctionDirections(string method, bool readOnly)
        => AssertDatumMappingError(DatumMappingSource(reader: readOnly, writer: !readOnly) +
            "public static class Functions { [Ankus.PgFunction] " + method + " }", "ANKUS019",
            readOnly ? "writing SQL results" : "reading SQL arguments");

    /// <summary>
    /// Each aggregate helper validates its own mapped array leaf, independently of transition state discovery.
    /// </summary>
    /// <param name="role">The selected aggregate role.</param>
    /// <param name="readOnly">Whether its output requires a missing writer.</param>
    [TestMethod]
    [DataRow("Transition", false)]
    [DataRow("Combine", false)]
    [DataRow("Final", true)]
    [DataRow("Serialize", false)]
    [DataRow("Deserialize", true)]
    [DataRow("MovingTransition", false)]
    [DataRow("MovingInverse", false)]
    [DataRow("MovingFinal", true)]
    public void DatumArraysRejectUnavailableAggregateDirections(string role, bool readOnly)
    {
        string method = readOnly ? "public static Ankus.PgArray<Value?>? " + role + "(int state) => null;" :
            "public static int " + role + "(Value?[]? state) => 0;";
        AssertDatumMappingError(DatumMappingSource(reader: readOnly, writer: !readOnly) +
            "[Ankus.PgAggregate] public static class Aggregate { " + method + " }", "ANKUS019",
            readOnly ? "writing SQL results" : "reading SQL arguments");
    }

    /// <summary>
    /// Array states, moving helpers and manual operators or casts retain complete SQL and raw owner flags.
    /// </summary>
    /// <param name="shaped">Whether the mapped state carries explicit dimensions.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void DatumArraysCompileAggregateRolesOperatorsAndCasts(bool shaped)
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
        string arrayType = shaped ? "Ankus.PgArray<Value?>" : "Value?[]";
        Compilation mapped = GenerateSqlControl(DatumMappingSource() + template.Replace("TYPE", arrayType, StringComparison.Ordinal)
            .Replace("INPUT", string.Empty, StringComparison.Ordinal).Replace("OUTPUT", string.Empty, StringComparison.Ordinal));
        const string binding = "Ankus.PgSqlType(\"_int4\", Schema=\"pg_catalog\")";
        Compilation raw = GenerateSqlControl(template.Replace("TYPE", "Ankus.PgDatum", StringComparison.Ordinal)
            .Replace("INPUT", "[" + binding + "]", StringComparison.Ordinal).Replace("OUTPUT", "[return: " + binding + "]", StringComparison.Ordinal));
        Assert.AreEqual(NormalizedDatumSql(raw).Replace("\"_int4\"", "\"int4\"[]", StringComparison.Ordinal), NormalizedDatumSql(mapped));
        string sql = ManifestValue(mapped, "Ankus.Sql");
        Assert.Contains("STYPE = \"pg_catalog\".\"int4\"[]", sql);
        Assert.Contains("MSTYPE = \"pg_catalog\".\"int4\"[]", sql);
        Assert.Contains("CREATE CAST (\"pg_catalog\".\"int4\"[] AS integer)", sql);
        Assert.Contains("LEFTARG = \"pg_catalog\".\"int4\"[], RIGHTARG = \"pg_catalog\".\"int4\"[]", sql);
        Assert.Contains("const bool polymorphic[2] = {true, false};", ManifestValue(mapped, "Ankus.NativeSource"));
        Assert.Contains("const bool polymorphic[2] = {true, true};", ManifestValue(mapped, "Ankus.NativeSource"));
        Assert.Contains("false, false, polymorphic, true);", ManifestValue(mapped, "Ankus.NativeSource"));
    }

    /// <summary>
    /// Array signatures cannot override their reusable leaf identity through slot-specific annotations.
    /// </summary>
    /// <param name="method">The conflicting scalar or TABLE signature.</param>
    [TestMethod]
    [DataRow("public static int Read([Ankus.PgSqlType(\"text\")] Value[] value) => 0;")]
    [DataRow("public static int Read([Ankus.PgCompositeType(\"pair\")] Ankus.PgArray<Value?>? value) => 0;")]
    [DataRow("[return: Ankus.PgSqlType(\"text\")] public static Value?[]? Read() => null;")]
    [DataRow("[return: Ankus.PgCompositeType(\"pair\", Column=\"renamed\"), Ankus.PgColumnNames(\"renamed\", \"number\")] public static System.Collections.Generic.IEnumerable<(Ankus.PgArray<Value?> Item, int Number)> Read() => [];")]
    public void DatumArraysRejectSlotBindingOverrides(string method)
        => AssertDatumMappingError(DatumMappingSource() + "public static class Functions { [Ankus.PgFunction] " + method + " }",
            "ANKUS019", "cannot override their mapping");

    /// <summary>
    /// Each array argument or result independently orders its scalar leaf provider before its consumer.
    /// </summary>
    /// <param name="method">The signature containing the sole provider dependency.</param>
    [TestMethod]
    [DataRow("public static int Use(Value[] value) => 0;")]
    [DataRow("public static int Use(params Value?[] value) => 0;")]
    [DataRow("public static Value[] Use() => [];")]
    [DataRow("public static Ankus.PgArray<Value?>? Use() => null;")]
    [DataRow("public static System.Collections.Generic.IEnumerable<Value?[]?> Use() => [];")]
    [DataRow("public static System.Collections.Generic.IEnumerable<(int First, Ankus.PgArray<Value?>? Second)> Use() => [];")]
    public void DatumArrayProvidersOrderIndependentSignatureSlots(string method)
    {
        Compilation compilation = GenerateSqlControl("""
            [assembly: Ankus.PgSql("z-provider", "SELECT 'type';", Requires=["zz-prerequisite"], Relocatable=true)]
            [assembly: Ankus.PgSql("zz-prerequisite", "SELECT 'prerequisite';", Relocatable=true)]
            [assembly: Ankus.PgSqlTypeProvider("z-provider", typeof(Value))]
            """ + DatumMappingSource(external: false, name: "item") +
            "public static class Functions { [Ankus.PgFunction(Id=\"a-consumer\", Sql=\"SELECT 'consumer';\", SqlRelocatable=true)] " + method + " }");
        Assert.AreEqual("SELECT 'prerequisite';\nSELECT 'type';\nSELECT 'consumer';\n", ManifestValue(compilation, "Ankus.Sql"));
        Assert.AreEqual("true", ManifestValue(compilation, "Ankus.Relocatable"));
    }

    /// <summary>
    /// A second TABLE column and aggregate-final array have independent providers, with no shared input edge masking them.
    /// </summary>
    [TestMethod]
    public void DatumArrayProvidersVisitIndependentColumnsAndAggregateFinals()
    {
        Compilation compilation = GenerateSqlControl("""
            [assembly: Ankus.PgSql("z-first", "SELECT 'first';", Relocatable=true)]
            [assembly: Ankus.PgSql("z-second", "SELECT 'second';", Requires=["zz-prerequisite"], Relocatable=true)]
            [assembly: Ankus.PgSql("zz-prerequisite", "SELECT 'prerequisite';", Relocatable=true)]
            [assembly: Ankus.PgSqlTypeProvider("z-first", typeof(Value))]
            [assembly: Ankus.PgSqlTypeProvider("z-second", typeof(Other))]
            """ + DatumMappingSource(external: false, name: "first") +
            DatumMappingSource("public struct Other { }", external: false, name: "second", type: "Other") + """
            public static class Functions
            {
                [Ankus.PgFunction(Id="a-table", Sql="SELECT 'table';", SqlRelocatable=true)]
                public static System.Collections.Generic.IEnumerable<(Value[] First, Ankus.PgArray<Other?> Second)> Rows() => [];
            }
            [Ankus.PgAggregate(InitialCondition="0")]
            public static class Total
            {
                public static int Transition(int state, int input) => state + input;
                public static Other?[] Final(int state) => [];
            }
            """);
        string sql = ManifestValue(compilation, "Ankus.Sql");
        AssertSqlControlBefore(sql, "SELECT 'first';", "SELECT 'table';");
        AssertSqlControlBefore(sql, "SELECT 'second';", "SELECT 'table';");
        AssertSqlControlBefore(sql, "SELECT 'prerequisite';", "SELECT 'second';");
        AssertSqlControlBefore(sql, "SELECT 'second';", "CREATE FUNCTION \"total_final\"");
        Assert.Contains("RETURNS \"second\"[] AS", sql);
        Assert.Contains("STYPE = integer", sql);
    }

    /// <summary>
    /// Referenced array-only signatures discover the leaf and both generated assemblies register it lazily.
    /// </summary>
    [TestMethod]
    public void DatumArrayRegistrationsComposeAcrossAssembliesWithoutBackendAccess()
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
            """).WithAssemblyName("GeneratedDatumArrayDependency");
        byte[] dependencyImage = EmitDatumMappingImage(dependency);
        (Compilation consumer, ImmutableArray<Diagnostic> diagnostics) = GenerateDatumMappingReference("""
            public static class Functions
            {
                [Ankus.PgFunction] public static int Count(Value?[] values) => values.Length;
            }
            public static class Probe
            {
                public static string Run()
                {
                    var shaped = new Ankus.PgArray<Value?>([default(Value), null], [2], [-3]);
                    var empty = new Ankus.PgArray<Value>(System.Array.Empty<Value>());
                    return $"{shaped.Count}|{shaped.Rank}|{shaped.GetValue(-3).HasValue}|{shaped.GetValue(-2).HasValue}|{empty.Count}|{empty.Rank}|{Converter.Constructions}";
                }
            }
            """, [MetadataReference.CreateFromImage(dependencyImage)]);
        AssertSqlControlCompilation(consumer, diagnostics);
        Assert.Contains("RegisterValue<global::Value>(", DatumMappingManaged(dependency));
        Assert.Contains("RegisterValue<global::Value>(", DatumMappingManaged(consumer));
        Assert.Contains("\"values\" \"pg_catalog\".\"int4\"[]", ManifestValue(consumer, "Ankus.Sql"));
        var load = new AssemblyLoadContext("GeneratedDatumArrayDependencies", isCollectible: true);
        try
        {
            using var dependencyStream = new MemoryStream(dependencyImage);
            using var consumerStream = new MemoryStream(EmitDatumMappingImage(consumer));
            Assembly dependencyAssembly = load.LoadFromStream(dependencyStream);
            Assembly consumerAssembly = load.LoadFromStream(consumerStream);
            System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(dependencyAssembly.ManifestModule.ModuleHandle);
            System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(consumerAssembly.ManifestModule.ModuleHandle);
            MethodInfo run = consumerAssembly.GetType("Probe", throwOnError: true)!.GetMethod("Run")!;
            Assert.AreEqual("2|1|True|False|0|0|0", Assert.IsInstanceOfType<string>(run.Invoke(null, null)));
        }
        finally
        {
            load.Unload();
        }
    }
}
