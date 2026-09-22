using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Ankus;

/// <summary>
/// Carries converted values between generated native wrappers and managed dispatchers.
/// Input buffers are borrowed for the duration of a call. Output buffers have an explicit matching allocator callback.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[StructLayout(LayoutKind.Sequential)]
public unsafe struct NativeValue
{
    private static readonly UTF8Encoding s_utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private long _integer;
    private byte* _data;
    private int _length;
    private byte _isNull;
    private delegate* unmanaged[Cdecl]<void*, void> _release;

    /// <summary>
    /// Gets or sets the integer, Boolean, OID, or floating-point bit representation.
    /// </summary>
    public long Integral { readonly get => _integer; set => _integer = value; }

    /// <summary>
    /// Gets or sets whether the value represents SQL NULL, using a one-byte C flag.
    /// </summary>
    public byte IsNull { readonly get => _isNull; set => _isNull = value; }

    /// <summary>
    /// Copies a borrowed UTF-8 input buffer into a managed string, rejecting malformed UTF-8.
    /// </summary>
    /// <returns>The decoded string.</returns>
    public readonly string ReadString() => s_utf8.GetString(new ReadOnlySpan<byte>(_data, _length));

    /// <summary>
    /// Copies an optional UTF-8 buffer, preserving the distinction between absent and empty diagnostics.
    /// </summary>
    /// <returns>The decoded string, or null if no buffer was supplied.</returns>
    internal readonly string? ReadOptionalString() => _data == null ? null : ReadString();

    /// <summary>
    /// Copies a borrowed bytea input buffer into a managed byte array.
    /// </summary>
    /// <returns>The binary value.</returns>
    public readonly byte[] ReadBytes() => new ReadOnlySpan<byte>(_data, _length).ToArray();

    /// <summary>
    /// Reads PostgreSQL's sixteen network-order UUID bytes without .NET's mixed-endian byte-array convention.
    /// </summary>
    /// <returns>The managed UUID.</returns>
    public readonly Guid ReadGuid() => new(new ReadOnlySpan<byte>(_data, _length), bigEndian: true);

    /// <summary>
    /// Reads an owned JSON value from the UTF-8 transport.
    /// </summary>
    /// <returns>The JSON value.</returns>
    public readonly PgJson ReadJson() => new(ReadString());

    /// <summary>
    /// Reads an owned JSONB text representation from the UTF-8 transport.
    /// </summary>
    /// <returns>The JSONB value.</returns>
    public readonly PgJsonb ReadJsonb() => new(ReadString());

    /// <summary>
    /// Copies a UUID into PostgreSQL's network-order sixteen-byte representation.
    /// </summary>
    /// <param name="value">The managed UUID.</param>
    /// <returns>The owned transport value.</returns>
    public static NativeValue FromGuid(Guid value)
    {
        NativeValue result = Allocate(16);
        value.TryWriteBytes(new Span<byte>(result._data, 16), bigEndian: true, out _);
        return result;
    }

    /// <summary>
    /// Encodes a managed string into an owned UTF-8 output buffer. PostgreSQL text cannot contain a zero character.
    /// The native wrapper invokes the supplied release callback after copying or if PostgreSQL raises an error.
    /// </summary>
    /// <param name="value">The managed string.</param>
    /// <returns>An owned output value.</returns>
    public static NativeValue FromString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException("PostgreSQL text cannot contain a zero character.", nameof(value));
        }

        int length = s_utf8.GetByteCount(value);
        NativeValue result = Allocate(length);
        try
        {
            s_utf8.GetBytes(value, new Span<byte>(result._data, length));
            return result;
        }
        catch
        {
            NativeMemory.Free(result._data);
            throw;
        }
    }

    /// <summary>
    /// Copies a managed binary value into an owned native output buffer with a matching release callback.
    /// </summary>
    /// <param name="value">The binary value.</param>
    /// <returns>An owned output value.</returns>
    public static NativeValue FromBytes(ReadOnlySpan<byte> value)
    {
        NativeValue result = Allocate(value.Length);
        value.CopyTo(new Span<byte>(result._data, result._length));
        return result;
    }

    /// <summary>
    /// Releases an owned transport buffer through its matching allocator and clears this value.
    /// </summary>
    internal void Release()
    {
        if (_release != null)
        {
            _release(_data);
        }

        this = default;
    }

    private static NativeValue Allocate(int length)
    {
        // A varlena includes a four-byte header and must fit PostgreSQL's MaxAllocSize.
        ArgumentOutOfRangeException.ThrowIfGreaterThan(length, 0x3FFFFFFF - 4);
        byte* data = (byte*)NativeMemory.Alloc((nuint)length + 1);
        data[length] = 0;
        return new NativeValue { _data = data, _length = length, _release = &ReleaseBuffer };
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void ReleaseBuffer(void* data) => NativeMemory.Free(data);
}
