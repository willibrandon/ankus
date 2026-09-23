namespace Ankus.Build.Tests;

/// <summary>
/// Verifies PostgreSQL build flags retain their argument boundaries without invoking a shell.
/// </summary>
[TestClass]
public sealed class UnixCompilerArgumentsTests
{
    /// <summary>
    /// Preserves dependency search paths, quoted macros and their original argument order.
    /// </summary>
    [TestMethod]
    public void SplitPreservesQuotedPathsAndDefinitions()
    {
        IReadOnlyList<string> result = UnixCompilerArguments.Split(
            " -I'/opt/dependency one/include'\t-isysroot \"/SDKs/Mac OS.sdk\" -DNAME='\"two words\"'\n-Iplain ");

        string[] expected =
        [
            "-I/opt/dependency one/include", "-isysroot", "/SDKs/Mac OS.sdk", "-DNAME=\"two words\"", "-Iplain",
        ];
        Assert.AreSequenceEqual(expected, result);
    }

    /// <summary>
    /// Distinguishes empty quoted words from separators and concatenates adjacent quoted segments.
    /// </summary>
    [TestMethod]
    public void SplitPreservesEmptyAndAdjacentQuotedWords()
    {
        IReadOnlyList<string> result = UnixCompilerArguments.Split("'' \"\" pre' two'\" three\"post a''b");

        string[] expected = ["", "", "pre two threepost", "ab"];
        Assert.AreSequenceEqual(expected, result);
    }

    /// <summary>
    /// Preserves escaped whitespace and removes shell line continuations without creating empty words.
    /// </summary>
    [TestMethod]
    public void SplitHonorsEscapesAndLineContinuations()
    {
        IReadOnlyList<string> result = UnixCompilerArguments.Split("\\\n -Ipath\\ with\\ spaces a\\\nb \\\\ \\' \\\"");

        string[] expected = ["-Ipath with spaces", "ab", "\\", "'", "\""];
        Assert.AreSequenceEqual(expected, result);
    }

    /// <summary>
    /// Applies the narrower backslash rules inside double quotes and literal rules inside single quotes.
    /// </summary>
    [TestMethod]
    public void SplitKeepsQuoteSpecificEscapeSemantics()
    {
        IReadOnlyList<string> result = UnixCompilerArguments.Split("\"a\\qb\\$c\\`d\\\"e\\\\f\" 'a\\ b\\$c'");

        string[] expected = ["a\\qb$c`d\"e\\f", "a\\ b\\$c"];
        Assert.AreSequenceEqual(expected, result);
    }

    /// <summary>
    /// Treats expansion syntax as ordinary data rather than executing commands or reading environment variables.
    /// </summary>
    [TestMethod]
    public void SplitDoesNotExpandShellSyntax()
    {
        IReadOnlyList<string> result = UnixCompilerArguments.Split("'$UNSET' '$(touch unexpected)' '`id`' '*.h' '~' ';' '#literal'");

        string[] expected = ["$UNSET", "$(touch unexpected)", "`id`", "*.h", "~", ";", "#literal"];
        Assert.AreSequenceEqual(expected, result);
    }

    /// <summary>
    /// Accepts an installation with no additional preprocessor arguments.
    /// </summary>
    /// <param name="flags">Empty input, separators, or a standalone line continuation.</param>
    [TestMethod]
    [DataRow("")]
    [DataRow(" \t\r\n ")]
    [DataRow("\\\n")]
    public void SplitAcceptsEmptyFlags(string flags)
    {
        Assert.IsEmpty(UnixCompilerArguments.Split(flags));
    }

    /// <summary>
    /// Rejects incomplete argument syntax instead of compiling with altered paths or definitions.
    /// </summary>
    /// <param name="flags">An incomplete escape or quoted argument.</param>
    [TestMethod]
    [DataRow("-Ipath\\")]
    [DataRow("'path")]
    [DataRow("\"path")]
    [DataRow("\"path\\")]
    public void SplitRejectsIncompleteSyntax(string flags)
    {
        Assert.ThrowsExactly<FormatException>(() => UnixCompilerArguments.Split(flags));
    }
}
