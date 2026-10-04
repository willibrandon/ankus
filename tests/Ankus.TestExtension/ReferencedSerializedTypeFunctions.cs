using Ankus.SerializationContracts;

namespace Ankus.TestExtension;

/// <summary>
/// Exercises exact serialization attributes imported from a separate compiled assembly.
/// </summary>
[PgSchema("serialized_values")]
public static class ReferencedSerializedTypeFunctions
{
    /// <summary>
    /// Persists every imported zero-containing serialization attribute in one owned graph.
    /// </summary>
    /// <param name="Member">The inherited renamed member.</param>
    /// <param name="Mode">The renamed enum value.</param>
    /// <param name="Variant">The tagged variant with renamed discriminator property and value.</param>
    [PgType(Name = "imported_contract", BinaryProtocol = true)]
    public sealed record Envelope(Member Member, Mode Mode, Variant Variant);

    /// <summary>
    /// Exchanges imported contracts and SQL NULL through direct and SPI ownership paths.
    /// </summary>
    [PgFunction]
    public static Envelope? ImportedContract(Envelope? value, int mode) => ArrayFunctions.Exchange(value, mode);
}
