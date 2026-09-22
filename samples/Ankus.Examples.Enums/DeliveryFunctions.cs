namespace Ankus.Examples.Enums;

/// <summary>
/// Demonstrates label conversion, enum arrays and typed SPI queries using the installation schema.
/// </summary>
public static class DeliveryFunctions
{
    /// <summary>
    /// Advances a delivery through its declared states.
    /// </summary>
    [PgFunction]
    public static DeliveryStatus AdvanceDelivery(DeliveryStatus status = DeliveryStatus.Pending) => status switch
    {
        DeliveryStatus.Pending => DeliveryStatus.InTransit,
        DeliveryStatus.InTransit or DeliveryStatus.Delivered => DeliveryStatus.Delivered,
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    /// <summary>
    /// Copies enum values through SPI while preserving SQL NULL and array dimensions.
    /// </summary>
    [PgFunction]
    public static PgArray<DeliveryStatus?>? DeliveryStatuses(PgArray<DeliveryStatus?>? values)
        => Spi.ExecuteScalar<PgArray<DeliveryStatus?>?>("SELECT $1", SpiParameter.Create(values));

    /// <summary>
    /// Resolves the enum type after installation, relocation or reinstallation.
    /// </summary>
    [PgFunction]
    public static uint DeliveryStatusOid() => PgEnums.GetTypeOid<DeliveryStatus>();
}
