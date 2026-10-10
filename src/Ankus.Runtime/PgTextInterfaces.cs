using System.Diagnostics.CodeAnalysis;

namespace Ankus;

/// <summary>
/// Formats PostgreSQL's canonical text for the value types that implement <see cref="ISpanFormattable"/>.
/// </summary>
internal static class PgTextFormatting
{
    /// <summary>
    /// Returns the canonical text, the only supported format.
    /// </summary>
    internal static string Format(string text, string? format)
        => string.IsNullOrEmpty(format) ? text : throw Unsupported(format);

    /// <summary>
    /// Copies the canonical text into a destination span.
    /// </summary>
    internal static bool TryFormat(string text, Span<char> destination, out int charsWritten, ReadOnlySpan<char> format)
    {
        if (!format.IsEmpty)
        {
            throw Unsupported(format.ToString());
        }

        if (text.TryCopyTo(destination))
        {
            charsWritten = text.Length;
            return true;
        }

        charsWritten = 0;
        return false;
    }

    private static FormatException Unsupported(string format)
        => new($"The format '{format}' is not supported: PostgreSQL text has one canonical form; use a null or empty format.");
}

/// <summary>
/// Implements .NET's parsing and span formatting contracts over PostgreSQL's text form.
/// </summary>
public readonly partial record struct PgNumeric : ISpanFormattable, IParsable<PgNumeric>
{
    /// <summary>
    /// Parses PostgreSQL text with the type's input function on the active backend thread, as
    /// <see cref="Parse(string)"/> does.
    /// </summary>
    /// <param name="s">The PostgreSQL text.</param>
    /// <param name="provider">Ignored: PostgreSQL's input function and session settings decide the syntax.</param>
    /// <returns>The parsed value.</returns>
    static PgNumeric IParsable<PgNumeric>.Parse(string s, IFormatProvider? provider) => Parse(s);

    /// <summary>
    /// Tries to parse PostgreSQL text on the active backend thread, as <see cref="TryParse(string, out PgNumeric)"/> does.
    /// </summary>
    /// <param name="s">The candidate text.</param>
    /// <param name="provider">Ignored: PostgreSQL's input function and session settings decide the syntax.</param>
    /// <param name="result">The parsed value, or the default on invalid input.</param>
    /// <returns>Whether the input was valid.</returns>
    static bool IParsable<PgNumeric>.TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out PgNumeric result)
        => TryParse(s, out result);

    /// <summary>
    /// Formats the value as PostgreSQL's canonical text, the same text as <see cref="ToString()"/>.
    /// </summary>
    /// <param name="format">Null or empty; PostgreSQL text has one canonical form.</param>
    /// <param name="formatProvider">Ignored: the text does not depend on culture.</param>
    /// <returns>The canonical text.</returns>
    /// <exception cref="FormatException">A format is specified.</exception>
    public string ToString(string? format, IFormatProvider? formatProvider) => PgTextFormatting.Format(ToString(), format);

    /// <summary>
    /// Copies the value's canonical PostgreSQL text into a span, as interpolated strings and other span-based
    /// formatters request it.
    /// </summary>
    /// <param name="destination">The span that receives the text.</param>
    /// <param name="charsWritten">The number of characters written.</param>
    /// <param name="format">Empty; PostgreSQL text has one canonical form.</param>
    /// <param name="provider">Ignored: the text does not depend on culture.</param>
    /// <returns>Whether the text fit in the destination.</returns>
    /// <exception cref="FormatException">A format is specified.</exception>
    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
        => PgTextFormatting.TryFormat(ToString(), destination, out charsWritten, format);
}

