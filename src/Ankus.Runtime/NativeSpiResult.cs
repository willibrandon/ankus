using System.Runtime.InteropServices;

namespace Ankus;

/// <summary>
/// Carries a native-owned SPI result and its allocator-specific cleanup callback.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NativeSpiResult
{
    /// <summary>
    /// Points to the column metadata array.
    /// </summary>
    internal NativeSpiColumn* _columns;

    /// <summary>
    /// Points to row-major result cells.
    /// </summary>
    internal NativeValue* _values;

    /// <summary>
    /// Contains the final statement's processed-row count.
    /// </summary>
    internal long _rowsAffected;

    /// <summary>
    /// Contains the materialized row count.
    /// </summary>
    internal int _rowCount;

    /// <summary>
    /// Contains the result column count.
    /// </summary>
    internal int _columnCount;

    /// <summary>
    /// Releases all native result allocations without invoking PostgreSQL APIs.
    /// </summary>
    internal delegate* unmanaged[Cdecl]<NativeSpiResult*, void> _release;

    /// <summary>
    /// Contains the identity of an opened or resolved cursor.
    /// </summary>
    internal long _cursorId;

    /// <summary>
    /// Contains the owned UTF-8 portal name.
    /// </summary>
    internal NativeValue _cursorName;

    /// <summary>
    /// Contains an owned scalar result from a native helper, including UTF-8 text and temporal values.
    /// </summary>
    internal NativeValue _text;

    /// <summary>
    /// Copies every cell and column into managed objects before native memory is released.
    /// </summary>
    /// <returns>The independent managed result.</returns>
    internal readonly SpiResult ToManaged()
    {
        var columns = new SpiColumn[_columnCount];
        for (int index = 0; index < columns.Length; index++)
        {
            columns[index] = new SpiColumn(_columns[index]._name.ReadString(), _columns[index]._typeOid);
        }

        IReadOnlyList<SpiColumn> metadata = Array.AsReadOnly(columns);
        var rows = new SpiRow[_rowCount];
        for (int row = 0; row < rows.Length; row++)
        {
            var cells = new object?[_columnCount];
            for (int column = 0; column < cells.Length; column++)
            {
                cells[column] = SpiType.FromNative(_values[row * _columnCount + column], _columns[column]._baseTypeOid);
            }

            rows[row] = new SpiRow(cells, metadata);
        }

        return new SpiResult(metadata, rows, _rowsAffected);
    }
}
