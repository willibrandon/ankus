using Microsoft.CodeAnalysis;

namespace Ankus.Generators.Tests;

public sealed partial class SpiInterpolationAnalyzerTests
{
    /// <summary>
    /// Analyzes commands in callbacks that framework code, external code or later enumeration can invoke.
    /// </summary>
    /// <param name="body">The valid consumer construction and command.</param>
    /// <param name="argument">The exact command argument receiving the diagnostic.</param>
    [TestMethod]
    [DataRow("string[] names = [value]; _ = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(names, n => Spi.ExecuteScalar<long>(\"SELECT count(*) FROM \" + n)));",
        "\"SELECT count(*) FROM \" + n", DisplayName = "select_concatenated_parameter")]
    [DataRow("string[] names = [value]; _ = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(names, n => { string sql = \"SELECT count(*) FROM \" + n; return Spi.ExecuteScalar<long>(sql); }));",
        "sql", DisplayName = "select_stored_command")]
    [DataRow("string[] names = [value]; _ = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(names, n => Spi.Execute(new System.Text.StringBuilder(\"SELECT \").Append(n).ToString())));",
        "new System.Text.StringBuilder(\"SELECT \").Append(n).ToString()", DisplayName = "select_builder_command")]
    [DataRow("string[] names = [value]; _ = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Where(names, n => Spi.Execute(\"SELECT \" + n) > 0));",
        "\"SELECT \" + n", DisplayName = "where_concatenated_parameter")]
    [DataRow("Held.Query = n => Spi.Execute(\"SELECT count(*) FROM \" + n);",
        "\"SELECT count(*) FROM \" + n", DisplayName = "escaped_lambda")]
    [DataRow("Held.Command = Count; long Count() { string sql = \"SELECT count(*) FROM \" + value; return Spi.Execute(sql); }",
        "sql", DisplayName = "escaped_local_function")]
    [DataRow("string table = Spi.QuoteIdentifier(value); Held.Command = () => Spi.Execute(\"SELECT * FROM \" + table); table = value;",
        "\"SELECT * FROM \" + table", DisplayName = "escaped_lambda_later_write")]
    [DataRow("string table = Spi.QuoteIdentifier(value); string[] names = [value]; System.Collections.Generic.IEnumerable<long> counts = System.Linq.Enumerable.Select(names, n => Spi.Execute(\"SELECT count(*) FROM \" + table)); table = value; _ = System.Linq.Enumerable.ToArray(counts);",
        "\"SELECT count(*) FROM \" + table", DisplayName = "deferred_select_later_write")]
    [DataRow("Held.Command = () => { System.Func<long> inner = () => Spi.Execute(\"SELECT \" + value); return inner(); };",
        "\"SELECT \" + value", DisplayName = "nested_escaped_lambda")]
    public async Task CallbackCommandsAreRejected(string body, string argument)
        => await AssertReachingRejected(body, argument);

    /// <summary>
    /// Preserves quoted and literal commands in callbacks that are invoked later or only by framework code.
    /// </summary>
    /// <param name="body">The safe consumer construction and command.</param>
    [TestMethod]
    [DataRow("string[] names = [value]; _ = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(names, n => Spi.ExecuteScalar<long>(\"SELECT count(*) FROM \" + Spi.QuoteIdentifier(n))));")]
    [DataRow("string[] names = [value]; _ = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(names, n => Spi.ExecuteScalar<long>(\"SELECT 1\")));")]
    [DataRow("Held.Query = n => Spi.Execute(\"SELECT count(*) FROM \" + Spi.QuoteIdentifier(n));")]
    [DataRow("string table = Spi.QuoteIdentifier(value); Held.Command = () => Spi.Execute(\"SELECT * FROM \" + table);")]
    [DataRow("string table = Spi.QuoteIdentifier(value); Held.Command = () => Spi.Execute(\"SELECT * FROM \" + table); table = Spi.QuoteIdentifier(\"other\");")]
    [DataRow("Held.Command = Count; long Count() => Spi.Execute(\"SELECT count(*) FROM \" + Spi.QuoteIdentifier(value));")]
    [DataRow("Held.Query = n => Spi.Execute(n);")]
    public async Task CallbackCommandsRemainValid(string body)
        => Assert.IsEmpty(await Analyze(body, ReachingHelpers, "bool flag = number != 0;"));