/// <summary>
/// Implements .NET's parsing and span formatting contracts over PostgreSQL's text form.
/// </summary>
public readonly partial record struct PgInet : ISpanFormattable, IParsable<PgInet>
{
    /// <summary>
    /// Parses PostgreSQL text with the type's input function on the active backend thread, as
    /// <see cref="Parse(string)"/> does.
    /// </summary>
    /// <param name="s">The PostgreSQL text.</param>
    /// <param name="provider">Ignored: PostgreSQL's input function and session settings decide the syntax.</param>
    /// <returns>The parsed value.</returns>
    static PgInet IParsable<PgInet>.Parse(string s, IFormatProvider? provider) => Parse(s);

    /// <summary>
    /// Tries to parse PostgreSQL text on the active backend thread, as <see cref="TryParse(string, out PgInet)"/> does.
    /// </summary>
    /// <param name="s">The candidate text.</param>
    /// <param name="provider">Ignored: PostgreSQL's input function and session settings decide the syntax.</param>
    /// <param name="result">The parsed value, or the default on invalid input.</param>
    /// <returns>Whether the input was valid.</returns>
    static bool IParsable<PgInet>.TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out PgInet result)
        => TryParse(s, out result);

    /// <summary>
    /// Formats the value as PostgreSQL's canonical text, the same text as <see cref="ToString()"/>.
    /// </summary>
    /// <param name="format">Null or empty; PostgreSQL text has one canonical form.</param>
    /// <param name="formatProvider">Ignored: the text does not depend on culture.</param>
    /// <returns>The canonical text.</returns>
    /// <exception cref="FormatException">A format is specified.</exception>
    public string ToString(string? format, IFormatProvider? formatProvider) => PgTextFormatting.Format(ToString(), format);

    /// <summary>
    /// Copies the value's canonical PostgreSQL text into a span, as interpolated strings and other span-based
    /// formatters request it.
    /// </summary>
    /// <param name="destination">The span that receives the text.</param>
    /// <param name="charsWritten">The number of characters written.</param>
    /// <param name="format">Empty; PostgreSQL text has one canonical form.</param>
    /// <param name="provider">Ignored: the text does not depend on culture.</param>
    /// <returns>Whether the text fit in the destination.</returns>
    /// <exception cref="FormatException">A format is specified.</exception>
    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
        => PgTextFormatting.TryFormat(ToString(), destination, out charsWritten, format);
}

/// <summary>
/// Implements .NET's parsing and span formatting contracts over PostgreSQL's text form.
/// </summary>
public readonly partial record struct PgCidr : ISpanFormattable, IParsable<PgCidr>
{
    /// <summary>
    /// Parses PostgreSQL text with the type's input function on the active backend thread, as
    /// <see cref="Parse(string)"/> does.
    /// </summary>
    /// <param name="s">The PostgreSQL text.</param>
    /// <param name="provider">Ignored: PostgreSQL's input function and session settings decide the syntax.</param>
    /// <returns>The parsed value.</returns>
    static PgCidr IParsable<PgCidr>.Parse(string s, IFormatProvider? provider) => Parse(s);

    /// <summary>
    /// Tries to parse PostgreSQL text on the active backend thread, as <see cref="TryParse(string, out PgCidr)"/> does.
    /// </summary>
    /// <param name="s">The candidate text.</param>
    /// <param name="provider">Ignored: PostgreSQL's input function and session settings decide the syntax.</param>
    /// <param name="result">The parsed value, or the default on invalid input.</param>
    /// <returns>Whether the input was valid.</returns>
    static bool IParsable<PgCidr>.TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out PgCidr result)
        => TryParse(s, out result);

    /// <summary>
    /// Formats the value as PostgreSQL's canonical text, the same text as <see cref="ToString()"/>.
    /// </summary>
    /// <param name="format">Null or empty; PostgreSQL text has one canonical form.</param>
    /// <param name="formatProvider">Ignored: the text does not depend on culture.</param>
    /// <returns>The canonical text.</returns>
    /// <exception cref="FormatException">A format is specified.</exception>
    public string ToString(string? format, IFormatProvider? formatProvider) => PgTextFormatting.Format(ToString(), format);

    /// <summary>
    /// Copies the value's canonical PostgreSQL text into a span, as interpolated strings and other span-based
    /// formatters request it.
    /// </summary>
    /// <param name="destination">The span that receives the text.</param>
    /// <param name="charsWritten">The number of characters written.</param>
    /// <param name="format">Empty; PostgreSQL text has one canonical form.</param>
    /// <param name="provider">Ignored: the text does not depend on culture.</param>
    /// <returns>Whether the text fit in the destination.</returns>
    /// <exception cref="FormatException">A format is specified.</exception>
    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
        => PgTextFormatting.TryFormat(ToString(), destination, out charsWritten, format);
}

