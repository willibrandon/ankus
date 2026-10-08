namespace Ankus.Examples.Threads;

/// <summary>
/// Ports pgrx's <c>pgthread</c> example to managed threads, which may compute but may not call PostgreSQL.
/// </summary>
/// <remarks>
/// PostgreSQL backends are single-threaded. Ankus grants backend access only to the thread on which PostgreSQL
/// invoked the current callback. SPI, logging, memory contexts and native bindings called from another thread
/// throw <see cref="InvalidOperationException"/> before reaching PostgreSQL.
/// </remarks>
public static class ThreadFunctions
{
    /// <summary>
    /// Returns pgrx's greeting.
    /// </summary>
    /// <returns>The greeting text.</returns>
    [PgFunction]
    public static string HelloPgthread() => "Hello, pgthread";

    /// <summary>
    /// Attempts the C example's thread SPI query from a new managed thread, then reports the rejection.
    /// </summary>
    /// <remarks>
    /// pgrx's C companion runs <c>SELECT 1;</c> through SPI on a POSIX thread and reports the failure after joining
    /// it. Here the managed thread is rejected before PostgreSQL runs anything, and the backend thread continues.
    /// </remarks>
    [PgFunction]
    public static void StartThread()
    {
        InvalidOperationException? rejection = null;
        var thread = new Thread(() =>
        {
            try
            {
                Spi.Execute("SELECT 1;");
            }
            catch (InvalidOperationException error)
            {
                rejection = error;
            }
        });
        thread.Start();
        thread.Join();
        if (rejection is not null)
        {
            PgLog.Error($"thread SPI work failed: {rejection.Message}");
        }
    }

    /// <summary>
    /// Sums detached managed values on thread-pool threads and returns the result from the backend thread.
    /// </summary>
    /// <param name="values">The values to sum; the array is a managed copy that worker threads may read.</param>
    /// <returns>The exact sum.</returns>
    /// <exception cref="OverflowException">The sum is outside the bigint range.</exception>
    [PgFunction]
    public static long ThreadSum(long[] values)
    {
        const int Partitions = 4;
        int size = (values.Length + Partitions - 1) / Partitions;
        var sums = new Task<Int128>[Partitions];
        for (int partition = 0; partition < Partitions; partition++)
        {
            int start = Math.Min(partition * size, values.Length);
            int end = Math.Min(start + size, values.Length);
            sums[partition] = Task.Run(() => SumRange(values, start, end));
        }

        // Waiting completes the work within this SQL call. It does not give the worker threads backend access.
        Int128[] partials = Task.WhenAll(sums).GetAwaiter().GetResult();
        Int128 total = 0;
        foreach (Int128 partial in partials)
        {
            total += partial;
        }

        return checked((long)total);
    }

    /// <summary>
    /// Sums one partition without calling PostgreSQL.
    /// </summary>
    private static Int128 SumRange(long[] values, int start, int end)
    {
        Int128 sum = 0;
        for (int index = start; index < end; index++)
        {
            sum += values[index];
        }

        return sum;
    }
}
