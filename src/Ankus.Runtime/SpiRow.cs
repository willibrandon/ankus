namespace Ankus;

/// <summary>
/// Contains a managed copy of one SPI result row, independent of PostgreSQL memory contexts.
/// </summary>
public sealed class SpiRow
{
    private readonly object?[] _values;
    private readonly IReadOnlyList<SpiColumn> _columns;
    private uint[]? _typeOids;

    /// <summary>
    /// Creates a row from converted native cells and shared column metadata.
    /// </summary>
    /// <param name="values">The managed cells.</param>
    /// <param name="columns">The query's columns.</param>
    internal SpiRow(object?[] values, IReadOnlyList<SpiColumn> columns)
    {
        _values = values;
        _columns = columns;
    }

    /// <summary>
    /// Gets a cell by its zero-based column ordinal. SQL NULL is represented by null.
    /// </summary>
    /// <param name="ordinal">The zero-based column ordinal.</param>
    /// <returns>The managed cell.</returns>
    public object? this[int ordinal] => _values[ordinal];

    /// <summary>
    /// Gets a cell by its exact, case-sensitive column name. Duplicate names resolve to the first column.
    /// </summary>
    /// <param name="name">The column name.</param>
    /// <returns>The managed cell or null for SQL NULL.</returns>
    public object? this[string name] => _values[GetOrdinal(name)];

    /// <summary>
    /// Gets the number of cells in this row.
    /// </summary>
    public int Count => _values.Length;

    /// <summary>
    /// Replaces a local cell and its type with a supported managed datum. The edit does not update PostgreSQL or result metadata.
    /// </summary>
    /// <typeparam name="T">The replacement type, including its type when the value is null.</typeparam>
    /// <param name="ordinal">The zero-based column ordinal.</param>
    /// <param name="value">The replacement value.</param>
    public void Set<T>(int ordinal, T value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ordinal);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(ordinal, Count);
        uint typeOid = SpiType.GetOid<T>();
        _typeOids ??= [.. _columns.Select(static column => column.TypeOid)];
        _values[ordinal] = value;
        _typeOids[ordinal] = typeOid;
    }

    /// <summary>
    /// Replaces a local cell by exact name. The replacement's declared CLR type determines its PostgreSQL type OID.
    /// </summary>
    /// <typeparam name="T">The replacement managed type.</typeparam>
    /// <param name="name">The column name; duplicate names resolve to the first column.</param>
    /// <param name="value">The replacement value.</param>
    public void Set<T>(string name, T value) => Set(GetOrdinal(name), value);

    /// <summary>
    /// Gets a cell's PostgreSQL type OID, including its original domain OID until replaced by Set.
    /// </summary>
    /// <param name="ordinal">The zero-based column ordinal.</param>
    /// <returns>The cell's current type OID.</returns>
    public uint GetTypeOid(int ordinal) => _typeOids is null ? _columns[ordinal].TypeOid : _typeOids[ordinal];

    /// <summary>
    /// Gets a cell's current PostgreSQL type OID by exact column name.
    /// </summary>
    /// <param name="name">The column name.</param>
    /// <returns>The cell's current type OID.</returns>
    public uint GetTypeOid(string name) => GetTypeOid(GetOrdinal(name));

    /// <summary>
    /// Gets a typed cell without implicit numeric or textual conversion.
    /// SQL NULL is accepted for nullable value types and reference types.
    /// </summary>
    /// <typeparam name="T">The expected managed type.</typeparam>
    /// <param name="ordinal">The zero-based column ordinal.</param>
    /// <returns>The cell value.</returns>
    public T Get<T>(int ordinal) => Convert<T>(_values[ordinal]);

    /// <summary>
    /// Gets a typed cell by an exact, case-sensitive column name. Duplicate names resolve to the first column.
    /// </summary>
    /// <typeparam name="T">The expected managed type.</typeparam>
    /// <param name="name">The column name.</param>
    /// <returns>The cell value.</returns>
    public T Get<T>(string name) => Get<T>(GetOrdinal(name));

    /// <summary>
    /// Resolves an exact, case-sensitive column name to its zero-based ordinal. Duplicate names resolve to the first column.
    /// </summary>
    /// <param name="name">The column name.</param>
    /// <returns>The zero-based ordinal.</returns>
    public int GetOrdinal(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        for (int index = 0; index < _columns.Count; index++)
        {
            if (_columns[index].Name == name)
            {
                return index;
            }
        }

        throw new ArgumentException($"The SPI result has no column named '{name}'.", nameof(name));
    }

    /// <summary>
    /// Converts a managed cell while preserving the distinction between SQL NULL and a default value.
    /// </summary>
    /// <typeparam name="T">The expected managed type.</typeparam>
    /// <param name="value">The managed cell.</param>
    /// <returns>The typed value.</returns>
    internal static T Convert<T>(object? value)
    {
        if (value is T result)
        {
            return result;
        }

        if (value is null)
        {
            if (default(T) is null)
            {
                return default!;
            }

            throw new InvalidOperationException("SQL NULL cannot be read as a non-nullable managed value.");
        }

        throw new InvalidCastException($"The SPI value cannot be read as '{typeof(T)}'.");
    }
}
