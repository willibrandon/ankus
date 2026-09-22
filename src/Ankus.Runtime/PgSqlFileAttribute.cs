namespace Ankus;

/// <summary>
/// Includes a compiler AdditionalFiles SQL input in the installation script with explicit dependency ordering.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class PgSqlFileAttribute : Attribute
{
    /// <summary>
    /// Declares a named SQL file without reading files from extension runtime code.
    /// </summary>
    /// <param name="name">The unique, case-sensitive dependency identifier.</param>
    /// <param name="path">The project-relative or absolute path of an AdditionalFiles input.</param>
    public PgSqlFileAttribute(string name, string path)
    {
        Name = name;
        Path = path;
    }

    /// <summary>
    /// Gets the file block's dependency identifier.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the path resolved relative to the extension project directory.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Gets or sets identifiers of declarations that must run before this file.
    /// </summary>
    public string[] Requires { get; set; } = [];

    /// <summary>
    /// Gets or sets identifiers of declarations that must run after this file.
    /// </summary>
    public string[] Before { get; set; } = [];

    /// <summary>
    /// Gets or sets whether this file runs first, last, or according to its explicit dependencies.
    /// </summary>
    public PgSqlOrder Order { get; set; }

    /// <summary>
    /// Gets or sets whether every object and reference in this file permits extension schema relocation.
    /// The default is false.
    /// </summary>
    public bool Relocatable { get; set; }
}
