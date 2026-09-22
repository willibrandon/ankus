namespace Ankus;

/// <summary>
/// Represents a PostgreSQL error raised by a guarded server call or deliberately reported by an extension.
/// </summary>
public sealed class PgException : Exception
{
    /// <summary>
    /// Creates an external-routine error with a default message.
    /// </summary>
    public PgException() : this("PostgreSQL extension function failed.")
    {
    }

    /// <summary>
    /// Creates an external-routine error.
    /// </summary>
    /// <param name="message">The primary error message.</param>
    public PgException(string message) : this(message, innerException: null)
    {
    }

    /// <summary>
    /// Creates an external-routine error with a managed cause.
    /// </summary>
    /// <param name="message">The primary error message.</param>
    /// <param name="innerException">The original managed error.</param>
    public PgException(string message, Exception? innerException) : this("38000", message, innerException: innerException)
    {
    }

    /// <summary>
    /// Creates an error with explicit PostgreSQL diagnostics.
    /// </summary>
    /// <param name="sqlState">The five-character uppercase SQLSTATE code.</param>
    /// <param name="message">The primary error message.</param>
    /// <param name="detail">Additional error detail.</param>
    /// <param name="hint">A suggested corrective action.</param>
    /// <param name="innerException">The original managed error, if any.</param>
    public PgException(string sqlState, string message, string? detail = null, string? hint = null, Exception? innerException = null)
        : base(message, innerException)
    {
        ArgumentNullException.ThrowIfNull(sqlState);
        if (sqlState.Length != 5 || sqlState.Any(static value => value is not (>= '0' and <= '9' or >= 'A' and <= 'Z')) ||
            sqlState == "00000")
        {
            throw new ArgumentException("An error SQLSTATE must contain five uppercase ASCII letters or digits and not be 00000.",
                nameof(sqlState));
        }

        SqlState = sqlState;
        Detail = detail;
        Hint = hint;
    }

    /// <summary>
    /// Gets the PostgreSQL SQLSTATE code.
    /// </summary>
    public string SqlState { get; }

    /// <summary>
    /// Gets additional error detail.
    /// </summary>
    public string? Detail { get; }

    /// <summary>
    /// Gets a suggested corrective action.
    /// </summary>
    public string? Hint { get; }
}
