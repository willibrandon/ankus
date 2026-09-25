using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Ankus.Generators.Tests;

public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Every parent controls only its documented SQL while preserving native callbacks, registration, consumers and helpers.
    /// </summary>
    /// <param name="kind">The independently configurable declaration kind.</param>
    [TestMethod]
    [DataRow("type")]
    [DataRow("enum")]
    [DataRow("aggregate")]
    [DataRow("ordering")]
    [DataRow("hashing")]
    public void DeclarationSqlDefaultsAndControlsPreserveOwnedBoundaries(string kind)
    {
        Compilation baseline = GenerateSqlControl(DeclarationSource(kind, string.Empty));
        Compilation explicitDefaults = GenerateSqlControl(DeclarationSource(kind, "GenerateSql = true, Sql = null, SqlRelocatable = true"));
        string defaultSql = ManifestValue(baseline, "Ankus.Sql");
        string owned = DeclarationOwnedSql(kind, baseline);
        Assert.Contains(owned, defaultSql);
        Assert.AreEqual(defaultSql, ManifestValue(explicitDefaults, "Ankus.Sql"));
        AssertSqlControlBoundary(baseline, explicitDefaults);
        Assert.AreEqual("true", ManifestValue(explicitDefaults, "Ankus.Relocatable"));
        string retained = defaultSql.Replace(owned, string.Empty, StringComparison.Ordinal);
        (string Options, string Replacement, string Relocatable)[] modes =
        [
            ("GenerateSql = false", string.Empty, "true"),
            ("Sql = \"SELECT 'owned replacement';\", SqlRelocatable = true", "SELECT 'owned replacement';\n", "true"),
            ("Sql = \"\"", string.Empty, "false"),
            ("Sql = \" \\t\"", " \t\n", "false"),
            ("Sql = \"-- owned comment\"", "-- owned comment\n", "false"),
            ("Sql = \"-- café 😀\"", "-- café 😀\n", "false"),
        ];
        foreach ((string options, string replacement, string relocatable) in modes)
        {
            Compilation changed = GenerateSqlControl(DeclarationSource(kind, options));
            AssertSqlControlBoundary(baseline, changed);
            Assert.AreEqual(defaultSql.Replace(owned, replacement, StringComparison.Ordinal), ManifestValue(changed, "Ankus.Sql"));
            Assert.AreEqual(relocatable, ManifestValue(changed, "Ankus.Relocatable"));
            string changedSql = ManifestValue(changed, "Ankus.Sql");
            Assert.DoesNotContain(owned, changedSql);
            Assert.AreEqual(retained, replacement.Length == 0 ? changedSql : changedSql.Replace(replacement, string.Empty, StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// Type substitutions use each independently emitted native I/O role without changing registration or binary availability.
    /// </summary>
    /// <param name="binary">Whether receive and send exports are declared.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void DeclarationSqlTypeTokensMatchActualExports(bool binary)
    {
        string setting = binary ? "true" : "false";
        string declaration = "[Ankus.PgType(BinaryProtocol = " + setting + ", OPTIONS)] public readonly record struct NAME(int Number);";
        string alpha = declaration.Replace("NAME", "Alpha", StringComparison.Ordinal);
        string beta = declaration.Replace("NAME", "Beta", StringComparison.Ordinal);
        Compilation baseline = GenerateSqlControl((alpha + beta).Replace("OPTIONS", "Sql = null", StringComparison.Ordinal));
        string template = "SELECT 'TYPE', '@INPUT_FUNCTION_NAME@', '@OUTPUT_FUNCTION_NAME@', '@INPUT_FUNCTION_NAME@', '@MODULE_PATHNAME@', " +
            "'@FUNCTION_NAME@', '@HASH_FUNCTION_SQL@'" + (binary ? ", '@RECEIVE_FUNCTION_NAME@', '@SEND_FUNCTION_NAME@'" : string.Empty) + ";";
        string first = alpha.Replace("OPTIONS", "Sql = " + SymbolDisplay.FormatLiteral(template.Replace("TYPE", "alpha", StringComparison.Ordinal), true), StringComparison.Ordinal);
        string second = beta.Replace("OPTIONS", "Sql = " + SymbolDisplay.FormatLiteral(template.Replace("TYPE", "beta", StringComparison.Ordinal), true), StringComparison.Ordinal);
        Compilation replacement = GenerateSqlControl(first + second);
        Compilation reversed = GenerateSqlControl(second + first);
        Compilation disabled = GenerateSqlControl((alpha + beta).Replace("OPTIONS", "GenerateSql = false", StringComparison.Ordinal));
        AssertSqlControlBoundary(baseline, replacement);
        AssertSqlControlBoundary(replacement, reversed);
        AssertSqlControlBoundary(baseline, disabled);
        Assert.AreEqual("-- No installable objects declared.\n", ManifestValue(disabled, "Ankus.Sql"));
        Assert.AreEqual(ManifestValue(replacement, "Ankus.Sql"), ManifestValue(reversed, "Ankus.Sql"));
        var expected = new StringBuilder();
        string[] names = ["alpha", "beta"];
        foreach (string name in names)
        {
            string input = DeclarationIoExport(baseline, name, "in");
            string output = DeclarationIoExport(baseline, name, "out");
            expected.Append(System.Globalization.CultureInfo.InvariantCulture,
                $"SELECT '{name}', '{input}', '{output}', '{input}', 'MODULE_PATHNAME', '@FUNCTION_NAME@', '@HASH_FUNCTION_SQL@'");
            if (binary)
            {
                expected.Append(System.Globalization.CultureInfo.InvariantCulture,
                    $", '{DeclarationIoExport(baseline, name, "recv")}', '{DeclarationIoExport(baseline, name, "send")}'");
            }

            expected.Append(";\n");
        }

        Assert.AreEqual(expected.ToString(), ManifestValue(replacement, "Ankus.Sql"));
        string[] exports = SqlControlExports(replacement);
        Assert.HasCount(binary ? 8 : 4, exports);
        Assert.HasCount(exports.Length, exports.Distinct(StringComparer.Ordinal));
        if (!binary)
        {
            Assert.IsEmpty(exports.Where(static name => name.EndsWith("_type_recv", StringComparison.Ordinal) || name.EndsWith("_type_send", StringComparison.Ordinal)));
        }
    }

    /// <summary>
    /// Unavailable binary substitutions cannot create empty or nonexistent exports, including occurrences inside comments.
    /// </summary>
    /// <param name="literal">The replacement containing an unavailable token.</param>
    [TestMethod]
    [DataRow("SELECT '@RECEIVE_FUNCTION_NAME@';")]
    [DataRow("SELECT '@SEND_FUNCTION_NAME@';")]
    [DataRow("-- @RECEIVE_FUNCTION_NAME@")]
    [DataRow("/* @SEND_FUNCTION_NAME@ */")]
    public void DeclarationSqlUnavailableBinaryTokensAreDiagnosed(string literal)
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("[Ankus.PgType(Sql = " + SymbolDisplay.FormatLiteral(literal, true) +
            ")] public readonly record struct Value(int Number); public static class Other { [Ankus.PgFunction] public static int Good() => 7; }");
        AssertSqlControlGraphError(compilation, diagnostics, "BinaryProtocol");
    }

    /// <summary>
    /// Family substitutions preserve exact quoted helper identities, long UTF-8 fallbacks and marker-like schema text.
    /// </summary>
    /// <param name="kind">The family configuration kind.</param>
    /// <param name="longName">Whether the SQL type needs a hashed helper-name fallback.</param>
    /// <param name="markerSchema">Whether the schema contains the module placeholder text.</param>
    [TestMethod]
    [DataRow("ordering", false, false)]
    [DataRow("hashing", false, false)]
    [DataRow("ordering", true, false)]
    [DataRow("hashing", true, false)]
    [DataRow("ordering", false, true)]
    [DataRow("hashing", false, true)]
    public void DeclarationSqlFamilyTokensPreserveQuotedHelperNames(string kind, bool longName, bool markerSchema)
    {
        string name = longName ? new string('é', 31) : "Value \" Name";
        string schema = markerSchema ? "@MODULE_PATHNAME@ \" Schema" : "Quoted \" Schema";
        string attribute = kind == "ordering" ? "PgOrdering" : "PgHashing";
        string token = kind == "ordering" ? "@COMPARISON_FUNCTION_SQL@" : "@HASH_FUNCTION_SQL@";
        string otherToken = kind == "ordering" ? "@HASH_FUNCTION_SQL@" : "@COMPARISON_FUNCTION_SQL@";
        string role = kind == "ordering" ? "cmp" : "hash";
        string source = "[Ankus.PgEnum(Name = " + SymbolDisplay.FormatLiteral(name, true) + ", Schema = " + SymbolDisplay.FormatLiteral(schema, true) +
            "), Ankus.PgEquality, Ankus." + attribute + "(OPTIONS)] public enum Value { Low = -1, High = 7 }";
        Compilation baseline = GenerateSqlControl(source.Replace("OPTIONS", "Sql = null", StringComparison.Ordinal));
        string export = Assert.ContainsSingle(SqlControlExports(baseline).Where(symbol => symbol.EndsWith("_" + role, StringComparison.Ordinal)));
        string helperLine = Assert.ContainsSingle(ManifestValue(baseline, "Ankus.Sql").Split('\n').Where(line => line.Contains("'" + export + "'", StringComparison.Ordinal)));
        string helper = helperLine["CREATE FUNCTION ".Length..helperLine.IndexOf('(', StringComparison.Ordinal)];
        string quotedSchema = "\"" + schema.Replace("\"", "\"\"", StringComparison.Ordinal) + "\".";
        Assert.StartsWith(quotedSchema, helper);
        string localName = helper[quotedSchema.Length..];
        Assert.IsLessThanOrEqualTo(63, Encoding.UTF8.GetByteCount(localName[1..^1].Replace("\"\"", "\"", StringComparison.Ordinal)));
        if (longName)
        {
            Assert.StartsWith("\"ankus_", localName);
        }
        else
        {
            Assert.AreEqual("\"Value \"\" Name_" + role + "\"", localName);
        }

        string literal = "SELECT @MODULE_PATHNAME@, " + token + ", " + token + ", '" + otherToken + "', '@INPUT_FUNCTION_NAME@'; -- tail";
        Compilation replacement = GenerateSqlControl(source.Replace("OPTIONS", "Sql = " + SymbolDisplay.FormatLiteral(literal, true), StringComparison.Ordinal));
        AssertSqlControlBoundary(baseline, replacement);
        string sql = ManifestValue(replacement, "Ankus.Sql");
        Assert.Contains(helperLine + "\n", sql);
        Assert.EndsWith($"SELECT MODULE_PATHNAME, {helper}, {helper}, '{otherToken}', '@INPUT_FUNCTION_NAME@'; -- tail\n", sql);
        Assert.DoesNotContain("CREATE OPERATOR FAMILY", sql);
        Assert.DoesNotContain("CREATE OPERATOR CLASS", sql);
        Assert.AreEqual("false", ManifestValue(replacement, "Ankus.Relocatable"));
    }

    /// <summary>
    /// Module substitution is universal while tokens belonging to another declaration remain literal.
    /// </summary>
    /// <param name="kind">The declaration owning the replacement.</param>
    [TestMethod]
    [DataRow("enum")]
    [DataRow("aggregate")]
    public void DeclarationSqlOutOfContextTokensRemainLiteral(string kind)
    {
        const string literal = "SELECT '@MODULE_PATHNAME@', '@FUNCTION_NAME@', '@INPUT_FUNCTION_NAME@', '@OUTPUT_FUNCTION_NAME@', " +
            "'@RECEIVE_FUNCTION_NAME@', '@SEND_FUNCTION_NAME@', '@COMPARISON_FUNCTION_SQL@', '@HASH_FUNCTION_SQL@';";
        Compilation compilation = GenerateSqlControl(DeclarationSource(kind, "Sql = " + SymbolDisplay.FormatLiteral(literal, true)));
        Assert.Contains("SELECT 'MODULE_PATHNAME', '@FUNCTION_NAME@', '@INPUT_FUNCTION_NAME@', '@OUTPUT_FUNCTION_NAME@', " +
            "'@RECEIVE_FUNCTION_NAME@', '@SEND_FUNCTION_NAME@', '@COMPARISON_FUNCTION_SQL@', '@HASH_FUNCTION_SQL@';\n", ManifestValue(compilation, "Ankus.Sql"));
    }

    /// <summary>
    /// Parent and helper SQL policies stay independent even when both are suppressed or replaced.
    /// </summary>
    /// <param name="parentDisabled">Whether the aggregate parent is suppressed.</param>
    /// <param name="helperDisabled">Whether the transition helper is suppressed.</param>
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void DeclarationSqlAggregateAndHelperPoliciesRemainIndependent(bool parentDisabled, bool helperDisabled)
    {
        const string source = """
            [Ankus.PgAggregate(InitialCondition = "0", PARENT)]
            public static class Value
            {
                [Ankus.PgFunction(HELPER)] public static int Transition(int state, int value) => state + value;
            }
            """;
        Compilation baseline = GenerateSqlControl(source.Replace("PARENT", "Sql = null", StringComparison.Ordinal).Replace("HELPER", "Sql = null", StringComparison.Ordinal));
        string parent = parentDisabled ? "GenerateSql = false" : "Sql = \"SELECT 'parent';\"";
        string helper = helperDisabled ? "GenerateSql = false" : "Sql = \"SELECT 'helper';\"";
        Compilation changed = GenerateSqlControl(source.Replace("PARENT", parent, StringComparison.Ordinal).Replace("HELPER", helper, StringComparison.Ordinal));
        AssertSqlControlBoundary(baseline, changed);
        string expected = (helperDisabled ? string.Empty : "SELECT 'helper';\n") + (parentDisabled ? string.Empty : "SELECT 'parent';\n");
        Assert.AreEqual(expected.Length == 0 ? "-- No installable objects declared.\n" : expected, ManifestValue(changed, "Ankus.Sql"));
        Assert.AreEqual(parentDisabled && helperDisabled ? "true" : "false", ManifestValue(changed, "Ankus.Relocatable"));
    }

    /// <summary>
    /// Suppressing either index family retains the sibling family and all comparison and hash support.
    /// </summary>
    /// <param name="kind">The suppressed family.</param>
    [TestMethod]
    [DataRow("ordering")]
    [DataRow("hashing")]
    public void DeclarationSqlFamilyPoliciesRemainIndependent(string kind)
    {
        const string source = "[Ankus.PgEnum, Ankus.PgEquality, Ankus.PgOrdering(ORDERING), Ankus.PgHashing(HASHING)] public enum Value { Low = 1, High = 9 }";
        Compilation baseline = GenerateSqlControl(source.Replace("ORDERING", "Sql = null", StringComparison.Ordinal).Replace("HASHING", "Sql = null", StringComparison.Ordinal));
        Compilation changed = GenerateSqlControl(source.Replace("ORDERING", kind == "ordering" ? "GenerateSql = false" : "Sql = null", StringComparison.Ordinal)
            .Replace("HASHING", kind == "hashing" ? "GenerateSql = false" : "Sql = null", StringComparison.Ordinal));
        AssertSqlControlBoundary(baseline, changed);
        Assert.AreEqual(ManifestValue(baseline, "Ankus.Sql").Replace(DeclarationOwnedSql(kind, baseline), string.Empty, StringComparison.Ordinal), ManifestValue(changed, "Ankus.Sql"));
        Assert.HasCount(8, SqlControlExports(changed));
        Assert.Contains(DeclarationOwnedSql(kind == "ordering" ? "hashing" : "ordering", baseline), ManifestValue(changed, "Ankus.Sql"));
    }

    /// <summary>
    /// Declaration aliases and incoming ordering edges survive replacement and suppression without erasing consumers.
    /// </summary>
    /// <param name="kind">The declaration policy kind.</param>
    [TestMethod]
    [DataRow("type")]
    [DataRow("enum")]
    [DataRow("aggregate")]
    [DataRow("ordering")]
    [DataRow("hashing")]
    public void DeclarationSqlControlsRetainDependencies(string kind)
    {
        const string graph = """
            [assembly: Ankus.PgSql("first", "SELECT 'first';", Order = Ankus.PgSqlOrder.Bootstrap)]
            [assembly: Ankus.PgSql("before", "SELECT 'before';")]
            [assembly: Ankus.PgSql("incoming", "SELECT 'incoming';", Before = new[] { "parent" })]
            [assembly: Ankus.PgSql("after", "SELECT 'after';", Requires = new[] { "parent" })]
            [assembly: Ankus.PgSql("last", "SELECT 'last';", Order = Ankus.PgSqlOrder.Finalize)]
            """;
        const string dependency = "Id = \"parent\", Requires = new[] { \"before\" }, ";
        Compilation replacement = GenerateSqlControl(graph + DeclarationSource(kind, dependency + "Sql = \"SELECT 'replacement';\""));
        Compilation disabled = GenerateSqlControl(graph + DeclarationSource(kind, dependency + "GenerateSql = false"));
        string sql = ManifestValue(replacement, "Ankus.Sql");
        Assert.StartsWith("SELECT 'first';\n", sql);
        Assert.EndsWith("SELECT 'last';\n", sql);
        AssertSqlControlBefore(sql, "SELECT 'before';", "SELECT 'replacement';");
        AssertSqlControlBefore(sql, "SELECT 'incoming';", "SELECT 'replacement';");
        AssertSqlControlBefore(sql, "SELECT 'replacement';", "SELECT 'after';");
        string disabledSql = ManifestValue(disabled, "Ankus.Sql");
        AssertSqlControlBefore(disabledSql, "SELECT 'before';", "SELECT 'after';");
        AssertSqlControlBefore(disabledSql, "SELECT 'incoming';", "SELECT 'after';");
        Assert.DoesNotContain("SELECT 'replacement';", disabledSql);
        AssertSqlControlBoundary(replacement, disabled);
        if (kind is "type" or "enum")
        {
            AssertSqlControlBefore(sql, "SELECT 'replacement';", "CREATE FUNCTION \"read\"");
            Assert.Contains("CREATE FUNCTION \"read\"", disabledSql);
        }
        else if (kind == "aggregate")
        {
            AssertSqlControlBefore(sql, "CREATE FUNCTION \"value_transition\"", "SELECT 'replacement';");
        }
        else
        {
            AssertSqlControlBefore(sql, "CREATE TYPE \"value\" AS ENUM", "SELECT 'replacement';");
            AssertSqlControlBefore(sql, kind == "ordering" ? "CREATE FUNCTION \"value_cmp\"" : "CREATE FUNCTION \"value_hash\"", "SELECT 'replacement';");
        }
    }

    /// <summary>
    /// Every parent still validates missing, duplicate and cyclic graph identities in both SQL control modes.
    /// </summary>
    /// <param name="kind">The declaration policy kind.</param>
    [TestMethod]
    [DataRow("type")]
    [DataRow("enum")]
    [DataRow("aggregate")]
    [DataRow("ordering")]
    [DataRow("hashing")]
    public void DeclarationSqlControlsRetainGraphDiagnostics(string kind)
    {
        (string Prefix, string Options, string Reason)[] invalid =
        [
            (string.Empty, "Id = \"parent\", Requires = new[] { \"missing\" }", "missing dependency"),
            ("[assembly: Ankus.PgSql(\"parent\", \"SELECT 1;\")]", "Id = \"parent\"", "declared more than once"),
            ("[assembly: Ankus.PgSql(\"before\", \"SELECT 1;\", Requires = new[] { \"parent\" })]", "Id = \"parent\", Requires = new[] { \"before\" }", "cycle"),
        ];
        string[] policies = ["GenerateSql = false", "Sql = \"SELECT 'replacement';\""];
        foreach ((string prefix, string options, string reason) in invalid)
        {
            foreach (string policy in policies)
            {
                (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate(prefix + DeclarationSource(kind, options + ", " + policy));
                AssertSqlControlGraphError(compilation, diagnostics, reason);
            }
        }
    }

    /// <summary>
    /// SQL suppression does not discard declared type, aggregate or retained helper signature collision checks.
    /// </summary>
    /// <param name="kind">The declaration policy kind.</param>
    /// <param name="expected">The existing collision diagnostic.</param>
    [TestMethod]
    [DataRow("type", "ANKUS005")]
    [DataRow("enum", "ANKUS005")]
    [DataRow("aggregate", "ANKUS002")]
    [DataRow("ordering", "ANKUS005")]
    [DataRow("hashing", "ANKUS005")]
    public void DeclarationSqlControlsRetainNameCollisions(string kind, string expected)
    {
        string[] policies = ["GenerateSql = false", "Sql = \"SELECT 1;\""];
        foreach (string policy in policies)
        {
            string source = kind switch
            {
                "type" => "[Ankus.PgType(Name = \"same\", " + policy + ")] public readonly record struct First(int Number);" +
                    "[Ankus.PgType(Name = \"same\", " + policy + ")] public readonly record struct Second(int Number);",
                "enum" => "[Ankus.PgEnum(Name = \"same\", " + policy + ")] public enum First { A }" +
                    "[Ankus.PgEnum(Name = \"same\", " + policy + ")] public enum Second { B }",
                "aggregate" => "[Ankus.PgAggregate(Name = \"same\", " + policy + ")] public static class First { public static int Transition(int state, int value) => state + value; }" +
                    "[Ankus.PgAggregate(Name = \"same\", " + policy + ")] public static class Second { public static int Transition(int state, int value) => state + value; }",
                "ordering" => DeclarationSource(kind, policy) + "public static class Other { [Ankus.PgFunction(Name = \"value_cmp\")] public static int Compare(Value left, Value right) => 0; }",
                "hashing" => DeclarationSource(kind, policy) + "public static class Other { [Ankus.PgFunction(Name = \"value_hash\")] public static int Hash(Value value) => 0; }",
                _ => throw new ArgumentOutOfRangeException(nameof(kind)),
            };
            (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate(source);
            Assert.IsNotEmpty(diagnostics);
            Assert.IsEmpty(diagnostics.Where(diagnostic => diagnostic.Id != expected || diagnostic.Severity != DiagnosticSeverity.Error));
            if (expected == "ANKUS005")
            {
                AssertSqlControlGraphError(compilation, diagnostics, string.Empty);
            }
        }
    }

    /// <summary>
    /// One generator driver observes each declaration's SQL-only edits without stale text, flags or native symbols.
    /// </summary>
    /// <param name="kind">The declaration policy kind.</param>
    [TestMethod]
    [DataRow("type")]
    [DataRow("enum")]
    [DataRow("aggregate")]
    [DataRow("ordering")]
    [DataRow("hashing")]
    public void DeclarationSqlAttributeEditsInvalidateOutput(string kind)
    {
        CSharpCompilation input = CSharpCompilation.Create("GeneratorTest", references: s_references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, nullableContextOptions: NullableContextOptions.Enable));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new PgFunctionGenerator().AsSourceGenerator());
        Compilation? baseline = null;
        string? originalSql = null;
        string? owned = null;
        (string Options, string? Replacement, string Relocatable)[] edits =
        [
            (string.Empty, null, "true"),
            ("Sql = \"SELECT 'first';\"", "SELECT 'first';\n", "false"),
            ("Sql = \"SELECT 'second';\", SqlRelocatable = true", "SELECT 'second';\n", "true"),
            ("GenerateSql = false", string.Empty, "true"),
            ("Sql = null", null, "true"),
        ];
        foreach ((string options, string? replacement, string relocatable) in edits)
        {
            CSharpCompilation current = input.AddSyntaxTrees(CSharpSyntaxTree.ParseText(DeclarationSource(kind, options), cancellationToken: context.CancellationToken));
            driver = driver.RunGeneratorsAndUpdateCompilation(current, out Compilation output, out ImmutableArray<Diagnostic> diagnostics, context.CancellationToken);
            AssertSqlControlCompilation(output, diagnostics);
            baseline ??= output;
            originalSql ??= ManifestValue(output, "Ankus.Sql");
            owned ??= DeclarationOwnedSql(kind, output);
            AssertSqlControlBoundary(baseline, output);
            Assert.AreEqual(replacement is null ? originalSql : originalSql.Replace(owned, replacement, StringComparison.Ordinal), ManifestValue(output, "Ankus.Sql"));
            Assert.AreEqual(relocatable, ManifestValue(output, "Ankus.Relocatable"));
        }
    }

    /// <summary>
    /// Relocation defaults and mixed declarations compose identically for every independently owned boundary.
    /// </summary>
    /// <param name="kind">The declaration policy kind.</param>
    [TestMethod]
    [DataRow("type")]
    [DataRow("enum")]
    [DataRow("aggregate")]
    [DataRow("ordering")]
    [DataRow("hashing")]
    public void DeclarationSqlRelocationRequiresEveryReplacement(string kind)
    {
        (string Options, string Prefix, bool FixedSchema, string Expected)[] cases =
        [
            ("Sql = null, SqlRelocatable = false", string.Empty, false, "true"),
            ("GenerateSql = false, SqlRelocatable = false", string.Empty, false, "true"),
            ("GenerateSql = false, SqlRelocatable = true", string.Empty, false, "true"),
            ("Sql = \"\"", string.Empty, false, "false"),
            ("Sql = \"SELECT 1;\"", string.Empty, false, "false"),
            ("Sql = \"SELECT 1;\", SqlRelocatable = true", string.Empty, false, "true"),
            ("Sql = \"SELECT 1;\", SqlRelocatable = true", "[assembly: Ankus.PgSql(\"other\", \"SELECT 2;\")]", false, "false"),
            ("Sql = \"SELECT 1;\", SqlRelocatable = true", "[assembly: Ankus.PgSql(\"other\", \"SELECT 2;\", Relocatable = true)]", false, "true"),
            ("Sql = \"SELECT 1;\", SqlRelocatable = true", string.Empty, true, "false"),
            ("Sql = \"SELECT 1;\", SqlRelocatable = true", "[Ankus.PgEnum(Sql = \"SELECT 2;\")] public enum Other { First }", false, "false"),
        ];
        foreach ((string options, string prefix, bool fixedSchema, string expected) in cases)
        {
            string source = DeclarationSource(kind, options);
            if (fixedSchema)
            {
                source = "[Ankus.PgSchema(\"Fixed \\\" Schema\")] public static class Container {" + source + "}";
            }

            Compilation compilation = GenerateSqlControl(prefix + source);
            Assert.AreEqual(expected, ManifestValue(compilation, "Ankus.Relocatable"));
            if (fixedSchema)
            {
                AssertSqlControlBefore(ManifestValue(compilation, "Ankus.Sql"), "CREATE SCHEMA IF NOT EXISTS \"Fixed \"\" Schema\";", "SELECT 1;");
            }
        }
    }

    /// <summary>
    /// Conflicting options and malformed Unicode reject complete manifests for all five parent attributes.
    /// </summary>
    /// <param name="kind">The declaration policy kind.</param>
    [TestMethod]
    [DataRow("type")]
    [DataRow("enum")]
    [DataRow("aggregate")]
    [DataRow("ordering")]
    [DataRow("hashing")]
    public void InvalidDeclarationSqlOptionsAreDiagnosed(string kind)
    {
        string[] options =
        [
            "GenerateSql = false, Sql = \"SELECT 1;\"",
            "GenerateSql = false, Sql = \"\"",
            "Sql = \"SELECT '\\0';\"",
            "Sql = \"SELECT '\\ud800';\"",
            "Sql = \"SELECT '\\udfff';\"",
        ];
        foreach (string option in options)
        {
            (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate(DeclarationSource(kind, option) +
                "public static class Other { [Ankus.PgFunction] public static int Good() => 7; }");
            AssertSqlControlGraphError(compilation, diagnostics, string.Empty);
        }
    }

    /// <summary>
    /// Suppressed or replaced SQL does not relax storage, enum, aggregate or exact value-contract validation.
    /// </summary>
    /// <param name="source">The invalid declaration with a SQL policy placeholder.</param>
    /// <param name="id">The independently expected existing diagnostic.</param>
    [TestMethod]
    [DataRow("[Ankus.PgType(POLICY)] public readonly record struct Value(System.Uri Location);", "ANKUS017")]
    [DataRow("[Ankus.PgEnum(POLICY), System.Flags] public enum Value { First = 1, Last = 2 }", "ANKUS006")]
    [DataRow("[Ankus.PgAggregate(POLICY)] public static class Value { public static string Transition(int state, int input) => input.ToString(); }", "ANKUS012")]
    [DataRow("[Ankus.PgType, Ankus.PgEquality, Ankus.PgOrdering(POLICY)] public readonly record struct Value(int Number);", "ANKUS018")]
    [DataRow("[Ankus.PgType, Ankus.PgEquality, Ankus.PgHashing(POLICY)] public readonly record struct Value(int Number);", "ANKUS018")]
    public void DeclarationSqlDoesNotBypassContracts(string source, string id)
    {
        string[] policies = ["GenerateSql = false", "Sql = \"\""];
        foreach (string policy in policies)
        {
            (_, ImmutableArray<Diagnostic> diagnostics) = Generate(source.Replace("POLICY", policy, StringComparison.Ordinal));
            Assert.IsNotEmpty(diagnostics);
            foreach (Diagnostic diagnostic in diagnostics)
            {
                Assert.AreEqual(id, diagnostic.Id);
                Assert.AreEqual(DiagnosticSeverity.Error, diagnostic.Severity);
                Assert.IsTrue(diagnostic.Location.IsInSource);
            }
        }
    }

    /// <summary>
    /// Creates valid parents with consumers and independent helper declarations that must survive SQL suppression.
    /// </summary>
    private static string DeclarationSource(string kind, string options)
    {
        const string consumer = "public static class Functions { [Ankus.PgFunction] public static int Read(Value value) => (int)value; }";
        string custom = options.Length == 0 ? "BinaryProtocol = true" : "BinaryProtocol = true, " + options;
        return kind switch
        {
            "type" => "[Ankus.PgType(" + custom + "), Ankus.PgEquality, Ankus.PgOrdering, Ankus.PgHashing] public enum Value { Low = 3, High = -1 }" + consumer,
            "enum" => "[Ankus.PgEnum(" + options + ")] public enum Value { Low = 3, High = -1 }" + consumer,
            "aggregate" => "[Ankus.PgAggregate(InitialCondition = \"0\"" + (options.Length == 0 ? string.Empty : ", " + options) +
                ")] public static class Value { public static int Transition(int state, int value) => state + value; }",
            "ordering" => "[Ankus.PgEnum, Ankus.PgEquality, Ankus.PgOrdering(" + options + ")] public enum Value { Low = 3, High = -1 }",
            "hashing" => "[Ankus.PgEnum, Ankus.PgEquality, Ankus.PgHashing(" + options + ")] public enum Value { Low = 3, High = -1 }",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    /// <summary>
    /// Gives independently specified complete SQL for each owned declaration boundary, using exports only as opaque identities.
    /// </summary>
    private static string DeclarationOwnedSql(string kind, Compilation compilation)
    {
        if (kind == "type")
        {
            return "CREATE TYPE \"value\";\n" +
                $"CREATE FUNCTION \"value_in\"(cstring) RETURNS \"value\" AS 'MODULE_PATHNAME', '{DeclarationIoExport(compilation, "value", "in")}' LANGUAGE c IMMUTABLE STRICT PARALLEL SAFE;\n" +
                $"CREATE FUNCTION \"value_out\"(\"value\") RETURNS cstring AS 'MODULE_PATHNAME', '{DeclarationIoExport(compilation, "value", "out")}' LANGUAGE c IMMUTABLE STRICT PARALLEL SAFE;\n" +
                $"CREATE FUNCTION \"value_recv\"(internal) RETURNS \"value\" AS 'MODULE_PATHNAME', '{DeclarationIoExport(compilation, "value", "recv")}' LANGUAGE c IMMUTABLE STRICT PARALLEL SAFE;\n" +
                $"CREATE FUNCTION \"value_send\"(\"value\") RETURNS bytea AS 'MODULE_PATHNAME', '{DeclarationIoExport(compilation, "value", "send")}' LANGUAGE c IMMUTABLE STRICT PARALLEL SAFE;\n" +
                "CREATE TYPE \"value\" (\n    INTERNALLENGTH = variable, INPUT = \"value_in\", OUTPUT = \"value_out\",\n" +
                "    RECEIVE = \"value_recv\", SEND = \"value_send\",\n    ALIGNMENT = int4, STORAGE = extended);\n";
        }

        return kind switch
        {
            "enum" => "CREATE TYPE \"value\" AS ENUM (E'Low', E'High');\n",
            "aggregate" => "CREATE AGGREGATE \"value\"(\"value\" integer) (\n    SFUNC = \"value_transition\",\n    STYPE = integer,\n" +
                "    FINALFUNC_MODIFY = READ_ONLY,\n    INITCOND = E'0',\n    PARALLEL = UNSAFE\n);\n",
            "ordering" => "CREATE OPERATOR FAMILY \"value_btree_ops\" USING btree;\n" +
                "CREATE OPERATOR CLASS \"value_btree_ops\" DEFAULT FOR TYPE \"value\" USING btree FAMILY \"value_btree_ops\" AS\n" +
                "    OPERATOR 1 < (\"value\",\"value\"),\n    OPERATOR 2 <= (\"value\",\"value\"),\n    OPERATOR 3 = (\"value\",\"value\"),\n" +
                "    OPERATOR 4 >= (\"value\",\"value\"),\n    OPERATOR 5 > (\"value\",\"value\"),\n    FUNCTION 1 \"value_cmp\"(\"value\",\"value\");\n",
            "hashing" => "CREATE OPERATOR FAMILY \"value_hash_ops\" USING hash;\n" +
                "CREATE OPERATOR CLASS \"value_hash_ops\" DEFAULT FOR TYPE \"value\" USING hash FAMILY \"value_hash_ops\" AS\n" +
                "    OPERATOR 1 = (\"value\",\"value\"),\n    FUNCTION 1 \"value_hash\"(\"value\");\n",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    /// <summary>
    /// Reads a type's actual role export from its default SQL and independently checks that the linker exports it.
    /// </summary>
    private static string DeclarationIoExport(Compilation compilation, string name, string role)
    {
        string line = Assert.ContainsSingle(ManifestValue(compilation, "Ankus.Sql").Split('\n').Where(line =>
            line.StartsWith("CREATE FUNCTION \"" + name + "_" + role + "\"(", StringComparison.Ordinal)));
        string export = line.Split('\'')[3];
        Assert.Contains(export, SqlControlExports(compilation));
        Assert.EndsWith("_type_" + role, export);
        return export;
    }
}
