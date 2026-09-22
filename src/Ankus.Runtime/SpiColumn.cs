namespace Ankus;

/// <summary>
/// Describes a column returned by a PostgreSQL SPI query.
/// </summary>
public sealed class SpiColumn
{
    /// <summary>
    /// Creates column metadata copied from a PostgreSQL tuple descriptor.
    /// </summary>
    /// <param name="name">The column name.</param>
    /// <param name="typeOid">The declared PostgreSQL type OID, including a domain's own OID.</param>
    internal SpiColumn(string name, uint typeOid)
    {
        Name = name;
        TypeOid = typeOid;
    }

    /// <summary>
    /// Gets the column name, preserving PostgreSQL identifier casing.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the declared PostgreSQL type OID.
    /// </summary>
    public uint TypeOid { get; }
}
