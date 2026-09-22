namespace Ankus.TestExtension;

/// <summary>
/// Places functions in an existing schema without making the schema an extension member.
/// </summary>
[PgSchema("public", Create = false)]
public static class DeclarationExistingSchema
{
    /// <summary>
    /// Provides an independently named probe in the shared public schema.
    /// </summary>
    [PgFunction]
    public static int DeclarationExisting() => 52;
}