    /// <summary>
    /// Applies the syntactic check to concatenation in code that never runs or escapes from the analyzed method.
    /// </summary>
    /// <param name="body">The valid consumer construction and command.</param>
    /// <param name="argument">The exact command argument receiving the diagnostic.</param>
    [TestMethod]
    [DataRow("_ = nameof(Count); long Count(string n) => Spi.Execute(\"SELECT count(*) FROM \" + n);", "\"SELECT count(*) FROM \" + n", DisplayName = "unreferenced_concatenation")]
    [DataRow("_ = nameof(Count); long Count(string n) => Spi.Execute(\"SELECT \" + (flag ? n : \"1\") + \" FROM t\");", "\"SELECT \" + (flag ? n : \"1\") + \" FROM t\"", DisplayName = "unreferenced_conditional_concatenation")]
    [DataRow("_ = nameof(Count); long Count(string n) => Spi.Execute(\"SELECT \" + Spi.QuoteIdentifier(n) + \", \" + n);", "\"SELECT \" + Spi.QuoteIdentifier(n) + \", \" + n", DisplayName = "unreferenced_mixed_concatenation")]
    public async Task UnanalyzedConcatenationIsRejected(string body, string argument)
        => await AssertReachingRejected(body, argument);

    /// <summary>
    /// Keeps the syntactic check precise for literal and quoted concatenation in code that never runs.
    /// </summary>
    /// <param name="body">The safe consumer construction and command.</param>
    [TestMethod]
    [DataRow("_ = nameof(Count); long Count(string n) => Spi.Execute(\"SELECT count(*) FROM \" + Spi.QuoteIdentifier(n));")]
    [DataRow("_ = nameof(Count); long Count(string n) => Spi.Execute(\"SELECT \" + \"1\");")]
    [DataRow("_ = nameof(Count); long Count(string n) => Spi.Execute(n);")]
    [DataRow("_ = nameof(Count); long Count(string n) { string table = Spi.QuoteIdentifier(n); return Spi.Execute(\"SELECT * FROM \" + table); }")]
    public async Task UnanalyzedQuotedConcatenationRemainsValid(string body)
        => Assert.IsEmpty(await Analyze(body, ReachingHelpers, "bool flag = number != 0;"));

    /// <summary>
    /// Keeps the original external value of a parameter on every path that does not replace it.
    /// </summary>
    /// <param name="body">The valid consumer construction and command.</param>
    /// <param name="argument">The exact command argument receiving the diagnostic.</param>
    [TestMethod]
    [DataRow("if (number == 0) { value = \"users\"; } Spi.Execute(\"SELECT * FROM \" + value);", "\"SELECT * FROM \" + value", DisplayName = "parameter_one_branch")]
    [DataRow("if (number == 0) { value = \"users\"; } Spi.Execute($\"SELECT * FROM {value}\");", "$\"SELECT * FROM {value}\"", DisplayName = "parameter_one_branch_interpolation")]
    [DataRow("if (number != 0) { } else { value = Spi.QuoteIdentifier(\"users\"); } Spi.Execute(\"SELECT * FROM \" + value);", "\"SELECT * FROM \" + value", DisplayName = "parameter_else_branch")]
    [DataRow("while (number-- > 0) { value = \"users\"; } Spi.Execute(\"SELECT * FROM \" + value);", "\"SELECT * FROM \" + value", DisplayName = "parameter_loop")]
    [DataRow("try { number = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture); value = \"users\"; } catch (System.FormatException) { } Spi.Execute(\"SELECT * FROM \" + value);", "\"SELECT * FROM \" + value", DisplayName = "parameter_try")]
    [DataRow("string[] names = [value]; _ = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(names, n => { if (flag) { n = \"users\"; } return Spi.Execute(\"SELECT * FROM \" + n); }));", "\"SELECT * FROM \" + n", DisplayName = "lambda_parameter_one_branch")]
    [DataRow("void Run(string table) { if (flag) { table = \"users\"; } Spi.Execute(\"SELECT * FROM \" + table); } Run(value);", "\"SELECT * FROM \" + table", DisplayName = "local_parameter_one_branch")]
    public async Task UnassignedParameterPathsAreRejected(string body, string argument)
        => await AssertReachingRejected(body, argument);