/// <summary>
/// Implements .NET's parsing and span formatting contracts over PostgreSQL's text form.
/// </summary>
public readonly partial record struct PgPoint : ISpanFormattable, IParsable<PgPoint>
{
    /// <summary>
    /// Parses PostgreSQL text with the type's input function on the active backend thread, as
    /// <see cref="Parse(string)"/> does.
    /// </summary>
    /// <param name="s">The PostgreSQL text.</param>
    /// <param name="provider">Ignored: PostgreSQL's input function and session settings decide the syntax.</param>
    /// <returns>The parsed value.</returns>
    static PgPoint IParsable<PgPoint>.Parse(string s, IFormatProvider? provider) => Parse(s);

    /// <summary>
    /// Tries to parse PostgreSQL text on the active backend thread, as <see cref="TryParse(string, out PgPoint)"/> does.
    /// </summary>
    /// <param name="s">The candidate text.</param>
    /// <param name="provider">Ignored: PostgreSQL's input function and session settings decide the syntax.</param>
    /// <param name="result">The parsed value, or the default on invalid input.</param>
    /// <returns>Whether the input was valid.</returns>
    static bool IParsable<PgPoint>.TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out PgPoint result)
        => TryParse(s, out result);

    /// <summary>
    /// Formats the value as PostgreSQL's canonical text, the same text as <see cref="ToString()"/>.
    /// </summary>
    /// <param name="format">Null or empty; PostgreSQL text has one canonical form.</param>
    /// <param name="formatProvider">Ignored: the text does not depend on culture.</param>
    /// <returns>The canonical text.</returns>
    /// <exception cref="FormatException">A format is specified.</exception>
    public string ToString(string? format, IFormatProvider? formatProvider) => PgTextFormatting.Format(ToString(), format);

    /// <summary>
    /// Copies the value's canonical PostgreSQL text into a span, as interpolated strings and other span-based
    /// formatters request it.
    /// </summary>
    /// <param name="destination">The span that receives the text.</param>
    /// <param name="charsWritten">The number of characters written.</param>
    /// <param name="format">Empty; PostgreSQL text has one canonical form.</param>
    /// <param name="provider">Ignored: the text does not depend on culture.</param>
    /// <returns>Whether the text fit in the destination.</returns>
    /// <exception cref="FormatException">A format is specified.</exception>
    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
        => PgTextFormatting.TryFormat(ToString(), destination, out charsWritten, format);
}

/// <summary>
/// Implements .NET's parsing and span formatting contracts over PostgreSQL's text form.
/// </summary>
public readonly partial record struct PgBox : ISpanFormattable, IParsable<PgBox>
{
    /// <summary>
    /// Parses PostgreSQL text with the type's input function on the active backend thread, as
    /// <see cref="Parse(string)"/> does.
    /// </summary>
    /// <param name="s">The PostgreSQL text.</param>
    /// <param name="provider">Ignored: PostgreSQL's input function and session settings decide the syntax.</param>
    /// <returns>The parsed value.</returns>
    static PgBox IParsable<PgBox>.Parse(string s, IFormatProvider? provider) => Parse(s);

    /// <summary>
    /// Tries to parse PostgreSQL text on the active backend thread, as <see cref="TryParse(string, out PgBox)"/> does.
    /// </summary>
    /// <param name="s">The candidate text.</param>
    /// <param name="provider">Ignored: PostgreSQL's input function and session settings decide the syntax.</param>
    /// <param name="result">The parsed value, or the default on invalid input.</param>
    /// <returns>Whether the input was valid.</returns>
    static bool IParsable<PgBox>.TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out PgBox result)
        => TryParse(s, out result);

    /// <summary>
    /// Formats the value as PostgreSQL's canonical text, the same text as <see cref="ToString()"/>.
    /// </summary>
    /// <param name="format">Null or empty; PostgreSQL text has one canonical form.</param>
    /// <param name="formatProvider">Ignored: the text does not depend on culture.</param>
    /// <returns>The canonical text.</returns>
    /// <exception cref="FormatException">A format is specified.</exception>
    public string ToString(string? format, IFormatProvider? formatProvider) => PgTextFormatting.Format(ToString(), format);

    /// <summary>
    /// Copies the value's canonical PostgreSQL text into a span, as interpolated strings and other span-based
    /// formatters request it.
    /// </summary>
    /// <param name="destination">The span that receives the text.</param>
    /// <param name="charsWritten">The number of characters written.</param>
    /// <param name="format">Empty; PostgreSQL text has one canonical form.</param>
    /// <param name="provider">Ignored: the text does not depend on culture.</param>
    /// <returns>Whether the text fit in the destination.</returns>
    /// <exception cref="FormatException">A format is specified.</exception>
    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
        => PgTextFormatting.TryFormat(ToString(), destination, out charsWritten, format);
}

