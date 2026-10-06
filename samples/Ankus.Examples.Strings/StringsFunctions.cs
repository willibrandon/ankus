using System.Text;

namespace Ankus.Examples.Strings;

/// <summary>
/// Ports pgrx's strings example with full Unicode casing, UTF-8 byte slicing and terminator-aware sets.
/// </summary>
public static class StringsFunctions
{
    /// <summary>
    /// Rejects invalid scalar sequences instead of inserting UTF-8 replacement characters.
    /// </summary>
    private static readonly UTF8Encoding s_utf8 = new(false, true);

    /// <summary>
    /// Returns the exact static greeting from the upstream strings sample.
    /// </summary>
    /// <returns>The present static text.</returns>
    [PgFunction]
    public static string ReturnStatic() => "This is a static string xxx";

    /// <summary>
    /// Applies locale-independent full Unicode lowercase, including dotted-I expansion and final sigma.
    /// </summary>
    /// <param name="input">The complete text.</param>
    /// <returns>The exact lowercase scalar sequence.</returns>
    [PgFunction]
    public static string ToLowercase(string input) => UnicodeLowercase.Convert(input);

    /// <summary>
    /// Returns an owned substring selected by start-inclusive and end-exclusive UTF-8 byte offsets.
    /// </summary>
    /// <param name="input">The lifetime-checked PostgreSQL text.</param>
    /// <param name="start">The starting byte offset at a Unicode scalar boundary.</param>
    /// <param name="end">The exclusive ending byte offset at a Unicode scalar boundary.</param>
    /// <returns>The selected text, independent of the borrowed source after decoding.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The offsets are negative, reversed or outside the text.</exception>
    /// <exception cref="ArgumentException">An offset splits a Unicode scalar.</exception>
    [PgFunction]
    public static string Substring(PgTextView input, int start, int end)
    {
        ArgumentNullException.ThrowIfNull(input);
        return SliceUtf8(input.DangerousGetUtf8Span(), start, end);
    }

    /// <summary>
    /// Appends the extra text and the upstream example's final literal x to an independent result.
    /// </summary>
    /// <param name="input">The initial text.</param>
    /// <param name="extra">The appended text.</param>
    /// <returns>The complete concatenation ending in x.</returns>
    [PgFunction]
    public static string Append(string input, string extra)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(extra);
        s_utf8.GetByteCount(input);
        s_utf8.GetByteCount(extra);
        return string.Concat(input, extra, "x");
    }

    /// <summary>
    /// Collects terminator-separated pieces, preserving leading and interior empty pieces.
    /// </summary>
    /// <param name="input">The complete text.</param>
    /// <param name="pattern">The exact ordinal separator, or empty for Unicode scalar splitting.</param>
    /// <returns>All pieces except the final terminator's empty suffix.</returns>
    [PgFunction]
    public static string[] Split(string input, string pattern) => [.. SplitTerminated(input, pattern)];

    /// <summary>
    /// Streams the same terminator-separated pieces as a PostgreSQL set.
    /// </summary>
    /// <param name="input">The complete owned managed text.</param>
    /// <param name="pattern">The exact ordinal separator.</param>
    /// <returns>The pieces in source order, without borrowing a span across row callbacks.</returns>
    [PgFunction]
    public static IEnumerable<string> SplitSet(string input, string pattern) => SplitTerminated(input, pattern);

    /// <summary>
    /// Streams zero-based ordinal and text columns named i and s, matching the upstream table function.
    /// </summary>
    /// <param name="input">The complete owned managed text.</param>
    /// <param name="pattern">The exact ordinal separator.</param>
    /// <returns>The exact ordered pieces paired with their ordinal.</returns>
    [PgFunction]
    public static IEnumerable<(int i, string s)> SplitTable(string input, string pattern)
        => SplitTerminated(input, pattern).Select(static (piece, index) => (index, piece));

    /// <summary>
    /// Decodes one bounded slice of already validated UTF-8 without retaining the native source.
    /// </summary>
    /// <param name="input">The validated text bytes, consumed without intervening backend calls.</param>
    /// <param name="start">The inclusive UTF-8 byte offset.</param>
    /// <param name="end">The exclusive UTF-8 byte offset.</param>
    /// <returns>The independent decoded text.</returns>
    internal static string SliceUtf8(ReadOnlySpan<byte> input, int start, int end)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(start, input.Length);
        ArgumentOutOfRangeException.ThrowIfLessThan(end, start);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(end, input.Length);
        if (!IsBoundary(input, start))
        {
            throw new ArgumentException("The start offset must be a UTF-8 scalar boundary.", nameof(start));
        }

        if (!IsBoundary(input, end))
        {
            throw new ArgumentException("The end offset must be a UTF-8 scalar boundary.", nameof(end));
        }

        return s_utf8.GetString(input[start..end]);
    }

    /// <summary>
    /// Implements Rust's split_terminator semantics without culture-dependent matching or UTF-16 scalar splitting.
    /// </summary>
    /// <param name="input">The complete validated scalar string.</param>
    /// <param name="pattern">The ordinal scalar-string separator.</param>
    /// <returns>The ordered pieces with only the final terminator suffix removed.</returns>
    private static IEnumerable<string> SplitTerminated(string input, string pattern)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(pattern);
        s_utf8.GetByteCount(input);
        s_utf8.GetByteCount(pattern);
        if (pattern.Length == 0)
        {
            yield return string.Empty;
            foreach (Rune scalar in input.EnumerateRunes())
            {
                yield return scalar.ToString();
            }

            yield break;
        }

        if (input.Length == 0)
        {
            yield break;
        }

        int start = 0;
        while (true)
        {
            int separator = input.IndexOf(pattern, start, StringComparison.Ordinal);
            if (separator < 0)
            {
                yield return input[start..];
                yield break;
            }

            yield return input[start..separator];
            start = separator + pattern.Length;
            if (start == input.Length)
            {
                yield break;
            }
        }
    }

    /// <summary>
    /// Checks an inclusive UTF-8 byte position, including both ends of empty slices.
    /// </summary>
    /// <param name="input">The validated text bytes.</param>
    /// <param name="index">An offset already checked against the buffer bounds.</param>
    /// <returns>Whether the position is outside a continuation-byte sequence.</returns>
    private static bool IsBoundary(ReadOnlySpan<byte> input, int index)
        => index == input.Length || (input[index] & 0xc0) != 0x80;
}
