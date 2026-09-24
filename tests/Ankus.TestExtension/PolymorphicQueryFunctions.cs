using System.Globalization;

namespace Ankus.TestExtension;

/// <summary>
/// Exercises typed polymorphic result conversion, native ownership, and first-row SPI semantics.
/// </summary>
public static class PolymorphicQueryFunctions
{
    /// <summary>
    /// Returns a nullable result after its query, plan, or function-call storage has been released.
    /// </summary>
    /// <param name="value">The resolved type witness.</param>
    /// <param name="mode">The result API to exercise.</param>
    /// <param name="call">The raw input including typed NULL.</param>
    /// <returns>The independently retained query result.</returns>
    [PgFunction]
    public static PgAnyElement? PolyQueryValue(PgAnyElement? value, int mode, PgFunctionContext call)
        => ReadResult<PgAnyElement?>(call.Arguments[0], mode);

    /// <summary>
    /// Returns an array after converting its eager cells and closing its query owner.
    /// </summary>
    /// <param name="value">The nullable array witness.</param>
    /// <param name="mode">The result API.</param>
    /// <param name="call">The exact input identity and NULL flag.</param>
    /// <returns>The retained array.</returns>
    [PgFunction]
    public static PgAnyArray? PolyQueryArray(PgAnyArray? value, int mode, PgFunctionContext call)
    {
        PgAnyArray? result = ReadResult<PgAnyArray?>(call.Arguments[0], mode);
        if (result is not null)
        {
            foreach (PgAnyElement? cell in result)
            {
                _ = cell?.Datum.ToPostgresString();
            }
        }

        return result;
    }

    /// <summary>
    /// Reports array identity without PostgreSQL's anyarray argument coercion stripping a domain first.
    /// </summary>
    /// <param name="value">The exact domain or array input.</param>
    /// <param name="mode">The result API.</param>
    /// <param name="call">The input datum.</param>
    /// <returns>The retained domain OID and complete array shape.</returns>
    [PgFunction]
    public static string PolyQueryArrayInfo(PgAnyElement value, int mode, PgFunctionContext call)
    {
        PgAnyArray result = ReadResult<PgAnyArray>(call.Arguments[0], mode);
        return string.Create(CultureInfo.InvariantCulture, $"{result.TypeOid}|{PolymorphicFunctions.PolyArrayShape(result)}");
    }

    /// <summary>
    /// Retains a query result across separate iterator advances and early termination.
    /// </summary>
    /// <param name="value">The type witness.</param>
    /// <param name="mode">The result API.</param>
    /// <param name="count">The requested row count.</param>
    /// <param name="call">The iterator-owned input.</param>
    /// <returns>The same retained result on each advance.</returns>
    [PgFunction]
    public static IEnumerable<PgAnyElement?> PolyQueryRepeat(PgAnyElement? value, int mode, int count, PgFunctionContext call)
    {
        PgAnyElement? result = ReadResult<PgAnyElement?>(call.Arguments[0], mode);
        for (int index = 0; index < count; index++)
        {
            yield return result;
        }
    }

    /// <summary>
    /// Retains typed query values while materialization resets its temporary row context.
    /// </summary>
    /// <param name="value">The type witness.</param>
    /// <param name="mode">The result API.</param>
    /// <param name="count">The requested row count.</param>
    /// <param name="call">The iterator-owned input.</param>
    /// <returns>The retained result on each materialized row.</returns>
    [PgFunction(SetMode = PgSetMode.Materialize)]
    public static IEnumerable<PgAnyElement?> PolyQueryMaterialized(PgAnyElement? value, int mode, int count, PgFunctionContext call)
        => PolyQueryRepeat(value, mode, count, call);

