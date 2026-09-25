namespace Ankus;

/// <summary>
/// Declares the SQL block that supplies a catalog type used by raw or composite function bindings.
/// </summary>
/// <remarks>
/// Adds installation dependencies without parsing SQL or registering managed value conversions.
/// The block may declare a shell type when its completion is ordered separately.
/// </remarks>
/// <param name="sqlId">The dependency identifier of a PgSql or PgSqlFile block.</param>
/// <param name="name">The exact, unquoted catalog type name, without a schema prefix.</param>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class PgSqlTypeProviderAttribute(string sqlId, string name) : Attribute
{
    /// <summary>
    /// Gets the dependency identifier of the supplying SQL block.
    /// </summary>
    public string SqlId { get; } = sqlId;

    /// <summary>
    /// Gets the exact catalog name of the supplied type, or array element type.
    /// </summary>
    public string Name { get; } = name;

    /// <summary>
    /// Gets or sets the fixed type schema. Null matches only bindings that omit their schema.
    /// A fixed schema prevents extension schema relocation.
    /// </summary>
    public string? Schema { get; set; }
}
