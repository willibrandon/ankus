namespace Ankus.Examples.Schemas;

/// <summary>
/// Declares functions without a fixed schema, so PostgreSQL creates them in the schema selected by CREATE EXTENSION.
/// </summary>
public static class DefaultSchemaFunctions
{
    /// <summary>
    /// Identifies the installation schema's greeting.
    /// </summary>
    /// <returns>A fixed greeting.</returns>
    [PgFunction]
    public static string HelloDefaultSchema() => "Hello from the schema where you installed this extension";

    /// <summary>
    /// Returns an array of the installation schema's custom type with that schema as the function's search path.
    /// </summary>
    /// <returns>A one-element array.</returns>
    [PgFunction(Name = "return_vec_of_customtype", SearchPath = [PgSearchPath.ExtensionSchema])]
    public static MyType[] ReturnVecOfCustomType() => [new("test")];
}
