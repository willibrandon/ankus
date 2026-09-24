using System.Buffers;

namespace Ankus;

/// <summary>
/// Defines the text and binary storage representations of a generated PostgreSQL base type.
/// </summary>
/// <remarks>
/// The generated mapping constructs one codec on first use inside the managed error boundary. Implementations must not retain borrowed
/// input buffers or PostgreSQL call state. Storage bytes exclude PostgreSQL's variable-length header.
/// Preserve the storage format across extension upgrades or provide an explicit data migration.
/// </remarks>
/// <typeparam name="T">The managed type carrying PgType.</typeparam>
public abstract class PgTypeCodec<T>
{
    /// <summary>
    /// Parses the SQL text representation into an independent managed value.
    /// </summary>
    /// <param name="text">The input converted from the database encoding.</param>
    /// <returns>The parsed value.</returns>
    public abstract T Parse(string text);

    /// <summary>
    /// Formats a managed value for SQL text output.
    /// </summary>
    /// <param name="value">The present managed value.</param>
    /// <returns>The text representation without a zero terminator.</returns>
    public abstract string Format(T value);

    /// <summary>
    /// Reads an independent managed value from the complete stored payload.
    /// </summary>
    /// <param name="payload">The borrowed payload, valid only during this call.</param>
    /// <returns>The decoded value.</returns>
    public abstract T Read(ReadOnlySpan<byte> payload);

    /// <summary>
    /// Writes the complete stored payload for a present managed value.
    /// </summary>
    /// <param name="value">The value to store.</param>
    /// <param name="destination">The output buffer; do not retain it after this call.</param>
    public abstract void Write(T value, IBufferWriter<byte> destination);
}
