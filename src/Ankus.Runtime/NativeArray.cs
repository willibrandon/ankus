using System.Buffers;
using System.Buffers.Binary;

namespace Ankus;

public unsafe partial struct NativeValue
{
    /// <summary>
    /// Gets whether the auxiliary field marks a pointer-free array transport for a non-scalar datum.
    /// </summary>
    internal readonly bool IsArray => _auxiliary1 == -1;

    /// <summary>
    /// Reads an owned array with its PostgreSQL dimensions and lower bounds.
    /// </summary>
    /// <typeparam name="T">The scalar element type.</typeparam>
    /// <returns>The converted array. SQL NULL elements require reference or nullable value types.</returns>
    public readonly PgArray<T> ReadArray<T>()
    {
        uint oid = ReadArrayElementOid();
        if (SpiType.GetOid<T>() != oid)
        {
            throw new InvalidCastException($"Array element OID {oid} cannot be read as '{typeof(T)}'.");
        }

        return ReadArrayData<T>(oid);
    }

    /// <summary>
    /// Copies an array into one allocator-matched native transport buffer.
    /// </summary>
    /// <typeparam name="T">The scalar element type.</typeparam>
    /// <param name="value">The array.</param>
    /// <returns>The owned transport.</returns>
    public static NativeValue FromArray<T>(PgArray<T> value) => FromArray((IPgArray)value);

    /// <summary>
    /// Serializes array shape and scalar elements into one allocator-matched native buffer.
    /// </summary>
    /// <param name="value">The source array.</param>
    /// <returns>The owned transport, which the caller must release.</returns>
    internal static NativeValue FromArray(IPgArray value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var buffer = new ArrayBufferWriter<byte>();
        WriteInt(buffer, value.Lengths.Length);
        WriteInt(buffer, value.Count);
        WriteInt(buffer, checked((int)value.ElementOid));
        for (int index = 0; index < value.Lengths.Length; index++)
        {
            WriteInt(buffer, value.Lengths[index]);
            WriteInt(buffer, value.LowerBounds[index]);
        }

        for (int index = 0; index < value.Count; index++)
        {
            NativeValue item = SpiType.ToNative(value.GetElement(index));
            try
            {
                if ((long)buffer.WrittenCount + 28 + item._length > 0x3FFFFFFF - 4)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), "The converted array exceeds PostgreSQL's buffer capacity.");
                }

