using System.Text.Json;

namespace Ankus.PgConfig;

/// <summary>
/// Describes the native library and SQL files produced by one extension publish.
/// </summary>
public sealed class PublishedExtension
{
    /// <summary>
    /// Gets the manifest filename placed alongside a published native library.
    /// </summary>
    public const string FileName = "ankus.extension.json";

    /// <summary>
    /// Creates a manifest with filename-only artifact names.
    /// </summary>
    /// <param name="postgresMajor">The PostgreSQL header major used to compile the extension.</param>
    /// <param name="runtimeIdentifier">The Native AOT runtime identifier.</param>
    /// <param name="library">The native library filename.</param>
    /// <param name="control">The control filename under extension/.</param>
    /// <param name="sql">The versioned SQL filename under extension/.</param>
    public PublishedExtension(int postgresMajor, string runtimeIdentifier, string library, string control, string sql)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(postgresMajor, 13);
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeIdentifier);
        ValidateFileName(library);
        ValidateFileName(control);
        ValidateFileName(sql);
        if (!control.EndsWith(".control", StringComparison.Ordinal) || !sql.EndsWith(".sql", StringComparison.Ordinal))
        {
            throw new ArgumentException("Expected a .control file and a versioned .sql file.");
        }

        PostgresMajor = postgresMajor;
        RuntimeIdentifier = runtimeIdentifier;
        Library = library;
        Control = control;
        Sql = sql;
    }

    /// <summary>
    /// Gets the PostgreSQL major whose headers were used to compile this library.
    /// </summary>
    public int PostgresMajor { get; }

    /// <summary>
    /// Gets the runtime identifier used by Native AOT.
    /// </summary>
    public string RuntimeIdentifier { get; }

    /// <summary>
    /// Gets the native library filename.
    /// </summary>
    public string Library { get; }

    /// <summary>
    /// Gets the control filename within the extension directory.
    /// </summary>
    public string Control { get; }

    /// <summary>
    /// Gets the versioned SQL filename within the extension directory.
    /// </summary>
    public string Sql { get; }

    /// <summary>
    /// Reads a published manifest without reflection-based deserialization.
    /// </summary>
    /// <param name="directory">The publish directory.</param>
    /// <returns>The validated manifest.</returns>
    public static PublishedExtension Read(string directory)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, FileName)));
        JsonElement root = document.RootElement;
        if (root.GetProperty("formatVersion").GetInt32() != 1)
        {
            throw new FormatException("Unsupported Ankus extension manifest version.");
        }

        return new PublishedExtension(root.GetProperty("postgresMajor").GetInt32(),
            root.GetProperty("runtimeIdentifier").GetString()!, root.GetProperty("library").GetString()!,
            root.GetProperty("control").GetString()!, root.GetProperty("sql").GetString()!);
    }

    /// <summary>
    /// Writes the manifest to an existing artifact directory.
    /// </summary>
    /// <param name="directory">The artifact directory.</param>
    public void Write(string directory)
    {
        using FileStream stream = File.Create(Path.Combine(directory, FileName));
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        writer.WriteNumber("formatVersion", 1);
        writer.WriteNumber("postgresMajor", PostgresMajor);
        writer.WriteString("runtimeIdentifier", RuntimeIdentifier);
        writer.WriteString("library", Library);
        writer.WriteString("control", Control);
        writer.WriteString("sql", Sql);
        writer.WriteEndObject();
    }

    private static void ValidateFileName(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value is "." or ".." || !value.All(static c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' or '+'))
        {
            throw new ArgumentException("Extension artifacts must have filenames without directory components.", nameof(value));
        }
    }
}
