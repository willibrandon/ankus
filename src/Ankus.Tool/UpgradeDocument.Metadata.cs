using System.Xml;
using System.Xml.Linq;

namespace Ankus.Tool;

internal sealed partial class UpgradeDocument
{
    private readonly Dictionary<(int Start, string Name, string Condition), (XElement Item, string Value)> _metadata = [];

    /// <summary>
    /// Adds version metadata restricted to a framework identity without rewriting the original item or its other metadata.
    /// </summary>
    /// <param name="item">The source-located package item.</param>
    /// <param name="name">The metadata name.</param>
    /// <param name="condition">The original condition combined with an exact item identity.</param>
    /// <param name="value">The resolved requirement.</param>
    internal void AddItemMetadata(XElement item, string name, string condition, string value)
    {
        (int Start, string Name, string Condition) key = (GetPosition(item), name, condition);
        if (_metadata.TryGetValue(key, out (XElement Item, string Value) existing) && existing.Value != value)
        {
            throw new InvalidOperationException($"Conflicting framework versions target the same item in '{Path}'.");
        }

        _metadata[key] = (item, value);
    }

    private IEnumerable<(int Start, int Length, string Value)> GetMetadataEdits()
    {
        foreach (IGrouping<int, KeyValuePair<(int Start, string Name, string Condition), (XElement Item, string Value)>> group
            in _metadata.GroupBy(static entry => entry.Key.Start))
        {
            XElement item = group.First().Value.Item;
            string metadata = string.Concat(group.Select(entry => new XElement(item.Name.Namespace + entry.Key.Name,
                new XAttribute("Condition", entry.Key.Condition), entry.Value.Value).ToString(SaveOptions.DisableFormatting)));
            (int close, int length, string suffix) = FindItemClosingTag(item);
            yield return (close, length, (length == 2 ? ">" : "") + metadata + suffix);
        }
    }

    private (int Start, int Length, string Suffix) FindItemClosingTag(XElement item)
    {
        int position = GetPosition(item);
        using var source = new StringReader(Text);
        using XmlReader reader = XmlReader.Create(source);
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element || GetPosition((IXmlLineInfo)reader) != position)
            {
                continue;
            }

            if (reader.IsEmptyElement)
            {
                int end = FindTagEnd(position);
                return (end - 1, 2, "</" + reader.Name + ">");
            }

            int depth = reader.Depth;
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)
                {
                    int closing = Text.LastIndexOf("</", GetPosition((IXmlLineInfo)reader), StringComparison.Ordinal);
                    return (closing, 0, "");
                }
            }
        }

        throw new InvalidOperationException($"Cannot locate the package item in '{Path}'.");
    }

    private int GetPosition(IXmlLineInfo location)
        => _lines[location.LineNumber - 1] + location.LinePosition - 1;

    private int FindTagEnd(int position)
    {
        char delimiter = '\0';
        for (int index = position; index < Text.Length; index++)
        {
            char character = Text[index];
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
                return index;
            }
        }

        throw new InvalidOperationException($"Cannot locate the package item's closing bracket in '{Path}'.");
    }
}
