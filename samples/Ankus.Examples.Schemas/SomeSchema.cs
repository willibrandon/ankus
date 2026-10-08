namespace Ankus.Examples.Schemas;

/// <summary>
/// Creates the extension-owned some_schema schema and places the nested type and function in it.
/// </summary>
[PgSchema("some_schema")]
public static class SomeSchema
{
    /// <summary>
    /// Returns a greeting that callers reach through the some_schema qualifier.
    /// </summary>
    /// <returns>A fixed greeting.</returns>
    [PgFunction]
    public static string HelloSomeSchema() => "Hello from some_schema";

    /// <summary>
    /// Stores text in some_schema, using generated CBOR storage and JSON text.
    /// </summary>
    /// <param name="Value">The stored text.</param>
    [PgType(Name = "mysomeschematype")]
    public sealed record MySomeSchemaType(string Value);
}
