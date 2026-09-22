namespace Ankus.TestExtension;

/// <summary>
/// Tests server encoding for enum type and schema identifiers as well as labels.
/// </summary>
[PgSchema("schéma")]
public static class EnumUnicodeFunctions
{
    /// <summary>
    /// A fixed-schema enum whose identifier and label require server transcoding.
    /// </summary>
    [PgEnum(Name = "état")]
    public enum State
    {
        /// <summary>
        /// A Unicode label representable in UTF-8 and LATIN1.
        /// </summary>
        [PgEnumLabel("café")]
        Ready,
    }

    /// <summary>
    /// Resolves the fixed type name through guarded catalog access.
    /// </summary>
    [PgFunction]
    public static uint UnicodeEnumOid() => PgEnums.GetTypeOid<State>();

    /// <summary>
    /// Exchanges an enum through SPI after decoding fixed-schema Unicode identifiers.
    /// </summary>
    [PgFunction]
    public static State UnicodeEnum(State value) => Spi.ExecuteScalar<State>("SELECT $1", SpiParameter.Create(value));
}
