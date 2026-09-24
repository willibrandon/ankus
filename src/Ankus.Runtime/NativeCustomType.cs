namespace Ankus;

public unsafe partial struct NativeValue
{
    /// <summary>
    /// Gets whether this value carries a generated base type's binary payload.
    /// </summary>
    internal readonly bool IsCustomType => _auxiliary1 == -7;

    /// <summary>
    /// Reads a present generated custom type into independent managed storage.
    /// </summary>
    /// <typeparam name="T">The generated managed type.</typeparam>
    /// <returns>The decoded value.</returns>
    public readonly T ReadCustom<T>() => (T)PgTypeRegistry.Require(typeof(T)).Read(this);

    /// <summary>
    /// Encodes a generated custom type into owned native transport.
    /// </summary>
    /// <typeparam name="T">The generated managed type.</typeparam>
    /// <param name="value">The managed value or SQL NULL.</param>
    /// <returns>The owned transport envelope.</returns>
    public static NativeValue FromCustom<T>(T value) => value is null ? new() { IsNull = 1 } :
        PgTypeRegistry.Require(typeof(T)).Write(value);

    /// <summary>
    /// Reads a borrowed binary payload only after checking exact type and transport shape.
    /// </summary>
    internal readonly ReadOnlySpan<byte> ReadCustomPayload(uint oid)
    {
        if (!IsCustomType || _isNull != 0 || unchecked((uint)_auxiliary2) != oid || oid == 0 ||
            _length < 0 || _length != 0 && _data == null)
        {
            throw new InvalidOperationException("Invalid custom PostgreSQL type payload or type identity.");
        }

        return new(_data, _length);
    }

    /// <summary>
    /// Copies a binary payload and its exact SQL type into allocator-matched native storage.
    /// </summary>
    internal static NativeValue FromCustomPayload(ReadOnlySpan<byte> payload, uint oid)
    {
        NativeValue result = FromBytes(payload);
        result._auxiliary1 = -7;
        result._auxiliary2 = unchecked((int)oid);
        return result;
    }
}
