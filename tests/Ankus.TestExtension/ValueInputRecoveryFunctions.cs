using System.Globalization;

namespace Ankus.TestExtension;

/// <summary>
/// Exercises deliberately recoverable value input in callback and parallel execution contexts.
/// </summary>
public static class ValueInputRecoveryFunctions
{
    private static readonly Dictionary<(string Family, bool Invalid), string[]> s_parallel = [];
    private static string[] s_callback = [];

    /// <summary>
    /// Parses once per worker and preserves the worker's identity for real parallel execution assertions.
    /// </summary>
    /// <param name="row">The table-derived row preventing planner constant folding.</param>
    /// <param name="family">The built-in value family.</param>
    /// <param name="invalid">Whether the native parser receives malformed input.</param>
    /// <returns>The worker identity, success flag and detached value.</returns>
    [PgFunction(ParallelSafety = PgParallelSafety.Safe, Volatility = PgVolatility.Stable)]
    public static string[] ValueInputParallel(int row, string family, bool invalid)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        if (!s_parallel.TryGetValue((family, invalid), out string[]? result))
        {
            (bool success, string text) = Parse(family, invalid);
            result = [Environment.ProcessId.ToString(CultureInfo.InvariantCulture), success.ToString(), text];
            s_parallel.Add((family, invalid), result);
        }

        return result;
    }

    /// <summary>
    /// Registers input parsing in a pre-commit phase where independent recovery subtransactions are forbidden.
    /// </summary>
    /// <param name="family">The built-in value family.</param>
    /// <param name="invalid">Whether the callback encounters a native input error.</param>
    [PgFunction]
    public static void ValueInputCallbackRegister(string family, bool invalid)
    {
        s_callback = [];
        _ = PgTransaction.RegisterCallback(PgTransactionEvent.PreCommit, () =>
        {
            try
            {
                (bool success, string text) = Parse(family, invalid);
                s_callback = [success.ToString(), text, "returned"];
            }
            catch (PgException error)
            {
                // An ordinary catch must not turn this unrecovered callback into a commit.
                s_callback = [error.SqlState, error.Message, "caught"];
            }
        });
    }

    /// <summary>
    /// Reads managed observations after the same backend has committed or aborted the callback transaction.
    /// </summary>
    /// <returns>The callback's exact outcome.</returns>
    [PgFunction]
    public static string[] ValueInputCallbackSnapshot() => s_callback;

    /// <summary>
    /// Converts independent representative input for each native value family.
    /// </summary>
    /// <param name="family">The built-in family.</param>
    /// <param name="invalid">Whether input is malformed.</param>
    /// <returns>The parse flag and detached text.</returns>
    private static (bool Success, string Text) Parse(string family, bool invalid)
    {
        switch (family)
        {
            case "numeric":
                bool numericSuccess = PgNumeric.TryParse(invalid ? "not a number" : "42", out PgNumeric numeric);
                return (numericSuccess, numericSuccess ? numeric.Text : string.Empty);
            case "temporal":
                bool dateSuccess = PgDate.TryParse(invalid ? "not a date" : "2024-02-29", out PgDate date);
                return (dateSuccess, dateSuccess ? date.ToPostgresString() : string.Empty);
            case "network":
                bool networkSuccess = PgInet.TryParse(invalid ? "256.0.0.1" : "192.0.2.1", out PgInet network);
                return (networkSuccess, networkSuccess ? network.ToString() : string.Empty);
            case "geometry":
                bool pointSuccess = PgPoint.TryParse(invalid ? "not a point" : "(1,2)", out PgPoint point);
                return (pointSuccess, pointSuccess ? point.ToString() : string.Empty);
            case "range":
                bool rangeSuccess = PgRange.TryParse<int>(invalid ? "[5,2)" : "[1,3)", out PgRange<int>? range);
                return (rangeSuccess, rangeSuccess ? range!.ToPostgresString() : string.Empty);
            default:
                throw new ArgumentOutOfRangeException(nameof(family));
        }
    }
}