    /// <summary>
    /// Accepts a parameter after every reaching path replaces it with literal or quoted SQL.
    /// </summary>
    /// <param name="body">The safe consumer construction and command.</param>
    [TestMethod]
    [DataRow("if (number == 0) { value = \"users\"; } else { value = \"orders\"; } Spi.Execute(\"SELECT * FROM \" + value);")]
    [DataRow("value = \"users\"; Spi.Execute(\"SELECT * FROM \" + value);")]
    [DataRow("value = number == 0 ? Spi.QuoteIdentifier(value) : \"orders\"; Spi.Execute(\"SELECT * FROM \" + value);")]
    [DataRow("if (number == 0) { value = \"users\"; } Spi.Execute(value);")]
    [DataRow("void Run(string table) { table = Spi.QuoteIdentifier(table); Spi.Execute(\"SELECT * FROM \" + table); } Run(value);")]
    public async Task ReplacedParameterPathsRemainValid(string body)
        => Assert.IsEmpty(await Analyze(body, ReachingHelpers, "bool flag = number != 0;"));

    /// <summary>
    /// Formats builders, tuples and framework concatenations from their actual reaching text.
    /// </summary>
    /// <param name="body">The valid consumer construction and command.</param>
    /// <param name="argument">The exact command argument receiving the diagnostic.</param>
    [TestMethod]
    [DataRow("var name = new System.Text.StringBuilder(); name.Append(value); Spi.Execute($\"SELECT * FROM {name}\");", "$\"SELECT * FROM {name}\"", DisplayName = "builder_interpolated")]
    [DataRow("var name = new System.Text.StringBuilder(value); Spi.Execute(\"SELECT * FROM \" + name);", "\"SELECT * FROM \" + name", DisplayName = "builder_concatenated")]
    [DataRow("var name = new System.Text.StringBuilder(value); string sql = \"SELECT * FROM \"; sql += name; Spi.Execute(sql);", "sql", DisplayName = "builder_compound")]
    [DataRow("var name = new System.Text.StringBuilder(value); var sql = new System.Text.StringBuilder(\"SELECT * FROM \"); sql.Append(name); Spi.Execute(sql.ToString());", "sql.ToString()", DisplayName = "builder_appended_builder")]
    [DataRow("var name = new System.Text.StringBuilder(value); Spi.Execute(string.Format(\"SELECT * FROM {0}\", name));", "string.Format(\"SELECT * FROM {0}\", name)", DisplayName = "builder_formatted")]
    [DataRow("var name = new System.Text.StringBuilder(\"users\"); name.Append(value); object boxed = name; Spi.Execute(\"SELECT * FROM \" + boxed);", "\"SELECT * FROM \" + boxed", DisplayName = "builder_boxed")]
    [DataRow("var sql = new System.Text.StringBuilder(\"SELECT * FROM \"); sql.Append(value, 0, value.Length); Spi.Execute(sql.ToString());", "sql.ToString()", DisplayName = "builder_substring_append")]
    [DataRow("var sql = new System.Text.StringBuilder(\"SELECT * FROM \"); sql.Insert(sql.Length, value); Spi.Execute(sql.ToString());", "sql.ToString()", DisplayName = "builder_insert")]
    [DataRow("var sql = new System.Text.StringBuilder(\"SELECT * FROM users WHERE name = 'x'\"); sql.Replace(\"x\", value); Spi.Execute(sql.ToString());", "sql.ToString()", DisplayName = "builder_replace")]
    [DataRow("(string Name, int Number) pair = (value, 1); Spi.Execute($\"SELECT {pair}\");", "$\"SELECT {pair}\"", DisplayName = "tuple_interpolated")]
    [DataRow("Spi.Execute(string.Concat(\"SELECT * FROM \", value));", "string.Concat(\"SELECT * FROM \", value)", DisplayName = "string_concat")]
    [DataRow("Spi.Execute(string.Concat(\"SELECT \", \"* FROM \", value, \";\"));", "string.Concat(\"SELECT \", \"* FROM \", value, \";\")", DisplayName = "string_concat_four")]
    [DataRow("Spi.Execute(\"SELECT * FROM users WHERE name = '{0}'\".Replace(\"{0}\", value));", "\"SELECT * FROM users WHERE name = '{0}'\".Replace(\"{0}\", value)", DisplayName = "string_replace_template")]
    [DataRow("Spi.Execute(\"SELECT * FROM \".Insert(14, value));", "\"SELECT * FROM \".Insert(14, value)", DisplayName = "string_insert")]
    [DataRow("Spi.Execute(\"SELECT * FROM \" + string.Concat(\"'\", Spi.QuoteIdentifier(value), \"'\"));", "\"SELECT * FROM \" + string.Concat(\"'\", Spi.QuoteIdentifier(value), \"'\")", DisplayName = "string_concat_quoted_context")]
    [DataRow("Spi.Execute(string.Join(\" \", \"SELECT\", value, \"FROM\", \"users\"));", "string.Join(\" \", \"SELECT\", value, \"FROM\", \"users\")", DisplayName = "string_join_params")]
    [DataRow("Spi.Execute((\"SELECT * FROM \" + value).Trim());", "(\"SELECT * FROM \" + value).Trim()", DisplayName = "transformed_unsafe_text")]
    public async Task FormattedObjectsAreRejected(string body, string argument)
        => await AssertReachingRejected(body, argument);

