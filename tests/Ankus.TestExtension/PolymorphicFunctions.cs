using System.Globalization;

namespace Ankus.TestExtension;

/// <summary>
/// Exercises polymorphic values and arrays through real PostgreSQL scalar and set calls.
/// </summary>
public static class PolymorphicFunctions
{
    /// <summary>
    /// Returns the exact input type and value, including SQL NULL.
    /// </summary>
    /// <param name="value">The resolved input.</param>
    /// <returns>The unchanged value.</returns>
    [PgFunction]
    public static PgAnyElement? PolyIdentity(PgAnyElement? value) => value;

    /// <summary>
    /// Returns SQL NULL while preserving the caller's resolved result type.
    /// </summary>
    /// <param name="value">The type witness.</param>
    /// <returns>SQL NULL, subject to the resolved type's constraints.</returns>
    [PgFunction]
    public static PgAnyElement? PolyNull(PgAnyElement? value) => null;

    /// <summary>
    /// Returns an array without losing bounds, shape, element identity, or NULL cells.
    /// </summary>
    /// <param name="value">The resolved array.</param>
    /// <returns>The unchanged array.</returns>
    [PgFunction]
    public static PgAnyArray? PolyArrayIdentity(PgAnyArray? value) => value;

    /// <summary>
    /// Reports the input's actual type and server-formatted text.
    /// </summary>
    /// <param name="value">The nullable polymorphic input.</param>
    /// <returns>The type and value.</returns>
    [PgFunction]
    public static string PolyDescribe(PgAnyElement? value)
        => value is null ? "NULL" : string.Create(CultureInfo.InvariantCulture, $"{value.TypeOid}|{value.Datum.ToPostgresString()}");

    /// <summary>
    /// Reports exact shape and nullable cells, including types unknown to managed mappings.
    /// </summary>
    /// <param name="array">The polymorphic input array.</param>
    /// <returns>The shape and ordered cell text.</returns>
    [PgFunction]
    public static string PolyArrayShape(PgAnyArray array)
    {
        string shape = string.Join(";", Enumerable.Range(0, array.Rank).Select(dimension =>
            string.Create(CultureInfo.InvariantCulture, $"{array.GetLowerBound(dimension)}:{array.GetLength(dimension)}")));
        string values = string.Join(";", array.Select(value => value?.Datum.ToPostgresString() ?? "NULL"));
        return string.Create(CultureInfo.InvariantCulture, $"{array.ElementTypeOid}|{array.Rank}|{array.Count}|{shape}|{values}");
    }

    /// <summary>
    /// Returns raw cells over multiple PostgreSQL calls.
    /// </summary>
    /// <param name="array">The retained array.</param>
    /// <returns>The nullable elements.</returns>
    [PgFunction]
    public static IEnumerable<PgAnyElement?> PolyElements(PgAnyArray array)
    {
        foreach (PgAnyElement? value in array)
        {
            yield return value;
        }
    }

    /// <summary>
    /// Materializes the same polymorphic element contract.
    /// </summary>
    /// <param name="array">The retained array.</param>
    /// <returns>The nullable elements.</returns>
    [PgFunction(SetMode = PgSetMode.Materialize)]
    public static IEnumerable<PgAnyElement?> PolyMaterialized(PgAnyArray array) => PolyElements(array);

    /// <summary>
    /// Emits concrete and polymorphic TABLE columns together.
    /// </summary>
    /// <param name="array">The retained array.</param>
    /// <returns>The ordinal and nullable value of each cell.</returns>
    [PgFunction]
    public static IEnumerable<(int Ordinal, PgAnyElement? Value)> PolyTable(PgAnyArray array)
    {
        for (int index = 0; index < array.Count; index++)
        {
            yield return (index, array[index]);
        }
    }

    /// <summary>
    /// Retains a polymorphic scalar across separate iterator advances.
    /// </summary>
    /// <param name="value">The retained input.</param>
    /// <param name="count">The row count.</param>
    /// <returns>Repeated copies of the input.</returns>
    [PgFunction]
    public static IEnumerable<PgAnyElement?> PolyRepeat(PgAnyElement? value, int count)
    {
        for (int index = 0; index < count; index++)
        {
            yield return value;
        }
    }

    /// <summary>
    /// Calls a polymorphic function using a wrapper as a typed argument.
    /// </summary>
    /// <param name="value">The exact input.</param>
    /// <returns>The independently owned nested result.</returns>
    [PgFunction]
    public static PgAnyElement PolyNested(PgAnyElement value)
        => new(PgFunctions.CallRaw("datatype.poly_identity", PgMemoryContext.Current, PgFunctionArgument.Create(value)));

