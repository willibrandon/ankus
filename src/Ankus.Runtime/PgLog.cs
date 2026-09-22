namespace Ankus;

/// <summary>
/// Reports through PostgreSQL's client and server diagnostics on the active backend thread.
/// </summary>
public static class PgLog
{
    /// <summary>
    /// Tests PostgreSQL's current reporting thresholds before formatting a message.
    /// </summary>
    /// <param name="level">The reporting severity.</param>
    /// <returns>Whether PostgreSQL would process this level for either destination.</returns>
    public static bool IsEnabled(PgLogLevel level)
    {
        ValidateLevel(level);
        return NativeBackend.IsLogEnabled(level);
    }

    /// <summary>
    /// Reports literal text. ERROR throws PgException; FATAL and PANIC unwind to the generated native boundary before reporting.
    /// </summary>
    /// <param name="level">The reporting severity.</param>
    /// <param name="message">The primary message.</param>
    public static void Write(PgLogLevel level, string message) => Write(level, new PgDiagnostic(message));

    /// <summary>
    /// Reports structured diagnostics using PostgreSQL's filtering and routing rules.
    /// Terminal reports must be allowed to propagate out of the extension method.
    /// </summary>
    /// <param name="level">The reporting severity.</param>
    /// <param name="diagnostic">The message and optional diagnostic fields.</param>
    public static void Write(PgLogLevel level, PgDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        ValidateLevel(level);
        NativeBackend.CheckAccess();
        if (diagnostic.SqlState is string state &&
            (state.Length != 5 || state.Any(static value => value is not (>= '0' and <= '9' or >= 'A' and <= 'Z')) ||
            (level >= PgLogLevel.Error && state == "00000")))
        {
            throw new ArgumentException("SQLSTATE must contain five uppercase ASCII letters or digits; errors cannot use 00000.",
                nameof(diagnostic));
        }

        if (level == PgLogLevel.Error)
        {
            throw diagnostic.ToException();
        }

        if (level >= PgLogLevel.Fatal)
        {
            throw new PgTerminalException(level, diagnostic);
        }

        NativeBackend.Report(level, diagnostic);
    }

    private static void ValidateLevel(PgLogLevel level)
    {
        if (level is < PgLogLevel.Debug5 or > PgLogLevel.Panic)
        {
            throw new ArgumentOutOfRangeException(nameof(level));
        }
    }
}
