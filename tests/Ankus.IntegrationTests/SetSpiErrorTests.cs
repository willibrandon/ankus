using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies that PostgreSQL errors raised by SPI inside set-returning functions reach the client intact, as pgrx's
/// <c>spi_in_iterator</c> and <c>spi_in_setof</c> require, while iterator cleanup runs and the backend stays usable.
/// </summary>
/// <param name="context">The current test context.</param>
[TestClass]
[DoNotParallelize]
public sealed class SetSpiErrorTests(TestContext context)
{
    private const string MissingColumn = "column \"cause_an_error\" does not exist";

    /// <summary>
    /// An SPI error before the first row, or while rows stream or materialize, fails the query with PostgreSQL's
    /// diagnostic, runs the iterator's cleanup once and leaves the backend able to run the same function again.
    /// </summary>
    /// <param name="function">The set-returning function.</param>
    /// <param name="failAt">The row whose query fails.</param>
    [TestMethod]
    [DataRow("spi_error_table(@failAt, false)", 0)]
    [DataRow("spi_error_table(@failAt, false)", 3)]
    [DataRow("spi_error_table_materialized(@failAt, false)", 0)]
    [DataRow("spi_error_table_materialized(@failAt, false)", 6)]
    [DataRow("spi_error_set_of(@failAt)", 0)]
    [DataRow("spi_error_set_of(@failAt)", 4)]
    public Task SpiErrorsInsideSetsReachTheClient(string function, int failAt)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(SpiErrorsInsideSetsReachTheClient), async (connection, transaction, token) =>
        {
            await ResetCleanupsAsync(connection, transaction, token);
            await transaction.SaveAsync("set_spi_error", token);
            await using (var failing = new NpgsqlCommand("SELECT count(*) FROM set_spi_errors." + function, connection, transaction))
            {
                failing.Parameters.AddWithValue("failAt", failAt);
                PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => failing.ExecuteScalarAsync(token));
                Assert.AreEqual(PostgresErrorCodes.UndefinedColumn, error.SqlState);
                Assert.AreEqual(MissingColumn, error.MessageText);
            }

            await transaction.RollbackAsync("set_spi_error", token);
            Assert.AreEqual(1, await ScalarAsync<int>(connection, transaction, "SELECT set_spi_errors.spi_error_cleanups()", token));
            Assert.AreEqual(
                await ScalarAsync<string>(connection, transaction, CatalogNames("relation.relname"), token),
                await ScalarAsync<string>(connection, transaction,
                    "SELECT string_agg(relname, ',' ORDER BY ordinality) FROM set_spi_errors.spi_error_table(-1, false) WITH ORDINALITY",
                    token));
            Assert.AreEqual(1, await ScalarAsync<int>(connection, transaction, "SELECT set_spi_errors.spi_error_cleanups()", token));
        }, context.CancellationToken);

    /// <summary>
    /// An iterator that catches the SPI error continues in the same transaction, reports the SQLSTATE in that row and
    /// returns every row, because each statement recovers through its own subtransaction.
    /// </summary>
    /// <param name="function">The recovering set-returning function.</param>
    [TestMethod]
    [DataRow("spi_error_table(3, true)")]
    [DataRow("spi_error_table_materialized(3, true)")]
    public Task CaughtSpiErrorsContinueTheSet(string function)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(CaughtSpiErrorsContinueTheSet), async (connection, transaction, token) =>
        {
            await ResetCleanupsAsync(connection, transaction, token);
            Assert.AreEqual(
                await ScalarAsync<string>(connection, transaction,
                    CatalogNames("relation.oid || ':' || CASE WHEN ordinality = 4 THEN '<42703>' ELSE relation.relname END"), token),
                await ScalarAsync<string>(connection, transaction,
                    "SELECT string_agg(id || ':' || relname, ',' ORDER BY ordinality) FROM set_spi_errors." + function + " WITH ORDINALITY",
                    token));
            Assert.AreEqual(1, await ScalarAsync<int>(connection, transaction, "SELECT set_spi_errors.spi_error_cleanups()", token));
        }, context.CancellationToken);

    /// <summary>
    /// Reads the expected rows directly from <c>pg_class</c>, in the functions' relation order.
    /// </summary>
    private static string CatalogNames(string projection)
        => "SELECT string_agg(" + projection + ", ',' ORDER BY ordinality) " +
            "FROM unnest('{1213,1214,1232,1233,1247,1249,1255}'::oid[]) WITH ORDINALITY AS wanted(oid, ordinality) " +
            "JOIN pg_class relation ON relation.oid = wanted.oid";

    private static async Task ResetCleanupsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken token)
        => await ScalarAsync<int>(connection, transaction, "SELECT set_spi_errors.spi_error_cleanups()", token);

    private static async Task<T> ScalarAsync<T>(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql,
        CancellationToken token)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        return Assert.IsInstanceOfType<T>(await command.ExecuteScalarAsync(token));
    }
}
