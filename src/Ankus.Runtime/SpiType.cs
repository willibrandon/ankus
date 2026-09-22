namespace Ankus;

/// <summary>
/// Converts supported SPI values without reflection or runtime-generated marshaling code.
/// </summary>
internal static class SpiType
{
    /// <summary>
    /// Resolves a statically declared managed type to a built-in PostgreSQL OID.
    /// </summary>
    /// <typeparam name="T">The managed parameter type.</typeparam>
    /// <returns>The PostgreSQL OID.</returns>
    internal static uint GetOid<T>() => GetOid(typeof(T));

    /// <summary>
    /// Resolves a managed type using known type identities without inspecting members or creating types dynamically.
    /// </summary>
    /// <param name="type">The declared managed type.</param>
    /// <returns>The PostgreSQL OID.</returns>
    internal static uint GetOid(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (type == typeof(bool) || type == typeof(bool?))
        {
            return 16;
        }

        if (type == typeof(byte[]))
        {
            return 17;
        }

        if (type == typeof(sbyte) || type == typeof(sbyte?))
        {
            return 18;
        }

        if (type == typeof(long) || type == typeof(long?))
        {
            return 20;
        }

        if (type == typeof(short) || type == typeof(short?))
        {
            return 21;
        }

        if (type == typeof(int) || type == typeof(int?))
        {
            return 23;
        }

        if (type == typeof(string))
        {
            return 25;
        }

        if (type == typeof(uint) || type == typeof(uint?))
        {
            return 26;
        }

        if (type == typeof(float) || type == typeof(float?))
        {
            return 700;
        }

        if (type == typeof(double) || type == typeof(double?))
        {
            return 701;
        }

        throw new NotSupportedException($"SPI parameters of managed type '{type}' do not have a registered PostgreSQL conversion.");
    }

    /// <summary>
    /// Copies a managed parameter into an owned native transport value.
    /// </summary>
    /// <param name="value">The managed value.</param>
    /// <returns>The native value, whose buffers must be released by the caller.</returns>
    internal static NativeValue ToNative(object? value) => value switch
    {
        null => new NativeValue { IsNull = 1 },
        bool boolean => new NativeValue { Integral = boolean ? 1 : 0 },
        sbyte number => new NativeValue { Integral = number },
        short number => new NativeValue { Integral = number },
        int number => new NativeValue { Integral = number },
        long number => new NativeValue { Integral = number },
        uint number => new NativeValue { Integral = number },
        float number => new NativeValue { Integral = BitConverter.SingleToInt32Bits(number) },
        double number => new NativeValue { Integral = BitConverter.DoubleToInt64Bits(number) },
        string text => NativeValue.FromString(text),
        byte[] bytes => NativeValue.FromBytes(bytes),
        _ => throw new NotSupportedException("The SPI parameter does not have a registered PostgreSQL conversion."),
    };

    /// <summary>
    /// Copies a native cell into its managed representation using the column's underlying PostgreSQL type.
    /// </summary>
    /// <param name="value">The borrowed native cell.</param>
    /// <param name="oid">The PostgreSQL base type OID.</param>
    /// <returns>The managed value, or null for SQL NULL.</returns>
    internal static object? FromNative(NativeValue value, uint oid)
    {
        if (value.IsNull != 0)
        {
            return null;
        }

        return oid switch
        {
            16 => value.Integral != 0,
            17 => value.ReadBytes(),
            18 => (sbyte)value.Integral,
            20 => value.Integral,
            21 => (short)value.Integral,
            23 => (int)value.Integral,
            25 or 1042 or 1043 => value.ReadString(),
            26 => (uint)value.Integral,
            700 => BitConverter.Int32BitsToSingle((int)value.Integral),
            701 => BitConverter.Int64BitsToDouble(value.Integral),
            _ => throw new NotSupportedException($"SPI result type OID {oid} does not have a registered managed conversion."),
        };
    }
}
