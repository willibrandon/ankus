[assembly: PgSql("create-or-replace-first", """
    CREATE SCHEMA replace_values;
    CREATE FUNCTION replace_values.create_or_replace_method() RETURNS boolean LANGUAGE sql AS 'SELECT false';
    """)]

namespace Ankus.TestExtension;

/// <summary>
/// Replaces a definition that installation SQL created earlier in the same install, as pgrx's
/// <c>create_or_replace</c> option does.
/// </summary>
[PgSchema("replace_values", Create = false)]
public static class CreateOrReplaceFunctions
{
    /// <summary>
    /// Replaces the SQL function that returns false.
    /// </summary>
    /// <returns>True.</returns>
    [PgFunction(CreateOrReplace = true, Requires = ["create-or-replace-first"])]
    public static bool CreateOrReplaceMethod() => true;

    /// <summary>
    /// Creates a function that did not exist, using the same replacement form.
    /// </summary>
    /// <returns>Forty-two.</returns>
    [PgFunction(CreateOrReplace = true, Requires = ["create-or-replace-first"])]
    public static int CreateOrReplaceMethodOther() => 42;
}
