namespace Ankus.Examples.Schemas;

/// <summary>
/// Places the nested type in PostgreSQL's existing pg_catalog schema, which every search path includes.
/// </summary>
/// <remarks>
/// Creating objects in pg_catalog requires a superuser. The extension adds members to the schema without owning it.
/// </remarks>
[PgSchema("pg_catalog", Create = false)]
public static class PgCatalogSchema
{
    /// <summary>
    /// Stores text in pg_catalog, using generated CBOR storage and JSON text.
    /// </summary>
    /// <param name="Value">The stored text.</param>
    [PgType(Name = "mypgcatalogtype")]
    public sealed record MyPgCatalogType(string Value);
}
