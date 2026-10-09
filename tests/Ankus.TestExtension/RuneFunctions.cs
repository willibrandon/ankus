using System.Text;

namespace Ankus.TestExtension;

/// <summary>
/// Exercises <see cref="Rune"/>, the counterpart of pgrx's Rust <c>char</c>, as <c>varchar</c> through generated
/// boundaries and each SPI ownership path.
/// </summary>
public static class RuneFunctions
{
    /// <summary>
    /// Returns a strict Rune argument unchanged, as pgrx's <c>rt_char</c> round trips do.
    /// </summary>
    /// <param name="value">The character.</param>
    /// <returns>The same character.</returns>
    [PgFunction]
    public static Rune RuneRoundTrip(Rune value) => value;

    /// <summary>
    /// Converts a Rune argument to text, as pgrx's <c>char</c> to <c>String</c> round trips do.
    /// </summary>
    /// <param name="value">The character.</param>
    /// <returns>The character's UTF-16 text.</returns>
    [PgFunction]
    public static string RuneToText(Rune value) => value.ToString();

    /// <summary>
    /// Reports the scalar value the generated reader decoded, independently of the varchar writer.
    /// </summary>
    /// <param name="value">The character.</param>
    /// <returns>The Unicode scalar value.</returns>
    [PgFunction]
    public static int RuneScalar(Rune value) => value.Value;

    /// <summary>
    /// Creates a Rune from a scalar value, independently of the varchar reader.
    /// </summary>
    /// <param name="value">The Unicode scalar value.</param>
    /// <returns>The character.</returns>
    [PgFunction]
    public static Rune RuneFromScalar(int value) => new(value);

    /// <summary>
    /// Returns a nullable Rune through the selected SPI path.
    /// </summary>
    /// <param name="value">The character or SQL NULL.</param>
    /// <param name="mode">The direct, query, plan, session, cursor, retained-plan or edited-row path.</param>
    /// <returns>The independently owned character.</returns>
    [PgFunction]
    public static Rune? ExchangeRune(Rune? value, int mode) => ArrayFunctions.Exchange(value, mode);

    /// <summary>
    /// Returns a nullable Rune array through the selected SPI path.
    /// </summary>
    /// <param name="value">The characters, which may include SQL NULL elements, or SQL NULL.</param>
    /// <param name="mode">The conversion path.</param>
    /// <returns>The independently owned array.</returns>
    [PgFunction]
    public static PgArray<Rune?>? ExchangeRunes(PgArray<Rune?>? value, int mode) => ArrayFunctions.Exchange(value, mode);

    /// <summary>
    /// Returns a Rune vector, which rejects SQL NULL elements.
    /// </summary>
    /// <param name="values">The characters.</param>
    /// <returns>The characters in reverse order.</returns>
    [PgFunction]
    public static Rune[] ReverseRunes(Rune[] values) => [.. values.Reverse()];

    /// <summary>
    /// Reads the first cell of a query as a nullable Rune.
    /// </summary>
    /// <param name="sql">A query returning one character-text column.</param>
    /// <returns>The character's scalar value, or SQL NULL for SQL NULL or no rows.</returns>
    [PgFunction]
    public static int? RuneFromQuery(string sql) => Spi.ExecuteScalar<Rune?>(sql)?.Value;
}