/// <summary>
/// Implements .NET's parsing and span formatting contracts over PostgreSQL's text form.
/// </summary>
public readonly partial record struct PgCircle : ISpanFormattable, IParsable<PgCircle>
{
    /// <summary>
    /// Parses PostgreSQL text with the type's input function on the active backend thread, as
    /// <see cref="Parse(string)"/> does.
    /// </summary>
    /// <param name="s">The PostgreSQL text.</param>
    /// <param name="provider">Ignored: PostgreSQL's input function and session settings decide the syntax.</param>
    /// <returns>The parsed value.</returns>
    static PgCircle IParsable<PgCircle>.Parse(string s, IFormatProvider? provider) => Parse(s);

    /// <summary>
    /// Tries to parse PostgreSQL text on the active backend thread, as <see cref="TryParse(string, out PgCircle)"/> does.
    /// </summary>
    /// <param name="s">The candidate text.</param>
    /// <param name="provider">Ignored: PostgreSQL's input function and session settings decide the syntax.</param>
    /// <param name="result">The parsed value, or the default on invalid input.</param>
    /// <returns>Whether the input was valid.</returns>
    static bool IParsable<PgCircle>.TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out PgCircle result)
        => TryParse(s, out result);

    /// <summary>
    /// Formats the value as PostgreSQL's canonical text, the same text as <see cref="ToString()"/>.
    /// </summary>
    /// <param name="format">Null or empty; PostgreSQL text has one canonical form.</param>
    /// <param name="formatProvider">Ignored: the text does not depend on culture.</param>
    /// <returns>The canonical text.</returns>
    /// <exception cref="FormatException">A format is specified.</exception>
    public string ToString(string? format, IFormatProvider? formatProvider) => PgTextFormatting.Format(ToString(), format);

    /// <summary>
    /// Copies the value's canonical PostgreSQL text into a span, as interpolated strings and other span-based
    /// formatters request it.
    /// </summary>
    /// <param name="destination">The span that receives the text.</param>
    /// <param name="charsWritten">The number of characters written.</param>
    /// <param name="format">Empty; PostgreSQL text has one canonical form.</param>
    /// <param name="provider">Ignored: the text does not depend on culture.</param>
    /// <returns>Whether the text fit in the destination.</returns>
    /// <exception cref="FormatException">A format is specified.</exception>
    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
        => PgTextFormatting.TryFormat(ToString(), destination, out charsWritten, format);
}

