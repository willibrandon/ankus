namespace Ankus;

/// <summary>
/// Supplies a stable, equality-compatible hash for PostgreSQL hash indexes and joins.
/// </summary>
/// <remarks>
/// Equal values must return equal hashes, regardless of their storage representation. Results must remain
/// identical across backend processes, platforms and extension versions. Ordinary object, string, record
/// and HashCode hashes do not provide this stability. PgHash can hash an explicitly normalized value key.
/// Implementations must be immutable and parallel safe. A changed contract requires rebuilding dependent indexes.
/// </remarks>
public interface IPgHashable
{
    /// <summary>
    /// Returns the stable 32-bit PostgreSQL hash of this value's equality key.
    /// </summary>
    /// <returns>The hash bit pattern, represented as a signed integer.</returns>
    int GetPostgresHashCode();
}
