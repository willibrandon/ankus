namespace Ankus.Examples.Enums;

/// <summary>
/// Stores exact PostgreSQL labels with declaration ordering independent of the C# numeric values.
/// </summary>
[PgEnum]
public enum DeliveryStatus
{
    /// <summary>
    /// The package is awaiting dispatch.
    /// </summary>
    [PgEnumLabel("pending")]
    Pending = 10,

    /// <summary>
    /// The package is on its way.
    /// </summary>
    [PgEnumLabel("in transit")]
    InTransit = 30,

    /// <summary>
    /// The package has arrived.
    /// </summary>
    [PgEnumLabel("delivered")]
    Delivered = 20,
}
