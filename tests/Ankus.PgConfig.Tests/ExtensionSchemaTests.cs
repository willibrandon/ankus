using System.Buffers.Binary;
using System.Text;

namespace Ankus.PgConfig.Tests;

/// <summary>
/// Verifies the embedded schema contract independently of its native C emitter.
/// </summary>
[TestClass]
public sealed class ExtensionSchemaTests
{
    private const string Document = """
        {"formatVersion":1,"name":"probe","version":"0.1.0-beta.1","postgresMajor":18,
         "runtimeIdentifier":"linux-x64","library":"Probe.so","relocatable":true,
         "sql":"SELECT 'café 🐘', 'MODULE_PATHNAME';\n"}
        """;

    /// <summary>
    /// Preserves Unicode, SQL newlines, substitution markers and the original publication identity.
    /// </summary>
    /// <param name="padding">Native linker zero padding after the length-delimited JSON.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(511)]
    public void ReadsIndependentMetadata(int padding)
    {
        ExtensionSchema schema = ExtensionSchema.Decode(Payload(Document, padding), "linux-x64");
        Assert.AreEqual("probe", schema.Name);
        Assert.AreEqual("0.1.0-beta.1", schema.Version);
        Assert.AreEqual(18, schema.Artifacts.PostgresMajor);
        Assert.AreEqual("linux-x64", schema.Artifacts.RuntimeIdentifier);
        Assert.AreEqual("Probe.so", schema.Artifacts.Library);
        Assert.AreEqual("probe.control", schema.Artifacts.Control);
        Assert.AreEqual("probe--0.1.0-beta.1.sql", schema.Artifacts.Sql);
        Assert.IsTrue(schema.Relocatable);
        Assert.AreEqual("SELECT 'café 🐘', 'MODULE_PATHNAME';\n", schema.Sql);
    }

    /// <summary>
    /// Accepts a nonrelocatable extension and preserves intentional SQL whitespace.
    /// </summary>
    [TestMethod]
    public void PreservesNonrelocatableAndWhitespaceContracts()
    {
        string document = Document.Replace("true", "false", StringComparison.Ordinal)
            .Replace("SELECT ", "  SELECT ", StringComparison.Ordinal);
        ExtensionSchema schema = ExtensionSchema.Decode(Payload(document), "linux-x64");
        Assert.IsFalse(schema.Relocatable);
        Assert.AreEqual("  SELECT 'café 🐘', 'MODULE_PATHNAME';\n", schema.Sql);
    }

    /// <summary>
    /// Rejects corrupt framing, nonzero padding and invalid UTF-8 before returning any SQL.
    /// </summary>
    /// <param name="corruption">The invalid framing partition.</param>
    [TestMethod]
    [DataRow("magic")]
    [DataRow("empty")]
    [DataRow("overflow")]
    [DataRow("truncated")]
    [DataRow("padding")]
    [DataRow("utf8")]
    public void RejectsInvalidFraming(string corruption)
    {
        byte[] data = Payload(Document, corruption == "padding" ? 1 : 0);
        switch (corruption)
        {
            case "magic":
                data[0] = 0;
                break;
            case "empty":
                BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), 0);
                break;
            case "overflow":
                BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), uint.MaxValue);
                break;
            case "truncated":
                Array.Resize(ref data, data.Length - 1);
                break;
            case "padding":
                data[^1] = 1;
                break;
            case "utf8":
                data[12] = 255;
                break;
        }

        Assert.ThrowsExactly<FormatException>(() => ExtensionSchema.Decode(data, "linux-x64"));
    }

    /// <summary>
    /// Rejects malformed JSON, unsupported versions, duplicate fields and inconsistent publication identity.
    /// </summary>
    /// <param name="original">The original independently encoded field.</param>
    /// <param name="replacement">The invalid JSON fragment.</param>
    [TestMethod]
    [DataRow("\"formatVersion\":1", "\"formatVersion\":2")]
    [DataRow("\"formatVersion\":1", "\"formatVersion\":1,\"formatVersion\":1")]
    [DataRow("\"formatVersion\":1", "\"formatVersion\":2147483648")]
    [DataRow("\"name\":\"probe\"", "\"name\":null")]
    [DataRow("\"name\":\"probe\"", "\"name\":\"\"")]
    [DataRow("\"name\":\"probe\"", "\"name\":\"../probe\"")]
    [DataRow("\"name\":\"probe\"", "\"name\":\"probe+suffix\"")]
    [DataRow("\"name\":\"probe\"", "\"name\":\"bad--name\"")]
    [DataRow("\"name\":\"probe\"", "\"name\":\"bad\\u0000name\"")]
    [DataRow("\"name\":\"probe\"", "\"name\":\"bad\\ud800name\"")]
    [DataRow("\"name\":\"probe\",", "")]
    [DataRow("\"version\":\"0.1.0-beta.1\"", "\"version\":\"-1\"")]
    [DataRow("\"version\":\"0.1.0-beta.1\"", "\"version\":\"1--2\"")]
    [DataRow("\"postgresMajor\":18", "\"postgresMajor\":12")]
    [DataRow("\"postgresMajor\":18", "\"postgresMajor\":20")]
    [DataRow("\"postgresMajor\":18", "\"postgresMajor\":\"18\"")]
    [DataRow("\"runtimeIdentifier\":\"linux-x64\"", "\"runtimeIdentifier\":\"win-x64\"")]
    [DataRow("\"library\":\"Probe.so\"", "\"library\":\"../Probe.so\"")]
    [DataRow("\"relocatable\":true", "\"relocatable\":\"true\"")]
    [DataRow("{", "[")]
    public void RejectsInvalidMetadata(string original, string replacement)
    {
        byte[] data = Payload(Document.Replace(original, replacement, StringComparison.Ordinal));
        Assert.ThrowsExactly<FormatException>(() => ExtensionSchema.Decode(data, "linux-x64"));
    }

    /// <summary>
    /// Rejects every truncated framing header before interpreting its fields.
    /// </summary>
    [TestMethod]
    public void RejectsShortHeaders()
    {
        byte[] payload = Payload(Document);
        for (int length = 0; length < 12; length++)
        {
            byte[] truncated = payload[..length];
            Assert.ThrowsExactly<FormatException>(() => ExtensionSchema.Decode(truncated, "linux-x64"));
        }
    }

    private static byte[] Payload(string json, int padding = 0)
    {
        byte[] content = Encoding.UTF8.GetBytes(json);
        byte[] payload = new byte[12 + content.Length + padding];
        "ANKUSSC\0"u8.CopyTo(payload);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8), (uint)content.Length);
        content.CopyTo(payload, 12);
        return payload;
    }
}
