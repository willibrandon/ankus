using System.Collections.Immutable;
using Ankus.PgConfig;
using Microsoft.CodeAnalysis;

namespace Ankus.Generators.Tests;

public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Derived equality members remain one selectable family so ordering/hash selection includes the negator's implementation.
    /// </summary>
    [TestMethod]
    public void SchemaGraphRetainsCompleteDerivedFamilies()
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            [Ankus.PgEnum, Ankus.PgEquality, Ankus.PgOrdering, Ankus.PgHashing]
            public enum Choice { A, B }
            """);
        Assert.IsEmpty(diagnostics);
        ExtensionSchemaGraph graph = ExtensionSchemaGraph.Parse(ManifestValue(compilation, "Ankus.SqlGraph"));
        ExtensionSchemaItem equality = graph.Items.Single(static item => item.Kind == "equality");
        ExtensionSchemaItem ordering = graph.Items.Single(static item => item.Kind == "ordering");
        ExtensionSchemaItem hashing = graph.Items.Single(static item => item.Kind == "hashing");
        ExtensionSchemaItem[] equalityMembers = [.. graph.Items.Where(item => item.Owner == equality.Id)];
        Assert.AreSequenceEqual(["function", "function", "operator", "operator"], equalityMembers.Select(static item => item.Kind));
        Assert.AreSequenceEqual(["FUNCTION \"choice_eq\"(\"choice\",\"choice\")", "FUNCTION \"choice_ne\"(\"choice\",\"choice\")",
            "OPERATOR =(\"choice\",\"choice\")", "OPERATOR <>(\"choice\",\"choice\")"], equalityMembers.SelectMany(static item => item.Attachments));
        Assert.HasCount(9, graph.Items.Where(item => item.Owner == ordering.Id));
        Assert.HasCount(1, graph.Items.Where(item => item.Owner == hashing.Id));
        Assert.Contains("choice_btree_ops", ordering.Names);
        Assert.Contains("choice_hash_ops", hashing.Names);
        Assert.Contains("choice_eq", equalityMembers[0].Names);
        Assert.AreEqual(ManifestValue(compilation, "Ankus.Sql"), graph.Sql);
    }

    /// <summary>
    /// Aggregate helpers retain bare SQL names independently of CLR callback names and injected state.
    /// </summary>
    [TestMethod]
    public void SchemaGraphRetainsAggregateHelperAliases()
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            [Ankus.PgAggregate(Schema = "s", InitialCondition = "0")]
            public sealed class Total : Ankus.IPgAggregate<int,int>
            {
                public static int Transition(Ankus.PgAggregateContext context,int state, int value) => state + value;
            }
            """);
        Assert.IsEmpty(diagnostics);
        ExtensionSchemaGraph graph = ExtensionSchemaGraph.Parse(ManifestValue(compilation, "Ankus.SqlGraph"));
        ExtensionSchemaItem helper = graph.Items.Single(static item => item.Kind == "function");
        Assert.Contains("total_transition", helper.Names);
        Assert.Contains("s.total_transition", helper.Names);
        Assert.AreEqual("FUNCTION \"s\".\"total_transition\"(integer,integer)", Assert.ContainsSingle(helper.Attachments));
    }

    /// <summary>
    /// Aggregate attachment identities use PostgreSQL's star, variadic and ordered argument syntax.
    /// </summary>
    /// <param name="options">The aggregate's argument mode.</param>
    /// <param name="parameters">The aggregated transition inputs.</param>
    /// <param name="arguments">The compiler-checked input group.</param>
    /// <param name="final">An optional final callback carrying direct arguments.</param>
    /// <param name="identity">The exact identity accepted after ADD AGGREGATE.</param>
    [TestMethod]
    [DataRow("", "System.ValueTuple arguments", "System.ValueTuple", "", "AGGREGATE \"aggregate_probe\"(*)")]
    [DataRow("", "params int[] values", "int[]", "", "AGGREGATE \"aggregate_probe\"(VARIADIC integer[])")]
    [DataRow("Kind = Ankus.PgAggregateKind.OrderedSet,", "int value", "int", "", "AGGREGATE \"aggregate_probe\"(ORDER BY integer)")]
    [DataRow("Kind = Ankus.PgAggregateKind.OrderedSet,", "int value", "int", "public static int Final(Ankus.PgAggregateContext context,int state, int direct) => state;",
        "AGGREGATE \"aggregate_probe\"(integer ORDER BY integer)")]
    public void SchemaGraphPreservesAggregateAttachmentSignatures(string options, string parameters, string arguments, string final, string identity)
    {
        string finalCapability = final.Length == 0 ? string.Empty : ",Ankus.IPgFinalizingAggregate<int,int,int>";
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate($$"""
            [Ankus.PgAggregate({{options}} InitialCondition = "0")]
            public sealed class AggregateProbe : Ankus.IPgAggregate<int,{{arguments}}>{{finalCapability}}
            {
                public static int Transition(Ankus.PgAggregateContext context,int state,{{parameters}}) => state;
                {{final}}
            }
            """);
        Assert.IsEmpty(diagnostics);
        ExtensionSchemaGraph graph = ExtensionSchemaGraph.Parse(ManifestValue(compilation, "Ankus.SqlGraph"));
        ExtensionSchemaItem aggregate = graph.Items.Single(static item => item.Kind == "aggregate");
        Assert.AreEqual(identity, Assert.ContainsSingle(aggregate.Attachments));
    }

    /// <summary>
    /// Custom function providers retain exact authored overload signatures for selection and extension ownership.
    /// </summary>
    [TestMethod]
    public void SchemaGraphRetainsCustomFunctionProviders()
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            [assembly: Ankus.PgSql("functions", "CREATE FUNCTION named(integer) RETURNS integer LANGUAGE sql AS $$SELECT $1$$;")]
            [assembly: Ankus.PgSqlFunctionProvider("functions", "named(integer)")]
            [assembly: Ankus.PgSqlFunctionProvider("functions", "named(text)")]
            """);
        Assert.IsEmpty(diagnostics);
        ExtensionSchemaItem item = Assert.ContainsSingle(ExtensionSchemaGraph.Parse(ManifestValue(compilation, "Ankus.SqlGraph")).Items);
        Assert.AreSequenceEqual(["functions", "named(integer)", "named(text)"], item.Names);
        Assert.AreSequenceEqual(["FUNCTION named(integer)", "FUNCTION named(text)"], item.Attachments);
        Assert.IsEmpty(item.Dependencies);
    }

    /// <summary>
    /// Invalid provider references and signatures fail compilation without publishing a partial graph.
    /// </summary>
    /// <param name="provider">The invalid custom function declaration.</param>
    /// <param name="message">The actionable graph diagnostic.</param>
    [TestMethod]
    [DataRow("[assembly: Ankus.PgSqlFunctionProvider(\"missing\", \"f()\")]", "existing PgSql")]
    [DataRow("[assembly: Ankus.PgSqlFunctionProvider(\"sql\", \"\")]", "nonempty SQL signature")]
    [DataRow("[assembly: Ankus.PgSqlFunctionProvider(\"sql\", \"f\\0()\")]", "nonempty SQL signature")]
    [DataRow("[assembly: Ankus.PgSqlFunctionProvider(\"sql\", \"f()\")] [assembly: Ankus.PgSqlFunctionProvider(\"sql\", \"f()\")]", "only one custom provider")]
    public void SchemaGraphRejectsInvalidFunctionProviders(string provider, string message)
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("""[assembly: Ankus.PgSql("sql", "SELECT 1;")]""" + provider);
        Diagnostic diagnostic = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual("ANKUS005", diagnostic.Id);
        Assert.Contains(message, diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
        Assert.IsFalse(compilation.Assembly.GetAttributes().Any(static attribute => attribute.ConstructorArguments.Length == 2 &&
            attribute.ConstructorArguments[0].Value as string == "Ankus.SqlGraph"));
    }

    /// <summary>
    /// Retains typed signatures, source names, schema edges and function-owned operators in native graph metadata.
    /// </summary>
    [TestMethod]
    public void SchemaGraphRetainsTypedDeclarationsAndFamilies()
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            [assembly: Ankus.PgSql("first", "SELECT 'bootstrap';", Order = Ankus.PgSqlOrder.Bootstrap)]
            [Ankus.PgSchema("s", Id = "schema")]
            public static class Functions
            {
                [Ankus.PgFunction(Name = "same", Id = "equal"), Ankus.PgOperator("===", Id = "operator")]
                public static bool Equal(Kind left, Kind right) => left == right;
                [Ankus.PgFunction(Name = "same", Id = "integer")]
                public static int Same(int value) => value;
            }
            [Ankus.PgEnum(Name = "kind", Schema = "s", Id = "kind")]
            public enum Kind { A, B }
            """);
        Assert.IsEmpty(diagnostics);
        Assert.IsEmpty(compilation.GetDiagnostics(context.CancellationToken).Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        ExtensionSchemaGraph graph = ExtensionSchemaGraph.Parse(ManifestValue(compilation, "Ankus.SqlGraph"));
        Assert.AreEqual(ManifestValue(compilation, "Ankus.Sql"), graph.Sql);
        ExtensionSchemaItem function = graph.Items.Single(static item => item.Names.Contains("equal"));
        ExtensionSchemaItem kind = graph.Items.Single(static item => item.Kind == "enum");
        ExtensionSchemaItem schema = graph.Items.Single(static item => item.Kind == "schema");
        ExtensionSchemaItem bootstrap = graph.Items.Single(static item => item.Kind == "sql");
        ExtensionSchemaItem operation = graph.Items.Single(static item => item.Kind == "operator");
        Assert.AreEqual("FUNCTION \"s\".\"same\"(\"s\".\"kind\",\"s\".\"kind\")", Assert.ContainsSingle(function.Attachments));
        Assert.Contains("Functions.Equal", function.Names);
        Assert.Contains("Functions.Equal(Kind, Kind)", function.Names);
        Assert.AreSequenceEqual(new[] { bootstrap.Id, schema.Id, kind.Id }.Order(StringComparer.Ordinal), function.Dependencies);
        Assert.AreEqual(function.Id, operation.Owner);
        Assert.AreEqual("OPERATOR \"s\".===(\"s\".\"kind\",\"s\".\"kind\")", Assert.ContainsSingle(operation.Attachments));
        Assert.AreEqual("TYPE \"s\".\"kind\"", Assert.ContainsSingle(kind.Attachments));
        Assert.HasCount(2, graph.Items.Where(static item => item.Names.Contains("same")));
    }

    /// <summary>
    /// Encoded graphs remain byte-identical across reordered source declarations and preserve SQL newline normalization.
    /// </summary>
    [TestMethod]
    public void SchemaGraphEncodingIsDeterministic()
    {
        const string First = """[assembly: Ankus.PgSql("z", "SELECT 'café';\r\n")]""";
        const string Second = """[assembly: Ankus.PgSql("a", "SELECT 1;\r")]""";
        (Compilation left, ImmutableArray<Diagnostic> diagnostics) = Generate(First + Second);
        (Compilation right, ImmutableArray<Diagnostic> reordered) = Generate(Second + First);
        Assert.IsEmpty(diagnostics);
        Assert.IsEmpty(reordered);
        string encoded = ManifestValue(left, "Ankus.SqlGraph");
        Assert.AreEqual(encoded, ManifestValue(right, "Ankus.SqlGraph"));
        ExtensionSchemaGraph graph = ExtensionSchemaGraph.Parse(encoded);
        Assert.AreEqual("-- Generated by Ankus. Do not edit.\n\n" +
            "/* <begin connected objects> */\n-- a\n\nSELECT 1;\n/* </end connected objects> */\n\n" +
            "/* <begin connected objects> */\n-- z\n\nSELECT 'café';\n/* </end connected objects> */\n\n", graph.Sql);
        Assert.AreEqual(ManifestValue(left, "Ankus.Sql"), graph.Sql);
    }

    /// <summary>
    /// Suppression and replacement preserve addressable family nodes without inventing separate SQL fragments.
    /// </summary>
    /// <param name="policy">The declared SQL policy.</param>
    /// <param name="expected">The resulting complete installation script.</param>
    [TestMethod]
    [DataRow("GenerateSql = false", "-- Generated by Ankus. Do not edit.\n\n-- No installable objects declared.\n")]
    [DataRow("Sql = \"SELECT 'replacement';\"", "-- Generated by Ankus. Do not edit.\n\n/* <begin connected objects> */\n-- Functions.Equal(int, int)\n\nSELECT 'replacement';\n/* </end connected objects> */\n\n")]
    public void SchemaGraphRetainsSuppressedAndReplacedFamilies(string policy, string expected)
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate($$"""
            public static class Functions
            {
                [Ankus.PgFunction(Id = "root", {{policy}}), Ankus.PgOperator("===", Id = "operator")]
                public static bool Equal(int left, int right) => left == right;
            }
            """);
        Assert.IsEmpty(diagnostics);
        ExtensionSchemaGraph graph = ExtensionSchemaGraph.Parse(ManifestValue(compilation, "Ankus.SqlGraph"));
        Assert.AreEqual(expected, graph.Sql);
        Assert.HasCount(2, graph.Items);
        ExtensionSchemaItem root = graph.Items.Single(static item => item.Kind == "function");
        ExtensionSchemaItem operation = graph.Items.Single(static item => item.Kind == "operator");
        Assert.AreEqual(root.Id, operation.Owner);
        Assert.IsEmpty(operation.Sql);
        Assert.AreEqual("FUNCTION \"equal\"(integer,integer)", Assert.ContainsSingle(root.Attachments));
        Assert.AreEqual("OPERATOR ===(integer,integer)", Assert.ContainsSingle(operation.Attachments));
    }
}
