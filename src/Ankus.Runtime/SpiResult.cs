using System.Collections;

namespace Ankus;

/// <summary>
/// Contains materialized SPI rows and column metadata that remain valid after SPI disconnects.
/// </summary>
public sealed class SpiResult : IReadOnlyList<SpiRow>
{
    private readonly SpiRow[] _rows;

    /// <summary>
    /// Creates a fully materialized result.
    /// </summary>
    /// <param name="columns">The column metadata.</param>
    /// <param name="rows">The result rows.</param>
    /// <param name="rowsAffected">The final statement's processed-row count.</param>
    internal SpiResult(IReadOnlyList<SpiColumn> columns, SpiRow[] rows, long rowsAffected)
    {
        Columns = columns;
        _rows = rows;
        RowsAffected = rowsAffected;
    }

    /// <summary>
    /// Gets the result's columns, including when a query returns no rows.
    /// </summary>
    public IReadOnlyList<SpiColumn> Columns { get; }

    /// <summary>
    /// Gets the row count reported by PostgreSQL for the final statement.
    /// </summary>
    public long RowsAffected { get; }

    /// <summary>
    /// Gets the number of materialized rows.
    /// </summary>
    public int Count => _rows.Length;

    /// <summary>
    /// Gets a row by its zero-based index.
    /// </summary>
    /// <param name="index">The row index.</param>
    /// <returns>The result row.</returns>
    public SpiRow this[int index] => _rows[index];

    /// <summary>
    /// Enumerates rows in PostgreSQL result order.
    /// </summary>
    /// <returns>The row enumerator.</returns>
    public IEnumerator<SpiRow> GetEnumerator() => ((IEnumerable<SpiRow>)_rows).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