    /// <summary>
    /// Passes a polymorphic array to PostgreSQL's built-in JSON converter.
    /// </summary>
    /// <param name="array">The typed native array.</param>
    /// <returns>The built-in JSON result.</returns>
    [PgFunction]
    public static PgJson PolyArrayJson(PgAnyArray array)
        => PgFunctions.Call<PgJson>("pg_catalog.array_to_json", PgFunctionArgument.Create(array));

    /// <summary>
    /// Copies arrays and cells out of a short-lived owner before releasing it.
    /// </summary>
    /// <param name="array">The typed input array.</param>
    /// <returns>The independently owned array.</returns>
    [PgFunction]
    public static PgAnyArray PolyArrayOwnership(PgAnyArray array)
    {
        PgAnyArray expired;
        PgAnyArray retained;
        using (PgMemoryContext owner = PgMemoryContext.Create("temporary polymorphic array"))
        {
            expired = array.CopyTo(owner);
            retained = expired.CopyTo(PgMemoryContext.Current);
        }

        try
        {
            expired.Datum.DangerousGetBits();
            throw new InvalidOperationException("Expired array remained accessible.");
        }
        catch (ObjectDisposedException)
        {
            foreach (PgAnyElement? value in expired)
            {
                if (value is null)
                {
                    continue;
                }

                try
                {
                    value.Datum.DangerousGetBits();
                    throw new InvalidOperationException("Expired array cell remained accessible.");
                }
                catch (ObjectDisposedException)
                {
                    // Each cell must retain the original array owner's generation.
                }
            }
        }

        return retained;
    }

    /// <summary>
    /// Validates a runtime conversion to an array using the value's real catalog type.
    /// </summary>
    /// <param name="value">The candidate native array.</param>
    /// <returns>The checked flattened count.</returns>
    [PgFunction]
    public static int PolyArrayCount(PgAnyElement value) => new PgAnyArray(value.Datum).Count;

    /// <summary>
    /// Exercises ordinary temporal values whose auxiliary fields resemble raw transport markers.
    /// </summary>
    /// <returns>The exact transported month and timezone offset values.</returns>
    [PgFunction]
    public static string PolyMarkerSafety()
    {
        PgInterval interval = Spi.ExecuteScalar<PgInterval>("SELECT $1", SpiParameter.Create(new PgInterval(-6, 0, 42)));
        PgTimeTz time = Spi.ExecuteScalar<PgTimeTz>("SELECT $1", SpiParameter.Create(new PgTimeTz(new PgTime(42), -6)));
        return string.Create(CultureInfo.InvariantCulture, $"{interval.Months}|{interval.Microseconds}|{time.OffsetSeconds}");
    }

    /// <summary>
    /// Produces an incompatible or stale raw result to exercise return validation.
    /// </summary>
    /// <param name="value">The expected result type witness.</param>
    /// <param name="stale">Whether to release the source owner before returning.</param>
    /// <returns>The intentionally invalid result.</returns>
    [PgFunction]
    public static PgAnyElement PolyInvalid(PgAnyElement value, bool stale)
    {
        if (stale)
        {
            using PgMemoryContext owner = PgMemoryContext.Create("expired polymorphic output");
            return value.CopyTo(owner);
        }

        using SpiRawResult result = Spi.QueryRaw("SELECT 'wrong type'::text");
        return new PgAnyElement(result[0][0].CopyTo(PgMemoryContext.Current));
    }

    /// <summary>
    /// Copies a value out of a released owner and verifies exact managed conversion checks.
    /// </summary>
    /// <param name="value">The integer input.</param>
    /// <returns>The copied integer and conversion failure marker.</returns>
    [PgFunction]
    public static string PolyOwnership(PgAnyElement value)
    {
        PgAnyElement retained;
        PgAnyElement expired;
        using (PgMemoryContext owner = PgMemoryContext.Create("temporary polymorphic value"))
        {
            expired = value.CopyTo(owner);
            retained = expired.CopyTo(PgMemoryContext.Current);
        }

        string marker;
        try
        {
            expired.Read<int>();
            throw new InvalidOperationException("Expired storage remained accessible.");
        }
        catch (ObjectDisposedException)
        {
            marker = "expired";
        }

        try
        {
            retained.Read<string>();
            throw new InvalidOperationException("Incorrect managed conversion was accepted.");
        }
        catch (InvalidCastException)
        {
            marker += "|exact";
        }

        return string.Create(CultureInfo.InvariantCulture, $"{retained.Read<int>()}|{marker}");
    }
}
