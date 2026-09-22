namespace Ankus.Build;

/// <summary>
/// Creates PostgreSQL extension control and versioned installation files from generated SQL.
/// </summary>
internal static class ExtensionPackage
{
    /// <summary>
    /// Creates installable artifacts that resolve the native library through PostgreSQL's dynamic library search path.
    /// </summary>
    /// <param name="name">The SQL extension name and control-file basename.</param>
    /// <param name="version">The PostgreSQL extension version.</param>
    /// <param name="library">The native library filename, including its platform-specific suffix.</param>
    /// <param name="sql">The generated installation SQL containing MODULE_PATHNAME placeholders.</param>
    /// <param name="relocatable">Whether all generated objects can move with the extension's installation schema.</param>
    /// <returns>The filenames and UTF-8 text to publish in the extension directory.</returns>
    internal static IReadOnlyDictionary<string, string> Create(string name, string version, string library, string sql, bool relocatable)
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
            !library.All(static character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-'))
        {
            throw new ArgumentException("The native library must have a filename without directory components.", nameof(library));
        }

        string control = $"default_version = '{version}'\nmodule_pathname = '{library}'\nrelocatable = {(relocatable ? "true" : "false")}\n";
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [name + ".control"] = control,
            [name + "--" + version + ".sql"] = sql,
        };
    }
}
