using System.Text;

namespace Ankus.Generators;

/// <summary>
/// Encodes detached installation nodes with the schema reader's exact format and allocation limits.
/// </summary>
internal static class InstallationGraphEncoding
{
    /// <summary>
    /// Encodes the resolved installation graph independently of runtime serializers or analyzer-side dependencies.
    /// </summary>
    /// <param name="nodes">The ordered graph fields and independently rendered SQL.</param>
    /// <returns>The versioned base64 graph or its original validation error.</returns>
    internal static Output Encode(EquatableArray<InstallationGraphModel.EncodedNode> nodes)
    {
        if (nodes.Count > 100_000)
        {
            return new(null, "An embedded installation graph cannot exceed 100,000 declarations.");
        }

        try
        {
            return new(EncodeCore(nodes), null);
        }
        catch (FormatException error)
        {
            return new(null, error.Message);
        }
    }

    /// <summary>
    /// Writes length-delimited graph fields while enforcing the consumer's allocation limits.
    /// </summary>
    /// <returns>The bounded graph in base64 format.</returns>
    private static string EncodeCore(EquatableArray<InstallationGraphModel.EncodedNode> nodes)
    {
        var utf8 = new UTF8Encoding(false, true);
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, utf8, leaveOpen: true))
        {
            writer.Write(Encoding.ASCII.GetBytes("ANKUSG2\0"));
            WriteText(SqlProvenance.Preamble);
            writer.Write(nodes.Count);
            foreach (InstallationGraphModel.EncodedNode entity in nodes)
            {
                WriteText(entity.Key);
                WriteText(entity.Kind);
                WriteText(entity.Sql);
                WriteText(entity.Owner);
                WriteTexts(entity.Names);
                WriteTexts(entity.Dependencies);
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

    /// <summary>
    /// Contains graph bytes or a deterministic bounds error for the current diagnostic boundary.
    /// </summary>
    /// <param name="Graph">The encoded graph, or none when a size limit is exceeded.</param>
    /// <param name="Error">The original validation error, or none after successful encoding.</param>
    internal sealed record Output(string? Graph, string? Error);
}
