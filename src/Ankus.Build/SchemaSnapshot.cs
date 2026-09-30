using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ankus.PgConfig;

namespace Ankus.Build;

/// <summary>
/// Retains generated SQL and its matching graph independently of later managed function bodies.
/// </summary>
/// <param name="name">The extension name.</param>
/// <param name="version">The extension version.</param>
/// <param name="library">The native library filename.</param>
/// <param name="major">The PostgreSQL major.</param>
/// <param name="runtimeIdentifier">The native target.</param>
/// <param name="nativeContract">The digest of native wrappers and exports.</param>
/// <param name="relocatable">Whether generated declarations permit relocation.</param>
/// <param name="sql">The exact installation SQL.</param>
/// <param name="graph">The matching encoded graph, or null for legacy generators.</param>
internal sealed class SchemaSnapshot(string name, string version, string library, int major, string runtimeIdentifier,
    string nativeContract, bool relocatable, string sql, string? graph)
{
    private const int MaximumBytes = 128 * 1024 * 1024;

    /// <summary>
    /// Gets whether the retained declarations permit relocation.
    /// </summary>
    internal bool Relocatable { get; } = relocatable;

    /// <summary>
    /// Gets the retained installation SQL.
    /// </summary>
    internal string Sql { get; } = sql;

    /// <summary>
    /// Gets the matching compiler graph.
    /// </summary>
    internal string? Graph { get; } = graph;

    /// <summary>
    /// Selects newly generated declarations or a prior compatible publication without executing extension code.
    /// </summary>
    /// <param name="path">The prior snapshot, or null for normal generation.</param>
    /// <param name="manifest">The current compiled native contract.</param>
    /// <param name="name">The extension name.</param>
    /// <param name="version">The extension version.</param>
    /// <param name="library">The native filename.</param>
    /// <param name="major">The PostgreSQL major.</param>
    /// <param name="runtimeIdentifier">The native target.</param>
    /// <returns>The schema used for both installation files and embedded metadata.</returns>
    internal static SchemaSnapshot Select(string? path, ExtensionManifest manifest, string name, string version,
        string library, int major, string runtimeIdentifier)
    {
        string contract = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(manifest.NativeSource + "\0" + manifest.Exports)));
        if (path is null)
        {
            return new(name, version, library, major, runtimeIdentifier, contract, manifest.Relocatable, manifest.Sql, manifest.SqlGraph);
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("No saved schema exists for this build. Run without schema reuse first.", path);
        }

        using FileStream stream = File.OpenRead(path);
        if (stream.Length is 0 or > MaximumBytes)
        {
            throw new FormatException("The saved schema has an invalid size. Regenerate the schema.");
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(stream);
            JsonElement root = document.RootElement;
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in root.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw new FormatException("The saved schema contains duplicate properties. Regenerate the schema.");
                }
            }

            if (root.GetProperty("formatVersion").GetInt32() != 1 || root.GetProperty("name").GetString() != name ||
                root.GetProperty("version").GetString() != version || root.GetProperty("library").GetString() != library ||
                root.GetProperty("postgresMajor").GetInt32() != major || root.GetProperty("runtimeIdentifier").GetString() != runtimeIdentifier)
            {
                throw new FormatException("The saved schema belongs to a different extension or build target. Regenerate the schema.");
            }

            if (root.GetProperty("nativeContract").GetString() != contract)
            {
                throw new FormatException("Native declarations changed since the saved schema. Run without schema reuse to regenerate it.");
            }

            string retainedSql = root.GetProperty("sql").GetString() ?? throw new FormatException("The saved schema has no SQL.");
            string? retainedGraph = root.GetProperty("graph").GetString();
            bool retainedRelocatable = root.GetProperty("relocatable").GetBoolean();
            if (retainedSql.Length == 0 || retainedSql.Contains('\0') ||
                retainedGraph is not null && ExtensionSchemaGraph.Parse(retainedGraph).Sql != retainedSql)
            {
                throw new FormatException("The saved SQL and graph are invalid or disagree. Regenerate the schema.");
            }

            _ = new UTF8Encoding(false, true).GetByteCount(retainedSql);
            return new(name, version, library, major, runtimeIdentifier, contract, retainedRelocatable, retainedSql, retainedGraph);
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or OverflowException or EncoderFallbackException)
        {
            throw new FormatException("Invalid saved schema metadata. Regenerate the schema.", error);
        }
    }

    /// <summary>
    /// Writes a prepared snapshot; successful publication commits it to the retained location separately.
    /// </summary>
    /// <param name="path">The owned intermediate file.</param>
    internal void Write(string path)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("formatVersion", 1);
            writer.WriteString("name", name);
            writer.WriteString("version", version);
            writer.WriteString("library", library);
            writer.WriteNumber("postgresMajor", major);
            writer.WriteString("runtimeIdentifier", runtimeIdentifier);
            writer.WriteString("nativeContract", nativeContract);
            writer.WriteBoolean("relocatable", Relocatable);
            writer.WriteString("sql", Sql);
            writer.WriteString("graph", Graph);
            writer.WriteEndObject();
        }

        File.WriteAllBytes(path, stream.ToArray());
    }

    /// <summary>
    /// Replaces the retained snapshot only after a complete publication, preserving the prior file on failure.
    /// </summary>
    /// <param name="prepared">The prepared schema file.</param>
    /// <param name="destination">The snapshot selected by PostgreSQL major and test-publication mode.</param>
    internal static void Commit(string prepared, string destination)
    {
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.Copy(prepared, temporary);
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}