    /// <summary>
    /// Accepts builders and framework concatenations whose reaching text contains only literals and complete quoted fragments.
    /// </summary>
    /// <param name="body">The safe consumer construction and command.</param>
    [TestMethod]
    [DataRow("var name = new System.Text.StringBuilder(Spi.QuoteIdentifier(value)); Spi.Execute(\"SELECT * FROM \" + name);")]
    [DataRow("var name = new System.Text.StringBuilder(); name.Append(Spi.QuoteIdentifier(value)); Spi.Execute($\"SELECT * FROM {name}\");")]
    [DataRow("var sql = new System.Text.StringBuilder(\"SELECT 1\"); Spi.Execute($\"{sql}\");")]
    [DataRow("var name = new System.Text.StringBuilder(Spi.QuoteIdentifier(value)); var sql = new System.Text.StringBuilder(\"SELECT * FROM \"); sql.Append(name); Spi.Execute(sql.ToString());")]
    [DataRow("Spi.Execute(string.Concat(\"SELECT * FROM \", Spi.QuoteIdentifier(value)));")]
    [DataRow("Spi.Execute(string.Concat(\"SELECT \", Spi.QuoteLiteral(value), \" AS \", Spi.QuoteIdentifier(value)));")]
    [DataRow("Spi.Execute(\"SELECT 1 \".Trim());")]
    [DataRow("Spi.Execute(value.Trim());")]
    public async Task FormattedQuotedObjectsRemainValid(string body)
        => Assert.IsEmpty(await Analyze(body, ReachingHelpers, "bool flag = number != 0;"));

