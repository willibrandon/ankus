using System.Globalization;

namespace Ankus.TestExtension;

/// <summary>
/// Exercises PostgreSQL transaction IDs through generated and guarded Native AOT boundaries.
/// </summary>
public static class TransactionIdFunctions
{
    /// <summary>
    /// Exchanges a transaction ID through the requested ownership path.
    /// </summary>
    /// <param name="value">The transaction ID or SQL NULL.</param>
    /// <param name="mode">The direct, SPI, plan, session, cursor, retained-plan, or edited-row path.</param>
    /// <returns>The detached transaction ID.</returns>
    [PgFunction]
    public static PgTransactionId? ExchangeTransactionId(PgTransactionId? value, int mode)
        => ArrayFunctions.Exchange(value, mode);

    /// <summary>
    /// Expands a transaction ID using the epoch nearest PostgreSQL's next transaction ID.
    /// </summary>
    /// <param name="value">The transaction ID.</param>
    /// <returns>The unsigned 64-bit transaction ID text.</returns>
    [PgFunction]
    public static string TransactionIdToFull(PgTransactionId value)
        => value.ToFullTransactionId().ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Returns PostgreSQL's invalid transaction ID to verify its SQL NULL representation.
    /// </summary>
    /// <returns>The invalid transaction ID.</returns>
    [PgFunction]
    public static PgTransactionId InvalidTransactionId() => PgTransactionId.Invalid;
}
