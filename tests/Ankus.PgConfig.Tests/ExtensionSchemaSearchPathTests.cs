namespace Ankus.PgConfig.Tests;

/// <summary>
/// Verifies executable selected SQL has an exact installation-schema target and never rescans inserted values.
/// </summary>
public sealed partial class ExtensionSchemaGraphTests
{
    /// <summary>
    /// Explicit and fixed schema targets resolve both owned identities and reserved path tokens.
    /// </summary>
    /// <param name="target">The actual installation schema, including token-looking identifier values.</param>
    /// <param name="fixedSchema">Whether the publication fixes that same schema.</param>
    [TestMethod]
    [DataRow("public", false)]
    [DataRow("Mixed café schema", false)]
    [DataRow("Mixed \" schema", false)]
    [DataRow("@extschema@", false)]
    [DataRow("'MODULE_PATHNAME'", false)]
    [DataRow(" ", false)]
    [DataRow("Mixed café schema", true)]
    public void SelectionResolvesExtensionSchemaSearchPaths(string target, bool fixedSchema)
    {
        const string Sql = "CREATE FUNCTION \0f() RETURNS integer AS 'MODULE_PATHNAME', 'entry' LANGUAGE c SET search_path TO \"pg_catalog\", @extschema@, \"pg_temp\";\n";
        ExtensionSchema schema = Schema(Encode(new Entry("function", "function", Sql, Names: ["f"], Attachments: ["FUNCTION \0f()"])),
            fixedSchema ? target : null);
        string quoted = "\"" + target.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        ExtensionSchemaSelection selected = fixedSchema ? schema.Select(["f"]) : schema.Select(["f"], target);

        Assert.AreEqual("BEGIN;\n\nCREATE FUNCTION " + quoted + ".f() RETURNS integer AS '$libdir/Probe.so', 'entry' LANGUAGE c SET search_path TO \"pg_catalog\", " +
            quoted + ", \"pg_temp\";\nALTER EXTENSION \"probe\" ADD FUNCTION " + quoted + ".f();\n\nCOMMIT;\n", selected.Sql);
        Assert.AreEqual(Sql.Replace("\0", string.Empty, StringComparison.Ordinal), schema.Sql);
        Assert.IsEmpty(selected.Warnings);
        Assert.AreEqual(selected.Sql, schema.Select(["f"], target).Sql);
        Assert.AreEqual("CREATE FUNCTION " + quoted + ".f() RETURNS integer AS '$libdir/Probe.so', 'entry' LANGUAGE c SET search_path TO \"pg_catalog\", " +
            quoted + ", \"pg_temp\";\n", schema.Select(["f"], target, alterExtension: false).Sql);
    }

    /// <summary>
    /// Missing targets fail only for selected SQL that needs substitution; unrelated closures remain usable.
    /// </summary>
    [TestMethod]
    public void SelectionRequiresOnlySelectedExtensionSchemaTargets()
    {
        ExtensionSchema schema = Schema(Encode(
            new Entry("function", "function", "SELECT @extschema@;", Names: ["f"]),
            new Entry("independent", "sql", "SELECT 42;", Names: ["other"])));
        InvalidOperationException error = Assert.ThrowsExactly<InvalidOperationException>(() => schema.Select(["f"]));
        Assert.Contains("installation schema", error.Message);
        Assert.Contains("--schema", error.Message);
        Assert.AreEqual("SELECT 42;\n", schema.Select(["other"], alterExtension: false).Sql);
        Assert.ThrowsExactly<InvalidOperationException>(() => schema.Select(["f"], alterExtension: false));
        Assert.AreEqual("SELECT \"target\";\n", schema.Select(["f"], "target", alterExtension: false).Sql);
    }

    /// <summary>
    /// Invalid schema bytes and fixed-schema conflicts are rejected before any partial selected SQL is returned.
    /// </summary>
    /// <param name="kind">The invalid byte or Unicode partition to construct at runtime.</param>
    [TestMethod]
    [DataRow("empty")]
    [DataRow("zero")]
    [DataRow("high surrogate")]
    [DataRow("low surrogate")]
    public void SelectionRequiresValidExtensionSchemaTarget(string kind)
    {
        string target = kind switch
        {
            "empty" => string.Empty,
            "zero" => "bad\0schema",
            "high surrogate" => "bad" + new string('\ud800', 1) + "schema",
            _ => "bad" + new string('\udc00', 1) + "schema",
        };
        ExtensionSchema schema = Schema(Encode(new Entry("function", "function", "SELECT @extschema@;", Names: ["f"])));
        ArgumentException error = Assert.ThrowsExactly<ArgumentException>(() => schema.Select(["f"], target));
        Assert.AreEqual("extensionSchema", error.ParamName);
        ArgumentException conflict = Assert.ThrowsExactly<ArgumentException>(() =>
            Schema(Encode(new Entry("function", "function", "SELECT @extschema@;", Names: ["f"])), "fixed").Select(["f"], "other"));
        Assert.AreEqual("extensionSchema", conflict.ParamName);
        Assert.Contains("fixed control schema", conflict.Message);
    }

    /// <summary>
    /// Identifier limits are measured in UTF-8 bytes without rejecting a legal 63-byte target.
    /// </summary>
    /// <param name="unicode">Whether a two-byte scalar contributes to the limit.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void SelectionEnforcesExtensionSchemaByteLimit(bool unicode)
    {
        string target = unicode ? new string('é', 31) + "a" : new string('a', 63);
        ExtensionSchema schema = Schema(Encode(new Entry("function", "function", "SELECT @extschema@;", Names: ["f"])));
        Assert.AreEqual("SELECT \"" + target + "\";\n", schema.Select(["f"], target, alterExtension: false).Sql);
        ArgumentException error = Assert.ThrowsExactly<ArgumentException>(() => schema.Select(["f"], target + "a"));
        Assert.AreEqual("extensionSchema", error.ParamName);
    }

    /// <summary>
    /// Null targets cannot silently fall back to another namespace through the explicit overload.
    /// </summary>
    [TestMethod]
    public void SelectionRejectsNullExplicitExtensionSchema()
    {
        ExtensionSchema schema = Schema(Encode(new Entry("function", "function", "SELECT @extschema@;", Names: ["f"])));
        ArgumentNullException error = Assert.ThrowsExactly<ArgumentNullException>(() => schema.Select(["f"], extensionSchema: null!));
        Assert.AreEqual("extensionSchema", error.ParamName);
    }
}
