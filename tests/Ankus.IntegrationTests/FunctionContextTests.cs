using System.Globalization;
using Npgsql;
using NpgsqlTypes;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies injected function metadata and raw arguments in actual PostgreSQL calls.
/// </summary>
/// <param name="context">The test cancellation context.</param>
[TestClass]
public sealed class FunctionContextTests(TestContext context)
{
    /// <summary>
    /// Copies actual types, NULL flags, collation, and arguments across nested calls and native error recovery.
    /// </summary>
    /// <param name="number">The nullable number.</param>
    /// <param name="text">The nullable text.</param>
    [TestMethod]
    [DataRow(0, "")]
    [DataRow(42, "héllo 🐘")]
    [DataRow(null, "owned")]
    [DataRow(17, null)]
    [DataRow(null, null)]
    public Task ScalarSnapshotsRetainExactCallMetadata(int? number, string? text)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(ScalarSnapshotsRetainExactCallMetadata), async (connection, transaction, token) =>
        {
            await using var command = new NpgsqlCommand("""
                SELECT datatype.context_snapshot($1, $2 COLLATE "C"),
                    'datatype.context_snapshot(integer,text)'::regprocedure::oid, '"C"'::regcollation::oid,
                    datatype.context_identity(), 'datatype.context_identity()'::regprocedure::oid,
                    datatype.context_thread(42)
                """, connection, transaction);
            command.Parameters.AddWithValue(NpgsqlDbType.Integer, (object?)number ?? DBNull.Value);
            command.Parameters.AddWithValue(NpgsqlDbType.Text, (object?)text ?? DBNull.Value);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
            Assert.IsTrue(await reader.ReadAsync(token));
            string expected = reader.GetFieldValue<uint>(1).ToString(CultureInfo.InvariantCulture) + "|25|" +
                reader.GetFieldValue<uint>(2).ToString(CultureInfo.InvariantCulture) + "|23:" +
                (number?.ToString(CultureInfo.InvariantCulture) ?? "NULL") + ";25:" + (text ?? "NULL");
            Assert.AreEqual(expected, reader.GetString(0));
            Assert.AreEqual(reader.GetFieldValue<uint>(4), reader.GetFieldValue<uint>(3));
            Assert.IsTrue(reader.GetBoolean(5));
        }, context.CancellationToken);

    /// <summary>
    /// Uses the same native entry point through a domain signature to distinguish expression identity from generated defaults.
    /// </summary>
    [TestMethod]
    public Task DomainArgumentsRetainTheirActualCatalogTypes()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(DomainArgumentsRetainTheirActualCatalogTypes), async (connection, transaction, token) =>
        {
            await using var setup = new NpgsqlCommand("""
                CREATE DOMAIN pg_temp.context_number AS integer;
                CREATE DOMAIN pg_temp.context_text AS text;
                DO $body$
                DECLARE target record;
                BEGIN
                    SELECT probin, prosrc INTO target FROM pg_proc
                    WHERE oid = 'datatype.context_snapshot(integer,text)'::regprocedure;
                    EXECUTE format('CREATE FUNCTION pg_temp.context_domain(pg_temp.context_number, pg_temp.context_text)
                        RETURNS text AS %L, %L LANGUAGE c', target.probin, target.prosrc);
                END
                $body$;
                """, connection, transaction);
            await setup.ExecuteNonQueryAsync(token);
            setup.CommandText = """
                SELECT pg_temp.context_domain(42::pg_temp.context_number, 'owned'::pg_temp.context_text COLLATE "C"),
                    'pg_temp.context_domain(pg_temp.context_number,pg_temp.context_text)'::regprocedure::oid,
                    '"C"'::regcollation::oid, 'pg_temp.context_number'::regtype::oid, 'pg_temp.context_text'::regtype::oid
                """;
            await using NpgsqlDataReader reader = await setup.ExecuteReaderAsync(token);
            Assert.IsTrue(await reader.ReadAsync(token));
            string expected = string.Create(CultureInfo.InvariantCulture,
                $"{reader.GetFieldValue<uint>(1)}|25|{reader.GetFieldValue<uint>(2)}|{reader.GetFieldValue<uint>(3)}:42;{reader.GetFieldValue<uint>(4)}:owned");
            Assert.AreEqual(expected, reader.GetString(0));
        }, context.CancellationToken);

    /// <summary>
    /// Resolves operand types and result metadata from an OpExpr as well as an ordinary FuncExpr.
    /// </summary>
    [TestMethod]
    public Task OperatorExpressionsExposeTheirActualOperand()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(OperatorExpressionsExposeTheirActualOperand), async (connection, transaction, token) =>
        {
            await using var command = new NpgsqlCommand("SELECT datatype.context_operand(41), OPERATOR(datatype.@~#) 41", connection, transaction);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
            Assert.IsTrue(await reader.ReadAsync(token));
            Assert.AreEqual(42, reader.GetInt32(0));
            Assert.AreEqual(42, reader.GetInt32(1));
        }, context.CancellationToken);

    /// <summary>
    /// Rejects native access after scalar cleanup while retaining an explicit transaction-owned copy.
    /// </summary>
    /// <param name="isNull">Whether the scalar argument is NULL.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task ExpiredArgumentsFailAndExplicitCopiesRemainLive(bool isNull)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(ExpiredArgumentsFailAndExplicitCopiesRemainLive), async (connection, transaction, token) =>
        {
            await using var command = new NpgsqlCommand("SELECT datatype.context_remember($1, repeat('owned', 10000))", connection, transaction);
            command.Parameters.AddWithValue(NpgsqlDbType.Integer, isNull ? DBNull.Value : 42);
            await command.ExecuteNonQueryAsync(token);
            command.Parameters.Clear();
            command.CommandText = "SELECT datatype.context_expired()";
            Assert.AreEqual("2|2278|23,25|" + string.Concat(Enumerable.Repeat("owned", 10000)), await command.ExecuteScalarAsync(token));
        }, context.CancellationToken);

    /// <summary>
    /// Keeps raw text and metadata throughout streaming or materialized execution and early iterator disposal.
    /// </summary>
    /// <param name="materialize">Whether to require materialization.</param>
    /// <param name="limit">Whether to stop after the first output row.</param>
    /// <param name="isNull">Whether the reference argument is SQL NULL.</param>
    /// <param name="count">The number of rows produced by the iterator.</param>
    [TestMethod]
    [DataRow(false, false, false, 3)]
    [DataRow(false, true, false, 3)]
    [DataRow(false, false, true, 3)]
    [DataRow(true, false, false, 3)]
    [DataRow(true, true, true, 3)]
    [DataRow(false, false, false, 0)]
    [DataRow(true, false, true, 0)]
    public Task SetSnapshotsSurviveYieldsAndCleanup(bool materialize, bool limit, bool isNull, int count)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(SetSnapshotsSurviveYieldsAndCleanup), async (connection, transaction, token) =>
        {
            string function = materialize ? "context_materialized" : "context_rows";
            await using var metadata = new NpgsqlCommand($"SELECT 'datatype.{function}(text,integer,boolean)'::regprocedure::oid, '\"C\"'::regcollation::oid",
                connection, transaction);
            uint functionOid;
            uint collation;
            await using (NpgsqlDataReader reader = await metadata.ExecuteReaderAsync(token))
            {
                Assert.IsTrue(await reader.ReadAsync(token));
                functionOid = reader.GetFieldValue<uint>(0);
                collation = reader.GetFieldValue<uint>(1);
            }

            string text = string.Concat(Enumerable.Repeat("héllo 🐘", 1000));
            await using var command = new NpgsqlCommand($"SELECT value FROM datatype.{function}($1 COLLATE \"C\", $2, false) value" + (limit ? " LIMIT 1" : ""),
                connection, transaction);
            command.Parameters.AddWithValue(NpgsqlDbType.Text, isNull ? DBNull.Value : text);
            command.Parameters.AddWithValue(count);
            int rows = 0;
            await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(token))
            {
                string expected = string.Create(CultureInfo.InvariantCulture, $"{functionOid}|25|{collation}|25:{(isNull ? "NULL" : text)};23:{count};16:f");
                while (await reader.ReadAsync(token))
                {
                    Assert.AreEqual(expected, reader.GetString(0));
                    rows++;
                }
            }

            Assert.AreEqual(limit ? 1 : count, rows);
            command.Parameters.Clear();
            command.CommandText = "SELECT datatype.context_cleanup()";
            Assert.AreEqual(1, await command.ExecuteScalarAsync(token));
        }, context.CancellationToken);

    /// <summary>
    /// Preserves the error diagnostic and live arguments during iterator abort cleanup, then recovers in the same backend.
    /// </summary>
    /// <param name="materialize">Whether to use a materialized set.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task IteratorErrorsUnwindWithLiveArguments(bool materialize)
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        int backend = connection.ProcessID;
        string function = materialize ? "context_materialized" : "context_rows";
        await using var command = new NpgsqlCommand($"SELECT * FROM datatype.{function}('owned', 3, true)", connection);
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteNonQueryAsync(token));
        Assert.AreEqual("P7805", error.SqlState);
        Assert.AreEqual("function context iterator failure", error.MessageText);
        command.CommandText = "SELECT datatype.context_cleanup()";
        Assert.AreEqual(1, await command.ExecuteScalarAsync(token));
        Assert.AreEqual(backend, connection.ProcessID);
    }

    /// <summary>
    /// Keeps raw argument storage alive while PostgreSQL aborts a partially consumed iterator.
    /// </summary>
    [TestMethod]
    public async Task ExecutorAbortPreservesArgumentsUntilIteratorDisposal()
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        int backend = connection.ProcessID;
        await using var command = new NpgsqlCommand("""
            SELECT 1/(length(value)-length(value)) FROM
                (SELECT datatype.context_rows('owned', 3, false) AS value OFFSET 0) AS rows
            """, connection);
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteNonQueryAsync(token));
        Assert.AreEqual(PostgresErrorCodes.DivisionByZero, error.SqlState);
        command.CommandText = "SELECT datatype.context_cleanup()";
        Assert.AreEqual(1, await command.ExecuteScalarAsync(token));
        Assert.AreEqual(backend, connection.ProcessID);
    }
}
