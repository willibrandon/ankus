using System.Collections;

namespace Ankus;

/// <summary>
/// Provides raw datums with explicit PostgreSQL types and NULL flags for one SPI row.
/// </summary>
public sealed class SpiRawRow : IReadOnlyList<PgDatum>
{
    private readonly PgDatum[] _values;
    private readonly IReadOnlyList<SpiColumn> _columns;

    /// <summary>
    /// Captures values and the result's immutable column metadata.
    /// </summary>
    /// <param name="values">The checked native values.</param>
    /// <param name="columns">The column metadata.</param>
    internal SpiRawRow(PgDatum[] values, IReadOnlyList<SpiColumn> columns)
    {
        _values = values;
        _columns = columns;
    }

    /// <summary>
    /// Gets the number of cells.
    /// </summary>
    public int Count => _values.Length;

    /// <summary>
    /// Gets a raw value by its zero-based ordinal. SQL NULL has IsNull set on its datum.
    /// </summary>
    /// <param name="index">The column ordinal.</param>
    /// <returns>The raw value.</returns>
    public PgDatum this[int index] => _values[index];

    /// <summary>
    /// Gets the first column with an exact case-sensitive name.
    /// </summary>
    /// <param name="name">The column name.</param>
    /// <returns>The raw value, including its SQL NULL flag.</returns>
    public PgDatum this[string name]
    {
        get
        {
            ArgumentNullException.ThrowIfNull(name);
            for (int index = 0; index < _columns.Count; index++)
            {
                if (_columns[index].Name == name)
                {
                    return _values[index];
                }
            }

            throw new ArgumentException($"The SPI result has no column named '{name}'.", nameof(name));
        }
    }

    /// <summary>
    /// Enumerates datums in column order.
    /// </summary>
    /// <returns>The value enumerator.</returns>
    public IEnumerator<PgDatum> GetEnumerator() => ((IEnumerable<PgDatum>)_values).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