    /// <summary>
    /// Verifies typed raw rows and cursor batches share their owner's lifetime while copies survive disposal.
    /// </summary>
    /// <param name="value">An array with a present first element.</param>
    /// <param name="cursor">Whether to fetch a cursor batch instead of a query result.</param>
    /// <returns>The retained first cell and exact shape.</returns>
    [PgFunction]
    public static string PolyQueryOwnership(PgAnyArray value, bool cursor)
    {
        SpiParameter parameter = SpiParameter.Create(value);
        PgAnyArray expired;
        PgAnyElement cell;
        PgAnyArray retained;
        int independent;
        using (SpiRawResult raw = cursor ? Fetch(parameter) : Spi.QueryRaw("SELECT $1 AS value, 42 AS number", parameter))
        {
            expired = raw[0].Get<PgAnyArray>("value");
            cell = expired[0] ?? throw new InvalidOperationException("A present first cell is required.");
            independent = raw[0].Get<int>(1);
            retained = expired.CopyTo(PgMemoryContext.Current);
            if (raw[0].Get<PgAnyElement>(0).TypeOid != value.TypeOid)
            {
                throw new InvalidOperationException("Raw row lost its declared type.");
            }
        }

        ExpectDisposed(() => expired.Read<PgAnyArray>());
        ExpectDisposed(() => cell.Read<PgAnyElement>());
        using PgMemoryContext reset = PgMemoryContext.Create("polymorphic reset probe");
        PgAnyElement resetCell = retained[0]!.CopyTo(reset);
        reset.Reset();
        ExpectDisposed(() => resetCell.Read<PgAnyElement>());
        return string.Create(CultureInfo.InvariantCulture,
            $"{independent}|{retained[0]!.Datum.ToPostgresString()}|{PolymorphicFunctions.PolyArrayShape(retained)}");
    }

    /// <summary>
    /// Executes one supplied first-row query for NULL, empty, missing-column, and write-effect tests.
    /// </summary>
    /// <param name="sql">The test SQL.</param>
    /// <param name="columns">The requested column count.</param>
    /// <returns>The result or the exact failure category.</returns>
    [PgFunction]
    public static string PolyQueryContract(string sql, int columns)
    {
        try
        {
            PgAnyElement? value;
            if (columns == 1)
            {
                value = Spi.ExecuteScalar<PgAnyElement?>(sql);
            }
            else if (columns == 2)
            {
                (value, int? second) = Spi.ExecuteScalars<PgAnyElement?, int?>(sql);
                if (second.HasValue && second != 73)
                {
                    throw new InvalidOperationException("Wrong second value.");
                }
            }
            else if (columns == 4)
            {
                (int first, value) = Spi.ExecuteScalars<int, PgAnyElement?>(sql);
                if (first != 73)
                {
                    throw new InvalidOperationException("Wrong leading value.");
                }
            }
            else if (columns == 5)
            {
                (int first, PgAnyArray array, string third) = Spi.ExecuteScalars<int, PgAnyArray, string>(sql);
                if (first != 73 || third != "owned" || !array.Read<int[]>().AsSpan().SequenceEqual([1, 2]))
                {
                    throw new InvalidOperationException("Wrong middle array or surrounding values.");
                }

                value = array.Read<PgAnyElement>();
            }
            else
            {
                (int? first, string? second, value) = Spi.ExecuteScalars<int?, string?, PgAnyElement?>(sql);
                if (first.HasValue && (first != 73 || second != "owned"))
                {
                    throw new InvalidOperationException("Wrong leading values.");
                }
            }

            return value?.Datum.ToPostgresString() ?? "NULL";
        }
        catch (InvalidCastException)
        {
            return "InvalidCastException";
        }
        catch (InvalidOperationException exception)
        {
            return exception.Message;
        }
    }

    /// <summary>
    /// Captures function-result validation errors before invoking side-effecting scalar functions as arrays.
    /// </summary>
    /// <param name="name">The test function name.</param>
    /// <returns>The native SQLSTATE.</returns>
    [PgFunction]
    public static string PolyCallArrayError(string name)
    {
        try
        {
            _ = PgFunctions.Call<PgAnyArray?>(name);
            return "unexpected success";
        }
        catch (PgException exception)
        {
            return exception.SqlState;
        }
    }

