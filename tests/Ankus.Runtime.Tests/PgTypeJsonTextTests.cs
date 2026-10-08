using System.Text;

namespace Ankus.Runtime.Tests;

/// <summary>
/// Verifies that generated custom-type JSON text uses pgrx's serde_json compact escaping.
/// </summary>
[TestClass]
public sealed class PgTypeJsonTextTests
{
    /// <summary>
    /// Escapes only quotation marks, reverse solidus and C0 controls, with serde_json's short and lowercase escapes.
    /// </summary>
    [TestMethod]
    public void JsonTextEscapesOnlyJsonSyntaxAndControls()
    {
        string unescaped = new([(char)0x7F, '<', '>', '&', '\'', '+', '`', ' ', (char)0xE9, (char)0x4E2D, (char)0xD83D, (char)0xDE00,
            (char)0x2028, (char)0x2029, (char)0xFFFF]);
        string key = "k" + unescaped;
        var label = new Label(key, "\"\\/\b\f\n\r\t" + new string([(char)0, (char)0x1F]) + unescaped);
        var codec = new GeneratedLabelCodec();

        string text = codec.Format(label);

        Assert.AreEqual("{\"" + key + "\":\"\\\"\\\\/\\b\\f\\n\\r\\t\\u0000\\u001f" + unescaped + "\"}", text);
        Assert.AreEqual(label, codec.Parse(text));
        Assert.AreEqual(label, codec.Parse(
            "{\"k\\u007f<>&'+` \\u00e9\\u4E2D\\ud83d\\ude00\\u2028\\u2029\\uffff\":"
            + "\"\\\"\\\\\\/\\b\\f\\n\\r\\t\\u0000\\u001F\\u007F<>&'+` \\u00E9\\u4e2d\\uD83D\\uDE00\\u2028\\u2029\\uFFFF\"}"));
    }

    /// <summary>
    /// Keeps rejecting unpaired surrogates before writing JSON text instead of escaping or replacing them.
    /// </summary>
    /// <param name="key">The property name.</param>
    /// <param name="value">The string value.</param>
    [TestMethod]
    [DataRow("key", "\ud800")]
    [DataRow("\udc00", "value")]
    public void JsonTextRejectsUnpairedSurrogates(string key, string value)
        => Assert.ThrowsExactly<EncoderFallbackException>(() => new GeneratedLabelCodec().Format(new Label(key, value)));

    /// <summary>
    /// Carries one serialized property whose name and value both contain arbitrary text.
    /// </summary>
    private sealed record Label(string Key, string Value);

    /// <summary>
    /// Emulates a generated codec that writes the label's key as a property name and its value as a string.
    /// </summary>
    private sealed class GeneratedLabelCodec : PgSerializedTypeCodec<Label>
    {
        /// <inheritdoc />
        protected override Label ReadValue(ref PgTypeReader reader)
        {
            reader.ReadStartObject();
            string key = reader.ReadPropertyName() ?? throw new FormatException("Expected one member.");
            string value = reader.ReadString();
            if (reader.ReadPropertyName() is not null)
            {
                throw new FormatException("Unexpected member.");
            }

            return new(key, value);
        }

        /// <inheritdoc />
        protected override void WriteValue(PgTypeWriter writer, Label value)
        {
            writer.WriteStartObject(1);
            writer.WritePropertyName(value.Key);
            writer.WriteString(value.Value);
            writer.WriteEndObject();
        }
    }
}
