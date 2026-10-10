using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace Ankus.PgConfig.Tests;

/// <summary>
/// Verifies graph framing, exact dependency selection and executable attachment contracts using independent fixtures.
/// </summary>
[TestClass]
public sealed partial class ExtensionSchemaGraphTests
{
    /// <summary>
    /// An independent graph retains SQL, aliases, dependencies and stable DOT identities without runtime execution.
    /// </summary>
    [TestMethod]
    public void GraphRetainsOrderedDeclarationsAndEdges()
    {
        ExtensionSchema schema = Fixture();
        Assert.IsNotNull(schema.Graph);
        ExtensionSchemaGraph graph = schema.Graph;
        Assert.AreSequenceEqual(["schema", "type", "first", "second", "operator", "unrelated", "raw"],
            graph.Items.Select(static item => item.Id));
        ExtensionSchemaItem function = graph.Items[3];
        Assert.AreSequenceEqual(["first", "type"], function.Dependencies);
        Assert.AreSequenceEqual(["second", "Functions.Second"], function.Names);
        Assert.AreEqual("function", function.Kind);
        Assert.AreEqual("first", graph.Items[4].Owner);
        Assert.AreEqual(schema.Sql, graph.Sql);
        Assert.AreEqual("""
            digraph Ankus {
              n0 [label="schema: s"];
              n1 [label="type: Kind"];
              n2 [label="function: first"];
              n3 [label="function: second"];
              n4 [label="operator: ==="];
              n5 [label="function: unrelated"];
              n6 [label="sql: raw"];
              n0 -> n1;
              n1 -> n2;
              n2 -> n3;
              n1 -> n3;
              n2 -> n4;
            }

            """.ReplaceLineEndings("\n"), graph.ToGraphviz());
    }

    /// <summary>
    /// Selection includes transitive prerequisites and a function's operator family once in installation order.
    /// </summary>
    [TestMethod]
    public void SelectionClosesDependenciesAndDeclarationFamilies()
    {
        ExtensionSchemaSelection selected = Fixture().Select(["Functions.Second", "first", "second"]);
        Assert.AreSequenceEqual(["schema", "type", "first", "second", "operator"],
            selected.Items.Select(static item => item.Id));
        Assert.IsEmpty(selected.Warnings);
        Assert.AreEqual("""
            BEGIN;

            CREATE SCHEMA s;
            ALTER EXTENSION "probe" ADD SCHEMA s;
            CREATE TYPE s.kind AS ENUM ('café 🐘');
            ALTER EXTENSION "probe" ADD TYPE s.kind;
            CREATE FUNCTION s.first(s.kind) RETURNS integer AS '$libdir/Probe.so', 'first' LANGUAGE c;
            ALTER EXTENSION "probe" ADD FUNCTION s.first(s.kind);
            SELECT 'second';
            ALTER EXTENSION "probe" ADD FUNCTION s.second();
            SELECT 'operator';
            ALTER EXTENSION "probe" ADD OPERATOR s.===(s.kind, s.kind);

            COMMIT;

            """.ReplaceLineEndings("\n"), selected.Sql);
    }

    /// <summary>
    /// Selecting an attached operator resolves the complete backing-function family and excludes its consumers.
    /// </summary>
    [TestMethod]
    public void OperatorSelectionResolvesItsFamilyWithoutConsumers()
    {
        ExtensionSchemaSelection selected = Fixture().Select(["==="], alterExtension: false);
        Assert.AreSequenceEqual(["schema", "type", "first", "operator"], selected.Items.Select(static item => item.Id));
        Assert.DoesNotContain("second", selected.Sql);
        Assert.DoesNotContain("BEGIN", selected.Sql);
        Assert.DoesNotContain("COMMIT", selected.Sql);
        Assert.DoesNotContain("ALTER EXTENSION", selected.Sql);
        Assert.Contains("'$libdir/Probe.so'", selected.Sql);
        Assert.IsEmpty(selected.Warnings);
    }