/// <summary>
/// Implements .NET's parsing and span formatting contracts over PostgreSQL's text form.
/// </summary>
public readonly partial record struct PgLine : ISpanFormattable, IParsable<PgLine>
{
    /// <summary>
    /// Parses PostgreSQL text with the type's input function on the active backend thread, as
    /// <see cref="Parse(string)"/> does.
    /// </summary>
    /// <param name="s">The PostgreSQL text.</param>
    /// <param name="provider">Ignored: PostgreSQL's input function and session settings decide the syntax.</param>
    /// <returns>The parsed value.</returns>
    static PgLine IParsable<PgLine>.Parse(string s, IFormatProvider? provider) => Parse(s);

    /// <summary>
    /// Tries to parse PostgreSQL text on the active backend thread, as <see cref="TryParse(string, out PgLine)"/> does.
    /// </summary>
    /// <param name="s">The candidate text.</param>
    /// <param name="provider">Ignored: PostgreSQL's input function and session settings decide the syntax.</param>
    /// <param name="result">The parsed value, or the default on invalid input.</param>
    /// <returns>Whether the input was valid.</returns>
    static bool IParsable<PgLine>.TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out PgLine result)
        => TryParse(s, out result);

    /// <summary>
    /// Formats the value as PostgreSQL's canonical text, the same text as <see cref="ToString()"/>.
    /// </summary>
    /// <param name="format">Null or empty; PostgreSQL text has one canonical form.</param>
    /// <param name="formatProvider">Ignored: the text does not depend on culture.</param>
    /// <returns>The canonical text.</returns>
    /// <exception cref="FormatException">A format is specified.</exception>
    public string ToString(string? format, IFormatProvider? formatProvider) => PgTextFormatting.Format(ToString(), format);

    /// <summary>
    /// Copies the value's canonical PostgreSQL text into a span, as interpolated strings and other span-based
    /// formatters request it.
    /// </summary>
    /// <param name="destination">The span that receives the text.</param>
    /// <param name="charsWritten">The number of characters written.</param>
    /// <param name="format">Empty; PostgreSQL text has one canonical form.</param>
    /// <param name="provider">Ignored: the text does not depend on culture.</param>
    /// <returns>Whether the text fit in the destination.</returns>
    /// <exception cref="FormatException">A format is specified.</exception>
    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
        => PgTextFormatting.TryFormat(ToString(), destination, out charsWritten, format);
}

/// <summary>
/// Implements .NET's parsing and span formatting contracts over PostgreSQL's text form.
/// </summary>
public readonly partial record struct PgLineSegment : ISpanFormattable, IParsable<PgLineSegment>
{
    /// <summary>
    /// Parses PostgreSQL text with the type's input function on the active backend thread, as
    /// <see cref="Parse(string)"/> does.
    /// </summary>
    /// <param name="s">The PostgreSQL text.</param>
    /// <param name="provider">Ignored: PostgreSQL's input function and session settings decide the syntax.</param>
    /// <returns>The parsed value.</returns>
    static PgLineSegment IParsable<PgLineSegment>.Parse(string s, IFormatProvider? provider) => Parse(s);

    /// <summary>
    /// Tries to parse PostgreSQL text on the active backend thread, as <see cref="TryParse(string, out PgLineSegment)"/> does.
    /// </summary>
    /// <param name="s">The candidate text.</param>
    /// <param name="provider">Ignored: PostgreSQL's input function and session settings decide the syntax.</param>
    /// <param name="result">The parsed value, or the default on invalid input.</param>
    /// <returns>Whether the input was valid.</returns>
    static bool IParsable<PgLineSegment>.TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out PgLineSegment result)
        => TryParse(s, out result);

    /// <summary>
    /// Formats the value as PostgreSQL's canonical text, the same text as <see cref="ToString()"/>.
    /// </summary>
    /// <param name="format">Null or empty; PostgreSQL text has one canonical form.</param>
    /// <param name="formatProvider">Ignored: the text does not depend on culture.</param>
    /// <returns>The canonical text.</returns>
    /// <exception cref="FormatException">A format is specified.</exception>
    public string ToString(string? format, IFormatProvider? formatProvider) => PgTextFormatting.Format(ToString(), format);

    /// <summary>
    /// Copies the value's canonical PostgreSQL text into a span, as interpolated strings and other span-based
    /// formatters request it.
    /// </summary>
    /// <param name="destination">The span that receives the text.</param>
    /// <param name="charsWritten">The number of characters written.</param>
    /// <param name="format">Empty; PostgreSQL text has one canonical form.</param>
    /// <param name="provider">Ignored: the text does not depend on culture.</param>
    /// <returns>Whether the text fit in the destination.</returns>
    /// <exception cref="FormatException">A format is specified.</exception>
    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
        => PgTextFormatting.TryFormat(ToString(), destination, out charsWritten, format);
}

