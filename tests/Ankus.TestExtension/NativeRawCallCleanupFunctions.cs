namespace Ankus.TestExtension;

/// <summary>
/// Uses generated native release calls from iterator disposal during PostgreSQL query unwinding.
/// </summary>
public static class NativeRawCallCleanupFunctions
{
    private static int s_releases;

    /// <summary>
    /// Allocates directly in the iterator owner and yields until PostgreSQL completes or aborts enumeration.
    /// </summary>
    /// <param name="body">The generated native pfree body address.</param>
    /// <returns>A streaming sequence whose finally block owns the detached allocation.</returns>
    [PgFunction(SetMode = PgSetMode.ValuePerCall)]
    public static IEnumerable<int> RawCallCleanup(long body)
    {
        s_releases = 0;
        return Rows((nint)body, Allocate());
    }

    /// <summary>
    /// Reads the number of completed native releases after the previous executor and its callbacks have finished.
    /// </summary>
    /// <returns>The completed native release count.</returns>
    [PgFunction]
    public static int RawCallCleanupReleases() => s_releases;

    private static IEnumerable<int> Rows(nint body, nint storage)
    {
        try
        {
            yield return 1;
            yield return 2;
        }
        finally
        {
            Release(body, storage);
            s_releases++;
        }
    }

    private static unsafe nint Allocate()
    {
        using PgAllocation allocation = PgMemoryContext.Current.Allocate(32);
        allocation.Write<int>(731);
        return (nint)allocation.DangerousDetach();
    }

    private static unsafe void Release(nint body, nint storage)
        => NativeRawCall.Invoke(body, [new((nint)(&storage), (nuint)sizeof(nint))], 0, 0);
}
