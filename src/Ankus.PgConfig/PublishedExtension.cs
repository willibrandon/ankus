using System.Text.Json;

namespace Ankus.PgConfig;

/// <summary>
/// Describes the native library and SQL files produced by one extension publish.
/// </summary>
public sealed class PublishedExtension
{
    private static readonly StringComparer s_fileComparer = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

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
        : this(postgresMajor, runtimeIdentifier, library, control, sql, [])
    {
    }

    /// <summary>
    /// Creates a manifest with an owned, ordered collection of extension upgrade scripts.
    /// </summary>
    /// <param name="postgresMajor">The PostgreSQL header major used to compile the extension.</param>
    /// <param name="runtimeIdentifier">The Native AOT runtime identifier.</param>
    /// <param name="library">The native library filename.</param>
    /// <param name="control">The control filename under extension/.</param>
    /// <param name="sql">The installation SQL filename under extension/.</param>
    /// <param name="upgradeScripts">Upgrade filenames in extension--old--new.sql form.</param>
    public PublishedExtension(int postgresMajor, string runtimeIdentifier, string library, string control, string sql,
        IReadOnlyList<string> upgradeScripts)
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

        ArgumentNullException.ThrowIfNull(upgradeScripts);
        string[] upgrades = [.. upgradeScripts];
        var names = new HashSet<string>(s_fileComparer) { control, sql };
        string prefix = control[..^".control".Length] + "--";
        foreach (string script in upgrades)
        {
            ValidateFileName(script);
            if (!script.StartsWith(prefix, StringComparison.Ordinal) || !script.EndsWith(".sql", StringComparison.Ordinal))
            {
                throw new ArgumentException("Upgrade scripts must belong to the extension and end in .sql.", nameof(upgradeScripts));
            }

            string[] versions = script[prefix.Length..^4].Split("--", StringSplitOptions.None);
            if (versions.Length != 2 || versions.Any(static version => version.Length == 0 ||
                    version[0] == '-' || version[^1] == '-') || !names.Add(script))
            {
                throw new ArgumentException("Upgrade scripts require unique filenames with two nonempty PostgreSQL versions.", nameof(upgradeScripts));
            }
        }

        Array.Sort(upgrades, StringComparer.Ordinal);
        UpgradeScripts = Array.AsReadOnly(upgrades);
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
    /// Gets the ordered upgrade filenames within the extension directory.
    /// </summary>
    public IReadOnlyList<string> UpgradeScripts { get; }

    /// <summary>
    /// Reads a published manifest without reflection-based deserialization.
    /// </summary>
    /// <param name="directory">The publish directory.</param>
    /// <returns>The validated manifest.</returns>
    public static PublishedExtension Read(string directory)
        => ReadFile(Path.Combine(directory, FileName));

    private static PublishedExtension ReadFile(string path)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement root = document.RootElement;
            var properties = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in root.EnumerateObject())
            {
                if (!properties.Add(property.Name))
                {
                    throw new FormatException("Duplicate extension manifest property.");
                }
            }

            int format = root.GetProperty("formatVersion").GetInt32();
            if (format is not (1 or 2))
            {
                throw new FormatException("Unsupported Ankus extension manifest version.");
            }

            if (format == 1 && root.TryGetProperty("upgradeScripts", out _))
            {
                throw new FormatException("Upgrade scripts require extension manifest version 2.");
            }

            string[] upgrades = format == 2
                ? [.. root.GetProperty("upgradeScripts").EnumerateArray().Select(static item => item.GetString()!)] : [];
            return new PublishedExtension(root.GetProperty("postgresMajor").GetInt32(),
                root.GetProperty("runtimeIdentifier").GetString()!, root.GetProperty("library").GetString()!,
                root.GetProperty("control").GetString()!, root.GetProperty("sql").GetString()!, upgrades);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or
            ArgumentException or OverflowException)
        {
            throw new FormatException("Invalid Ankus extension manifest.", error);
        }
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
        writer.WriteNumber("formatVersion", UpgradeScripts.Count == 0 ? 1 : 2);
        writer.WriteNumber("postgresMajor", PostgresMajor);
        writer.WriteString("runtimeIdentifier", RuntimeIdentifier);
        writer.WriteString("library", Library);
        writer.WriteString("control", Control);
        writer.WriteString("sql", Sql);
        if (UpgradeScripts.Count > 0)
        {
            writer.WriteStartArray("upgradeScripts");
            foreach (string script in UpgradeScripts)
            {
                writer.WriteStringValue(script);
            }

            writer.WriteEndArray();
        }

        writer.WriteEndObject();
    }

    /// <summary>
    /// Invalidates a previous publication while retaining its file inventory for the next successful publish.
    /// </summary>
    /// <param name="directory">The publish directory.</param>
    public static void Invalidate(string directory)
    {
        string manifest = Path.Combine(directory, FileName);
        if (File.Exists(manifest))
        {
            File.Move(manifest, Path.Combine(directory, "ankus.extension.previous.json"), overwrite: true);
        }
    }

    /// <summary>
    /// Validates the complete payload, removes obsolete owned SQL, and makes the publication installable.
    /// </summary>
    /// <param name="directory">The directory containing the native library and extension files.</param>
    public void CompletePublish(string directory)
    {
        Invalidate(directory);
        string[] files = [Control, Sql, .. UpgradeScripts];
        foreach (string path in files.Select(file => Path.Combine(directory, "extension", file))
                     .Prepend(Path.Combine(directory, Library)))
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("The published extension is incomplete.", path);
            }
        }

        string previousPath = Path.Combine(directory, "ankus.extension.previous.json");
        if (File.Exists(previousPath))
        {
            PublishedExtension previous = ReadFile(previousPath);
            string[] previousFiles = [previous.Control, previous.Sql, .. previous.UpgradeScripts];
            foreach (string file in previousFiles.Except(files, s_fileComparer))
            {
                File.Delete(Path.Combine(directory, "extension", file));
            }
        }

        Write(directory);
        File.Delete(previousPath);
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