/// <summary>
/// Implements .NET's parsing contract over PostgreSQL's input function.
/// </summary>
public readonly partial record struct PgInterval : IParsable<PgInterval>
{
    /// <summary>
    /// Parses PostgreSQL text with the type's input function on the active backend thread, as
    /// <see cref="Parse(string)"/> does.
    /// </summary>
    /// <param name="s">The PostgreSQL text.</param>
    /// <param name="provider">Ignored: PostgreSQL's input function and session settings decide the syntax.</param>
    /// <returns>The parsed value.</returns>
    static PgInterval IParsable<PgInterval>.Parse(string s, IFormatProvider? provider) => Parse(s);

    /// <summary>
    /// Tries to parse PostgreSQL text on the active backend thread, as <see cref="TryParse(string, out PgInterval)"/> does.
    /// </summary>
    /// <param name="s">The candidate text.</param>
    /// <param name="provider">Ignored: PostgreSQL's input function and session settings decide the syntax.</param>
    /// <param name="result">The parsed value, or the default on invalid input.</param>
    /// <returns>Whether the input was valid.</returns>
    static bool IParsable<PgInterval>.TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out PgInterval result)
        => TryParse(s, out result);
}

/// <summary>
/// Implements .NET's parsing contract over PostgreSQL's input function.
/// </summary>
public readonly partial record struct PgDate : IParsable<PgDate>
{
    /// <summary>
    /// Parses PostgreSQL text with the type's input function on the active backend thread, as
    /// <see cref="Parse(string)"/> does.
    /// </summary>
    /// <param name="s">The PostgreSQL text.</param>
    /// <param name="provider">Ignored: PostgreSQL's input function and session settings decide the syntax.</param>
    /// <returns>The parsed value.</returns>
    static PgDate IParsable<PgDate>.Parse(string s, IFormatProvider? provider) => Parse(s);

    /// <summary>
    /// Tries to parse PostgreSQL text on the active backend thread, as <see cref="TryParse(string, out PgDate)"/> does.
    /// </summary>
    /// <param name="s">The candidate text.</param>
    /// <param name="provider">Ignored: PostgreSQL's input function and session settings decide the syntax.</param>
    /// <param name="result">The parsed value, or the default on invalid input.</param>
    /// <returns>Whether the input was valid.</returns>
    static bool IParsable<PgDate>.TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out PgDate result)
        => TryParse(s, out result);
}

/// <summary>
/// Implements .NET's parsing contract over PostgreSQL's input function.
/// </summary>
public readonly partial record struct PgTime : IParsable<PgTime>
{
    /// <summary>
    /// Parses PostgreSQL text with the type's input function on the active backend thread, as
    /// <see cref="Parse(string)"/> does.
    /// </summary>
    /// <param name="s">The PostgreSQL text.</param>
    /// <param name="provider">Ignored: PostgreSQL's input function and session settings decide the syntax.</param>
    /// <returns>The parsed value.</returns>
    static PgTime IParsable<PgTime>.Parse(string s, IFormatProvider? provider) => Parse(s);

    /// <summary>
    /// Tries to parse PostgreSQL text on the active backend thread, as <see cref="TryParse(string, out PgTime)"/> does.
    /// </summary>
    /// <param name="s">The candidate text.</param>
    /// <param name="provider">Ignored: PostgreSQL's input function and session settings decide the syntax.</param>
    /// <param name="result">The parsed value, or the default on invalid input.</param>
    /// <returns>Whether the input was valid.</returns>
    static bool IParsable<PgTime>.TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out PgTime result)
        => TryParse(s, out result);
}