                Span<byte> header = buffer.GetSpan(28);
                BinaryPrimitives.WriteInt64BigEndian(header, item._integer);
                BinaryPrimitives.WriteInt32BigEndian(header[8..], item._auxiliary1);
                BinaryPrimitives.WriteInt32BigEndian(header[12..], item._auxiliary2);
                BinaryPrimitives.WriteInt32BigEndian(header[16..], item._temporalInfinity);
                BinaryPrimitives.WriteInt32BigEndian(header[20..], item._isNull);
                BinaryPrimitives.WriteInt32BigEndian(header[24..], item._length);
                buffer.Advance(28);
                buffer.Write(new ReadOnlySpan<byte>(item._data, item._length));
            }
            finally
            {
                item.Release();
            }
        }

        NativeValue result = FromBytes(buffer.WrittenSpan);
        result._auxiliary1 = -1;
        return result;
    }

    private readonly uint ReadArrayElementOid()
    {
        if (!IsArray || _isNull != 0 || _data == null || _length < 12)
        {
            throw new InvalidOperationException("Invalid native array header.");
        }

        uint oid = BinaryPrimitives.ReadUInt32BigEndian(new ReadOnlySpan<byte>(_data + 8, 4));
        oid = oid is 1042 or 1043 ? 25 : oid;
        _ = SpiArray.ArrayOid(oid);
        return oid;
    }

    private readonly PgArray<T> ReadArrayData<T>(uint oid)
    {
        ReadOnlySpan<byte> data = new(_data, _length);
        int rank = BinaryPrimitives.ReadInt32BigEndian(data);
        int count = BinaryPrimitives.ReadInt32BigEndian(data[4..]);
        if (rank is < 0 or > 6 || count < 0 || 12L + rank * 8L + count * 28L > data.Length)
        {
            throw new InvalidOperationException("Invalid native array shape or element count.");
        }

        var lengths = new int[rank];
        var bounds = new int[rank];
        int offset = 12;
        for (int index = 0; index < rank; index++, offset += 8)
        {
            lengths[index] = BinaryPrimitives.ReadInt32BigEndian(data[offset..]);
            bounds[index] = BinaryPrimitives.ReadInt32BigEndian(data[(offset + 4)..]);
        }

        SpiArray.ValidateShape(count, lengths, bounds);
        if (count == 0 && rank != 0)
        {
            throw new InvalidOperationException("Empty native arrays must have rank zero.");
        }

        var values = new T[count];
        for (int index = 0; index < count; index++)
        {
            if (data.Length - offset < 28)
            {
                throw new InvalidOperationException("Truncated native array element.");
            }

            ReadOnlySpan<byte> header = data[offset..];
            int length = BinaryPrimitives.ReadInt32BigEndian(header[24..]);
            int isNull = BinaryPrimitives.ReadInt32BigEndian(header[20..]);
            if (length < 0 || length > data.Length - offset - 28 || isNull is < 0 or > 1 || (isNull == 1 && length != 0))
            {
                throw new InvalidOperationException("Invalid native array element.");
            }

            var item = new NativeValue
            {
                _integer = BinaryPrimitives.ReadInt64BigEndian(header),
                _auxiliary1 = BinaryPrimitives.ReadInt32BigEndian(header[8..]),
                _auxiliary2 = BinaryPrimitives.ReadInt32BigEndian(header[12..]),
                _temporalInfinity = BinaryPrimitives.ReadInt32BigEndian(header[16..]),
                _isNull = (byte)isNull,
                _length = length,
                _data = _data + offset + 28,
            };
            values[index] = SpiRow.Convert<T>(SpiType.FromNative(item, oid));
            offset += 28 + length;
        }

        if (offset != data.Length)
        {
            throw new InvalidOperationException("Unexpected trailing native array data.");
        }

        return new PgArray<T>(values, (lengths, bounds));
    }

    /// <summary>
    /// Materializes the transport's scalar element type with nullable value elements and owned managed storage.
    /// </summary>
    /// <returns>The converted array, retaining its dimensions and lower bounds.</returns>
    internal readonly IPgArray ReadArray()
    {
        uint oid = ReadArrayElementOid();
        return oid switch
        {
            16 => ReadArrayData<bool?>(oid), 17 => ReadArrayData<byte[]?>(oid), 18 => ReadArrayData<sbyte?>(oid),
            20 => ReadArrayData<long?>(oid), 21 => ReadArrayData<short?>(oid), 23 => ReadArrayData<int?>(oid),
            25 => ReadArrayData<string?>(oid), 26 => ReadArrayData<uint?>(oid),
            700 => ReadArrayData<float?>(oid), 701 => ReadArrayData<double?>(oid), 2950 => ReadArrayData<Guid?>(oid),
            114 => ReadArrayData<PgJson?>(oid), 3802 => ReadArrayData<PgJsonb?>(oid), 1700 => ReadArrayData<PgNumeric?>(oid),
            1082 => ReadArrayData<PgDate?>(oid), 1083 => ReadArrayData<PgTime?>(oid), 1266 => ReadArrayData<PgTimeTz?>(oid),
            1114 => ReadArrayData<PgTimestamp?>(oid), 1184 => ReadArrayData<PgTimestampTz?>(oid), 1186 => ReadArrayData<PgInterval?>(oid),
            869 => ReadArrayData<PgInet?>(oid), 650 => ReadArrayData<PgCidr?>(oid),
            600 => ReadArrayData<PgPoint?>(oid), 601 => ReadArrayData<PgLineSegment?>(oid), 602 => ReadArrayData<PgPath>(oid),
            603 => ReadArrayData<PgBox?>(oid), 604 => ReadArrayData<PgPolygon>(oid), 628 => ReadArrayData<PgLine?>(oid),
            718 => ReadArrayData<PgCircle?>(oid),
            _ => throw new NotSupportedException($"Array element OID {oid} has no managed conversion."),
        };
    }

    private static void WriteInt(ArrayBufferWriter<byte> buffer, int value)
    {
        BinaryPrimitives.WriteInt32BigEndian(buffer.GetSpan(4), value);
        buffer.Advance(4);
    }
}
