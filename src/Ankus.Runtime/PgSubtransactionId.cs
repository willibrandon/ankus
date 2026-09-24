using System.Globalization;

namespace Ankus;

/// <summary>
/// Represents a PostgreSQL subtransaction ID used by subtransaction callbacks.
/// </summary>
/// <param name="Value">The raw subtransaction ID.</param>
public readonly record struct PgSubtransactionId(uint Value)
{
    /// <summary>
    /// Gets PostgreSQL's invalid subtransaction ID.
    /// </summary>
    public static PgSubtransactionId Invalid => new(0);

    /// <summary>
    /// Gets the top-level transaction's subtransaction ID.
    /// </summary>
    public static PgSubtransactionId Top => new(1);

    /// <summary>
    /// Gets whether the subtransaction ID is valid.
    /// </summary>
    public bool IsValid => Value != Invalid.Value;

    /// <summary>
    /// Formats the raw subtransaction ID as an unsigned decimal number.
    /// </summary>
    /// <returns>The PostgreSQL subtransaction ID text.</returns>
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}