/// <summary>
/// Implements .NET's parsing contract over PostgreSQL's input function.
/// </summary>
public readonly partial record struct PgTimeTz : IParsable<PgTimeTz>
{
    /// <summary>
    /// Parses PostgreSQL text with the type's input function on the active backend thread, as
    /// <see cref="Parse(string)"/> does.
    /// </summary>
    /// <param name="s">The PostgreSQL text.</param>
    /// <param name="provider">Ignored: PostgreSQL's input function and session settings decide the syntax.</param>
    /// <returns>The parsed value.</returns>
    static PgTimeTz IParsable<PgTimeTz>.Parse(string s, IFormatProvider? provider) => Parse(s);

    /// <summary>
    /// Tries to parse PostgreSQL text on the active backend thread, as <see cref="TryParse(string, out PgTimeTz)"/> does.
    /// </summary>
    /// <param name="s">The candidate text.</param>
    /// <param name="provider">Ignored: PostgreSQL's input function and session settings decide the syntax.</param>
    /// <param name="result">The parsed value, or the default on invalid input.</param>
    /// <returns>Whether the input was valid.</returns>
    static bool IParsable<PgTimeTz>.TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out PgTimeTz result)
        => TryParse(s, out result);
}

/// <summary>
/// Implements .NET's parsing contract over PostgreSQL's input function.
/// </summary>
public readonly partial record struct PgTimestamp : IParsable<PgTimestamp>
{
    /// <summary>
    /// Parses PostgreSQL text with the type's input function on the active backend thread, as
    /// <see cref="Parse(string)"/> does.
    /// </summary>
    /// <param name="s">The PostgreSQL text.</param>
    /// <param name="provider">Ignored: PostgreSQL's input function and session settings decide the syntax.</param>
    /// <returns>The parsed value.</returns>
    static PgTimestamp IParsable<PgTimestamp>.Parse(string s, IFormatProvider? provider) => Parse(s);

    /// <summary>
    /// Tries to parse PostgreSQL text on the active backend thread, as <see cref="TryParse(string, out PgTimestamp)"/> does.
    /// </summary>
    /// <param name="s">The candidate text.</param>
    /// <param name="provider">Ignored: PostgreSQL's input function and session settings decide the syntax.</param>
    /// <param name="result">The parsed value, or the default on invalid input.</param>
    /// <returns>Whether the input was valid.</returns>
    static bool IParsable<PgTimestamp>.TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out PgTimestamp result)
        => TryParse(s, out result);
}

/// <summary>
/// Implements .NET's parsing contract over PostgreSQL's input function.
/// </summary>
public readonly partial record struct PgTimestampTz : IParsable<PgTimestampTz>
{
    /// <summary>
    /// Parses PostgreSQL text with the type's input function on the active backend thread, as
    /// <see cref="Parse(string)"/> does.
    /// </summary>
    /// <param name="s">The PostgreSQL text.</param>
    /// <param name="provider">Ignored: PostgreSQL's input function and session settings decide the syntax.</param>
    /// <returns>The parsed value.</returns>
    static PgTimestampTz IParsable<PgTimestampTz>.Parse(string s, IFormatProvider? provider) => Parse(s);

    /// <summary>
    /// Tries to parse PostgreSQL text on the active backend thread, as <see cref="TryParse(string, out PgTimestampTz)"/> does.
    /// </summary>
    /// <param name="s">The candidate text.</param>
    /// <param name="provider">Ignored: PostgreSQL's input function and session settings decide the syntax.</param>
    /// <param name="result">The parsed value, or the default on invalid input.</param>
    /// <returns>Whether the input was valid.</returns>
    static bool IParsable<PgTimestampTz>.TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out PgTimestampTz result)
        => TryParse(s, out result);
}

/// <summary>
/// Implements .NET's parsing and span formatting contracts over PostgreSQL's text form.
/// </summary>
public sealed partial class PgPath : ISpanFormattable, IParsable<PgPath>
{
    /// <summary>
    /// Parses PostgreSQL text with the type's input function on the active backend thread, as
    /// <see cref="Parse(string)"/> does.
    /// </summary>
    /// <param name="s">The PostgreSQL text.</param>
    /// <param name="provider">Ignored: PostgreSQL's input function and session settings decide the syntax.</param>
    /// <returns>The parsed value.</returns>
    static PgPath IParsable<PgPath>.Parse(string s, IFormatProvider? provider) => Parse(s);

