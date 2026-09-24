using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Proves raw datum storage, exact identity, custom conversions, and recovery in PostgreSQL.
/// </summary>
/// <param name="context">The test cancellation context.</param>
[TestClass]
public sealed class SpiRawTests(TestContext context)
{
    /// <summary>
    /// Gets values and metadata cases across every SPI connection and plan owner.
    /// </summary>
    public static IEnumerable<(int Api, string Sql, string Expected)> Values
    {
        get
        {
            for (int api = 0; api < 4; api++)
            {
                yield return (api, "SELECT 0 AS zero, NULL::integer AS missing, '-9223372036854775808'::bigint AS lo", "zero:23,missing:23,lo:20;1;0|<null>|-9223372036854775808");
                yield return (api, "SELECT 'café 🐘'::text AS text, decode('00ff007f', 'hex') AS bytes", "text:25,bytes:17;1;café 🐘|\\x00ff007f");
                yield return (api, "SELECT '00:11:22:33:44:55:66:77'::macaddr8 AS custom", "custom:774;1;00:11:22:33:44:55:66:77");
                yield return (api, "SELECT '101001'::bit(6) AS custom", "custom:1560;1;101001");
                yield return (api, "SELECT n AS value FROM generate_series(1, 3) n ORDER BY n", "value:23;3;1/2/3");
                yield return (api, "SELECT 1 AS empty WHERE false", "empty:23;0;");
                yield return (api, "SELECT", ";1;");
                yield return (api, "CREATE TEMP TABLE raw_no_result (n integer)", ";0;");
            }
        }
    }

