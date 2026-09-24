using Npgsql;
using NpgsqlTypes;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies multi-column first-row reads, complete writes, and recovery in PostgreSQL.
/// </summary>
/// <param name="context">The per-test context.</param>
[TestClass]
public sealed class SpiScalarTests(TestContext context)
{
    /// <summary>
    /// Gets pair and triple cases for all four SPI ownership paths.
    /// </summary>
    public static IEnumerable<(int Api, int Columns, string Sql, int? Number, string? Text, byte[]? Bytes, string Expected)> ValueCases
    {
        get
        {
            for (int api = 0; api < 4; api++)
            {
                yield return (api, 2, "SELECT $1, $2", int.MinValue, "café 🐘", null, "-2147483648|café 🐘");
                yield return (api, 2, "SELECT $1, $2", 0, "", null, "0|");
                yield return (api, 2, "SELECT $1, $2", null, "present", null, "<null>|present");
                yield return (api, 2, "SELECT $1, $2", 7, null, null, "7|<null>");
                yield return (api, 2, "SELECT $1, $2 WHERE false", 9, "unused", null, "<null>|<null>");
                yield return (api, 2, "CREATE TEMP TABLE spi_no_result (value integer)", null, null, null, "<null>|<null>");
                yield return (api, 2, "SELECT $1, $2, point(1, 2)", 42, "copied", null, "42|copied");
                yield return (api, 2, "SELECT n, $2 FROM generate_series(1, 3) n ORDER BY n", null, "first", null, "1|first");
                yield return (api, 2, "SELECT 99, 'earlier'; SELECT $1, $2", 42, "final", null, "42|final");
                yield return (api, 3, "SELECT $1, $2, $3", int.MaxValue, "café 🐘", [0, 255, 0, 127], "2147483647|café 🐘|00FF007F");
                yield return (api, 3, "SELECT $1, $2, $3", 0, "", [], "0||");
                yield return (api, 3, "SELECT $1, $2, $3", null, "present", [1], "<null>|present|01");
                yield return (api, 3, "SELECT $1, $2, $3", 7, null, [1], "7|<null>|01");
                yield return (api, 3, "SELECT $1, $2, $3", 7, "present", null, "7|present|<null>");
                yield return (api, 3, "SELECT $1, $2, $3 WHERE false", 9, "unused", [1], "<null>|<null>|<null>");
                yield return (api, 3, "CREATE TEMP TABLE spi_no_result (value integer)", null, null, null, "<null>|<null>|<null>");
                yield return (api, 3, "SELECT $1, $2, $3, point(1, 2)", 42, "copied", [0, 255], "42|copied|00FF");
                yield return (api, 3, "SELECT n, $2, $3 FROM generate_series(1, 3) n ORDER BY n", null, "first", [1], "1|first|01");
                yield return (api, 3, "SELECT 99, 'earlier', NULL; SELECT $1, $2, $3", 42, "final", [1], "42|final|01");
                yield return (api, 3, "SELECT $1, repeat($2, 10000), $3", 42, "x", [0, 255], "42|" + new string('x', 10000) + "|00FF");
            }
        }
    }