    /// <summary>
    /// Rejects composite formatting that places mixed quoted and literal text inside another SQL token.
    /// </summary>
    /// <param name="body">The valid consumer construction and command.</param>
    /// <param name="argument">The exact command argument receiving the diagnostic.</param>
    [TestMethod]
    [DataRow("Spi.Execute(string.Format(\"SELECT {0}\", \"E\" + Spi.QuoteLiteral(value)));", "string.Format(\"SELECT {0}\", \"E\" + Spi.QuoteLiteral(value))", DisplayName = "format_escape_prefix")]
    [DataRow("Spi.Execute(string.Format(\"SELECT {0}'\", \"'\" + Spi.QuoteLiteral(value)));", "string.Format(\"SELECT {0}'\", \"'\" + Spi.QuoteLiteral(value))", DisplayName = "format_quoted_context")]
    [DataRow("Spi.Execute(string.Format(\"SELECT {0}, {1}, {2}, {3}\", value, 1, 2, 3));", "string.Format(\"SELECT {0}, {1}, {2}, {3}\", value, 1, 2, 3)", DisplayName = "format_params_span_raw")]
    [DataRow("Spi.Execute(string.Format(\"SELECT {0}, {1}, {2}, '{3}'\", 1, 2, 3, Spi.QuoteLiteral(value)));", "string.Format(\"SELECT {0}, {1}, {2}, '{3}'\", 1, 2, 3, Spi.QuoteLiteral(value))", DisplayName = "format_params_span_quoted_context")]
    [DataRow("Spi.Execute(new System.Text.StringBuilder().AppendFormat(\"SELECT {0}, {1}, {2}, {3}\", value, 1, 2, 3).ToString());", "new System.Text.StringBuilder().AppendFormat(\"SELECT {0}, {1}, {2}, {3}\", value, 1, 2, 3).ToString()", DisplayName = "append_format_params_span_raw")]
    [DataRow("Spi.Execute(string.Format(\"SELECT {0}\", (value, 1)));", "string.Format(\"SELECT {0}\", (value, 1))", DisplayName = "format_tuple")]
    [DataRow("Spi.Execute(string.Format(\"SELECT {0}\", Spi.QuoteLiteral(value) + value));", "string.Format(\"SELECT {0}\", Spi.QuoteLiteral(value) + value)", DisplayName = "format_raw_suffix")]
    [DataRow("Spi.Execute(string.Format(\"SELECT {0} FROM {1}\", Spi.QuoteIdentifier(value), value));", "string.Format(\"SELECT {0} FROM {1}\", Spi.QuoteIdentifier(value), value)", DisplayName = "format_raw_argument")]
    [DataRow("Spi.Execute(string.Format(\"SELECT {0}\", \"-- \" + Spi.QuoteLiteral(value) + \"\\n\" + Spi.QuoteLiteral(value)));", "string.Format(\"SELECT {0}\", \"-- \" + Spi.QuoteLiteral(value) + \"\\n\" + Spi.QuoteLiteral(value))", DisplayName = "format_comment_fragment")]
    [DataRow("string inner = string.Format(\"{0} AS label\", Spi.QuoteLiteral(value)); Spi.Execute(string.Format(\"SELECT '{0}'\", inner));", "string.Format(\"SELECT '{0}'\", inner)", DisplayName = "nested_format_quoted_context")]
    [DataRow("Spi.Execute(new System.Text.StringBuilder().AppendFormat(\"SELECT {0}\", \"E\" + Spi.QuoteLiteral(value)).ToString());", "new System.Text.StringBuilder().AppendFormat(\"SELECT {0}\", \"E\" + Spi.QuoteLiteral(value)).ToString()", DisplayName = "append_format_escape_prefix")]
    public async Task MixedFormatFragmentsAreRejected(string body, string argument)
        => await AssertReachingRejected(body, argument);

    /// <summary>
    /// Accepts composite formatting whose arguments combine literal text with complete quoted fragments outside other tokens.
    /// </summary>
    /// <param name="body">The safe consumer construction and command.</param>
    [TestMethod]
    [DataRow("Spi.Execute(string.Format(\"SELECT {0}\", Spi.QuoteLiteral(value) + \" AS label\"));")]
    [DataRow("Spi.Execute(string.Format(\"SELECT {0} FROM {1}\", Spi.QuoteIdentifier(value), \"pg_catalog.\" + Spi.QuoteIdentifier(value)));")]
    [DataRow("Spi.Execute(string.Format(\"SELECT {0}, {0}\", Spi.QuoteLiteral(value) + \" || \" + Spi.QuoteLiteral(value)));")]
    [DataRow("string inner = string.Format(\"{0} AS label\", Spi.QuoteLiteral(value)); Spi.Execute(string.Format(\"SELECT {0}\", inner));")]
    [DataRow("Spi.Execute(new System.Text.StringBuilder().AppendFormat(\"SELECT {0}\", Spi.QuoteLiteral(value) + \" AS label\").ToString());")]
    [DataRow("Spi.Execute(string.Format(\"SELECT {0}, {1}, {2}, {3}\", 1, 2, 3, Spi.QuoteLiteral(value)));")]
    [DataRow("Spi.Execute(new System.Text.StringBuilder().AppendFormat(\"SELECT {0}, {1}, {2}, {3}\", 1, 2, 3, Spi.QuoteLiteral(value)).ToString());")]
    [DataRow("Spi.Execute(string.Join(\", \", \"SELECT 1\", Spi.QuoteLiteral(value), \"3\"));")]
    public async Task MixedFormatFragmentsRemainValid(string body)
        => Assert.IsEmpty(await Analyze(body, ReachingHelpers, "bool flag = number != 0;"));

