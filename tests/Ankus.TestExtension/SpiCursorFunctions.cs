namespace Ankus.TestExtension;

/// <summary>
/// Exercises cursor batching, portal lifetime tracking, detached ownership, and native error cleanup.
/// </summary>
public static class SpiCursorFunctions
{
    private static SpiCursor? s_cursor;

    /// <summary>
    /// Fetches all rows in batches, then reads the retained managed copies after closing the cursor.
    /// </summary>
    /// <param name="count">The number of generated rows.</param>
    /// <param name="batchSize">The fetch batch size.</param>
    /// <param name="prepared">Whether to open from a plan that is immediately disposed.</param>
    /// <returns>The ordered values and final empty batch's metadata.</returns>
    [PgFunction]
    public static string CursorBatches(int count, int batchSize, bool prepared)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        const string sql = "SELECT n AS value FROM generate_series(1, $1) AS s(n)";
        SpiCursor cursor;
        if (prepared)
        {
            using SpiPreparedStatement statement = Spi.Prepare(sql, typeof(int));
            cursor = statement.OpenCursor(readOnly: true, SpiParameter.Create(count));
        }
        else
        {
            cursor = Spi.OpenCursor(sql, readOnly: true, SpiParameter.Create(count));
        }

        var rows = new List<SpiRow>();
        SpiResult batch;
        using (cursor)
        {
            do
            {
                batch = cursor.Fetch(batchSize);
                rows.AddRange(batch);
            }

            while (batch.Count != 0);
        }

