using System.Security;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Ankus.Tool;

/// <summary>
/// Retains a manifest's original bytes and applies edits only to selected version tokens.
/// </summary>
internal sealed partial class UpgradeDocument
{
    private readonly Encoding _encoding;
    private readonly byte[] _preamble;
    private readonly Dictionary<(int Start, int Length), string> _edits = [];
    private readonly int[] _lines;

    private UpgradeDocument(string path, byte[] original, string text, Encoding encoding)
    {
        Path = path;
        Original = original;
        Text = text;
        _encoding = encoding;
        byte[] preamble = encoding.GetPreamble();
        _preamble = original.AsSpan().StartsWith(preamble) ? preamble : [];
        _lines = GetLineStarts(text);
    }

    /// <summary>
    /// Gets the absolute destination path, following a manifest symbolic link to its target.
    /// </summary>
    internal string Path { get; }

    /// <summary>
    /// Gets the unchanged bytes used to detect concurrent edits and restore failed transactions.
    /// </summary>
    internal byte[] Original { get; }

    /// <summary>
    /// Gets the original text with its line endings preserved.
    /// </summary>
    internal string Text { get; }

    /// <summary>
    /// Gets whether at least one token changes.
    /// </summary>
    internal bool Changed => _edits.Count != 0 || _metadata.Count != 0;

    /// <summary>
    /// Reads a manifest without normalizing its encoding, whitespace, or quotes.
    /// </summary>
    /// <param name="path">The manifest path.</param>
    /// <returns>The captured original manifest.</returns>
    internal static UpgradeDocument Read(string path)
    {
        path = System.IO.Path.GetFullPath(path);
        FileSystemInfo? link = File.ResolveLinkTarget(path, returnFinalTarget: true);
        path = link?.FullName ?? path;
        byte[] original = File.ReadAllBytes(path);
        using var stream = new MemoryStream(original);
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
        string text = reader.ReadToEnd();
        return new UpgradeDocument(path, original, text, reader.CurrentEncoding);
    }

    /// <summary>
    /// Parses XML with source locations for exact token replacement.
    /// </summary>
    /// <returns>The source XML tree.</returns>
    internal XDocument ReadXml() => XDocument.Parse(Text, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);

    /// <summary>
    /// Replaces one XML attribute or scalar element value without rewriting surrounding XML.
    /// </summary>
    /// <param name="node">The source-located attribute or element.</param>
    /// <param name="value">The new decoded value.</param>
    internal void Replace(XObject node, string value)
    {
        string current = node is XAttribute attribute ? attribute.Value : ((XElement)node).Value;
        if (current == value)
        {
            return;
        }

        var location = (IXmlLineInfo)node;
        int start = _lines[location.LineNumber - 1] + location.LinePosition - 1;
        string escaped = SecurityElement.Escape(value);
        if (node is XAttribute)
        {
            start = Text.IndexOf('=', start) + 1;
            while (char.IsWhiteSpace(Text[start]))
            {
                start++;
            }

            char quote = Text[start++];
            Replace(start, Text.IndexOf(quote, start) - start, escaped);
            return;
        }

        var element = (XElement)node;
        if (Text[start] != '<' && start > 0 && Text[start - 1] == '<')
        {
            start--;
        }

        if (element.Nodes().Any(static child => child is not XText))
        {
            throw new InvalidOperationException($"Cannot replace a structured version element in '{Path}'.");
        }

        int end = start;
        char delimiter = '\0';
        while (++end < Text.Length)
        {
            char character = Text[end];
            if (delimiter != '\0')
            {
                if (character == delimiter)
                {
                    delimiter = '\0';
                }
            }
            else if (character is '\'' or '"')
            {
                delimiter = character;
            }
            else if (character == '>')
            {
                break;
            }
        }

        if (Text[end - 1] == '/')
        {
            int nameEnd = start + 1;
            while (!char.IsWhiteSpace(Text[nameEnd]) && Text[nameEnd] is not '/' and not '>')
            {
                nameEnd++;
            }

            Replace(end - 1, 2, ">" + escaped + "</" + Text[(start + 1)..nameEnd] + ">");
        }
        else
        {
            start = end + 1;
            Replace(start, Text.IndexOf("</", start, StringComparison.Ordinal) - start, escaped);
        }
    }

    /// <summary>
    /// Records an exact text replacement, rejecting overlapping or conflicting declarations.
    /// </summary>
    /// <param name="start">The original character offset.</param>
    /// <param name="length">The original token length.</param>
    /// <param name="value">The replacement text, already escaped for its file format.</param>
    internal void Replace(int start, int length, string value)
    {
        if (Text.AsSpan(start, length).SequenceEqual(value))
        {
            return;
        }

        foreach (((int position, int count), string replacement) in _edits)
        {
            if (position == start && count == length && replacement == value)
            {
                return;
            }

            if (start < position + count && position < start + length)
            {
                throw new InvalidOperationException($"Conflicting framework versions target the same declaration in '{Path}'.");
            }
        }

        _edits.Add((start, length), value);
    }

    /// <summary>
    /// Renders planned edits while preserving every other character.
    /// </summary>
    /// <returns>The updated manifest text.</returns>
    internal string Render()
    {
        var result = new StringBuilder(Text);
        IEnumerable<(int Start, int Length, string Value)> edits = _edits
            .Select(static edit => (edit.Key.Start, edit.Key.Length, edit.Value)).Concat(GetMetadataEdits());
        foreach ((int start, int length, string value) in edits.OrderByDescending(static edit => edit.Start))
        {
            result.Remove(start, length).Insert(start, value);
        }

        return result.ToString();
    }

    /// <summary>
    /// Encodes the planned text with the original encoding and byte-order mark policy.
    /// </summary>
    /// <returns>The complete replacement file bytes.</returns>
    internal byte[] RenderBytes() => [.. _preamble, .. _encoding.GetBytes(Render())];

    private static int[] GetLineStarts(string text)
    {
        List<int> lines = [0];
        for (int index = 0; index < text.Length; index++)
        {
            if (text[index] == '\r')
            {
                if (index + 1 < text.Length && text[index + 1] == '\n')
                {
                    index++;
                }

                lines.Add(index + 1);
            }
            else if (text[index] == '\n')
            {
                lines.Add(index + 1);
            }
        }

        return [.. lines];
    }
}
