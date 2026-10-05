namespace Ankus.TestExtension;

/// <summary>
/// Exercises callback failure during rollback owned by managed SPI and explicit subtransaction APIs.
/// </summary>
public static class CallbackRecoveryFunctions
{
    /// <summary>
    /// Deliberately catches a terminal managed exception so the native callback frame must preserve its intent.
    /// </summary>
    /// <param name="level">FATAL or PANIC represented by its logging enum value.</param>
    internal static void ReportCaughtTerminal(int level)
    {
        if (level != (int)PgLogLevel.Fatal && level != (int)PgLogLevel.Panic)
        {
            throw new ArgumentOutOfRangeException(nameof(level));
        }

        try
        {
            PgLog.Write((PgLogLevel)level, new PgDiagnostic("terminal cleanup café")
            {
                SqlState = "P7806",
                Detail = "terminal cleanup naïve",
                Hint = "restart after terminal cleanup déjà",
            });
        }
        catch (Exception exception) when (exception.Message == "terminal cleanup café")
        {
            // This catches user-visible managed unwinding, never the native terminal intent.
        }
    }

    /// <summary>
    /// Recovers the original error after native callback drain and performs further SQL in the same managed entry.
    /// </summary>
    /// <param name="spi">Whether SPI owns the failed subtransaction instead of the explicit managed API.</param>
    /// <param name="cleanupThrows">Whether the registered callback also throws during rollback.</param>
    /// <returns>The exact primary fields, callback state and successful later SPI value.</returns>
    [PgFunction]
    public static string[] CallbackSubtransactionRecovery(bool spi, bool cleanupThrows)
    {
        string[] primary = ["00000", "No error", string.Empty, string.Empty];
        try
        {
            if (spi)
            {
                string query = cleanupThrows ? "SELECT datatype.memory_callback_prepare(2, true); " :
                    "SELECT datatype.memory_callback_prepare(2, false); ";
                Spi.Execute(query + "DO $$ BEGIN RAISE EXCEPTION 'managed primary cleanup failure' USING " +
                    "ERRCODE='23514', DETAIL='managed primary detail', HINT='managed primary hint'; END $$");
            }
            else
            {
                PgTransaction.RunInSubtransaction(() =>
                {
                    _ = MemoryCallbackFunctions.MemoryCallbackPrepare(2, cleanupThrows);
                    throw new PgException("23514", "managed primary cleanup failure", "managed primary detail", "managed primary hint");
                });
            }
        }
        catch (PgException failure)
        {
            primary = [failure.SqlState, failure.Message, failure.Detail ?? string.Empty, failure.Hint ?? string.Empty];
        }

        return [.. primary, MemoryCallbackFunctions.MemoryCallbackImplicitState(),
            Spi.ExecuteScalar<int>("SELECT 42").ToString(System.Globalization.CultureInfo.InvariantCulture)];
    }
}
