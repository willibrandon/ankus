namespace Ankus.Build;

/// <summary>
/// Creates PostgreSQL extension control and versioned installation files from generated SQL.
/// </summary>
internal static class ExtensionPackage
{
    /// <summary>
    /// Creates primary artifacts with the same validated author settings for metadata queries and native publication.
    /// </summary>
    /// <param name="name">The extension name.</param>
    /// <param name="version">The extension version.</param>
    /// <param name="library">The native library filename.</param>
    /// <param name="sql">The generated installation SQL.</param>
    /// <param name="relocatable">Whether generated objects permit relocation.</param>
    /// <param name="authored">The author control text, or null when no author control was selected.</param>
    /// <param name="postgresMajor">The selected PostgreSQL major.</param>
    /// <param name="versionedLibrary">Whether the library filename carries the version and SQL names it directly.</param>
    /// <returns>The primary control and installation SQL artifacts.</returns>
    internal static IReadOnlyDictionary<string, string> Create(string name, string version, string library,
        string sql, bool relocatable, string? authored, int postgresMajor, bool versionedLibrary = false)
    {
        var package = new Dictionary<string, string>(Create(name, version, library, sql, relocatable, versionedLibrary),
            StringComparer.Ordinal);
        if (authored is not null)
        {
            (package[name + ".control"], _) = ExtensionControlSettings.Merge(package[name + ".control"], authored, postgresMajor);
        }

        return package;
    }

    /// <summary>
    /// Creates installable artifacts that resolve the native library through PostgreSQL's dynamic library search path.
    /// </summary>
    /// <param name="name">The SQL extension name and control-file basename.</param>
    /// <param name="version">The PostgreSQL extension version.</param>
    /// <param name="library">The native library filename, including its platform-specific suffix.</param>
    /// <param name="sql">The generated installation SQL containing MODULE_PATHNAME placeholders.</param>
    /// <param name="relocatable">Whether all generated objects can move with the extension's installation schema.</param>
    /// <param name="versionedLibrary">
    /// Whether to omit <c>module_pathname</c>, as cargo-pgrx's versioned shared-object mode does. Each version's SQL
    /// then names its own library, so the primary control file cannot redirect older versions to the current library.
    /// </param>
    /// <returns>The filenames and UTF-8 text to publish in the extension directory.</returns>
    internal static IReadOnlyDictionary<string, string> Create(string name, string version, string library, string sql, bool relocatable,
        bool versionedLibrary = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(library);
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        if (name.Length > 63 || !name.All(static character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.') ||
            name.Contains("--", StringComparison.Ordinal) || name[0] == '-' || name[^1] == '-')
        {
            throw new ArgumentException("The extension name must be a 1-63 character filename-safe PostgreSQL name.", nameof(name));
        }

        if (!version.All(static character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '+' or '-') ||
            version.Contains("--", StringComparison.Ordinal) || version[0] == '-' || version[^1] == '-')
        {
            throw new ArgumentException("The extension version must be a filename-safe PostgreSQL version name.", nameof(version));
        }

        if (library is "." or ".." ||
            !library.All(static character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '+' or '-'))
        {
            throw new ArgumentException("The native library must have a filename without directory components.", nameof(library));
        }

        string module = versionedLibrary ? string.Empty : $"module_pathname = '{library}'\n";
        string control = $"default_version = '{version}'\n{module}encoding = 'UTF8'\nrelocatable = {(relocatable ? "true" : "false")}\n";
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [name + ".control"] = control,
            [name + "--" + version + ".sql"] = sql,
        };
    }
}
