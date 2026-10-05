using Microsoft.CodeAnalysis;

namespace Ankus.Generators.Tests;

public sealed partial class SpiInterpolationAnalyzerTests
{
    /// <summary>
    /// Requires the exact raw-command diagnostic for misplaced fragments and reaching unsafe construction.
    /// </summary>
    /// <param name="body">The valid consumer construction and command.</param>
    /// <param name="argument">The exact command argument receiving the diagnostic.</param>
    [TestMethod]
    [DataRow("Spi.Execute($\"SELECT '{Spi.QuoteIdentifier(value)}'\");", "$\"SELECT '{Spi.QuoteIdentifier(value)}'\"", DisplayName = "inside_string")]
    [DataRow("Spi.Execute($\"SELECT \\\"{Spi.QuoteIdentifier(value)}\\\"\");", "$\"SELECT \\\"{Spi.QuoteIdentifier(value)}\\\"\"", DisplayName = "inside_identifier")]
    [DataRow("Spi.Execute($\"SELECT 1 -- {Spi.QuoteIdentifier(value)}\");", "$\"SELECT 1 -- {Spi.QuoteIdentifier(value)}\"", DisplayName = "inside_line_comment")]
    [DataRow("Spi.Execute($\"SELECT 1 /* outer /* {Spi.QuoteIdentifier(value)} */ end */\");", "$\"SELECT 1 /* outer /* {Spi.QuoteIdentifier(value)} */ end */\"", DisplayName = "inside_nested_comment")]
    [DataRow("Spi.Execute($\"SELECT $tag${Spi.QuoteLiteral(value)}$tag$\");", "$\"SELECT $tag${Spi.QuoteLiteral(value)}$tag$\"", DisplayName = "inside_dollar_string")]
    [DataRow("Spi.Execute($\"SELECT E'{Spi.QuoteIdentifier(value)}'\");", "$\"SELECT E'{Spi.QuoteIdentifier(value)}'\"", DisplayName = "inside_escape_string")]
    [DataRow("Spi.Execute(\"SELECT '\" + Spi.QuoteIdentifier(value) + \"'\");", "\"SELECT '\" + Spi.QuoteIdentifier(value) + \"'\"", DisplayName = "quoted_concatenation")]
    [DataRow("Spi.Execute(string.Format(\"SELECT '{0}'\", Spi.QuoteIdentifier(value)));", "string.Format(\"SELECT '{0}'\", Spi.QuoteIdentifier(value))", DisplayName = "quoted_format")]
    [DataRow("string command = $\"SELECT {value}\"; Spi.Execute(command);", "command", DisplayName = "stored_interpolation")]
    [DataRow("Spi.Execute(\"SELECT \" + value);", "\"SELECT \" + value", DisplayName = "raw_concatenation")]
    [DataRow("Spi.Execute(new System.Text.StringBuilder(\"SELECT \").Append(value).ToString());", "new System.Text.StringBuilder(\"SELECT \").Append(value).ToString()", DisplayName = "builder_value")]
    [DataRow("Spi.Execute(new System.Text.StringBuilder(\"SELECT '\").Append(Spi.QuoteIdentifier(value)).Append(\"'\").ToString());", "new System.Text.StringBuilder(\"SELECT '\").Append(Spi.QuoteIdentifier(value)).Append(\"'\").ToString()", DisplayName = "builder_quoted_context")]
    [DataRow("Spi.Execute(new System.Text.StringBuilder().AppendFormat(\"SELECT {0}\", value).ToString());", "new System.Text.StringBuilder().AppendFormat(\"SELECT {0}\", value).ToString()", DisplayName = "builder_format_value")]
    [DataRow("var sql = new System.Text.StringBuilder(\"SELECT \"); sql.Append(value); Spi.Execute(sql.ToString());", "sql.ToString()", DisplayName = "builder_local_value")]
    [DataRow("string sql = \"SELECT 1\"; sql = $\"SELECT {value}\"; Spi.Execute(sql);", "sql", DisplayName = "command_local_written")]
    [DataRow("var sql = new System.Text.StringBuilder(\"SELECT '\"); sql.Append(Spi.QuoteIdentifier(value)); sql.Append(\"'\"); Spi.Execute(sql.ToString());", "sql.ToString()", DisplayName = "builder_local_quoted_context")]
    [DataRow("var sql = new System.Text.StringBuilder(\"SELECT \"); var alias = sql; alias.Append(value); Spi.Execute(sql.ToString());", "sql.ToString()", DisplayName = "builder_alias_value")]
    [DataRow("string sql = \"SELECT 1\"; if (flag) { sql = $\"SELECT {value}\"; } Spi.Execute(sql);", "sql", DisplayName = "conditional_write")]
    [DataRow("var sql = new System.Text.StringBuilder(); while (flag) { sql.Append(value); flag = false; } Spi.Execute(sql.ToString());", "sql.ToString()", DisplayName = "loop_write")]
    [DataRow("string sql = \"SELECT 1\"; try { } finally { sql = $\"SELECT {value}\"; } Spi.Execute(sql);", "sql", DisplayName = "finally_write")]
    [DataRow("string sql = \"SELECT 1\"; try { sql = $\"SELECT {value}\"; throw new System.Exception(); } catch { Spi.Execute(sql); }", "sql", DisplayName = "catch_write")]
    [DataRow("string sql = Spi.QuoteLiteral(value); System.Action change = () => sql = value; change(); Spi.Execute($\"SELECT {sql}\");", "$\"SELECT {sql}\"", DisplayName = "delegate_write")]
    [DataRow("string sql = Spi.QuoteLiteral(value); void Change() { sql = value; } Change(); Spi.Execute($\"SELECT {sql}\");", "$\"SELECT {sql}\"", DisplayName = "local_function_write")]
    [DataRow("Spi.Connect(session => session.Execute($\"SELECT {value}\"));", "$\"SELECT {value}\"", DisplayName = "nested_query")]
    [DataRow("Spi.Connect(session => session.Execute($\"SELECT '{Spi.QuoteIdentifier(value)}'\"));", "$\"SELECT '{Spi.QuoteIdentifier(value)}'\"", DisplayName = "nested_quoted_context")]
    [DataRow("string sql = Spi.QuoteLiteral(value); ref string alias = ref sql; alias = value; Spi.Execute($\"SELECT {sql}\");", "$\"SELECT {sql}\"", DisplayName = "ref_alias_write")]
    [DataRow("string sql = Spi.QuoteLiteral(value); (sql, _) = (value, 1); Spi.Execute($\"SELECT {sql}\");", "$\"SELECT {sql}\"", DisplayName = "deconstruction_write")]
    [DataRow("string sql = $\"SELECT {value}\"; Other.Maybe(() => sql = \"SELECT 1\"); Spi.Execute(sql);", "sql", DisplayName = "callback_may_not_run")]
    [DataRow("var sql = new System.Text.StringBuilder(\"SELECT \").Append(Spi.QuoteLiteral(value)); Other.Mutate(sql); Spi.Execute(sql.ToString());", "sql.ToString()", DisplayName = "external_builder_mutation")]
    [DataRow("Spi.Execute(new System.Text.StringBuilder(Spi.QuoteLiteral(value), 1, 3, 16).ToString());", "new System.Text.StringBuilder(Spi.QuoteLiteral(value), 1, 3, 16).ToString()", DisplayName = "builder_slice_quote")]
    [DataRow("Spi.Execute(new System.Text.StringBuilder(Spi.QuoteLiteral(value)).ToString(1, 3));", "new System.Text.StringBuilder(Spi.QuoteLiteral(value)).ToString(1, 3)", DisplayName = "builder_to_string_slice_quote")]
    [DataRow("var old = new System.Text.StringBuilder(); var current = old; while (flag) { old = current; current = new System.Text.StringBuilder().Append(value); flag = number-- > 0; } current.Clear(); current.Append(\"SELECT 1\"); Spi.Execute(old.ToString());", "old.ToString()", DisplayName = "separate_allocations_loop")]
    [DataRow("Spi.Execute($\"SELECT {value}\");", "$\"SELECT {value}\"", DisplayName = "direct_value_control")]
    [DataRow("object[] inputs = { value }; Spi.Execute(string.Format(\"SELECT {0}\", inputs));", "string.Format(\"SELECT {0}\", inputs)", DisplayName = "array_local_dynamic_format")]
    [DataRow("object[] inputs = { Spi.QuoteLiteral(value) }; inputs[0] = value; Spi.Execute(string.Format(\"SELECT {0}\", inputs));", "string.Format(\"SELECT {0}\", inputs)", DisplayName = "array_write_format")]
    [DataRow("object[] inputs = { Spi.QuoteLiteral(value) }; object[] alias = inputs; alias[0] = value; Spi.Execute(string.Format(\"SELECT {0}\", inputs));", "string.Format(\"SELECT {0}\", inputs)", DisplayName = "array_alias_format")]
    [DataRow("var old = new System.Text.StringBuilder(); var current = old; int iteration = 0; do { old = current; current = new System.Text.StringBuilder().Append(value); } while (++iteration < number); current.Clear(); current.Append(\"SELECT 1\"); Spi.Execute(old.ToString());", "old.ToString()", DisplayName = "do_loop_distinct_allocations")]
    [DataRow("string sql = Spi.QuoteLiteral(value); void Change(ref string text) { text = value; } Change(ref sql); Spi.Execute($\"SELECT {sql}\");", "$\"SELECT {sql}\"", DisplayName = "local_ref_write")]
    [DataRow("string sql = Spi.QuoteLiteral(value); void Change() { sql = value; } System.Action callback = Change; callback(); Spi.Execute($\"SELECT {sql}\");", "$\"SELECT {sql}\"", DisplayName = "local_method_group_write")]
    [DataRow("string sql = \"SELECT 1\"; System.Action callback = flag ? () => sql = $\"SELECT {value}\" : () => sql = \"SELECT 1\"; callback(); Spi.Execute(sql);", "sql", DisplayName = "delegate_choices")]
    public async Task ConstructedRawCommandsAreRejected(string body, string argument)
    {
        Diagnostic error = Assert.ContainsSingle(await Analyze(body, FlowHelpers, "bool flag = number != 0;"));
        Assert.AreEqual("ANKUS044", error.Id);
        Assert.AreEqual(DiagnosticSeverity.Error, error.Severity);
        Assert.AreEqual(argument, error.Location.SourceTree!.GetText(context.CancellationToken).ToString(error.Location.SourceSpan));
        Assert.Contains("Spi.Sql", error.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
        Assert.AreEqual("https://willibrandon.github.io/ankus/spi/#parameterized-interpolation", error.Descriptor.HelpLinkUri);
    }

    /// <summary>
    /// Preserves literal, quoted, overwritten and cleared command values through actual consumer control flow.
    /// </summary>
    /// <param name="body">The safe consumer construction and command.</param>
    [TestMethod]
    [DataRow("Spi.Execute($\"SELECT count(*) FROM {Spi.QuoteIdentifier(value)}\");")]
    [DataRow("Spi.Execute($\"SELECT {Spi.QuoteLiteral(value)}\");")]
    [DataRow("Spi.Execute(\"SELECT timestamp\" + Spi.QuoteLiteral(value));")]
    [DataRow("Spi.Execute(\"SELECT 'prefix'\\n\" + Spi.QuoteLiteral(value));")]
    [DataRow("Spi.Execute(Spi.QuoteLiteral(value) + \"\\n\" + Spi.QuoteLiteral(value));")]
    [DataRow("Spi.Execute(\"SELECT E'prefix'\\n\" + Spi.QuoteLiteral(value));")]
    [DataRow("Spi.Execute(string.Format(\"SELECT {0}\", Spi.QuoteLiteral(value)));")]
    [DataRow("Spi.Execute(new System.Text.StringBuilder(\"SELECT 1\").ToString());")]
    [DataRow("Spi.Execute(new System.Text.StringBuilder(\"SELECT 1 FROM \").Append(Spi.QuoteIdentifier(value)).ToString());")]
    [DataRow("Spi.Execute(new System.Text.StringBuilder().AppendFormat(\"SELECT {0}\", Spi.QuoteLiteral(value)).ToString());")]
    [DataRow("Spi.Execute(new System.Text.StringBuilder(\"SELECT \").Append(value).Clear().Append(\"SELECT 1\").ToString());")]
    [DataRow("string sql = $\"SELECT {value}\"; sql = \"SELECT 1\"; Spi.Execute(sql);")]
    [DataRow("var sql = new System.Text.StringBuilder(\"SELECT \"); sql.Append(value); sql.Clear(); sql.Append(\"SELECT 1\"); Spi.Execute(sql.ToString());")]
    [DataRow("var sql = new System.Text.StringBuilder(\"SELECT \"); sql.Append(Spi.QuoteLiteral(value)); Spi.Execute(sql.ToString());")]
    [DataRow("var sql = new System.Text.StringBuilder(\"SELECT \"); sql.Append(value); var alias = sql; alias.Clear(); alias.Append(\"SELECT 1\"); Spi.Execute(sql.ToString());")]
    [DataRow("var sql = new System.Text.StringBuilder(); if (flag) { sql.Append(value); } sql.Clear(); sql.Append(\"SELECT 1\"); Spi.Execute(sql.ToString());")]
    [DataRow("var sql = new System.Text.StringBuilder(); while (flag) { sql.Append(value); flag = false; } sql.Clear(); sql.Append(\"SELECT 1\"); Spi.Execute(sql.ToString());")]
    [DataRow("var sql = new System.Text.StringBuilder(); try { sql.Append(value); } finally { sql.Clear(); sql.Append(\"SELECT 1\"); } Spi.Execute(sql.ToString());")]
    [DataRow("var sql = new System.Text.StringBuilder(); try { sql.Append(value); throw new System.Exception(); } catch { sql.Clear(); sql.Append(\"SELECT 1\"); Spi.Execute(sql.ToString()); }")]
    [DataRow("Spi.Execute(string.Format(\"SELECT {0} {1}\", new object[] { Spi.QuoteLiteral(value), \"AS label\" }));")]
    [DataRow("string sql = Spi.QuoteIdentifier(value); sql = Spi.QuoteLiteral(value); Spi.Execute($\"SELECT {sql}\");")]
    [DataRow("Spi.Execute(new System.Text.StringBuilder(\"garbageSELECT 1\", 7, 8, 16).ToString());")]
    [DataRow("Spi.Execute(new System.Text.StringBuilder(\"garbageSELECT 1\").ToString(7, 8));")]
    [DataRow("Spi.Execute(new System.Text.StringBuilder().Append(value).ToString(0, 0));")]
    [DataRow("var sql = new System.Text.StringBuilder().Append(value); sql.Length = 0; sql.Append(\"SELECT 1\"); Spi.Execute(sql.ToString());")]
    [DataRow("object[] inputs = { value }; inputs[0] = 42; Spi.Execute(string.Format(\"SELECT {0}\", inputs));")]
    [DataRow("string Fragment() => Spi.QuoteLiteral(value); Spi.Execute($\"SELECT {Fragment()}\");")]
    [DataRow("Spi.Execute(string.Format(\"SELECT 1\", value));")]
    [DataRow("Spi.Execute(string.Format(\"SELECT {0}, {1}\", Spi.QuoteLiteral(value), \"'$9999999999999999990000000000'\"));")]
    public async Task ProvenCommandConstructionRemainsValid(string body)
        => Assert.IsEmpty(await Analyze(body, FlowHelpers, "bool flag = number != 0;"));

    /// <summary>
    /// Supplies ordinary helpers outside the analyzed method without executing their consumer code.
    /// </summary>
    private const string FlowHelpers = """
        internal static class Other
        {
            internal static void Maybe(System.Action callback) { }
            internal static void Mutate(System.Text.StringBuilder builder) { }
        }
        """;
}