    /// <summary>
    /// Opaque custom SQL is retained exactly and explicitly identifies its missing attachment inventory.
    /// </summary>
    [TestMethod]
    public void CustomSqlReportsUnattachedObjects()
    {
        ExtensionSchemaSelection selected = Fixture().Select(["raw"]);
        Assert.AreEqual("raw", Assert.ContainsSingle(selected.Items).Id);
        Assert.Contains("Custom SQL 'raw'", Assert.ContainsSingle(selected.Warnings));
        Assert.Contains("SELECT $$raw ; ' quotes$$; -- tail\n", selected.Sql);
        ExtensionSchemaSelection detached = Fixture().Select(["raw"], alterExtension: false);
        Assert.AreEqual("SELECT $$raw ; ' quotes$$; -- tail\n", detached.Sql);
        Assert.IsEmpty(detached.Warnings);
        ExtensionSchemaSelection located = Schema(Encode(new Entry("located", "sql",
            "/* <begin connected objects> */\n-- Sql/Setup.cs:12\n\nSELECT 1;\n/* </end connected objects> */\n", Names: ["setup"]))).Select(["setup"]);
        Assert.AreEqual("Custom SQL 'setup' at Sql/Setup.cs:12 has no declared created objects; attach them to the extension manually.",
            Assert.ContainsSingle(located.Warnings));
    }

    /// <summary>
    /// Empty and unknown selections fail before any partial script is returned.
    /// </summary>
    /// <param name="name">The selected identifier, or null for an empty selection.</param>
    [TestMethod]
    [DataRow("")]
    [DataRow(" ")]
    [DataRow("missing")]
    [DataRow("First")]
    public void SelectionRejectsInvalidNames(string name)
    {
        ExtensionSchema schema = Fixture();
        Assert.ThrowsExactly<ArgumentException>(() => schema.Select([name]));
    }

    /// <summary>
    /// Overloads require distinct signatures while aliases within one function family are unambiguous.
    /// </summary>
    [TestMethod]
    public void SelectionRejectsAmbiguityAndAcceptsExactSignatures()
    {
        ExtensionSchema schema = Schema(Encode(
            new("a", "function", "SELECT 1;", Names: ["f", "f(integer)", "Numbers.F"], Attachments: ["FUNCTION f(integer)"]),
            new("b", "function", "SELECT 2;", Names: ["f", "f(text)", "Words.F"], Attachments: ["FUNCTION f(text)"])));
        ArgumentException error = Assert.ThrowsExactly<ArgumentException>(() => schema.Select(["f"]));
        Assert.StartsWith("Ambiguous schema item 'f': a, b. Use a qualified name or signature.", error.Message);
        Assert.AreEqual("SELECT 2;\n", schema.Select(["f(text)"], alterExtension: false).Sql);
        Assert.AreEqual("SELECT 1;\n", schema.Select(["Numbers.F"], alterExtension: false).Sql);
    }

    /// <summary>
    /// Disabled SQL produces no attachment, including blank family members belonging to a disabled root.
    /// </summary>
    [TestMethod]
    public void DisabledFamiliesDoNotAttachMissingObjects()
    {
        ExtensionSchema schema = Schema(Encode(
            new("a", "function", "", Names: ["f"], Attachments: ["FUNCTION f()"]),
            new("b", "operator", "", Owner: "a", Names: ["="], Dependencies: ["a"], Attachments: ["OPERATOR =(integer,integer)"])));
        ExtensionSchemaSelection selected = schema.Select(["="]);
        Assert.HasCount(2, selected.Items);
        Assert.AreEqual("BEGIN;\n\n\nCOMMIT;\n", selected.Sql);
    }

    /// <summary>
    /// Replacement roots retain attachments for family SQL consolidated into the root fragment.
    /// </summary>
    [TestMethod]
    public void ReplacementFamiliesRetainAttachments()
    {
        ExtensionSchema schema = Schema(Encode(
            new("a", "function", "SELECT 'replacement';", Names: ["f"], Attachments: ["FUNCTION f()"]),
            new("b", "operator", "", Owner: "a", Names: ["="], Dependencies: ["a"], Attachments: ["OPERATOR =(integer,integer)"])));
        Assert.AreEqual("BEGIN;\n\nSELECT 'replacement';\nALTER EXTENSION \"probe\" ADD FUNCTION f();\n" +
            "ALTER EXTENSION \"probe\" ADD OPERATOR =(integer,integer);\n\nCOMMIT;\n", schema.Select(["f"]).Sql);
    }

    /// <summary>
    /// Empty graphs have a stable SQL comment and a valid empty DOT document.
    /// </summary>
    [TestMethod]
    public void EmptyGraphHasDefinedOutputs()
    {
        ExtensionSchemaGraph graph = ExtensionSchemaGraph.Parse(Encode());
        Assert.IsEmpty(graph.Items);
        Assert.AreEqual("-- No installable objects declared.\n", graph.Sql);
        Assert.AreEqual("digraph Ankus {\n}\n", graph.ToGraphviz());
        Assert.ThrowsExactly<ArgumentException>(() => Schema(Encode()).Select([]));
    }