    /// <summary>
    /// Follows writes through reference aliases, compound and coalescing assignment, and deconstruction.
    /// </summary>
    /// <param name="body">The valid consumer construction and command.</param>
    /// <param name="argument">The exact command argument receiving the diagnostic.</param>
    [TestMethod]
    [DataRow("string sql = Spi.QuoteLiteral(value); string other = Spi.QuoteLiteral(value); ref string alias = ref (flag ? ref sql : ref other); alias = value; Spi.Execute($\"SELECT {sql}\");", "$\"SELECT {sql}\"", DisplayName = "conditional_ref_alias")]
    [DataRow("string sql = Spi.QuoteLiteral(value); ref string alias = ref sql; alias += value; Spi.Execute($\"SELECT {sql}\");", "$\"SELECT {sql}\"", DisplayName = "ref_alias_compound")]
    [DataRow("string? sql = flag ? null : Spi.QuoteLiteral(value); ref string? alias = ref sql; alias ??= value; Spi.Execute($\"SELECT {sql}\");", "$\"SELECT {sql}\"", DisplayName = "ref_alias_coalesce")]
    [DataRow("string sql = Spi.QuoteLiteral(value); ref string alias = ref sql; (alias, number) = (value, 1); Spi.Execute($\"SELECT {sql}\");", "$\"SELECT {sql}\"", DisplayName = "ref_alias_deconstruction")]
    [DataRow("string sql = Spi.QuoteLiteral(value); ref string first = ref sql; ref string second = ref first; second = value; Spi.Execute($\"SELECT {sql}\");", "$\"SELECT {sql}\"", DisplayName = "ref_alias_chain")]
    [DataRow("string sql = Spi.QuoteLiteral(value); string other = \"SELECT 1\"; ref string alias = ref other; if (flag) { alias = ref sql; } alias = value; Spi.Execute($\"SELECT {sql}\");", "$\"SELECT {sql}\"", DisplayName = "ref_alias_rebound")]
    [DataRow("string sql = $\"SELECT {value}\"; string other = \"SELECT 1\"; ref string alias = ref other; if (flag) { alias = ref sql; } alias = \"SELECT 2\"; Spi.Execute(sql);", "sql", DisplayName = "ref_alias_rebound_weak")]
    [DataRow("string sql = Spi.QuoteLiteral(value); ref string alias = ref Held.Same(ref sql); sql = Spi.QuoteLiteral(value); alias = value; Spi.Execute($\"SELECT {sql}\");", "$\"SELECT {sql}\"", DisplayName = "returned_ref_write")]
    [DataRow("string sql = Spi.QuoteLiteral(value); ref string alias = ref Held.Same(ref sql); sql = Spi.QuoteLiteral(value); alias += value; Spi.Execute($\"SELECT {sql}\");", "$\"SELECT {sql}\"", DisplayName = "returned_ref_compound")]
    [DataRow("string sql = Spi.QuoteLiteral(value); System.Span<string> view = new(ref sql); sql = Spi.QuoteLiteral(value); view[0] = value; Spi.Execute($\"SELECT {sql}\");", "$\"SELECT {sql}\"", DisplayName = "span_ref_write")]
    [DataRow("string sql = Spi.QuoteLiteral(value); Held.Same(ref sql) = value; Spi.Execute($\"SELECT {sql}\");", "$\"SELECT {sql}\"", DisplayName = "returned_ref_target")]
    [DataRow("string sql = Spi.QuoteLiteral(value); (sql, number) = Held.Pair(value); Spi.Execute($\"SELECT {sql}\");", "$\"SELECT {sql}\"", DisplayName = "opaque_deconstruction")]
    [DataRow("string sql = Spi.QuoteLiteral(value); (sql, number) = new Held.Box(value); Spi.Execute($\"SELECT {sql}\");", "$\"SELECT {sql}\"", DisplayName = "deconstruct_method")]
    [DataRow("var (sql, count) = (value, 1); Spi.Execute($\"SELECT {sql} LIMIT {count}\");", "$\"SELECT {sql} LIMIT {count}\"", DisplayName = "declared_deconstruction")]
    [DataRow("string sql = Spi.QuoteLiteral(value); ((sql, _), number) = ((value, 2), 1); Spi.Execute($\"SELECT {sql}\");", "$\"SELECT {sql}\"", DisplayName = "nested_deconstruction")]
    public async Task ReferenceWritesAreRejected(string body, string argument)
        => await AssertReachingRejected(body, argument);

