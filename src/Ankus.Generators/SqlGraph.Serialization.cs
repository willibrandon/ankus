using System.Text;

namespace Ankus.Generators;

internal sealed partial class SqlGraph
{
    /// <summary>
    /// Encodes the resolved installation graph independently of runtime serializers or analyzer-side dependencies.
    /// </summary>
    /// <returns>The versioned base64 graph consumed by build tools and native schema readers.</returns>
    internal string? Encode()
    {
        if (!_emitted)
        {
            throw new InvalidOperationException("The installation graph must be validated and emitted before encoding.");
        }

        if (_ordered.Count > 100_000)
        {
            Error(null, "An embedded installation graph cannot exceed 100,000 declarations.");
            return null;
        }

        try
        {
            return EncodeCore();
        }
        catch (FormatException error)
        {
            Error(null, error.Message);
            return null;
        }
    }

    /// <summary>
    /// Writes length-delimited graph fields while enforcing the consumer's allocation limits.
    /// </summary>
    /// <returns>The bounded graph in base64 format.</returns>
    private string EncodeCore()
    {
        var utf8 = new UTF8Encoding(false, true);
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, utf8, leaveOpen: true))
        {
            writer.Write(Encoding.ASCII.GetBytes("ANKUSG1\0"));
            writer.Write(_ordered.Count);
            foreach (SqlEntity entity in _ordered)
            {
                WriteText(entity.Key);
                WriteText(entity.Kind);
                WriteText(entity.SqlTemplate.Replace("\r\n", "\n").Replace('\r', '\n'));
                WriteText(entity.Owner?.Key ?? string.Empty);
                WriteTexts(entity.Names.Concat(entity.SelectionNames));
                WriteTexts(entity.Dependencies.Select(static dependency => dependency.Key));
                WriteTexts(entity.Attachments);
            }

            void WriteText(string value)
            {
                if (utf8.GetByteCount(value) > 32 * 1024 * 1024 - stream.Length - 4)
                {
                    throw new FormatException("An embedded installation graph cannot exceed 32 MiB.");
                }

                byte[] text = utf8.GetBytes(value);
                writer.Write(text.Length);
                writer.Write(text);
            }

            void WriteTexts(IEnumerable<string> values)
            {
                string[] sorted = [.. values.Distinct(StringComparer.Ordinal).OrderBy(static value => value, StringComparer.Ordinal)];
                if (sorted.Length > 100_000 || stream.Length + 4 > 32 * 1024 * 1024)
                {
                    throw new FormatException("An embedded installation graph exceeds its field count or 32 MiB size limit.");
                }

                writer.Write(sorted.Length);
                foreach (string value in sorted)
                {
                    WriteText(value);
                }
            }
        }

        return Convert.ToBase64String(stream.ToArray());
    }
}