    /// <summary>
    /// Fixed schemas qualify only typed identifier markers and leave matching parameter names and literals unchanged.
    /// </summary>
    [TestMethod]
    public void SelectionQualifiesCompilerMarkedIdentifiers()
    {
        ExtensionSchema schema = Schema(Encode(new Entry("function", "function",
            "CREATE FUNCTION \0\"value\"(\"value\" \0\"kind\") RETURNS text AS $$SELECT 'value', '\"kind\"'$$ LANGUAGE sql;",
            Names: ["value"], Attachments: ["FUNCTION \0\"value\"(\0\"kind\")"])), "Case \" Schema");
        Assert.AreEqual("Case \" Schema", schema.DefaultSchema);
        ExtensionSchemaSelection selected = schema.Select(["value"]);
        Assert.AreEqual("BEGIN;\n\nCREATE FUNCTION \"Case \"\" Schema\".\"value\"(\"value\" \"Case \"\" Schema\".\"kind\") " +
            "RETURNS text AS $$SELECT 'value', '\"kind\"'$$ LANGUAGE sql;\n" +
            "ALTER EXTENSION \"probe\" ADD FUNCTION \"Case \"\" Schema\".\"value\"(\"Case \"\" Schema\".\"kind\");\n\nCOMMIT;\n", selected.Sql);
        Assert.DoesNotContain("\0", schema.Sql);
        Assert.AreEqual("FUNCTION \"value\"(\"kind\")", Assert.ContainsSingle(Assert.ContainsSingle(selected.Items).Attachments));
    }

    /// <summary>
    /// Quoted whitespace schema names and intentionally blank replacement SQL remain valid, nonempty metadata.
    /// </summary>
    [TestMethod]
    public void PreservesWhitespaceSchemaAndReplacementSql()
    {
        ExtensionSchema schema = Schema(Encode(new Entry("sql", "sql", " \t\n", Names: ["blank"])), " ");
        Assert.AreEqual(" ", schema.DefaultSchema);
        Assert.AreEqual(" \t\n", schema.Sql);
        Assert.AreEqual(" \t\n", schema.Select(["blank"], alterExtension: false).Sql);
    }

    /// <summary>
    /// Quoted SQL identifiers retain whitespace-only names through graph parsing and exact selection.
    /// </summary>
    /// <param name="name">The nonempty quoted schema identifier.</param>
    [TestMethod]
    [DataRow(" ")]
    [DataRow("\t")]
    [DataRow("\n")]
    [DataRow("\u00a0")]
    public void PreservesWhitespaceOnlyQuotedNames(string name)
    {
        string sql = "CREATE SCHEMA \"" + name + "\";\n";
        ExtensionSchema schema = Schema(Encode(new Entry("schema", "schema", sql,
            Names: [name], Attachments: ["SCHEMA \"" + name + "\""])));
        Assert.IsNotNull(schema.Graph);
        Assert.AreEqual(name, Assert.ContainsSingle(Assert.ContainsSingle(schema.Graph.Items).Names));
        ExtensionSchemaSelection selected = schema.Select([name]);
        Assert.AreEqual("schema", Assert.ContainsSingle(selected.Items).Id);
        Assert.AreEqual("BEGIN;\n\n" + sql + "ALTER EXTENSION \"probe\" ADD SCHEMA \"" + name + "\";\n\nCOMMIT;\n", selected.Sql);
        Assert.IsEmpty(selected.Warnings);
    }

    /// <summary>
    /// Quoted names, backslashes, line breaks and Unicode cannot escape DOT labels.
    /// </summary>
    [TestMethod]
    public void GraphvizEscapesLabels()
    {
        ExtensionSchemaGraph graph = ExtensionSchemaGraph.Parse(Encode(new Entry("node", "sql", "SELECT 1;", Names: ["a\"\\\n🐘"])));
        Assert.AreEqual("digraph Ankus {\n  n0 [label=\"sql: a\\\"\\\\\\\\u000a🐘\"];\n}\n", graph.ToGraphviz());
    }