        Spi.Query("SELECT repeat('overwrite', 10000)");
        return string.Join(',', rows.Select(static row => row.Get<int>("value"))) + "|" +
            string.Join(',', batch.Columns.Select(static column => column.Name + ":" + column.TypeOid));
    }

    /// <summary>
    /// Observes PostgreSQL's zero-count fetch behavior before and after the first row.
    /// </summary>
    /// <param name="scroll">Whether to explicitly declare a scrollable cursor.</param>
    /// <returns>The batch sizes and fetched values.</returns>
    [PgFunction]
    public static string CursorZeroFetch(bool scroll)
    {
        if (scroll)
        {
            Spi.Execute("DECLARE current_row_cursor SCROLL CURSOR FOR SELECT generate_series(1, 2)");
        }

        using SpiCursor cursor = scroll ? Spi.FindCursor("current_row_cursor") : Spi.OpenCursor("SELECT generate_series(1, 2)");
        int before = cursor.Fetch(0).Count;
        int first = cursor.Fetch(1)[0].Get<int>(0);
        SpiResult current = cursor.Fetch(0);
        int second = cursor.Fetch(1)[0].Get<int>(0);
        return $"{before}:{first}:{current.Count}:{current[0].Get<int>(0)}:{second}";
    }

    /// <summary>
    /// Fetches in both directions through a scrollable PostgreSQL portal.
    /// </summary>
    /// <returns>The ordered batches after each movement.</returns>
    [PgFunction]
    public static string CursorBackward()
    {
        Spi.Execute("DECLARE backward_cursor SCROLL CURSOR FOR SELECT generate_series(1, 5)");
        using SpiCursor cursor = Spi.FindCursor("backward_cursor");
        string first = Values(cursor.Fetch(4));
        string previous = Values(cursor.Fetch(2, forward: false));
        string next = Values(cursor.Fetch(2));
        return first + "|" + previous + "|" + next;
    }

    /// <summary>
    /// Round-trips a nullable text parameter through a cursor and reads it after disposing the portal.
    /// </summary>
    /// <param name="value">The text input.</param>
    /// <returns>The owned text result.</returns>
    [PgFunction]
    public static string? CursorText(string? value)
    {
        SpiResult result;
        using (SpiCursor cursor = Spi.OpenCursor("SELECT $1", SpiParameter.Create(value)))
        {
            result = cursor.Fetch(1);
        }

        return result[0].Get<string?>(0);
    }

    /// <summary>
    /// Round-trips a nullable binary parameter through a cursor.
    /// </summary>
    /// <param name="value">The binary input.</param>
    /// <returns>The binary result.</returns>
    [PgFunction]
    public static byte[]? CursorBytes(byte[]? value)
    {
        using SpiCursor cursor = Spi.OpenCursor("SELECT $1", SpiParameter.Create(value));
        return cursor.Fetch(1)[0].Get<byte[]?>(0);
    }

    /// <summary>
    /// Opens a cursor for one batch with an explicit execution mode.
    /// </summary>
    /// <param name="sql">The row-producing SQL command.</param>
    /// <param name="readOnly">Whether to use read-only execution.</param>
    /// <param name="count">The requested batch size.</param>
    /// <returns>The batch's comma-separated integer values.</returns>
    [PgFunction]
    public static string CursorFirstBatch(string sql, bool readOnly, int count)
    {
        using SpiCursor cursor = Spi.OpenCursor(sql, readOnly);
        return Values(cursor.Fetch(count));
    }

    /// <summary>
    /// Stores a cursor beyond the current extension callback.
    /// </summary>
    /// <param name="sql">The cursor query.</param>
    /// <returns>The PostgreSQL portal name.</returns>
    [PgFunction]
    public static string CursorCache(string sql)
    {
        s_cursor?.Dispose();
        s_cursor = Spi.OpenCursor(sql);
        return s_cursor.Name;
    }

    /// <summary>
    /// Stores ownership of an existing PostgreSQL cursor by name.
    /// </summary>
    /// <param name="name">The portal name.</param>
    /// <returns>The resolved portal name.</returns>
    [PgFunction]
    public static string CursorCacheNamed(string name)
    {
        s_cursor?.Dispose();
        s_cursor = Spi.FindCursor(name);
        return s_cursor.Name;
    }

    /// <summary>
    /// Fetches a batch through the cached cursor object.
    /// </summary>
    /// <param name="count">The requested row count.</param>
    /// <returns>The comma-separated values.</returns>
    [PgFunction]
    public static string CursorCachedRows(int count) => Values(Cached().Fetch(count));

    /// <summary>
    /// Detaches the cached cursor and preserves its native portal.
    /// </summary>
    /// <returns>The name used to find the cursor again.</returns>
    [PgFunction]
    public static string CursorDetachCached() => Cached().Detach();

    /// <summary>
    /// Disposes the cached managed owner without dropping the object itself.
    /// </summary>
    [PgFunction]
    public static void CursorDisposeCached() => s_cursor?.Dispose();

    /// <summary>
    /// Finds an existing portal, fetches rows, and optionally leaves it open for another callback.
    /// </summary>
    /// <param name="name">The portal name.</param>
    /// <param name="count">The requested row count.</param>
    /// <param name="detach">Whether to detach rather than close after the fetch.</param>
    /// <returns>The comma-separated values.</returns>
    [PgFunction]
    public static string CursorFetchNamed(string name, int count, bool detach)
    {
        using SpiCursor cursor = Spi.FindCursor(name);
        SpiResult result = cursor.Fetch(count);
        if (detach)
        {
            cursor.Detach();
        }

        return Values(result);
    }

    /// <summary>
    /// Recovers from a native error during a batch fetch and executes a new SPI query.
    /// </summary>
    /// <returns>The SQLSTATE and the follow-up scalar.</returns>
    [PgFunction]
    public static string CursorRecover()
    {
        using SpiCursor cursor = Spi.OpenCursor("SELECT n / (3 - n) FROM generate_series(1, 3) AS s(n)");
        try
        {
            cursor.Fetch(3);
            return "unexpected success";
        }
        catch (PgException exception)
        {
            return exception.SqlState + ":" + Spi.ExecuteScalar<int>("SELECT 42");
        }
    }

    /// <summary>
    /// Attempts a cursor operation from a worker thread and then fetches successfully on the backend thread.
    /// </summary>
    /// <param name="mode">Zero fetches, one disposes, and two detaches from the worker thread.</param>
    /// <returns>The affinity diagnostic and successful backend result.</returns>
    [PgFunction]
    public static string CursorWorker(int mode)
    {
        using SpiCursor cursor = Spi.OpenCursor("SELECT 42");
        string diagnostic = Task.Run(() =>
        {
            try
            {
                switch (mode)
                {
                    case 0: cursor.Fetch(1); break;
                    case 1: cursor.Dispose(); break;
                    case 2: cursor.Detach(); break;
                    default: throw new ArgumentOutOfRangeException(nameof(mode));
                }

                return "unexpected success";
            }
            catch (InvalidOperationException exception)
            {
                return exception.Message;
            }
        }).GetAwaiter().GetResult();
        return diagnostic + "|" + cursor.Fetch(1)[0].Get<int>(0);
    }

    /// <summary>
    /// Attempts recursive disposal while the cached cursor is fetching this row.
    /// </summary>
    /// <param name="value">The row value.</param>
    /// <returns>The value when recursive disposal is rejected.</returns>
    [PgFunction]
    public static int CursorReentrantClose(int value)
    {
        try
        {
            Cached().Dispose();
            return -1;
        }
        catch (InvalidOperationException exception) when (exception.Message.Contains("while it is fetching", StringComparison.Ordinal))
        {
            return value;
        }
    }

    private static SpiCursor Cached() => s_cursor ?? throw new InvalidOperationException("No cached cursor.");

    private static string Values(SpiResult result) => string.Join(',', result.Select(static row => row.Get<int>(0)));
}
