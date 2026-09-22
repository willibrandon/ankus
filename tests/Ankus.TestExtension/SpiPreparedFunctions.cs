namespace Ankus.TestExtension;

/// <summary>
/// Exercises retained SPI plans, parameter contracts, invalidation, and backend-affine cleanup.
/// </summary>
public static class SpiPreparedFunctions
{
    private static SpiPreparedStatement? s_cached;

    /// <summary>
    /// Reuses a plan with different integer parameter values.
    /// </summary>
    /// <param name="count">The number of values to sum.</param>
    /// <returns>The sum produced through repeated plan execution.</returns>
    [PgFunction]
    public static int PreparedSum(int count)
    {
        using SpiPreparedStatement statement = Spi.Prepare("SELECT $1 + $2", typeof(int), typeof(int));
        int sum = 0;
        for (int value = 1; value <= count; value++)
        {
            sum = statement.ExecuteScalar<int>(SpiParameter.Create(sum), SpiParameter.Create(value));
        }

        return sum;
    }

    /// <summary>
    /// Reads copied text results after executing the same plan again with a nullable value.
    /// </summary>
    /// <param name="value">The second parameter value.</param>
    /// <returns>Both independently owned result values.</returns>
    [PgFunction]
    public static string PreparedText(string? value)
    {
        using SpiPreparedStatement statement = Spi.Prepare("SELECT $1 AS value", typeof(string));
        SpiResult first = statement.Query(SpiParameter.Create("first"));
        SpiResult second = statement.Query(SpiParameter.Create(value));
        return first[0].Get<string>("value") + ":" + (second[0].Get<string?>("value") ?? "<null>");
    }

    /// <summary>
    /// Round-trips a nullable binary parameter through a retained plan.
    /// </summary>
    /// <param name="value">The binary input.</param>
    /// <returns>The binary result.</returns>
    [PgFunction]
    public static byte[]? PreparedBytes(byte[]? value)
    {
        using SpiPreparedStatement statement = Spi.Prepare("SELECT $1", typeof(byte[]));
        return statement.ExecuteScalar<byte[]?>(SpiParameter.Create(value));
    }

    /// <summary>
    /// Executes a zero-parameter plan with explicit query options.
    /// </summary>
    /// <param name="sql">The SQL command.</param>
    /// <param name="readOnly">Whether to use a read-only SPI snapshot.</param>
    /// <param name="limit">The maximum returned rows.</param>
    /// <returns>The materialized row count.</returns>
    [PgFunction]
    public static int PreparedRows(string sql, bool readOnly, int limit)
    {
        using SpiPreparedStatement statement = Spi.Prepare(sql);
        return statement.Query(readOnly, limit).Count;
    }

    /// <summary>
    /// Reads a scalar from a zero-parameter retained plan.
    /// </summary>
    /// <param name="sql">The SQL command.</param>
    /// <returns>The first integer cell, or NULL.</returns>
    [PgFunction]
    public static int? PreparedScalar(string sql)
    {
        using SpiPreparedStatement statement = Spi.Prepare(sql);
        return statement.ExecuteScalar<int?>();
    }

    /// <summary>
    /// Recovers from a failed mutating plan and reuses that same plan with a valid parameter.
    /// </summary>
    /// <returns>The error SQLSTATE and successful result.</returns>
    [PgFunction]
    public static string PreparedRecover()
    {
        using SpiPreparedStatement statement = Spi.Prepare(
            "INSERT INTO prepared_values VALUES ($1); SELECT 12 / $1", typeof(int));
        try
        {
            statement.Execute(SpiParameter.Create(0));
            return "unexpected success";
        }
        catch (PgException exception)
        {
            return exception.SqlState + ":" + statement.ExecuteScalar<int>(SpiParameter.Create(2));
        }
    }

