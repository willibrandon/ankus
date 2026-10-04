using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Ankus.Generators.Tests;

public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Lazy typed arrays retain concrete SQL declarations and compile across scalar, set and table callbacks.
    /// </summary>
    /// <param name="element">The managed scalar element.</param>
    /// <param name="sql">The concrete SQL array type.</param>
    /// <param name="optional">Whether the whole array accepts SQL NULL.</param>
    [TestMethod]
    [DataRow("int", "integer[]", false)]
    [DataRow("int?", "integer[]", true)]
    [DataRow("string?", "text[]", false)]
    [DataRow("byte[]?", "bytea[]", true)]
    [DataRow("Ankus.PgTextView?", "text[]", true)]
    [DataRow("Ankus.PgByteaView?", "bytea[]", false)]
    [DataRow("Ankus.PgNumeric?", "numeric[]", false)]
    [DataRow("Ankus.PgRelation?", "regclass[]", true)]
    [DataRow("Ankus.PgRange<int>?", "int4range[]", true)]
    public void TypedBorrowedArraySignaturesCompileWithConcreteTypes(string element, string sql, bool optional)
    {
        string type = "Ankus.PgArrayView<" + element + ">" + (optional ? "?" : string.Empty);
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate(TypedBorrowedArrayMethods(type));
        AssertMemoryCompilationSucceeds(compilation, diagnostics);
        string[] statements = [.. OperatorCastStatements(compilation)];
        Assert.HasCount(4, statements);
        string identity = statements.Single(static statement => statement.StartsWith("CREATE FUNCTION \"identity\"(", StringComparison.Ordinal));
        Assert.Contains($"(\"value\" {sql}) RETURNS {sql} AS ", identity);
        Assert.Contains(optional ? " CALLED ON NULL INPUT " : " STRICT ", identity);
        Assert.Contains(statement => statement.Contains($"CREATE FUNCTION \"create\"() RETURNS {sql} AS ", StringComparison.Ordinal), statements);
        Assert.Contains(statement => statement.Contains($"CREATE FUNCTION \"rows\"(\"value\" {sql}) RETURNS SETOF {sql} AS ", StringComparison.Ordinal), statements);
        Assert.Contains(statement => statement.Contains($"RETURNS TABLE (\"cell\" {sql}, \"position\" integer)", StringComparison.Ordinal), statements);
        string managed = DatumMappingManaged(compilation);
        Assert.Contains("ReadBorrowedArray<", managed);
        Assert.Contains("ReadOwnedArrayView<", managed);
        Assert.Contains("NativeValue.FromPolymorphic(", managed);
        Assert.DoesNotContain(".ReadArray<", managed);
        Assert.DoesNotContain("NativeRelationScope", managed);
    }

    /// <summary>
    /// Returning existing native storage never requires an element writer, including retained aggregate state.
    /// </summary>
    /// <param name="declaration">The mapped element declaration.</param>
    [TestMethod]
    [DataRow("public struct Value { }")]
    [DataRow("public sealed class Value { }")]
    [DataRow("public enum Value : byte { Zero, Last = 255 }")]
    public void TypedBorrowedArraysReturnReadOnlyMappings(string declaration)
    {
        Compilation compilation = GenerateSqlControl(DatumMappingSource(declaration, writer: false) +
            TypedBorrowedArrayMethods("Ankus.PgArrayView<Value?>?") + """
            [Ankus.PgAggregate]
            public sealed class First : Ankus.IPgAggregate<Ankus.PgArrayView<Value?>?,int>,
                Ankus.IPgCombinableAggregate<Ankus.PgArrayView<Value?>?>,
                Ankus.IPgFinalizingAggregate<Ankus.PgArrayView<Value?>?,System.ValueTuple,Ankus.PgArrayView<Value?>?>,
                Ankus.IPgMovingAggregate<Ankus.PgArrayView<Value?>?,int>,
                Ankus.IPgMovingFinalizingAggregate<Ankus.PgArrayView<Value?>?,System.ValueTuple,Ankus.PgArrayView<Value?>?>
            {
                public static Ankus.PgArrayView<Value?>? Transition(Ankus.PgAggregateContext context,Ankus.PgArrayView<Value?>? state, int value) => state;
                public static Ankus.PgArrayView<Value?>? Combine(Ankus.PgAggregateContext context,Ankus.PgArrayView<Value?>? left, Ankus.PgArrayView<Value?>? right) => left ?? right;
                public static Ankus.PgArrayView<Value?>? Final(Ankus.PgAggregateContext context,Ankus.PgArrayView<Value?>? state,System.ValueTuple direct) => state;
                public static Ankus.PgArrayView<Value?>? MovingTransition(Ankus.PgAggregateContext context,Ankus.PgArrayView<Value?>? state, int value) => state;
                public static Ankus.PgArrayView<Value?>? MovingInverse(Ankus.PgAggregateContext context,Ankus.PgArrayView<Value?>? state, int value) => state;
                public static Ankus.PgArrayView<Value?>? MovingFinal(Ankus.PgAggregateContext context,Ankus.PgArrayView<Value?>? state,System.ValueTuple direct) => state;
            }
            """);
        string sql = InstallationBody(compilation);
        Assert.Contains("RETURNS SETOF \"pg_catalog\".\"int4\"[]", sql);
        Assert.Contains("STYPE = \"pg_catalog\".\"int4\"[]", sql);
        Assert.Contains("MSTYPE = \"pg_catalog\".\"int4\"[]", sql);
        string managed = DatumMappingManaged(compilation);
        Assert.Contains("ReadBorrowedArray<global::Value?>()", managed);
        Assert.Contains("ReadOwnedArrayView<global::Value?>()", managed);
        Assert.DoesNotContain("ReadMapped<global::Ankus.PgArrayView", managed);
        Assert.DoesNotContain("FromMapped<global::Ankus.PgArrayView", managed);
        Assert.Contains("Converter(), true, false);", managed);
    }

    /// <summary>
    /// Result-only signatures transport native arrays independently of element conversion capabilities.
    /// </summary>
    [TestMethod]
    public void TypedBorrowedArrayResultsAcceptMappingsWithoutReaders()
    {
        Compilation compilation = GenerateSqlControl(DatumMappingSource(reader: false) + """
            public static class Functions
            {
                [Ankus.PgFunction]
                public static Ankus.PgArrayView<Value>? Create() => null;
                [Ankus.PgFunction]
                public static System.Collections.Generic.IEnumerable<Ankus.PgArrayView<Value>?> Rows() => [null];
            }
            """);
        Assert.Contains("RETURNS SETOF \"pg_catalog\".\"int4\"[]", InstallationBody(compilation));
        Assert.Contains("Converter(), false, true);", DatumMappingManaged(compilation));
    }

    /// <summary>
    /// Argument preflight requires an element reader and rejects mapping overrides before emission.
    /// </summary>
    /// <param name="method">The unsupported mapped signature.</param>
    /// <param name="reader">Whether a reader is available.</param>
    /// <param name="message">The expected diagnostic explanation.</param>
    [TestMethod]
    [DataRow("public static int Read(Ankus.PgArrayView<Value?>? value) => 0;", false, "reading SQL arguments")]
    [DataRow("public static int Read([Ankus.PgSqlType(\"text\")] Ankus.PgArrayView<Value> value) => 0;", true, "cannot override")]
    [DataRow("[return: Ankus.PgCompositeType(\"pair\")] public static Ankus.PgArrayView<Value>? Read() => null;", true, "cannot override")]
    public void TypedBorrowedArraysRejectInvalidMappingContracts(string method, bool reader, string message)
        => AssertDatumMappingError(DatumMappingSource(reader: reader) + "public static class Functions { [Ankus.PgFunction] " + method + " }",
            reader ? "ANKUS151" : "ANKUS152", message);

    /// <summary>
    /// A borrowed collection does not make nested arrays or polymorphic scalar cells representable.
    /// </summary>
    /// <param name="type">The unsupported collection representation.</param>
    [TestMethod]
    [DataRow("Ankus.PgArrayView<int[]>")]
    [DataRow("Ankus.PgArrayView<Ankus.PgArray<int>>")]
    [DataRow("Ankus.PgArrayView<Ankus.PgArrayView<int>>")]
    [DataRow("Ankus.PgArrayView<Ankus.PgArrayView>")]
    [DataRow("Ankus.PgArrayView<Ankus.PgAnyElement>")]
    [DataRow("Ankus.PgArrayView<Ankus.PgAnyArray>")]
    [DataRow("Ankus.PgArrayView<Ankus.PgDatum>")]
    [DataRow("Ankus.PgArrayView<Ankus.PgInternal>")]
    [DataRow("Ankus.PgArrayView<object>")]
    [DataRow("Ankus.PgArrayView<int>[]")]
    [DataRow("Ankus.PgArray<Ankus.PgArrayView<int>>")]
    public void TypedBorrowedArraysRejectUnsupportedElements(string type)
    {
        (_, ImmutableArray<Diagnostic> diagnostics) = Generate($$"""
            public static class Functions
            {
                [Ankus.PgFunction]
                public static int Read({{type}} value) => 0;
            }
            """);
        AssertVirtualContextDiagnostic(diagnostics, "ANKUS040");
    }

    /// <summary>
    /// Mapped elements retain their provider and prerequisite ordering through the lazy container.
    /// </summary>
    [TestMethod]
    public void TypedBorrowedArraysRetainMappedProviderDependencies()
    {
        Compilation compilation = GenerateSqlControl("""
            [assembly: Ankus.PgSql("z-provider", "SELECT 'type';", Requires = ["zz-prerequisite"], Relocatable = true)]
            [assembly: Ankus.PgSql("zz-prerequisite", "SELECT 'prerequisite';", Relocatable = true)]
            [assembly: Ankus.PgSqlTypeProvider("z-provider", typeof(Value))]
            """ + DatumMappingSource(writer: false, external: false, name: "item") + """
            public static class Functions
            {
                [Ankus.PgFunction(Id = "a-consumer", Sql = "SELECT 'consumer';", SqlRelocatable = true)]
                public static Ankus.PgArrayView<Value?>? Read(Ankus.PgArrayView<Value?>? value) => value;
            }
            """);
        Assert.AreEqual("SELECT 'prerequisite';\nSELECT 'type';\nSELECT 'consumer';\n", InstallationBody(compilation));
    }

    /// <summary>
    /// Result-only TABLE columns and aggregate finals retain distinct mapped providers without an input root.
    /// </summary>
    [TestMethod]
    public void TypedBorrowedArraysRetainIndependentResultProviders()
    {
        Compilation compilation = GenerateSqlControl("""
            [assembly: Ankus.PgSql("z-first", "SELECT 'first';", Relocatable = true)]
            [assembly: Ankus.PgSql("z-second", "SELECT 'second';", Requires = ["zz-prerequisite"], Relocatable = true)]
            [assembly: Ankus.PgSql("zz-prerequisite", "SELECT 'prerequisite';", Relocatable = true)]
            [assembly: Ankus.PgSqlTypeProvider("z-first", typeof(Value))]
            [assembly: Ankus.PgSqlTypeProvider("z-second", typeof(Other))]
            """ + DatumMappingSource(writer: false, external: false, name: "first") +
            DatumMappingSource("public struct Other { }", writer: false, external: false, name: "second", type: "Other") + """
            public static class Functions
            {
                [Ankus.PgFunction(Id = "a-table", Sql = "SELECT 'table';", SqlRelocatable = true)]
                public static System.Collections.Generic.IEnumerable<(Ankus.PgArrayView<Value> First, Ankus.PgArrayView<Other?> Second)> Rows() => [];
            }
            [Ankus.PgAggregate(InitialCondition = "0")]
            public sealed class Total : Ankus.IPgAggregate<int,int>,
                Ankus.IPgFinalizingAggregate<int,System.ValueTuple,Ankus.PgArrayView<Other?>?>
            {
                public static int Transition(Ankus.PgAggregateContext context,int state, int input) => state + input;
                public static Ankus.PgArrayView<Other?>? Final(Ankus.PgAggregateContext context,int state,System.ValueTuple direct) => null;
            }
            """);
        string sql = InstallationBody(compilation);
        AssertSqlControlBefore(sql, "SELECT 'first';", "SELECT 'table';");
        AssertSqlControlBefore(sql, "SELECT 'second';", "SELECT 'table';");
        AssertSqlControlBefore(sql, "SELECT 'prerequisite';", "SELECT 'second';");
        AssertSqlControlBefore(sql, "SELECT 'second';", "CREATE FUNCTION \"total_final\"");
        Assert.Contains("RETURNS \"second\"[] AS", sql);
        Assert.Contains("STYPE = integer", sql);
    }

    /// <summary>
    /// Named composite bindings remain authoritative and retain the SQL supplier dependency.
    /// </summary>
    [TestMethod]
    public void TypedBorrowedArraysBindNamedComposites()
    {
        Compilation compilation = GenerateSqlControl("""
            [assembly: Ankus.PgSql("supplier", "SELECT 'supplier';")]
            [Ankus.PgType(Name = "item", GenerateSql = false, Requires = ["supplier"])]
            public readonly record struct Zulu(int Number);
            public static class Functions
            {
                [Ankus.PgFunction]
                [return: Ankus.PgCompositeType("item")]
                public static Ankus.PgArrayView<Ankus.PgHeapTuple?>? Read(
                    [Ankus.PgCompositeType("item")] Ankus.PgArrayView<Ankus.PgHeapTuple?>? value) => value;
            }
            """);
        string sql = InstallationBody(compilation);
        Assert.StartsWith("SELECT 'supplier';\nCREATE FUNCTION \"read\"(\"value\" \"item\"[])\nRETURNS \"item\"[]", sql);
        Assert.DoesNotContain("CREATE TYPE", sql);
    }

    /// <summary>
    /// Supplies distinct scalar and retained signatures for one independently selected representation.
    /// </summary>
    private static string TypedBorrowedArrayMethods(string type) => $$"""
        public static class Functions
        {
            [Ankus.PgFunction]
            public static {{type}} Identity({{type}} value) => value;
            [Ankus.PgFunction]
            public static {{type}} Create() => null!;
            [Ankus.PgFunction]
            public static System.Collections.Generic.IEnumerable<{{type}}> Rows({{type}} value)
            {
                yield return value;
            }
            [Ankus.PgFunction]
            public static System.Collections.Generic.IEnumerable<({{type}} Cell, int Position)> Columns({{type}} value)
            {
                yield return (value, 7);
            }
        }
        """;
}
