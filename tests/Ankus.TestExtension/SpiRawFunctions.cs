using System.Globalization;

namespace Ankus.TestExtension;

/// <summary>
/// Exercises raw SPI type identity, native ownership, conversions, and recovery in PostgreSQL.
/// </summary>
public static class SpiRawFunctions
{
    /// <summary>
    /// Reads exact metadata and server-formatted values after releasing SPI sessions and plans.
    /// </summary>
    /// <param name="api">The standalone, session, retained-plan, or session-plan selection.</param>
    /// <param name="sql">The query.</param>
    /// <param name="after">Commands to run before inspecting the copied values.</param>
    /// <returns>Column metadata, processed count, and formatted rows.</returns>
    [PgFunction]
    public static string SpiRawSnapshot(int api, string sql, string after)
    {
        using SpiRawResult result = Query(api, sql);
        Spi.Execute(after);
        var rows = new List<string>();
        foreach (SpiRawRow row in result)
        {
            var cells = new List<string>();
            foreach (PgDatum value in row)
            {
                // Bind the owned native value again, retaining declared type and typed NULLs.
                using SpiPreparedStatement plan = Spi.PrepareWithTypeOids("SELECT $1", value.TypeOid);
                using SpiRawResult rebound = plan.QueryRaw(SpiParameter.Create(value));
                PgDatum copy = rebound[0][0];
                if (copy.TypeOid != value.TypeOid || copy.IsNull != value.IsNull || copy.ToPostgresString() != value.ToPostgresString())
                {
                    throw new PgException("P7900", "Raw parameter identity or value changed.");
                }

                cells.Add(value.Read(static datum => datum.ToPostgresString()) ?? "<null>");
            }

            rows.Add(string.Join("|", cells));
        }

        return string.Join(",", result.Columns.Select(static column => column.Name + ":" + column.TypeOid.ToString(CultureInfo.InvariantCulture))) +
            ";" + result.RowsAffected.ToString(CultureInfo.InvariantCulture) + ";" + string.Join("/", rows);
    }

