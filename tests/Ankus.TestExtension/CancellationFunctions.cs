namespace Ankus.TestExtension;

/// <summary>
/// Attempts to swallow native cancellation through ordinary managed exception handlers.
/// </summary>
public static class CancellationFunctions
{
    [ThreadStatic]
    private static int s_observed;

    /// <summary>
    /// Runs a cancellable server operation and deliberately catches its exception.
    /// </summary>
    /// <param name="mode">Zero for SPI, one for recursive dispatch, two for explicit recovery, or three/four for one/two SPI sessions.</param>
    /// <param name="replace">Whether to replace the caught error with an unrelated exception.</param>
    /// <returns>A value that cancellation must prevent from reaching SQL.</returns>
    [PgFunction]
    public static int CancelCatch(int mode, bool replace)
    {
        try
        {
            switch (mode)
            {
                case 0:
                    Spi.Execute("SELECT pg_sleep(30)");
                    break;
                case 1:
                    Spi.Execute("SELECT datatype.cancel_catch(0, false)");
                    break;
                case 2:
                    PgTransaction.RunInSubtransaction(() => CancelCatch(0, false));
                    break;
                case 3:
                    Spi.Connect(session => session.Execute("SELECT pg_sleep(30)"));
                    break;
                case 4:
                    Spi.Connect(_ => Spi.Connect(session => session.Execute("SELECT pg_sleep(30)")));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode));
            }
        }
        catch (Exception exception)
        {
            if (exception is PgQueryCanceledException { Diagnostic.SqlState: PgSqlStates.QueryCanceled })
            {
                s_observed |= 1;
            }

            try
            {
                Spi.Execute("SELECT 1");
            }
            catch (PgQueryCanceledException)
            {
                s_observed |= 2;
            }

            if (replace)
            {
                throw new InvalidOperationException("replacement must not hide cancellation");
            }
        }
        finally
        {
            s_observed |= 4;
        }

        return 42;
    }

    /// <summary>
    /// Reads and clears the managed cancellation and cleanup observations in the same backend.
    /// </summary>
    /// <returns>The bit mask for typed cancellation, rejected subsequent work and managed finally execution.</returns>
    [PgFunction]
    public static int CancelObservations()
    {
        int result = s_observed;
        s_observed = 0;
        return result;
    }
}
