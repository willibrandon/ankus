namespace Ankus.TestExtension;

/// <summary>
/// Exercises identical serialized payloads with different PostgreSQL datum alignments.
/// </summary>
[PgSchema("aligned_values")]
public static class AlignedTypeFunctions
{
    /// <summary>
    /// Uses the default datum alignment.
    /// </summary>
    /// <param name="Text">The complete text payload.</param>
    /// <param name="Number">The exact signed value.</param>
    [PgType(BinaryProtocol = true)]
    public sealed record Ordinary(string Text, long Number);

    /// <summary>
    /// Uses eight-byte datum alignment with the same serialization contract.
    /// </summary>
    /// <param name="Text">The complete text payload.</param>
    /// <param name="Number">The exact signed value.</param>
    [PgType(Alignment = PgTypeAlignment.EightBytes, BinaryProtocol = true)]
    public sealed record Wide(string Text, long Number);

    /// <summary>
    /// Returns a value read from an ordinary-aligned heap attribute.
    /// </summary>
    [PgFunction]
    public static Ordinary EchoOrdinary(Ordinary value) => value;

    /// <summary>
    /// Returns a value read from an eight-byte-aligned heap attribute.
    /// </summary>
    [PgFunction]
    public static Wide EchoWide(Wide value) => value;

    /// <summary>
    /// Returns an array of ordinary-aligned elements without changing shape or nulls.
    /// </summary>
    [PgFunction]
    public static PgArray<Ordinary?> EchoOrdinaryArray(PgArray<Ordinary?> values) => values;

    /// <summary>
    /// Returns an array of eight-byte-aligned elements without changing shape or nulls.
    /// </summary>
    [PgFunction]
    public static PgArray<Wide?> EchoWideArray(PgArray<Wide?> values) => values;
}