    /// <summary>
    /// Rejects malformed parameter lists before a native plan is entered, then reuses the plan successfully.
    /// </summary>
    /// <param name="mode">The invalid argument-list case.</param>
    /// <returns>The parameter diagnostic and follow-up result.</returns>
    [PgFunction]
    public static string PreparedInvalidArguments(int mode)
    {
        using SpiPreparedStatement statement = Spi.Prepare("SELECT $1", typeof(int?));
        try
        {
            switch (mode)
            {
                case 0: statement.Execute(); break;
                case 1: statement.Execute(SpiParameter.Create(1), SpiParameter.Create(2)); break;
                case 2: statement.Execute(SpiParameter.Create("wrong")); break;
                case 3: statement.Execute(default(SpiParameter)); break;
                default: throw new ArgumentOutOfRangeException(nameof(mode));
            }

            return "unexpected success";
        }
        catch (ArgumentException exception) when (exception.ParamName == "parameters")
        {
            return exception.Message + "|" + statement.ExecuteScalar<int>(SpiParameter.Create(42));
        }
    }

    /// <summary>
    /// Attempts execution or disposal from a worker thread, then confirms the plan remains usable.
    /// </summary>
    /// <param name="dispose">Whether to attempt disposal rather than execution.</param>
    /// <returns>The thread-affinity diagnostic and backend-thread result.</returns>
    [PgFunction]
    public static string PreparedWorker(bool dispose)
    {
        using SpiPreparedStatement statement = Spi.Prepare("SELECT 42");
        string diagnostic = Task.Run(() =>
        {
            try
            {
                if (dispose)
                {
                    statement.Dispose();
                }
                else
                {
                    statement.Execute();
                }

                return "unexpected success";
            }
            catch (InvalidOperationException exception)
            {
                return exception.Message;
            }
        }).GetAwaiter().GetResult();
        return diagnostic + "|" + statement.ExecuteScalar<int>();
    }

    /// <summary>
    /// Exercises scoped plan cleanup during both success and native query failure.
    /// </summary>
    /// <param name="fail">Whether to fail during execution.</param>
    /// <returns>The processed-row count on success.</returns>
    [PgFunction]
    public static long PreparedScoped(bool fail)
    {
        using SpiPreparedStatement statement = Spi.Prepare("SELECT 12 / $1", typeof(int));
        return statement.Execute(SpiParameter.Create(fail ? 0 : 2));
    }

    /// <summary>
    /// Replaces the backend's cached statement and exposes its declared parameter count.
    /// </summary>
    /// <param name="sql">The SQL text with one declared integer parameter.</param>
    /// <returns>The declared parameter count.</returns>
    [PgFunction]
    public static int PreparedCache(string sql)
    {
        s_cached?.Dispose();
        s_cached = Spi.Prepare(sql, typeof(int));
        return s_cached.ParameterCount;
    }

    /// <summary>
    /// Executes the already cached statement without preparing a replacement.
    /// </summary>
    /// <param name="value">The bound integer.</param>
    /// <returns>The first result value.</returns>
    [PgFunction]
    public static int PreparedCachedValue(int value)
        => (s_cached ?? throw new InvalidOperationException("No cached statement.")).ExecuteScalar<int>(SpiParameter.Create(value));

    /// <summary>
    /// Disposes the cached statement while retaining its managed object to exercise disposed-state checks.
    /// </summary>
    [PgFunction]
    public static void PreparedDisposeCached() => s_cached?.Dispose();

    /// <summary>
    /// Attempts to dispose the statement whose execution reentered this extension.
    /// </summary>
    /// <param name="value">The value returned after rejecting disposal.</param>
    /// <returns>The input value if disposal was safely rejected.</returns>
    [PgFunction]
    public static int PreparedReentrantDispose(int value)
    {
        try
        {
            s_cached?.Dispose();
            return -1;
        }
        catch (InvalidOperationException exception) when (exception.Message.Contains("while it is executing", StringComparison.Ordinal))
        {
            return value;
        }
    }

    /// <summary>
    /// Recursively executes the same retained statement without transferring its ownership.
    /// </summary>
    /// <param name="value">The upper bound of the recursive sum.</param>
    /// <returns>The recursive sum.</returns>
    [PgFunction]
    public static int PreparedRecursive(int value)
        => value == 0 ? 0 : value + PreparedCachedValue(value - 1);
}