    /// <summary>
    /// Accepts reference and deconstruction writes whose every possible target keeps literal or quoted SQL.
    /// </summary>
    /// <param name="body">The safe consumer construction and command.</param>
    [TestMethod]
    [DataRow("string sql = Spi.QuoteLiteral(value); ref string alias = ref sql; alias = Spi.QuoteLiteral(\"fixed\"); Spi.Execute($\"SELECT {sql}\");")]
    [DataRow("string sql = Spi.QuoteLiteral(value); string other = Spi.QuoteLiteral(value); ref string alias = ref (flag ? ref sql : ref other); alias = Spi.QuoteIdentifier(value); Spi.Execute($\"SELECT {sql}, {other}\");")]
    [DataRow("var (sql, count) = (Spi.QuoteLiteral(value), 1); Spi.Execute($\"SELECT {sql} LIMIT {count}\");")]
    [DataRow("string sql = \"SELECT 1\"; (sql, number) = (Spi.QuoteLiteral(value), 1); Spi.Execute($\"SELECT {sql}\");")]
    [DataRow("string sql = $\"SELECT {value}\"; ref string alias = ref sql; alias = \"SELECT 1\"; Spi.Execute(sql);")]
    public async Task ReferenceWritesRemainValid(string body)
        => Assert.IsEmpty(await Analyze(body, ReachingHelpers, "bool flag = number != 0;"));

    /// <summary>
    /// Counts composite-format layout enumeration against the shared analysis budget and fails closed.
    /// </summary>
    [TestMethod]
    public async Task FormatLayoutEnumerationRespectsTheAnalysisBudget()
    {
        const int Choices = 24;
        string template = "SELECT {0:Q}" + string.Concat(Enumerable.Range(1, Choices).Select(static index =>
            FormattableString.Invariant($", {{{index}}}")));
        string arguments = string.Concat(Enumerable.Range(1, Choices).Select(static index =>
            FormattableString.Invariant($", flag ? \"a{index}\" : \"b{index}\"")));
        string command = "string.Format(\"" + template + "\", 1" + arguments + ")";
        await AssertReachingRejected("Spi.Execute(" + command + ");", command);
    }

    /// <summary>
    /// Keeps bounded composite formatting with many literal alternatives valid within the analysis budget.
    /// </summary>
    [TestMethod]
    public async Task FormatLayoutEnumerationAcceptsBoundedLiteralChoices()
    {
        const int Choices = 24;
        string template = "SELECT {0}" + string.Concat(Enumerable.Range(1, Choices).Select(static index =>
            FormattableString.Invariant($", {{{index}}}")));
        string arguments = string.Concat(Enumerable.Range(1, Choices).Select(static index =>
            FormattableString.Invariant($", flag ? \"a{index}\" : \"b{index}\"")));
        Assert.IsEmpty(await Analyze("Spi.Execute(string.Format(\"" + template + "\", 1" + arguments + "));", ReachingHelpers,
            "bool flag = number != 0;"));
    }

    /// <summary>
    /// Requires the exact command diagnostic for a reaching construction in the shared consumer method.
    /// </summary>
    /// <param name="body">The valid consumer construction and command.</param>
    /// <param name="argument">The exact command argument receiving the diagnostic.</param>
    private async Task AssertReachingRejected(string body, string argument)
    {
        Diagnostic error = Assert.ContainsSingle(await Analyze(body, ReachingHelpers, "bool flag = number != 0;"));
        Assert.AreEqual("ANKUS044", error.Id);
        Assert.AreEqual(DiagnosticSeverity.Error, error.Severity);
        Assert.AreEqual(argument, error.Location.SourceTree!.GetText(context.CancellationToken).ToString(error.Location.SourceSpan));
    }

    /// <summary>
    /// Supplies callback storage, reference-returning and deconstruction helpers outside the analyzed method.
    /// </summary>
    private const string ReachingHelpers = """
        internal static class Held
        {
            internal static System.Func<string, long>? Query { get; set; }
            internal static System.Func<long>? Command { get; set; }
            internal static (string Text, int Number) Pair(string value) => (value, 0);
            internal static ref string Same(ref string value) => ref value;
            internal sealed class Box(string text)
            {
                internal void Deconstruct(out string value, out int number)
                {
                    value = text;
                    number = 0;
                }
            }
        }
        """;
}
