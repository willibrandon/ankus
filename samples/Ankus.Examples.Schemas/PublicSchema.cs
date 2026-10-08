namespace Ankus.Examples.Schemas;

/// <summary>
/// Places the nested function in the existing public schema, independently of the installation schema.
/// </summary>
[PgSchema("public", Create = false)]
public static class PublicSchema
{
    /// <summary>
    /// Returns a greeting from public.
    /// </summary>
    /// <returns>A fixed greeting.</returns>
    [PgFunction]
    public static string HelloPublic() => "Hello from the public schema";
}