    /// <summary>
    /// Independently malformed graphs fail closed for framing, duplicate identities and invalid prerequisites or owners.
    /// </summary>
    /// <param name="corruption">The invalid graph partition.</param>
    [TestMethod]
    [DataRow("magic")]
    [DataRow("trailing")]
    [DataRow("count")]
    [DataRow("negative count")]
    [DataRow("text length")]
    [DataRow("utf8")]
    [DataRow("kind")]
    [DataRow("id")]
    [DataRow("duplicate id")]
    [DataRow("duplicate alias")]
    [DataRow("empty alias")]
    [DataRow("nul alias")]
    [DataRow("dependency")]
    [DataRow("self dependency")]
    [DataRow("forward dependency")]
    [DataRow("owner")]
    [DataRow("self owner")]
    [DataRow("nested owner")]
    [DataRow("nul")]
    public void RejectsMalformedGraphs(string corruption)
    {
        Entry first = new("a", "sql", "SELECT 1;", Names: ["one"]);
        Entry second = new("b", "function", "SELECT 2;", Names: ["two"], Dependencies: ["a"]);
        switch (corruption)
        {
            case "kind":
                first = first with { Kind = "unknown" };
                break;
            case "id":
                first = first with { Id = " " };
                break;
            case "duplicate id":
                second = second with { Id = "a" };
                break;
            case "duplicate alias":
                first = first with { Names = ["one", "one"] };
                break;
            case "empty alias":
                first = first with { Names = [""] };
                break;
            case "nul alias":
                first = first with { Names = ["bad\0name"] };
                break;
            case "dependency":
                second = second with { Dependencies = ["missing"] };
                break;
            case "self dependency":
                first = first with { Dependencies = ["a"] };
                break;
            case "forward dependency":
                first = first with { Dependencies = ["b"] };
                break;
            case "owner":
                first = first with { Owner = "missing" };
                break;
            case "self owner":
                first = first with { Owner = "a" };
                break;
            case "nested owner":
                first = first with { Owner = "b" };
                second = second with { Owner = "a" };
                break;
            case "nul":
                first = first with { Id = "bad\0name" };
                break;
        }

        byte[] bytes = Convert.FromBase64String(Encode(first, second));
        switch (corruption)
        {
            case "magic":
                bytes[0] = 0;
                break;
            case "trailing":
                bytes = [.. bytes, 1];
                break;
            case "count":
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), 100_001);
                break;
            case "negative count":
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), -1);
                break;
            case "text length":
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), int.MaxValue);
                break;
            case "utf8":
                bytes[16] = 255;
                break;
        }

        Assert.ThrowsExactly<FormatException>(() => ExtensionSchemaGraph.Parse(Convert.ToBase64String(bytes)));
    }

    /// <summary>
    /// Every truncated byte position of a valid graph is rejected, including internal text and array boundaries.
    /// </summary>
    [TestMethod]
    public void RejectsEveryTruncatedGraph()
    {
        byte[] bytes = Convert.FromBase64String(Encode(new Entry("node", "sql", "SELECT '🐘';", Names: ["probe"])));
        for (int length = 0; length < bytes.Length; length++)
        {
            string truncated = Convert.ToBase64String(bytes.AsSpan(0, length));
            Assert.ThrowsExactly<FormatException>(() => ExtensionSchemaGraph.Parse(truncated));
        }
    }

    /// <summary>
    /// Invalid base64 and absent graph arguments cannot become empty graphs.
    /// </summary>
    /// <param name="encoded">The invalid encoded graph.</param>
    [TestMethod]
    [DataRow("")]
    [DataRow(" ")]
    [DataRow("AA==")]
    [DataRow("not base64!")]
    public void RejectsInvalidGraphEncoding(string encoded)
    {
        Assert.ThrowsExactly<FormatException>(() => ExtensionSchemaGraph.Parse(encoded));
    }

    /// <summary>
    /// The public graph and selection APIs reject absent inputs independently of empty selections.
    /// </summary>
    [TestMethod]
    public void RejectsNullGraphAndSelection()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => ExtensionSchemaGraph.Parse(null!));
        ExtensionSchema schema = Fixture();
        Assert.ThrowsExactly<ArgumentNullException>(() => schema.Select(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => schema.Select([null!]));
    }

    /// <summary>
    /// The string-array limit accepts its last permitted value and rejects the adjacent oversized count.
    /// </summary>
    /// <param name="count">The number of independently declared selection aliases.</param>
    [TestMethod]
    [DataRow(99_999)]
    [DataRow(100_000)]
    [DataRow(100_001)]
    public void EnforcesGraphArrayCountBoundary(int count)
    {
        string[] names = [.. Enumerable.Range(0, count).Select(static index => index.ToString(System.Globalization.CultureInfo.InvariantCulture))];
        string encoded = Encode(new Entry("node", "sql", "SELECT 1;", Names: names));
        if (count > 100_000)
        {
            Assert.ThrowsExactly<FormatException>(() => ExtensionSchemaGraph.Parse(encoded));
        }
        else
        {
            ExtensionSchemaItem item = Assert.ContainsSingle(ExtensionSchemaGraph.Parse(encoded).Items);
            Assert.AreSequenceEqual(names, item.Names);
        }
    }

    /// <summary>
    /// The decoded byte limit accepts its exact endpoint and rejects a graph one byte beyond it.
    /// </summary>
    /// <param name="offset">The distance from the maximum encoded graph size.</param>
    [TestMethod]
    [DataRow(-1)]
    [DataRow(0)]
    [DataRow(1)]
    public void EnforcesGraphByteBoundary(int offset)
    {
        const int Maximum = 32 * 1024 * 1024;
        int overhead = Convert.FromBase64String(Encode(new Entry("node", "sql", ""))).Length;
        int length = Maximum - overhead + offset;
        string encoded = Encode(new Entry("node", "sql", new string(' ', length)));
        Assert.HasCount(Maximum + offset, Convert.FromBase64String(encoded));
        if (offset > 0)
        {
            Assert.ThrowsExactly<FormatException>(() => ExtensionSchemaGraph.Parse(encoded));
        }
        else
        {
            ExtensionSchemaItem item = Assert.ContainsSingle(ExtensionSchemaGraph.Parse(encoded).Items);
            Assert.AreEqual(length, item.Sql.Length);
            Assert.IsTrue(item.Sql.All(static character => character == ' '));
        }
    }

    private static ExtensionSchema Fixture() => Schema(Encode(
        new("schema", "schema", "CREATE SCHEMA s;\n", Names: ["s"], Attachments: ["SCHEMA s"]),
        new("type", "type", "CREATE TYPE s.kind AS ENUM ('café 🐘');\n", Names: ["Kind"], Dependencies: ["schema"], Attachments: ["TYPE s.kind"]),
        new("first", "function", "CREATE FUNCTION s.first(s.kind) RETURNS integer AS 'MODULE_PATHNAME', 'first' LANGUAGE c;\n",
            Names: ["first"], Dependencies: ["type"], Attachments: ["FUNCTION s.first(s.kind)"]),
        new("second", "function", "SELECT 'second';\n", Names: ["second", "Functions.Second"], Dependencies: ["first", "type"], Attachments: ["FUNCTION s.second()"]),
        new("operator", "operator", "SELECT 'operator';\n", Owner: "first", Names: ["==="], Dependencies: ["first"], Attachments: ["OPERATOR s.===(s.kind, s.kind)"]),
        new("unrelated", "function", "SELECT 'unrelated';", Names: ["unrelated"]),
        new("raw", "sql", "SELECT $$raw ; ' quotes$$; -- tail", Names: ["raw"])));

    private static ExtensionSchema Schema(string graph, string? defaultSchema = null)
    {
        string sql = ExtensionSchemaGraph.Parse(graph).Sql;
        using var document = new MemoryStream();
        using (var writer = new Utf8JsonWriter(document))
        {
            writer.WriteStartObject();
            writer.WriteNumber("formatVersion", 2);
            writer.WriteString("name", "probe");
            writer.WriteString("version", "0.1.0");
            writer.WriteNumber("postgresMajor", 18);
            writer.WriteString("runtimeIdentifier", "linux-x64");
            writer.WriteString("library", "Probe.so");
            writer.WriteBoolean("relocatable", defaultSchema is null);
            writer.WriteString("sql", sql);
            writer.WriteString("graph", graph);
            if (defaultSchema is not null)
            {
                writer.WriteString("schema", defaultSchema);
            }

            writer.WriteEndObject();
        }

        byte[] payload = new byte[12 + document.Length];
        "ANKUSSC\0"u8.CopyTo(payload);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8), (uint)document.Length);
        document.ToArray().CopyTo(payload, 12);
        return ExtensionSchema.Decode(payload, "linux-x64");
    }

    private static string Encode(params Entry[] entries)
    {
        using var stream = new MemoryStream();
        stream.Write("ANKUSG1\0"u8);
        Number(entries.Length);
        foreach (Entry entry in entries)
        {
            Text(entry.Id);
            Text(entry.Kind);
            Text(entry.Sql);
            Text(entry.Owner);
            Texts(entry.Names ?? []);
            Texts(entry.Dependencies ?? []);
            Texts(entry.Attachments ?? []);
        }

        return Convert.ToBase64String(stream.ToArray());

        void Number(int value)
        {
            Span<byte> number = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(number, value);
            stream.Write(number);
        }

        void Text(string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            Number(bytes.Length);
            stream.Write(bytes);
        }

        void Texts(string[] values)
        {
            Number(values.Length);
            foreach (string value in values)
            {
                Text(value);
            }
        }
    }

    /// <summary>
    /// Supplies independent wire fields without constructing production graph nodes.
    /// </summary>
    private sealed record Entry(string Id, string Kind, string Sql, string Owner = "", string[]? Names = null,
        string[]? Dependencies = null, string[]? Attachments = null);
}