    /// <summary>
    /// Tries to parse PostgreSQL text on the active backend thread, as <see cref="TryParse(string, out PgPath)"/> does.
    /// </summary>
    /// <param name="s">The candidate text.</param>
    /// <param name="provider">Ignored: PostgreSQL's input function and session settings decide the syntax.</param>
    /// <param name="result">The parsed value, or null on invalid input.</param>
    /// <returns>Whether the input was valid.</returns>
    static bool IParsable<PgPath>.TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out PgPath result)
        => TryParse(s, out result);

    /// <summary>
    /// Formats the value as PostgreSQL's canonical text, the same text as <see cref="ToString()"/>.
    /// </summary>
    /// <param name="format">Null or empty; PostgreSQL text has one canonical form.</param>
    /// <param name="formatProvider">Ignored: the text does not depend on culture.</param>
    /// <returns>The canonical text.</returns>
    /// <exception cref="FormatException">A format is specified.</exception>
    public string ToString(string? format, IFormatProvider? formatProvider) => PgTextFormatting.Format(ToString(), format);

    /// <summary>
    /// Copies the value's canonical PostgreSQL text into a span, as interpolated strings and other span-based
    /// formatters request it.
    /// </summary>
    /// <param name="destination">The span that receives the text.</param>
    /// <param name="charsWritten">The number of characters written.</param>
    /// <param name="format">Empty; PostgreSQL text has one canonical form.</param>
    /// <param name="provider">Ignored: the text does not depend on culture.</param>
    /// <returns>Whether the text fit in the destination.</returns>
    /// <exception cref="FormatException">A format is specified.</exception>
    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
        => PgTextFormatting.TryFormat(ToString(), destination, out charsWritten, format);
}

/// <summary>
/// Implements .NET's parsing and span formatting contracts over PostgreSQL's text form.
/// </summary>
public sealed partial class PgPolygon : ISpanFormattable, IParsable<PgPolygon>
{
    /// <summary>
    /// Parses PostgreSQL text with the type's input function on the active backend thread, as
    /// <see cref="Parse(string)"/> does.
    /// </summary>
    /// <param name="s">The PostgreSQL text.</param>
    /// <param name="provider">Ignored: PostgreSQL's input function and session settings decide the syntax.</param>
    /// <returns>The parsed value.</returns>
    static PgPolygon IParsable<PgPolygon>.Parse(string s, IFormatProvider? provider) => Parse(s);

    /// <summary>
    /// Tries to parse PostgreSQL text on the active backend thread, as <see cref="TryParse(string, out PgPolygon)"/> does.
    /// </summary>
    /// <param name="s">The candidate text.</param>
    /// <param name="provider">Ignored: PostgreSQL's input function and session settings decide the syntax.</param>
    /// <param name="result">The parsed value, or null on invalid input.</param>
    /// <returns>Whether the input was valid.</returns>
    static bool IParsable<PgPolygon>.TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out PgPolygon result)
        => TryParse(s, out result);

    /// <summary>
    /// Formats the value as PostgreSQL's canonical text, the same text as <see cref="ToString()"/>.
    /// </summary>
    /// <param name="format">Null or empty; PostgreSQL text has one canonical form.</param>
    /// <param name="formatProvider">Ignored: the text does not depend on culture.</param>
    /// <returns>The canonical text.</returns>
    /// <exception cref="FormatException">A format is specified.</exception>
    public string ToString(string? format, IFormatProvider? formatProvider) => PgTextFormatting.Format(ToString(), format);

    /// <summary>
    /// Copies the value's canonical PostgreSQL text into a span, as interpolated strings and other span-based
    /// formatters request it.
    /// </summary>
    /// <param name="destination">The span that receives the text.</param>
    /// <param name="charsWritten">The number of characters written.</param>
    /// <param name="format">Empty; PostgreSQL text has one canonical form.</param>
    /// <param name="provider">Ignored: the text does not depend on culture.</param>
    /// <returns>Whether the text fit in the destination.</returns>
    /// <exception cref="FormatException">A format is specified.</exception>
    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
        => PgTextFormatting.TryFormat(ToString(), destination, out charsWritten, format);
}
