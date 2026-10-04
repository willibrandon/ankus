namespace Ankus.Runtime.Tests.CompilerServices;

/// <summary>
/// Verifies positional-token ownership and SQL quoting boundaries before backend execution.
/// </summary>
[TestClass]
public sealed class SpiSqlInterpolationTests
{
    /// <summary>
    /// A following digit or identifier cannot extend the interpolation's ordinal token.
    /// </summary>
    /// <param name="suffix">The compiler-provided SQL suffix.</param>
    [TestMethod]
    [DataRow("0")]
    [DataRow("9")]
    [DataRow("name")]
    [DataRow("::int")]
    [DataRow("+2")]
    public void BoundOrdinalHasIndependentTokenBoundaries(string suffix)
    {
        var handler = new SpiSqlInterpolatedStringHandler(7 + suffix.Length, 1);
        handler.AppendLiteral("SELECT ");
        handler.AppendFormatted(42);
        handler.AppendLiteral(suffix);
        SpiCommand command = Spi.Sql(handler);
        Assert.AreEqual("SELECT ($1)" + suffix, command.CommandText);
        Assert.HasCount(1, command.Parameters);
        Assert.AreEqual(23U, command.Parameters[0].TypeOid);
        Assert.AreEqual(42, Assert.IsInstanceOfType<int>(command.Parameters[0].Value));
    }

    /// <summary>
    /// A literal positional token cannot reuse or extend the interpolation's binding vector.
    /// </summary>
    /// <param name="prefix">The SQL prefix containing an unowned positional token.</param>
    [TestMethod]
    [DataRow("SELECT $1 + ")]
    [DataRow("SELECT $0 + ")]
    [DataRow("SELECT $01 + ")]
    [DataRow("SELECT $10 + ")]
    [DataRow("SELECT $999999999999999999999999 + ")]
    public void LiteralPositionalTokensAreRejected(string prefix)
    {
        ArgumentException error = Assert.ThrowsExactly<ArgumentException>(() => Build(prefix, string.Empty));
        Assert.Contains("owns its positional parameters", error.Message);
    }

    /// <summary>
    /// Literal dollar text in PostgreSQL token contexts remains unchanged while the independent value is bound.
    /// </summary>
    /// <param name="prefix">SQL containing literal dollar text before a real interpolation.</param>
    [TestMethod]
    [DataRow("SELECT '$1', ")]
    [DataRow("SELECT 'a''$1', ")]
    [DataRow("SELECT \"name$1\", ")]
    [DataRow("SELECT name$1, ")]
    [DataRow("SELECT 雪$1, ")]
    [DataRow("SELECT $$ $1 $$, ")]
    [DataRow("SELECT $tag$ $1 $other$ $tag$, ")]
    [DataRow("SELECT $雪$ $1 $雪$, ")]
    [DataRow("SELECT E'\\\\$1', ")]
    [DataRow("SELECT U&'$1', ")]
    [DataRow("SELECT U&\"name$1\", ")]
    [DataRow("SELECT B'1', ")]
    [DataRow("SELECT X'AB', ")]
    [DataRow("SELECT N'$1', ")]
    [DataRow("SELECT 'a'\n'$1', ")]
    [DataRow("SELECT E'a' -- continued\n'\\\\$1', ")]
    [DataRow("SELECT /* $1 /* $2 */ $3 */ ")]
    [DataRow("-- $1\nSELECT ")]
    [DataRow("-- $1\rSELECT ")]
    [DataRow("-- $1\r\nSELECT ")]
    public void LiteralDollarContextsPreserveTextAndBindings(string prefix)
    {
        SpiCommand command = Build(prefix, string.Empty);
        Assert.AreEqual(prefix + "($1)", command.CommandText);
        Assert.HasCount(1, command.Parameters);
        Assert.AreEqual(42, Assert.IsInstanceOfType<int>(command.Parameters[0].Value));
    }

    /// <summary>
    /// An interpolation hidden in SQL text is rejected instead of silently becoming an unused binding.
    /// </summary>
    /// <param name="prefix">The opening SQL token context.</param>
    /// <param name="suffix">The closing SQL token context.</param>
    [TestMethod]
    [DataRow("SELECT '", "'")]
    [DataRow("SELECT 'a''", "'")]
    [DataRow("SELECT E'", "'")]
    [DataRow("SELECT E'\\'", "'")]
    [DataRow("SELECT \"", "\"")]
    [DataRow("SELECT $$", "$$")]
    [DataRow("SELECT $tag$", "$tag$")]
    [DataRow("SELECT $雪$", "$雪$")]
    [DataRow("SELECT -- ", "\n")]
    [DataRow("SELECT /* ", " */")]
    [DataRow("SELECT /* outer /* ", " */ */")]
    [DataRow("SELECT 'a'\n'", "'")]
    public void QuotedAndCommentedInterpolationsAreRejected(string prefix, string suffix)
    {
        ArgumentException error = Assert.ThrowsExactly<ArgumentException>(() => Build(prefix, suffix));
        Assert.Contains("outside strings, quoted identifiers and comments", error.Message);
    }

    /// <summary>
    /// A token-boundary interpretation that depends on ordinary-string escape mode fails before native execution.
    /// </summary>
    [TestMethod]
    public void AmbiguousOrdinaryEscapeBoundaryIsRejected()
    {
        ArgumentException error = Assert.ThrowsExactly<ArgumentException>(() => Build("SELECT '\\' || ", string.Empty));
        Assert.Contains("outside strings, quoted identifiers and comments", error.Message);
    }

    /// <summary>
    /// Literal and generated positional tokens remain independent after an earlier interpolation.
    /// </summary>
    [TestMethod]
    public void LiteralTokenAfterInterpolationIsRejected()
    {
        ArgumentException error = Assert.ThrowsExactly<ArgumentException>(() => Build("SELECT ", " + $1"));
        Assert.Contains("owns its positional parameters", error.Message);
    }

    /// <summary>
    /// A command with no bindings cannot accidentally reference an undeclared binding vector.
    /// </summary>
    [TestMethod]
    public void LiteralPositionalTokenWithoutBindingsIsRejected()
        => Assert.ThrowsExactly<ArgumentException>(() => SpiSqlInterpolation.Validate("SELECT $1", []));

    /// <summary>
    /// Malformed SQL without misplaced bindings remains PostgreSQL's parsing responsibility.
    /// </summary>
    /// <param name="text">The incomplete or non-delimiter literal text.</param>
    [TestMethod]
    [DataRow("SELECT '")]
    [DataRow("SELECT /*")]
    [DataRow("SELECT $tag$")]
    [DataRow("SELECT $tag")]
    [DataRow("SELECT $")]
    public void NonBindingSqlSyntaxIsLeftForPostgreSql(string text)
    {
        SpiSqlInterpolation.Validate(text, []);
        var handler = new SpiSqlInterpolatedStringHandler(text.Length, 0);
        handler.AppendLiteral(text);
        Assert.AreEqual(text, Spi.Sql(handler).CommandText);
    }

    /// <summary>
    /// Builds one ordinary value interpolation without capturing a ref struct in an assertion delegate.
    /// </summary>
    /// <param name="prefix">The literal SQL prefix.</param>
    /// <param name="suffix">The literal SQL suffix.</param>
    /// <returns>The independent command.</returns>
    private static SpiCommand Build(string prefix, string suffix)
    {
        var handler = new SpiSqlInterpolatedStringHandler(prefix.Length + suffix.Length, 1);
        handler.AppendLiteral(prefix);
        handler.AppendFormatted(42);
        handler.AppendLiteral(suffix);
        return Spi.Sql(handler);
    }
}
