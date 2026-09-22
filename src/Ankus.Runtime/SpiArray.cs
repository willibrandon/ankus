namespace Ankus;

/// <summary>
/// Statically closes supported array conversions so Native AOT needs no runtime type construction.
/// </summary>
internal static class SpiArray
{
    /// <summary>
    /// Validates PostgreSQL dimension limits, exclusive upper bounds, and the row-major element count.
    /// </summary>
    /// <param name="count">The supplied number of elements, including NULLs.</param>
    /// <param name="lengths">Zero through six dimension lengths.</param>
    /// <param name="lowerBounds">One lower bound per dimension, or an empty span to use one throughout.</param>
    internal static void ValidateShape(int count, ReadOnlySpan<int> lengths, ReadOnlySpan<int> lowerBounds)
    {
        if (lengths.Length > 6 || (!lowerBounds.IsEmpty && lowerBounds.Length != lengths.Length))
        {
            throw new ArgumentException("Arrays have at most six dimensions and one lower bound per dimension.", nameof(lengths));
        }

        long product = lengths.IsEmpty ? 0 : 1;
        for (int index = 0; index < lengths.Length; index++)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(lengths[index]);
            int lower = lowerBounds.IsEmpty ? 1 : lowerBounds[index];
            if ((long)lower + lengths[index] > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(lowerBounds), "A dimension's exclusive upper bound exceeds PostgreSQL's range.");
            }