    /// <summary>
    /// Verifies first-row values remain exact and owned after SPI sessions and prepared statements are released.
    /// </summary>
    /// <param name="api">The SPI owner selection.</param>
    /// <param name="columns">The number of requested columns.</param>
    /// <param name="sql">The SQL commands.</param>
    /// <param name="number">The bound integer.</param>
    /// <param name="text">The bound text.</param>
    /// <param name="bytes">The bound binary data.</param>
    /// <param name="expected">The exact copied values.</param>
    [TestMethod]
    [DynamicData(nameof(ValueCases))]
    public Task FirstRowValuesRemainExactAndOwned(int api, int columns, string sql, int? number, string? text, byte[]? bytes, string expected)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(FirstRowValuesRemainExactAndOwned),
            async (connection, transaction, token) =>
            {
                await using NpgsqlCommand command = CreateCommand(connection, transaction, api, columns, sql, number, text, bytes);
                Assert.AreEqual(expected, await command.ExecuteScalarAsync(token));
            }, context.CancellationToken);

    /// <summary>
    /// Verifies all INSERT RETURNING writes complete even though only the first row's leading cells are copied.
    /// </summary>
    /// <param name="api">The SPI owner selection.</param>
    /// <param name="columns">The number of requested columns.</param>
    [TestMethod]
    [DataRow(0, 2)]
    [DataRow(1, 2)]
    [DataRow(2, 2)]
    [DataRow(3, 2)]
    [DataRow(0, 3)]
    [DataRow(1, 3)]
    [DataRow(2, 3)]
    [DataRow(3, 3)]
    public Task FirstRowReadsCompleteAllWrites(int api, int columns)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(FirstRowReadsCompleteAllWrites),
            async (connection, transaction, token) =>
            {
                await using (var setup = new NpgsqlCommand("""
                    CREATE TEMP TABLE spi_scalar_writes (value integer);
                    CREATE TYPE pg_temp.spi_unregistered AS ENUM ('value')
                    """, connection, transaction))
                {
                    await setup.ExecuteNonQueryAsync(token);
                }

                const string sql = """
                    INSERT INTO spi_scalar_writes SELECT generate_series($1, 5)
                    RETURNING value, $2, $3, 'value'::pg_temp.spi_unregistered
                    """;
                await using (NpgsqlCommand command = CreateCommand(connection, transaction, api, columns, sql, 1, "saved", [0, 255]))
                {
                    Assert.AreEqual(columns == 2 ? "1|saved" : "1|saved|00FF", await command.ExecuteScalarAsync(token));
                }

                await using var values = new NpgsqlCommand("SELECT array_agg(value ORDER BY value) FROM spi_scalar_writes", connection, transaction);
                int[] actual = Assert.IsInstanceOfType<int[]>(await values.ExecuteScalarAsync(token));
                Assert.AreSequenceEqual([1, 2, 3, 4, 5], actual);
            }, context.CancellationToken);

    /// <summary>
    /// Gets empty, missing, NULL, wrong-type, and native-error cases across all SPI owners.
    /// </summary>
    public static IEnumerable<(int Api, int Columns, string Sql, string Error, string Message)> ErrorCases
    {
        get
        {
            for (int api = 0; api < 4; api++)
            {
                yield return (api, 2, "SELECT NULL::integer, 2", "InvalidOperationException", "SQL NULL");
                yield return (api, 2, "SELECT 1, NULL::integer", "InvalidOperationException", "SQL NULL");
                yield return (api, 3, "SELECT 1, 2, NULL::integer", "InvalidOperationException", "SQL NULL");
                yield return (api, 2, "SELECT 1, 2 WHERE false", "InvalidOperationException", "SQL NULL");
                yield return (api, 3, "SELECT 1, 2, 3 WHERE false", "InvalidOperationException", "SQL NULL");
                yield return (api, 2, "SELECT 1", "InvalidOperationException", "has 1 columns; column 2 was requested");
                yield return (api, 3, "SELECT 1, 2", "InvalidOperationException", "has 2 columns; column 3 was requested");
                yield return (api, 2, "SELECT FROM generate_series(1, 2)", "InvalidOperationException", "has 0 columns; column 1 was requested");
                yield return (api, 2, "SELECT 1::bigint, 2", "InvalidCastException", "cannot be read as 'System.Int32'");
                yield return (api, 2, "SELECT 1, 2::bigint", "InvalidCastException", "cannot be read as 'System.Int32'");
                yield return (api, 3, "SELECT 1, 2, '3'::text", "InvalidCastException", "cannot be read as 'System.Int32'");
                yield return (api, 2, "SELECT 1, 1 / 0", "22012", "division by zero");
                yield return (api, 3, "SELECT 1, 2, 1 / 0", "22012", "division by zero");
            }
        }
    }

    /// <summary>
    /// Verifies native errors and strict managed conversions can be caught without losing backend usability.
    /// </summary>
    /// <param name="api">The SPI owner selection.</param>
    /// <param name="columns">The requested column count.</param>
    /// <param name="sql">The failing commands.</param>
    /// <param name="error">The exact exception type or SQLSTATE.</param>
    /// <param name="message">The distinctive diagnostic.</param>
    [TestMethod]
    [DynamicData(nameof(ErrorCases))]
    public Task ResultErrorsPreserveBackendRecovery(int api, int columns, string sql, string error, string message)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(ResultErrorsPreserveBackendRecovery),
            async (connection, transaction, token) =>
            {
                int backend = connection.ProcessID;
                await using var command = new NpgsqlCommand("SELECT datatype.spi_scalar_recover($1, $2, $3)", connection, transaction);
                command.Parameters.AddWithValue(api);
                command.Parameters.AddWithValue(columns);
                command.Parameters.AddWithValue(sql);
                string result = Assert.IsInstanceOfType<string>(await command.ExecuteScalarAsync(token));
                Assert.StartsWith(error + ":", result);
                Assert.Contains(message, result);
                Assert.EndsWith("|42", result);
                Assert.AreEqual(backend, connection.ProcessID);
            }, context.CancellationToken);

    /// <summary>
    /// Verifies a conversion failure after copying earlier cells rolls back writes and releases the partial result.
    /// </summary>
    /// <param name="api">The SPI owner selection.</param>
    /// <param name="columns">The requested column count.</param>
    [TestMethod]
    [DataRow(0, 2)]
    [DataRow(1, 2)]
    [DataRow(2, 2)]
    [DataRow(3, 2)]
    [DataRow(0, 3)]
    [DataRow(1, 3)]
    [DataRow(2, 3)]
    [DataRow(3, 3)]
    public Task NativeConversionFailureRollsBackWrites(int api, int columns)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(NativeConversionFailureRollsBackWrites),
            async (connection, transaction, token) =>
            {
                await using (var setup = new NpgsqlCommand("""
                    CREATE TEMP TABLE spi_scalar_rollback (value integer);
                    CREATE TYPE pg_temp.spi_unregistered AS ENUM ('value')
                    """, connection, transaction))
                {
                    await setup.ExecuteNonQueryAsync(token);
                }

                string returning = columns == 2
                    ? "repeat('owned', 10000), 'value'::pg_temp.spi_unregistered"
                    : "repeat('owned', 10000), 2, 'value'::pg_temp.spi_unregistered";
                await using (var command = new NpgsqlCommand("SELECT datatype.spi_scalar_recover($1, $2, $3)", connection, transaction))
                {
                    command.Parameters.AddWithValue(api);
                    command.Parameters.AddWithValue(columns);
                    command.Parameters.AddWithValue("INSERT INTO spi_scalar_rollback VALUES (1), (2) RETURNING " + returning);
                    string result = Assert.IsInstanceOfType<string>(await command.ExecuteScalarAsync(token));
                    Assert.StartsWith("0A000:", result);
                    Assert.Contains("no generated Ankus conversion", result);
                    Assert.EndsWith("|42", result);
                }

                await using var count = new NpgsqlCommand("SELECT count(*) FROM spi_scalar_rollback", connection, transaction);
                Assert.AreEqual(0L, await count.ExecuteScalarAsync(token));
            }, context.CancellationToken);

    /// <summary>
    /// Binds nullable values with explicit Npgsql types to a pair or triple backend probe.
    /// </summary>
    /// <param name="connection">The active connection.</param>
    /// <param name="transaction">The owning transaction.</param>
    /// <param name="api">The SPI owner selection.</param>
    /// <param name="columns">The requested column count.</param>
    /// <param name="sql">The commands.</param>
    /// <param name="number">The integer value.</param>
    /// <param name="text">The text value.</param>
    /// <param name="bytes">The binary value.</param>
    /// <returns>The caller-owned command.</returns>
    private static NpgsqlCommand CreateCommand(NpgsqlConnection connection, NpgsqlTransaction transaction,
        int api, int columns, string sql, int? number, string? text, byte[]? bytes)
    {
        string function = columns == 2 ? "spi_scalar_pair" : "spi_scalar_triple";
        var command = new NpgsqlCommand($"SELECT datatype.{function}($1, $2, $3, $4, $5)", connection, transaction);
        command.Parameters.AddWithValue(api);
        command.Parameters.AddWithValue(sql);
        command.Parameters.AddWithValue(NpgsqlDbType.Integer, (object?)number ?? DBNull.Value);
        command.Parameters.AddWithValue(NpgsqlDbType.Text, (object?)text ?? DBNull.Value);
        command.Parameters.AddWithValue(NpgsqlDbType.Bytea, (object?)bytes ?? DBNull.Value);
        return command;
    }
}
