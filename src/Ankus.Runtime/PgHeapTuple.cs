using System.Text;

namespace Ankus;

/// <summary>
/// Owns the cells of a PostgreSQL composite or anonymous record independently of backend memory contexts.
/// Ordinals include dropped physical slots. Nested tuples, arrays, and binary buffers are ordinary shared references.
/// </summary>
public sealed class PgHeapTuple
{
    private readonly object?[] _values;

    /// <summary>
    /// Copies converted cells and retains immutable descriptor metadata.
    /// </summary>
    internal PgHeapTuple(PgTupleDescriptor descriptor, object?[] values)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(values);
        if (values.Length != descriptor.Attributes.Count)
        {
            throw new ArgumentException("Tuple cells must match the descriptor's physical attribute count.", nameof(values));
        }

        for (int index = 0; index < values.Length; index++)
        {
            if (descriptor.Attributes[index].IsDropped && values[index] is not null)
            {
                throw new ArgumentException("Dropped tuple attributes must contain SQL NULL.", nameof(values));
            }
        }

        Descriptor = descriptor;
        _values = [.. values];
    }

    /// <summary>
    /// Gets the immutable physical tuple description, including its type identity.
    /// </summary>
    public PgTupleDescriptor Descriptor { get; }

    /// <summary>
    /// Gets the number of physical cells, including dropped attributes.
    /// </summary>
    public int Count => _values.Length;

    /// <summary>
    /// Gets an owned cell by zero-based physical ordinal. SQL NULL and dropped attributes return null.
    /// </summary>
    /// <param name="ordinal">The physical ordinal.</param>
    /// <returns>The managed cell.</returns>
    public object? this[int ordinal] => _values[ValidateOrdinal(ordinal)];

    /// <summary>
    /// Gets the first live cell with the exact case-sensitive name.
    /// </summary>
    /// <param name="name">The exact attribute name.</param>
    /// <returns>The managed cell.</returns>
    public object? this[string name] => this[Descriptor.GetOrdinal(name)];

    /// <summary>
    /// Reads a cell with the same strict type and SQL NULL rules as an SPI row.
    /// </summary>
    /// <typeparam name="T">The requested managed type.</typeparam>
    /// <param name="ordinal">The zero-based physical ordinal.</param>
    /// <returns>The converted cell.</returns>
    public T Get<T>(int ordinal) => SpiRow.Convert<T>(this[ordinal]);

    /// <summary>
    /// Reads the first live attribute with the exact name using strict managed conversion.
    /// </summary>
    /// <typeparam name="T">The requested managed type.</typeparam>
    /// <param name="name">The exact attribute name.</param>
    /// <returns>The converted cell.</returns>
    public T Get<T>(string name) => Get<T>(Descriptor.GetOrdinal(name));

    /// <summary>
    /// Replaces a cell while preserving declared type, domain, collation, and type-modifier metadata.
    /// Native output applies current catalog and domain constraints before exposing the tuple to PostgreSQL.
    /// </summary>
    /// <typeparam name="T">The replacement's declared managed type, including its type when null.</typeparam>
    /// <param name="ordinal">The zero-based physical ordinal.</param>
    /// <param name="value">The replacement cell.</param>
    public void Set<T>(int ordinal, T value)
    {
        PgTupleAttributeInfo attribute = Descriptor.Attributes[ValidateOrdinal(ordinal)];
        if (attribute.IsDropped)
        {
            throw new InvalidOperationException("Dropped tuple attributes cannot be changed.");
        }

        uint oid = value is PgHeapTuple tuple ? tuple.Descriptor.BaseTypeOid :
            value is null && typeof(T) == typeof(PgHeapTuple) && attribute.IsComposite
                ? attribute.BaseTypeOid : SpiType.GetOid(value);
        SetCore(ordinal, oid, value);
    }

    /// <summary>
    /// Replaces the first live attribute with an exact matching name.
    /// </summary>
    /// <typeparam name="T">The replacement's managed type.</typeparam>
    /// <param name="name">The exact attribute name.</param>
    /// <param name="value">The replacement value.</param>
    public void Set<T>(string name, T value) => Set(Descriptor.GetOrdinal(name), value);

    /// <summary>
    /// Replaces a cell using an explicit parameter identity, including a typed NULL composite or array.
    /// </summary>
    /// <param name="ordinal">The zero-based physical ordinal.</param>
    /// <param name="value">The typed replacement.</param>
    public void Set(int ordinal, SpiParameter value) => SetCore(ordinal, value.TypeOid, value.Value);

    /// <summary>
    /// Replaces a named cell using an explicit parameter identity.
    /// </summary>
    /// <param name="name">The exact attribute name.</param>
    /// <param name="value">The typed replacement.</param>
    public void Set(string name, SpiParameter value) => Set(Descriptor.GetOrdinal(name), value);

    /// <summary>
    /// Copies the cell slots while sharing immutable metadata and nested reference values.
    /// Replacing a cell in either tuple leaves the other tuple's slots unchanged.
    /// </summary>
    /// <returns>A shallow detached copy.</returns>
    public PgHeapTuple Clone() => new(Descriptor, _values);

    /// <summary>
    /// Creates and registers an anonymous PostgreSQL record from named typed fields in the active backend.
    /// New field names must contain at most sixty-three UTF-8 bytes and cannot contain a zero character.
    /// </summary>
    /// <param name="fields">The field names and typed values in physical order.</param>
    /// <returns>An owned record with a canonical registered type modifier.</returns>
    public static PgHeapTuple Create(params (string Name, SpiParameter Value)[] fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(fields.Length, 1664);
        var attributes = new PgTupleAttributeInfo[fields.Length];
        object?[] values = new object?[fields.Length];
        for (int index = 0; index < fields.Length; index++)
        {
            (string name, SpiParameter value) = fields[index];
            ArgumentException.ThrowIfNullOrEmpty(name);
            if (name.Contains('\0', StringComparison.Ordinal) || Encoding.UTF8.GetByteCount(name) > 63)
            {
                throw new ArgumentException("Record field names must fit a PostgreSQL identifier without zero characters.", nameof(fields));
            }

            attributes[index] = new PgTupleAttributeInfo(name, value.TypeOid, value.TypeOid, -1, 0,
                isComposite: value.Value is PgHeapTuple || value.TypeOid == 2249);
            values[index] = value.Value;
        }

        return NativeBackend.CreateTuple(new PgHeapTuple(new PgTupleDescriptor(2249, -1, attributes), values));
    }

    private void SetCore(int ordinal, uint oid, object? value)
    {
        PgTupleAttributeInfo attribute = Descriptor.Attributes[ValidateOrdinal(ordinal)];
        if (attribute.IsDropped)
        {
            throw new InvalidOperationException("Dropped tuple attributes cannot be changed.");
        }

        uint expected = attribute.BaseTypeOid is 1042 or 1043 ? 25 : attribute.BaseTypeOid;
        if (oid != expected && oid != attribute.TypeOid)
        {
            throw new InvalidCastException($"PostgreSQL type OID {oid} cannot replace tuple attribute '{attribute.Name}' of type OID {attribute.TypeOid}.");
        }

        _values[ordinal] = value;
    }

    private int ValidateOrdinal(int ordinal)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ordinal);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(ordinal, Count);
        return ordinal;
    }
}
