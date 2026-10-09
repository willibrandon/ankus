namespace Ankus.TestExtension;

/// <summary>
/// Raises PostgreSQL errors from SPI inside set-returning functions, as pgrx's <c>spi_in_iterator</c> and
/// <c>spi_in_setof</c> do, before the first row and while rows stream.
/// </summary>
[PgSchema("set_spi_errors")]
public static class SetSpiErrorFunctions
{
    private static readonly int[] s_relations = [1213, 1214, 1232, 1233, 1247, 1249, 1255];
    private static int s_cleanups;

    /// <summary>
    /// Reads each catalog relation's name, failing at <paramref name="failAt"/> with an undefined column.
    /// </summary>
    /// <param name="failAt">The row whose query fails, or -1 for none.</param>
    /// <param name="recover">Whether the iterator catches the failure and reports its SQLSTATE in that row.</param>
    /// <returns>Each relation's OID and name.</returns>
    [PgFunction(SetMode = PgSetMode.ValuePerCall)]
    public static IEnumerable<(int Id, string? Relname)> SpiErrorTable(int failAt, bool recover) => Read(failAt, recover);

    /// <summary>
    /// Materializes the same rows, so a failure occurs while the executor's tuplestore fills.
    /// </summary>
    /// <param name="failAt">The row whose query fails, or -1 for none.</param>
    /// <param name="recover">Whether the iterator catches the failure and reports its SQLSTATE in that row.</param>
    /// <returns>Each relation's OID and name.</returns>
    [PgFunction(SetMode = PgSetMode.Materialize)]
    public static IEnumerable<(int Id, string? Relname)> SpiErrorTableMaterialized(int failAt, bool recover) => Read(failAt, recover);

    /// <summary>
    /// Returns only the names, as pgrx's <c>spi_in_setof</c> does.
    /// </summary>
    /// <param name="failAt">The row whose query fails, or -1 for none.</param>
    /// <returns>Each relation's name.</returns>
    [PgFunction(SetMode = PgSetMode.ValuePerCall)]
    public static IEnumerable<string?> SpiErrorSetOf(int failAt)
    {
        foreach ((int _, string? name) in Read(failAt, recover: false))
        {
            yield return name;
        }
    }

    /// <summary>
    /// Reports how many iterators have run their cleanup, then resets the count.
    /// </summary>
    /// <returns>The number of completed or abandoned iterators since the last call.</returns>
    [PgFunction]
    public static int SpiErrorCleanups() => Interlocked.Exchange(ref s_cleanups, 0);

    private static IEnumerable<(int Id, string? Relname)> Read(int failAt, bool recover)
    {
        try
        {
            for (int index = 0; index < s_relations.Length; index++)
            {
                int oid = s_relations[index];
                string sql = index == failAt
                    ? "SELECT CAUSE_AN_ERROR FROM pg_class WHERE oid = $1"
                    : "SELECT relname::text FROM pg_class WHERE oid = $1";
                string? name;
                try
                {
                    name = Spi.ExecuteScalar<string?>(sql, SpiParameter.Create((uint)oid));
                }
                catch (PgException error) when (recover)
                {
                    name = "<" + error.SqlState + ">";
                }

                yield return (oid, name);
            }
        }
        finally
        {
            Interlocked.Increment(ref s_cleanups);
        }
    }
}
