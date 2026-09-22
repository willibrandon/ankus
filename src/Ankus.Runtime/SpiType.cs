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
        if (type == typeof(PgPoint) || type == typeof(PgPoint?))
        {
            return 600;
        }

        if (type == typeof(PgLineSegment) || type == typeof(PgLineSegment?))
        {
            return 601;
        }

        if (type == typeof(PgPath))
        {
            return 602;
        }

        if (type == typeof(PgBox) || type == typeof(PgBox?))
        {
            return 603;
        }

        if (type == typeof(PgPolygon))
        {
            return 604;
        }

        if (type == typeof(PgLine) || type == typeof(PgLine?))
        {
            return 628;
        }

        if (type == typeof(PgCircle) || type == typeof(PgCircle?))
        {
            return 718;
        }

        if (type == typeof(PgInet) || type == typeof(PgInet?) || type == typeof(System.Net.IPAddress))
        {
            return 869;
        }

        if (type == typeof(PgCidr) || type == typeof(PgCidr?) || type == typeof(System.Net.IPNetwork) || type == typeof(System.Net.IPNetwork?))
        {
            return 650;
        }

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

        if (type == typeof(Guid) || type == typeof(Guid?))
        {
            return 2950;
        }

        if (type == typeof(PgJson) || type == typeof(PgJson?))
        {
            return 114;
        }

        if (type == typeof(PgJsonb) || type == typeof(PgJsonb?))
        {
            return 3802;
        }

        if (type == typeof(PgNumeric) || type == typeof(PgNumeric?) || type == typeof(decimal) || type == typeof(decimal?))
        {
            return 1700;
        }

        if (type == typeof(PgDate) || type == typeof(PgDate?) || type == typeof(DateOnly) || type == typeof(DateOnly?))
        {
            return 1082;
        }

        if (type == typeof(PgTime) || type == typeof(PgTime?) || type == typeof(TimeOnly) || type == typeof(TimeOnly?))
        {
            return 1083;
        }

        if (type == typeof(PgTimeTz) || type == typeof(PgTimeTz?))
        {
            return 1266;
        }

        if (type == typeof(PgTimestamp) || type == typeof(PgTimestamp?) || type == typeof(DateTime) || type == typeof(DateTime?))
        {
            return 1114;
        }

        if (type == typeof(PgTimestampTz) || type == typeof(PgTimestampTz?) ||
            type == typeof(DateTimeOffset) || type == typeof(DateTimeOffset?))
        {
            return 1184;
        }

        if (type == typeof(PgInterval) || type == typeof(PgInterval?) || type == typeof(TimeSpan) || type == typeof(TimeSpan?))
        {
            return 1186;
        }

        uint rangeOid = SpiRange.GetOid(type);
        if (rangeOid != 0)
        {
            return rangeOid;
        }

        uint arrayOid = SpiArray.GetOid(type);
        return arrayOid != 0 ? arrayOid : throw new NotSupportedException($"SPI parameters of managed type '{type}' do not have a registered PostgreSQL conversion.");
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
        Guid uuid => NativeValue.FromGuid(uuid),
        PgInet address => NativeValue.FromInet(address),
        PgPoint point => NativeValue.FromPoint(point),
        PgLine line => NativeValue.FromLine(line),
        PgLineSegment segment => NativeValue.FromLineSegment(segment),
        PgBox box => NativeValue.FromBox(box),
        PgCircle circle => NativeValue.FromCircle(circle),
        PgPath path => NativeValue.FromPath(path),
        PgPolygon polygon => NativeValue.FromPolygon(polygon),
        PgCidr network => NativeValue.FromCidr(network),
        System.Net.IPAddress address => NativeValue.FromInet(new PgInet(address)),
        System.Net.IPNetwork network => NativeValue.FromCidr(new PgCidr(network)),
        PgJson json => NativeValue.FromString(json.Text),
        PgJsonb json => NativeValue.FromString(json.Text),
        PgNumeric number => NativeValue.FromString(number.Text),
        decimal number => NativeValue.FromString(PgNumeric.FromDecimal(number).Text),
        PgDate date => NativeValue.FromDate(date),
        PgTime time => NativeValue.FromTime(time),
        PgTimeTz time => NativeValue.FromTimeTz(time),
        PgTimestamp timestamp => NativeValue.FromTimestamp(timestamp),
        PgTimestampTz timestamp => NativeValue.FromTimestampTz(timestamp),
        PgInterval interval => NativeValue.FromInterval(interval),
        DateOnly date => NativeValue.FromDate(PgDate.FromDateOnly(date)),
        TimeOnly time => NativeValue.FromTime(PgTime.FromTimeOnly(time)),
        DateTime timestamp => NativeValue.FromTimestamp(PgTimestamp.FromDateTime(timestamp)),
        DateTimeOffset timestamp => NativeValue.FromTimestampTz(PgTimestampTz.FromDateTimeOffset(timestamp)),
        TimeSpan interval => NativeValue.FromInterval(PgInterval.FromTimeSpan(interval)),
        IPgArray array => NativeValue.FromArray(array),
        IPgRange range => NativeValue.FromRange(range),
        Array array => NativeValue.FromArray(SpiArray.Wrap(array)),
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
            2950 => value.ReadGuid(),
            869 => value.ReadInet(),
            600 => value.ReadPoint(),
            601 => value.ReadLineSegment(),
            602 => value.ReadPath(),
            603 => value.ReadBox(),
            604 => value.ReadPolygon(),
            628 => value.ReadLine(),
            718 => value.ReadCircle(),
            3904 => value.ReadRange<int>(),
            3926 => value.ReadRange<long>(),
            3906 => value.ReadRange<PgNumeric>(),
            3912 => value.ReadRange<PgDate>(),
            3908 => value.ReadRange<PgTimestamp>(),
            3910 => value.ReadRange<PgTimestampTz>(),
            650 => value.ReadCidr(),
            114 => value.ReadJson(),
            3802 => value.ReadJsonb(),
            1700 => value.ReadNumeric(),
            1082 => value.ReadDate(),
            1083 => value.ReadTime(),
            1266 => value.ReadTimeTz(),
            1114 => value.ReadTimestamp(),
            1184 => value.ReadTimestampTz(),
            1186 => value.ReadInterval(),
            _ when value.IsArray => value.ReadArray(),
            _ => throw new NotSupportedException($"SPI result type OID {oid} does not have a registered managed conversion."),
        };
    }
}