    /// <summary>
    /// Reads one polymorphic column through every scalar, tuple, prepared, session, and catalog result entry point.
    /// </summary>
    /// <typeparam name="T">The requested wrapper.</typeparam>
    /// <param name="value">The exact typed input.</param>
    /// <param name="mode">The entry point selector.</param>
    /// <returns>The retained result after temporary owners end.</returns>
    private static T ReadResult<T>(PgDatum value, int mode)
    {
        const string sql = "SELECT $1, 73, 'owned'::text";
        SpiParameter parameter = SpiParameter.Create(value);
        switch (mode)
        {
            case 0:
                return Spi.ExecuteScalar<T>(sql, parameter);
            case 1:
                return Spi.Connect(session => session.ExecuteScalar<T>(sql, parameter));
            case 2:
                using (SpiPreparedStatement plan = Spi.PrepareWithTypeOids(sql, value.TypeOid))
                {
                    return plan.ExecuteScalar<T>(parameter);
                }

            case 3:
                using (SpiPreparedStatement plan = Spi.Connect(session => session.PrepareWithTypeOids(sql, value.TypeOid).Keep()))
                {
                    return plan.ExecuteScalar<T>(parameter);
                }

            case 4:
                return CheckPair(Spi.ExecuteScalars<T, int>(sql, parameter));
            case 5:
                return CheckTriple(Spi.ExecuteScalars<T, int, string>(sql, parameter));
            case 6:
                return CheckPair(Spi.Connect(session => session.ExecuteScalars<T, int>(sql, parameter)));
            case 7:
                return CheckTriple(Spi.Connect(session => session.ExecuteScalars<T, int, string>(sql, parameter)));
            case 8:
                return Spi.Connect(session =>
                {
                    using SpiPreparedStatement plan = session.PrepareWithTypeOids(sql, value.TypeOid);
                    return CheckPair(plan.ExecuteScalars<T, int>(parameter));
                });
            case 9:
                return Spi.Connect(session =>
                {
                    using SpiPreparedStatement plan = session.PrepareWithTypeOids(sql, value.TypeOid);
                    return CheckTriple(plan.ExecuteScalars<T, int, string>(parameter));
                });
            case 10:
                return Spi.Connect(_ => PgFunctions.Call<T>("datatype.poly_identity", PgFunctionArgument.Create(value)));
            case 11:
                uint oid = Spi.ExecuteScalar<uint>("SELECT 'datatype.poly_identity(anyelement)'::regprocedure::oid");
                return PgFunctions.Call<T>(oid, new PgFunctionCallOptions(), PgFunctionArgument.Create(value));
            default:
                throw new ArgumentOutOfRangeException(nameof(mode));
        }
    }

    /// <summary>
    /// Checks concrete columns alongside the polymorphic result.
    /// </summary>
    private static T CheckPair<T>((T Value, int Number) result)
        => result.Number == 73 ? result.Value : throw new InvalidOperationException("Wrong pair value.");

    /// <summary>
    /// Checks concrete numeric and text columns alongside the polymorphic result.
    /// </summary>
    private static T CheckTriple<T>((T Value, int Number, string Text) result)
        => result.Number == 73 && result.Text == "owned" ? result.Value : throw new InvalidOperationException("Wrong triple values.");

    /// <summary>
    /// Retains a raw batch after both the session and cursor close.
    /// </summary>
    private static SpiRawResult Fetch(SpiParameter parameter)
        => Spi.Connect(session =>
        {
            using SpiCursor cursor = session.OpenCursor("SELECT $1 AS value, 42 AS number", parameter);
            return cursor.FetchRaw(1);
        });

    /// <summary>
    /// Fails unless checked native access rejects the released owner.
    /// </summary>
    private static void ExpectDisposed(Action action)
    {
        try
        {
            action();
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        throw new InvalidOperationException("Released polymorphic storage remained accessible.");
    }
}