    /// <summary>
    /// Converts raw datums through the normal exact-type and NULL rules.
    /// </summary>
    /// <param name="sql">The one-cell query.</param>
    /// <param name="kind">The requested managed conversion.</param>
    /// <returns>The converted representation or error with a successful follow-up scalar.</returns>
    [PgFunction]
    public static string SpiRawRead(string sql, int kind)
    {
        string text;
        try
        {
            using SpiRawResult result = Spi.QueryRaw(sql);
            PgDatum value = result[0][0];
            text = kind switch
            {
                0 => value.Read<int>().ToString(CultureInfo.InvariantCulture),
                1 => value.Read<long>().ToString(CultureInfo.InvariantCulture),
                2 => value.Read<string?>() ?? "<null>",
                3 => Convert.ToHexString(value.Read<byte[]>()),
                4 => BitConverter.DoubleToInt64Bits(value.Read<double>()).ToString("X16", CultureInfo.InvariantCulture),
                5 => value.Read<int?>()?.ToString(CultureInfo.InvariantCulture) ?? "<null>",
                _ => throw new ArgumentOutOfRangeException(nameof(kind)),
            };
        }
        catch (Exception exception) when (exception is PgException or InvalidCastException or InvalidOperationException)
        {
            text = exception is PgException postgres ? postgres.SqlState : exception.GetType().Name;
        }

        return text + "|" + Spi.ExecuteScalar<int>("SELECT 42").ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Copies native storage to another owner and verifies both disposal and reset invalidate old datums.
    /// </summary>
    /// <param name="sql">The source datum query.</param>
    /// <param name="reset">Whether to reset or delete the copied datum's owner.</param>
    /// <returns>The preserved value and the number of rejected stale accesses.</returns>
    [PgFunction]
    public static string SpiRawLifetime(string sql, bool reset)
    {
        using PgMemoryContext owner = PgMemoryContext.Create("raw datum test");
        using SpiRawResult result = Spi.Connect(session => session.QueryRaw(sql));
        PgDatum source = result[0][0];
        PgDatum copy = source.CopyTo(owner);
        result.Dispose();
        result.Dispose();
        int rejected = RejectStale(source);
        string text = copy.ToPostgresString() ?? "<null>";
        if (reset)
        {
            owner.Reset();
        }
        else
        {
            owner.Dispose();
        }

        rejected += RejectStale(copy);
        Spi.Execute("SELECT 1");
        return text + "|" + rejected.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Reads raw zero bits as a value and as SQL NULL using an explicit lifetime anchor.
    /// </summary>
    /// <returns>The independent value and NULL states.</returns>
    [PgFunction]
    public static string SpiRawZero()
    {
        using PgMemoryContext owner = PgMemoryContext.Create("raw zero");
        PgDatum zero = PgDatum.DangerousCreate(0, 23, owner);
        PgDatum nil = PgDatum.DangerousCreate(0, 23, owner, isNull: true);
        return zero.Read<int>().ToString(CultureInfo.InvariantCulture) + "|" + (nil.Read<int?>()?.ToString(CultureInfo.InvariantCulture) ?? "<null>");
    }

    /// <summary>
    /// Exercises row limits and native errors followed by same-callback recovery.
    /// </summary>
    /// <param name="api">The SPI owner selection.</param>
    /// <param name="sql">The commands to execute.</param>
    /// <param name="readOnly">The snapshot mode.</param>
    /// <param name="limit">The row limit.</param>
    /// <returns>The row counts or error SQLSTATE and a follow-up scalar.</returns>
    [PgFunction]
    public static string SpiRawOptions(int api, string sql, bool readOnly, int limit)
    {
        string outcome;
        try
        {
            using SpiRawResult result = Query(api, sql, readOnly, limit);
            outcome = result.Count.ToString(CultureInfo.InvariantCulture) + ":" + result.RowsAffected.ToString(CultureInfo.InvariantCulture);
        }
        catch (Exception exception) when (exception is PgException or ArgumentOutOfRangeException)
        {
            outcome = exception is PgException postgres ? postgres.SqlState : exception.GetType().Name;
        }

        return outcome + "|" + Spi.ExecuteScalar<int>("SELECT 42").ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Copies raw values into managed rows and tuples without retaining their native owner's lifetime.
    /// </summary>
    /// <param name="isNull">Whether to copy SQL NULL.</param>
    /// <param name="parameter">Whether the tuple edit uses an explicit parameter.</param>
    /// <param name="stale">Whether the source has already expired.</param>
    /// <returns>The two managed values after source disposal.</returns>
    [PgFunction]
    public static string RawManagedAssignment(bool isNull, bool parameter, bool stale)
    {
        using SpiRawResult result = Spi.QueryRaw("SELECT 42 AS value, NULL::int AS missing");
        PgDatum value = result[0][isNull ? 1 : 0];
        SpiRow row = Spi.Query("SELECT 17 AS value")[0];
        PgHeapTuple tuple = PgHeapTuple.Create(("value", SpiParameter.Create(17)));
        if (stale)
        {
            result.Dispose();
        }

        int rejected = 0;
        try
        {
            row.Set("value", value);
        }
        catch (ObjectDisposedException)
        {
            rejected++;
        }

        try
        {
            if (parameter)
            {
                tuple.Set("value", SpiParameter.Create(value));
            }
            else
            {
                tuple.Set("value", value);
            }
        }
        catch (ObjectDisposedException)
        {
            rejected++;
        }

        result.Dispose();
        return (row.Get<int?>("value")?.ToString(CultureInfo.InvariantCulture) ?? "<null>") + "|" +
            (tuple.Get<int?>("value")?.ToString(CultureInfo.InvariantCulture) ?? "<null>") + "|" +
            row.GetTypeOid("value").ToString(CultureInfo.InvariantCulture) + "|" + rejected.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Attempts both raw access and binding after the native owner expires.
    /// </summary>
    /// <param name="value">The stale datum.</param>
    /// <returns>The number of rejected operations.</returns>
    private static int RejectStale(PgDatum value)
    {
        int rejected = 0;
        try
        {
            value.DangerousGetBits();
        }
        catch (ObjectDisposedException)
        {
            rejected++;
        }

        try
        {
            Spi.Execute("SELECT $1", SpiParameter.Create(value));
        }
        catch (ObjectDisposedException)
        {
            rejected++;
        }

        return rejected;
    }

    /// <summary>
    /// Returns raw values after closing the SPI connection and prepared plan used to obtain them.
    /// </summary>
    /// <param name="api">The SPI owner selection.</param>
    /// <param name="sql">The query.</param>
    /// <param name="readOnly">The snapshot mode.</param>
    /// <param name="limit">The row limit.</param>
    /// <returns>The independently owned native result.</returns>
    private static SpiRawResult Query(int api, string sql, bool readOnly = false, int limit = 0)
    {
        if (api == 0)
        {
            return Spi.QueryRaw(sql, readOnly, limit);
        }

        if (api == 1)
        {
            return Spi.Connect(session => session.QueryRaw(sql, readOnly, limit));
        }

        if (api == 2)
        {
            using SpiPreparedStatement plan = Spi.Prepare(sql);
            return plan.QueryRaw(readOnly, limit);
        }

        return Spi.Connect(session =>
        {
            using SpiPreparedStatement plan = session.Prepare(sql);
            return plan.QueryRaw(readOnly, limit);
        });
    }
}
