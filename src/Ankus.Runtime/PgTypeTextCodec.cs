namespace Ankus;

/// <summary>
/// Defines the SQL text representation of a PostgreSQL base type independently of its storage format.
/// </summary>
/// <remarks>
/// Select this codec through <see cref="PgTypeAttribute.TextCodec"/> to retain generated CBOR storage.
/// Ankus constructs one instance on first text use inside the managed error boundary. Binary operations
/// do not construct or invoke the text codec. Do not retain PostgreSQL call state in the instance.
/// Throw <see cref="PgException"/> to supply a PostgreSQL SQLSTATE and owned error diagnostics.
/// </remarks>
/// <typeparam name="T">The exact managed type carrying PgType.</typeparam>
public abstract class PgTypeTextCodec<T>
{
    /// <summary>
    /// Parses a present SQL text representation into an independent managed value.
    /// </summary>
    /// <param name="text">The input converted from the database encoding.</param>
    /// <returns>The non-null parsed value.</returns>
    public abstract T Parse(string text);

    /// <summary>
    /// Formats a present managed value for SQL text output.
    /// </summary>
    /// <param name="value">The present managed value.</param>
    /// <returns>The non-null text representation without a zero terminator.</returns>
    public abstract string Format(T value);
}