    /// <summary>
    /// Verifies values survive session and plan cleanup and bind again with exact types and NULL flags.
    /// </summary>
    /// <param name="api">The SPI owner selection.</param>
    /// <param name="sql">The raw query.</param>
    /// <param name="expected">The independently specified metadata and values.</param>
    [TestMethod]
    [DynamicData(nameof(Values))]
    public Task ResultsSurviveSpiOwnersAndRetainExactValues(int api, string sql, string expected)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(ResultsSurviveSpiOwnersAndRetainExactValues), async (connection, transaction, token) =>
        {
            await using var command = new NpgsqlCommand("SELECT datatype.spi_raw_snapshot($1, $2, $3)", connection, transaction);
            command.Parameters.AddWithValue(api);
            command.Parameters.AddWithValue(sql);
            command.Parameters.AddWithValue("SELECT repeat('overwrite', 10000)");
            Assert.AreEqual(expected, await command.ExecuteScalarAsync(token));
        }, context.CancellationToken);

    /// <summary>
    /// Verifies dynamically defined domains and unregistered enums retain their catalog identity, including typed NULLs.
    /// </summary>
    /// <param name="api">The SPI owner selection.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public Task DomainsAndUnregisteredEnumsRoundTrip(int api)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(DomainsAndUnregisteredEnumsRoundTrip), async (connection, transaction, token) =>
        {
            await using (var setup = new NpgsqlCommand("CREATE DOMAIN pg_temp.raw_positive AS int CHECK (VALUE > 0); CREATE TYPE pg_temp.raw_mood AS ENUM ('café 🐘')", connection, transaction))
            {
                await setup.ExecuteNonQueryAsync(token);
            }

            await using var command = new NpgsqlCommand("""
                SELECT datatype.spi_raw_snapshot($1,
                    'SELECT 7::pg_temp.raw_positive AS d, NULL::pg_temp.raw_positive AS n, ''café 🐘''::pg_temp.raw_mood AS e', 'SELECT 1'),
                    'd:' || 'pg_temp.raw_positive'::regtype::oid || ',n:' || 'pg_temp.raw_positive'::regtype::oid ||
                    ',e:' || 'pg_temp.raw_mood'::regtype::oid || ';1;7|<null>|café 🐘'
                """, connection, transaction);
            command.Parameters.AddWithValue(api);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
            Assert.IsTrue(await reader.ReadAsync(token));
            Assert.AreEqual(reader.GetString(1), reader.GetString(0));
        }, context.CancellationToken);

    /// <summary>
    /// Verifies toasted text and composite fields remain owned after their original table is removed.
    /// </summary>
    /// <param name="api">The SPI owner selection.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public Task ToastedValuesSurviveSourceRemoval(int api)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(ToastedValuesSurviveSourceRemoval), async (connection, transaction, token) =>
        {
            await using (var setup = new NpgsqlCommand("""
                CREATE TYPE pg_temp.raw_record AS (body text, data bytea);
                CREATE TEMP TABLE raw_toast (body text, data bytea);
                ALTER TABLE raw_toast ALTER COLUMN body SET STORAGE EXTERNAL;
                ALTER TABLE raw_toast ALTER COLUMN data SET STORAGE EXTERNAL;
                INSERT INTO raw_toast SELECT repeat('owned', 10000), decode(repeat('00ff', 10000), 'hex')
                """, connection, transaction))
            {
                await setup.ExecuteNonQueryAsync(token);
            }

            await using var command = new NpgsqlCommand("""
                SELECT datatype.spi_raw_snapshot($1,
                    'SELECT body AS t, data AS b, ROW(body, data)::pg_temp.raw_record AS r FROM raw_toast',
                    'DROP TABLE raw_toast'),
                    't:25,b:17,r:' || 'pg_temp.raw_record'::regtype::oid || ';1;' || repeat('owned', 10000) ||
                    '|\x' || repeat('00ff', 10000) || '|(' || repeat('owned', 10000) || ',"\\x' || repeat('00ff', 10000) || '")'
                """, connection, transaction);
            command.Parameters.AddWithValue(api);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
            Assert.IsTrue(await reader.ReadAsync(token));
            Assert.AreEqual(reader.GetString(1), reader.GetString(0));
        }, context.CancellationToken);

    /// <summary>
    /// Verifies strict managed conversions preserve bits, reject mismatched types, and recover on the same backend.
    /// </summary>
    /// <param name="sql">The source value.</param>
    /// <param name="kind">The requested conversion.</param>
    /// <param name="expected">The exact value or error.</param>
    [TestMethod]
    [DataRow("SELECT -2147483648", 0, "-2147483648")]
    [DataRow("SELECT '9223372036854775807'::bigint", 1, "9223372036854775807")]
    [DataRow("SELECT '-0'::float8", 4, "8000000000000000")]
    [DataRow("SELECT 'Infinity'::float8", 4, "7FF0000000000000")]
    [DataRow("SELECT 'café 🐘'::text", 2, "café 🐘")]
    [DataRow("SELECT decode('00ff007f', 'hex')", 3, "00FF007F")]
    [DataRow("SELECT NULL::int", 5, "<null>")]
    [DataRow("SELECT NULL::int", 0, "InvalidOperationException")]
    [DataRow("SELECT 42::bigint", 0, "InvalidCastException")]
    [DataRow("SELECT 1/0", 0, "22012")]
    public Task ManagedConversionsAndErrorsRecover(string sql, int kind, string expected)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(ManagedConversionsAndErrorsRecover), async (connection, transaction, token) =>
        {
            int backend = connection.ProcessID;
            await using var command = new NpgsqlCommand("SELECT datatype.spi_raw_read($1, $2)", connection, transaction);
            command.Parameters.AddWithValue(sql);
            command.Parameters.AddWithValue(kind);
            Assert.AreEqual(expected + "|42", await command.ExecuteScalarAsync(token));
            Assert.AreEqual(backend, connection.ProcessID);
        }, context.CancellationToken);

    /// <summary>
    /// Verifies by-value, pointer, and NULL datums share exact ownership invalidation rules.
    /// </summary>
    /// <param name="sql">The source query.</param>
    /// <param name="reset">Whether the copied owner resets or is deleted.</param>
    /// <param name="expected">The value that survives source disposal.</param>
    [TestMethod]
    [DataRow("SELECT 42", false, "42")]
    [DataRow("SELECT 42", true, "42")]
    [DataRow("SELECT 'owned text'", false, "owned text")]
    [DataRow("SELECT 'owned text'", true, "owned text")]
    [DataRow("SELECT NULL::int", false, "<null>")]
    [DataRow("SELECT NULL::int", true, "<null>")]
    public Task CopySurvivesDisposalAndExpiresWithDestination(string sql, bool reset, string expected)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(CopySurvivesDisposalAndExpiresWithDestination), async (connection, transaction, token) =>
        {
            await using var command = new NpgsqlCommand("SELECT datatype.spi_raw_lifetime($1, $2), datatype.spi_raw_zero()", connection, transaction);
            command.Parameters.AddWithValue(sql);
            command.Parameters.AddWithValue(reset);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
            Assert.IsTrue(await reader.ReadAsync(token));
            Assert.AreEqual(expected + "|4", reader.GetString(0));
            Assert.AreEqual("0|<null>", reader.GetString(1));
        }, context.CancellationToken);

    /// <summary>
    /// Verifies ordinary rows and tuples own converted raw edits, and stale sources leave their old values intact.
    /// </summary>
    /// <param name="isNull">Whether the source is SQL NULL.</param>
    /// <param name="parameter">Whether the tuple edit uses an explicit parameter.</param>
    /// <param name="stale">Whether the source was disposed before assignment.</param>
    /// <param name="expected">The independently expected owned values and rejected edits.</param>
    [TestMethod]
    [DataRow(false, false, false, "42|42|23|0")]
    [DataRow(false, true, false, "42|42|23|0")]
    [DataRow(true, false, false, "<null>|<null>|23|0")]
    [DataRow(true, true, false, "<null>|<null>|23|0")]
    [DataRow(false, false, true, "17|17|23|2")]
    [DataRow(false, true, true, "17|17|23|2")]
    [DataRow(true, false, true, "17|17|23|2")]
    [DataRow(true, true, true, "17|17|23|2")]
    public Task ManagedEditsCopyRawValuesAndRejectStaleSources(bool isNull, bool parameter, bool stale, string expected)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(ManagedEditsCopyRawValuesAndRejectStaleSources), async (connection, transaction, token) =>
        {
            await using var command = new NpgsqlCommand("SELECT datatype.raw_managed_assignment($1, $2, $3)", connection, transaction);
            command.Parameters.AddWithValue(isNull);
            command.Parameters.AddWithValue(parameter);
            command.Parameters.AddWithValue(stale);
            Assert.AreEqual(expected, await command.ExecuteScalarAsync(token));
        }, context.CancellationToken);

    /// <summary>
    /// Verifies query options and write rollback under each owner without compromising later backend calls.
    /// </summary>
    /// <param name="api">The SPI owner selection.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public Task LimitsReadOnlyAndWriteErrorsPreserveTransaction(int api)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(LimitsReadOnlyAndWriteErrorsPreserveTransaction), async (connection, transaction, token) =>
        {
            await using (var setup = new NpgsqlCommand("CREATE TEMP TABLE raw_writes (n int)", connection, transaction))
            {
                await setup.ExecuteNonQueryAsync(token);
            }

            await using var command = new NpgsqlCommand("""
                SELECT datatype.spi_raw_options($1, 'SELECT generate_series(1, 3)', true, 2),
                    datatype.spi_raw_options($1, 'SELECT 1', false, -1),
                    datatype.spi_raw_options($1, 'INSERT INTO raw_writes VALUES (9)', true, 0),
                    datatype.spi_raw_options($1, 'INSERT INTO raw_writes VALUES (9); SELECT 1/0', false, 0),
                    datatype.spi_raw_options($1, 'INSERT INTO raw_writes VALUES (1), (2), (3) RETURNING n', false, 0)
                """, connection, transaction);
            command.Parameters.AddWithValue(api);
            await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(token))
            {
                Assert.IsTrue(await reader.ReadAsync(token));
                Assert.AreEqual("2:2|42", reader.GetString(0));
                Assert.AreEqual("ArgumentOutOfRangeException|42", reader.GetString(1));
                Assert.AreEqual("0A000|42", reader.GetString(2));
                Assert.AreEqual("22012|42", reader.GetString(3));
                Assert.AreEqual("3:3|42", reader.GetString(4));
            }

            await using var count = new NpgsqlCommand("SELECT array_agg(n ORDER BY n) FROM raw_writes", connection, transaction);
            Assert.AreSequenceEqual([1, 2, 3], Assert.IsInstanceOfType<int[]>(await count.ExecuteScalarAsync(token)));
        }, context.CancellationToken);
}
