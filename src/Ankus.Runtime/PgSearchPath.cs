namespace Ankus;

/// <summary>
/// Names PostgreSQL search-path entries whose values are resolved when an extension is installed.
/// </summary>
public static class PgSearchPath
{
    /// <summary>
    /// Refers to the extension's installation schema in <see cref="PgFunctionAttribute.SearchPath"/>.
    /// PostgreSQL substitutes this token during CREATE EXTENSION; the generated extension is non-relocatable.
    /// </summary>
    public const string ExtensionSchema = "@extschema@";
}
