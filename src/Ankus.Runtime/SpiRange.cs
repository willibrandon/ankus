namespace Ankus;

/// <summary>
/// Statically maps supported range subtypes and performs checked .NET bound conversions for SPI.
/// </summary>
internal static class SpiRange
{
    /// <summary>
    /// Resolves the built-in range associated with a supported scalar subtype.
    /// </summary>
    /// <param name="subtype">The scalar OID.</param>
    /// <returns>The range OID.</returns>
    internal static uint RangeOid(uint subtype) => subtype switch
    {
        23 => 3904, 20 => 3926, 1700 => 3906, 1082 => 3912, 1114 => 3908, 1184 => 3910,
        _ => throw new NotSupportedException($"PostgreSQL type OID {subtype} has no supported built-in range."),
    };

    /// <summary>
    /// Resolves a statically supported generic range type without reflection over its members.
    /// </summary>
    /// <param name="type">The declared managed type.</param>
    /// <returns>The range OID, or zero when unsupported.</returns>
    internal static uint GetOid(Type type) => PgDatumRegistry.Find(type) is { RangeBound: not null } mapping ? mapping.GetOid() : BuiltInOid(type);

    /// <summary>
    /// Validates detached range construction without resolving a registered catalog identity.
    /// </summary>
    internal static void Require(Type type)
    {
        if (BuiltInOid(type) == 0 && PgDatumRegistry.Find(type) is not { RangeBound: not null })
        {
            throw new NotSupportedException($"Managed range type '{type}' has no supported bound mapping.");
        }
    }

    /// <summary>
    /// Resolves only built-in range identities, without consulting backend-dependent registrations.
    /// </summary>
    private static uint BuiltInOid(Type type) =>
        type == typeof(PgRange<int>) ? 3904u : type == typeof(PgRange<long>) ? 3926u :
        type == typeof(PgRange<PgNumeric>) || type == typeof(PgRange<decimal>) ? 3906u :
        type == typeof(PgRange<PgDate>) || type == typeof(PgRange<DateOnly>) ? 3912u :
        type == typeof(PgRange<PgTimestamp>) || type == typeof(PgRange<DateTime>) ? 3908u :
        type == typeof(PgRange<PgTimestampTz>) || type == typeof(PgRange<DateTimeOffset>) ? 3910u : 0;

    /// <summary>
    /// Converts range bounds into a statically known managed representation, preserving inclusion and empty state.
    /// </summary>
    /// <param name="range">The owned source range.</param>
    /// <param name="type">The requested managed type.</param>
    /// <returns>The converted range.</returns>
    internal static object Convert(IPgRange range, Type type)
    {
        if (GetOid(type) != range.TypeOid)
        {
            throw new InvalidCastException($"Range OID {range.TypeOid} cannot be read as '{type}'.");
        }

        return type == typeof(PgRange<int>) ? Convert<int>(range) :
            type == typeof(PgRange<long>) ? Convert<long>(range) :
            type == typeof(PgRange<PgNumeric>) ? Convert<PgNumeric>(range) :
            type == typeof(PgRange<decimal>) ? Convert<decimal>(range) :
            type == typeof(PgRange<PgDate>) ? Convert<PgDate>(range) :
            type == typeof(PgRange<DateOnly>) ? Convert<DateOnly>(range) :
            type == typeof(PgRange<PgTimestamp>) ? Convert<PgTimestamp>(range) :
            type == typeof(PgRange<DateTime>) ? Convert<DateTime>(range) :
            type == typeof(PgRange<PgTimestampTz>) ? Convert<PgTimestampTz>(range) :
            type == typeof(PgRange<DateTimeOffset>) ? Convert<DateTimeOffset>(range) :
            throw new InvalidCastException($"Unsupported range type '{type}'.");
    }

    private static PgRange<T> Convert<T>(IPgRange range) where T : struct => range.IsEmpty
        ? new PgRange<T>() : new PgRange<T>(range.LowerValue is null ? null : SpiRow.Convert<T>(range.LowerValue),
            range.UpperValue is null ? null : SpiRow.Convert<T>(range.UpperValue), range.LowerInclusive, range.UpperInclusive);
}