            product = checked(product * lengths[index]);
            if (product > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(lengths), "The array exceeds managed element capacity.");
            }
        }

        if (product != count)
        {
            throw new ArgumentException("The dimension lengths must describe exactly the supplied elements.", nameof(lengths));
        }
    }

    /// <summary>
    /// Maps a supported built-in scalar OID to its PostgreSQL array OID.
    /// </summary>
    /// <param name="element">The scalar element OID.</param>
    /// <returns>The array OID.</returns>
    /// <exception cref="NotSupportedException">The scalar type has no supported array mapping.</exception>
    internal static uint ArrayOid(uint element) => element switch
    {
        16 => 1000, 17 => 1001, 18 => 1002, 20 => 1016, 21 => 1005, 23 => 1007, 25 => 1009, 26 => 1028,
        700 => 1021, 701 => 1022, 2950 => 2951, 114 => 199, 3802 => 3807, 1700 => 1231,
        1082 => 1182, 1083 => 1183, 1266 => 1270, 1114 => 1115, 1184 => 1185, 1186 => 1187,
        _ => throw new NotSupportedException($"PostgreSQL type OID {element} is not a supported array element."),
    };

    /// <summary>
    /// Resolves a statically supported vector or shape-preserving array type without constructing generic types at runtime.
    /// </summary>
    /// <param name="type">The declared managed array type, including nullable value-type elements.</param>
    /// <returns>The built-in PostgreSQL array OID, or zero when the type is unsupported.</returns>
    internal static uint GetOid(Type type)
    {
        uint element =
            Matches<bool>(type) || Matches<bool?>(type) ? 16u :
            Matches<byte[]>(type) ? 17u :
            Matches<sbyte>(type) || Matches<sbyte?>(type) ? 18u :
            Matches<long>(type) || Matches<long?>(type) ? 20u :
            Matches<short>(type) || Matches<short?>(type) ? 21u :
            Matches<int>(type) || Matches<int?>(type) ? 23u :
            Matches<string>(type) ? 25u :
            Matches<uint>(type) || Matches<uint?>(type) ? 26u :
            Matches<float>(type) || Matches<float?>(type) ? 700u :
            Matches<double>(type) || Matches<double?>(type) ? 701u :
            Matches<Guid>(type) || Matches<Guid?>(type) ? 2950u :
            Matches<PgJson>(type) || Matches<PgJson?>(type) ? 114u :
            Matches<PgJsonb>(type) || Matches<PgJsonb?>(type) ? 3802u :
            Matches<PgNumeric>(type) || Matches<PgNumeric?>(type) || Matches<decimal>(type) || Matches<decimal?>(type) ? 1700u :
            Matches<PgDate>(type) || Matches<PgDate?>(type) || Matches<DateOnly>(type) || Matches<DateOnly?>(type) ? 1082u :
            Matches<PgTime>(type) || Matches<PgTime?>(type) || Matches<TimeOnly>(type) || Matches<TimeOnly?>(type) ? 1083u :
            Matches<PgTimeTz>(type) || Matches<PgTimeTz?>(type) ? 1266u :
            Matches<PgTimestamp>(type) || Matches<PgTimestamp?>(type) || Matches<DateTime>(type) || Matches<DateTime?>(type) ? 1114u :
            Matches<PgTimestampTz>(type) || Matches<PgTimestampTz?>(type) || Matches<DateTimeOffset>(type) || Matches<DateTimeOffset?>(type) ? 1184u :
            Matches<PgInterval>(type) || Matches<PgInterval?>(type) || Matches<TimeSpan>(type) || Matches<TimeSpan?>(type) ? 1186u : 0;
        return element == 0 ? 0 : ArrayOid(element);
    }

    /// <summary>
    /// Converts an owned array to a supported managed array type, rejecting element or shape loss.
    /// </summary>
    /// <param name="array">The source array and its PostgreSQL shape.</param>
    /// <param name="type">The requested vector or shape-preserving array type.</param>
    /// <returns>The typed array, reusing the source when its type already matches.</returns>
    internal static object Convert(IPgArray array, Type type)
        => Convert<bool>(array, type) ?? Convert<bool?>(array, type) ?? Convert<byte[]>(array, type) ??
           Convert<sbyte>(array, type) ?? Convert<sbyte?>(array, type) ?? Convert<short>(array, type) ?? Convert<short?>(array, type) ??
           Convert<int>(array, type) ?? Convert<int?>(array, type) ?? Convert<long>(array, type) ?? Convert<long?>(array, type) ??
           Convert<uint>(array, type) ?? Convert<uint?>(array, type) ?? Convert<float>(array, type) ?? Convert<float?>(array, type) ??
           Convert<double>(array, type) ?? Convert<double?>(array, type) ?? Convert<string>(array, type) ??
           Convert<Guid>(array, type) ?? Convert<Guid?>(array, type) ?? Convert<PgJson>(array, type) ?? Convert<PgJson?>(array, type) ??
           Convert<PgJsonb>(array, type) ?? Convert<PgJsonb?>(array, type) ?? Convert<PgNumeric>(array, type) ?? Convert<PgNumeric?>(array, type) ??
           Convert<decimal>(array, type) ?? Convert<decimal?>(array, type) ?? Convert<PgDate>(array, type) ?? Convert<PgDate?>(array, type) ??
           Convert<DateOnly>(array, type) ?? Convert<DateOnly?>(array, type) ?? Convert<PgTime>(array, type) ?? Convert<PgTime?>(array, type) ??
           Convert<TimeOnly>(array, type) ?? Convert<TimeOnly?>(array, type) ?? Convert<PgTimeTz>(array, type) ?? Convert<PgTimeTz?>(array, type) ??
           Convert<PgTimestamp>(array, type) ?? Convert<PgTimestamp?>(array, type) ?? Convert<DateTime>(array, type) ?? Convert<DateTime?>(array, type) ??
           Convert<PgTimestampTz>(array, type) ?? Convert<PgTimestampTz?>(array, type) ?? Convert<DateTimeOffset>(array, type) ?? Convert<DateTimeOffset?>(array, type) ??
           Convert<PgInterval>(array, type) ?? Convert<PgInterval?>(array, type) ?? Convert<TimeSpan>(array, type) ?? Convert<TimeSpan?>(array, type) ??
           throw new InvalidCastException($"The PostgreSQL array cannot be read as '{type}'.");

    /// <summary>
    /// Copies a supported managed vector into an array with PostgreSQL's default lower bound of one.
    /// </summary>
    /// <param name="value">The vector; binary elements remain managed byte-array references.</param>
    /// <returns>The shape-preserving array, with rank zero for an empty vector.</returns>
    internal static IPgArray Wrap(Array value) => value switch
    {
        bool[] items => new PgArray<bool>(items), bool?[] items => new PgArray<bool?>(items),
        byte[][] items => new PgArray<byte[]>(items), string[] items => new PgArray<string>(items),
        sbyte[] items => new PgArray<sbyte>(items), sbyte?[] items => new PgArray<sbyte?>(items),
        short[] items => new PgArray<short>(items), short?[] items => new PgArray<short?>(items),
        int[] items => new PgArray<int>(items), int?[] items => new PgArray<int?>(items),
        long[] items => new PgArray<long>(items), long?[] items => new PgArray<long?>(items),
        uint[] items => new PgArray<uint>(items), uint?[] items => new PgArray<uint?>(items),
        float[] items => new PgArray<float>(items), float?[] items => new PgArray<float?>(items),
        double[] items => new PgArray<double>(items), double?[] items => new PgArray<double?>(items),
        Guid[] items => new PgArray<Guid>(items), Guid?[] items => new PgArray<Guid?>(items),
        PgJson[] items => new PgArray<PgJson>(items), PgJson?[] items => new PgArray<PgJson?>(items),
        PgJsonb[] items => new PgArray<PgJsonb>(items), PgJsonb?[] items => new PgArray<PgJsonb?>(items),
        PgNumeric[] items => new PgArray<PgNumeric>(items), PgNumeric?[] items => new PgArray<PgNumeric?>(items),
        decimal[] items => new PgArray<decimal>(items), decimal?[] items => new PgArray<decimal?>(items),
        PgDate[] items => new PgArray<PgDate>(items), PgDate?[] items => new PgArray<PgDate?>(items),
        DateOnly[] items => new PgArray<DateOnly>(items), DateOnly?[] items => new PgArray<DateOnly?>(items),
        PgTime[] items => new PgArray<PgTime>(items), PgTime?[] items => new PgArray<PgTime?>(items),
        TimeOnly[] items => new PgArray<TimeOnly>(items), TimeOnly?[] items => new PgArray<TimeOnly?>(items),
        PgTimeTz[] items => new PgArray<PgTimeTz>(items), PgTimeTz?[] items => new PgArray<PgTimeTz?>(items),
        PgTimestamp[] items => new PgArray<PgTimestamp>(items), PgTimestamp?[] items => new PgArray<PgTimestamp?>(items),
        DateTime[] items => new PgArray<DateTime>(items), DateTime?[] items => new PgArray<DateTime?>(items),
        PgTimestampTz[] items => new PgArray<PgTimestampTz>(items), PgTimestampTz?[] items => new PgArray<PgTimestampTz?>(items),
        DateTimeOffset[] items => new PgArray<DateTimeOffset>(items), DateTimeOffset?[] items => new PgArray<DateTimeOffset?>(items),
        PgInterval[] items => new PgArray<PgInterval>(items), PgInterval?[] items => new PgArray<PgInterval?>(items),
        TimeSpan[] items => new PgArray<TimeSpan>(items), TimeSpan?[] items => new PgArray<TimeSpan?>(items),
        _ => throw new NotSupportedException("Use a supported scalar vector or PgArray<T> for a PostgreSQL array."),
    };

    /// <summary>
    /// Preserves array shape while applying exact scalar conversions and NULL checks to each element.
    /// </summary>
    /// <typeparam name="T">The requested scalar element type.</typeparam>
    /// <param name="array">The owned source array.</param>
    /// <returns>The original array when its type matches, otherwise a new typed copy.</returns>
    internal static PgArray<T> Cast<T>(IPgArray array)
    {
        if (array is PgArray<T> typed)
        {
            return typed;
        }

        if (SpiType.GetOid<T>() != array.ElementOid)
        {
            throw new InvalidCastException($"Array element OID {array.ElementOid} cannot be read as '{typeof(T)}'.");
        }

        var values = new T[array.Count];
        for (int index = 0; index < values.Length; index++)
        {
            values[index] = SpiRow.Convert<T>(array.GetElement(index));
        }

        return new PgArray<T>(values, ([.. array.Lengths], [.. array.LowerBounds]));
    }

    private static bool Matches<T>(Type type) => type == typeof(T[]) || type == typeof(PgArray<T>);

    private static object? Convert<T>(IPgArray array, Type type)
    {
        if (!Matches<T>(type))
        {
            return null;
        }

        PgArray<T> result = Cast<T>(array);
        return type == typeof(T[]) ? result.ToVector() : result;
    }
}
