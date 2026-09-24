using System.Buffers.Binary;
using System.Text.Json;
using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies generated base types using published Native AOT code in PostgreSQL.
/// </summary>
/// <param name="context">The test cancellation context.</param>
[TestClass]
public sealed class CustomTypeTests(TestContext context)
{
    /// <summary>
    /// Exchanges both managed type categories through direct, query, plan, cursor and edited-row paths.
    /// </summary>
    /// <param name="mode">The ownership path.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    [DataRow(5)]
    [DataRow(6)]
    [DataRow(7)]
    public async Task CustomScalarsArraysAndNullsCrossEveryOwnershipPath(int mode)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.IsTrue(await Scalar<bool>(connection, $$$"""
            SELECT custom_values.custom_number('-9223372036854775808',{{{mode}}})::text = '-9223372036854775808'
                AND custom_values.custom_number('9223372036854775807',{{{mode}}})::text = '9223372036854775807'
                AND custom_values.custom_number('0',{{{mode}}})::text = '0'
                AND custom_values.custom_number(NULL,{{{mode}}}) IS NULL
                AND custom_values.custom_message(' héllo 😀 ',{{{mode}}})::text = ' héllo 😀 '
                AND custom_values.custom_message('',{{{mode}}})::text = ''
                AND custom_values.custom_message(NULL,{{{mode}}}) IS NULL
                AND custom_values.custom_numbers('[2:3][-1:0]={{1,NULL},{0,9}}',{{{mode}}})::text = '[2:3][-1:0]={{1,NULL},{0,9}}'
                AND custom_values.custom_numbers('{}',{{{mode}}})::text = '{}'
                AND custom_values.custom_numbers(NULL,{{{mode}}}) IS NULL
                AND custom_values.custom_messages('[3:4]={hello,NULL}',{{{mode}}})::text = '[3:4]={hello,NULL}'
                AND pg_typeof(custom_values.custom_number('1',{{{mode}}})) = 'custom_values.number'::regtype
            """));
    }

    /// <summary>
    /// Uses custom values in SETOF, TABLE, casts, operators and independent aggregate groups.
    /// </summary>
    [TestMethod]
    public async Task CustomSetsOperatorsCastsAndAggregatesUseTypedStorage()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.AreEqual("{4,NULL,9}", await Scalar<string>(connection,
            "SELECT array_agg(value::text)::text FROM custom_values.custom_rows('4') value"));
        Assert.AreEqual("4:row,NULL:NULL", await Scalar<string>(connection,
            "SELECT string_agg(coalesce(number::text,'NULL') || ':' || coalesce(message::text,'NULL'),',') FROM custom_values.custom_table('4')"));
        Assert.AreEqual("4", await Scalar<string>(connection, "SELECT value::text FROM custom_values.custom_rows('4') value LIMIT 1"));
        Assert.IsTrue(await Scalar<bool>(connection,
            "SELECT '4'::custom_values.number::bigint = 4 AND '4'::custom_values.number OPERATOR(custom_values.===) '4'::custom_values.number"));
        Assert.AreEqual("{3,7}", await Scalar<string>(connection, """
            SELECT array_agg(total::text ORDER BY group_id)::text FROM (
                SELECT group_id, custom_values.custom_sum(value::text::custom_values.number) total
                FROM (VALUES (1,1),(1,2),(2,3),(2,4),(2,NULL)) input(group_id,value) GROUP BY group_id) groups
            """));
        Assert.IsTrue(await Scalar<bool>(connection,
            "SELECT custom_values.custom_sum(value) IS NULL FROM (SELECT NULL::custom_values.number value WHERE false) empty"));
    }

    /// <summary>
    /// Unwinds each codec operation before raising PostgreSQL errors and preserves the same backend.
    /// </summary>
    /// <param name="sql">The failing operation.</param>
    /// <param name="state">The expected SQLSTATE.</param>
    /// <param name="message">The exact diagnostic.</param>
    [TestMethod]
    [DataRow("SELECT '!parse'::custom_values.message", "P7901", "parse failed")]
    [DataRow("SELECT '!format'::custom_values.message::text", "P7902", "format failed")]
    [DataRow("SELECT custom_values.custom_message('!read',0)", "P7903", "read failed")]
    [DataRow("SELECT custom_values.custom_make_message('!write')", "P7904", "write failed")]
    [DataRow("SELECT 'value'::custom_values.fault::text", "P7905", "codec construction failed")]
    public async Task CustomCodecErrorsUnwindAndPreserveBackend(string sql, string state, string message)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => Scalar<object>(connection, sql));
        Assert.AreEqual(state, error.SqlState);
        Assert.AreEqual(message, error.MessageText);
        Assert.AreEqual("42", await Scalar<string>(connection, "SELECT custom_values.custom_number('42',1)::text"));
        Assert.AreEqual(connection.ProcessID, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }

    /// <summary>
    /// Retains custom storage through packed, compressed and external TOAST, independently of search_path.
    /// </summary>
    [TestMethod]
    public async Task CustomStorageSurvivesToastAndCatalogLookup()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        await Scalar<object>(connection, """
            SET search_path = pg_catalog;
            CREATE TEMP TABLE custom_toast (value custom_values.message);
            ALTER TABLE custom_toast ALTER COLUMN value SET STORAGE EXTERNAL;
            INSERT INTO custom_toast SELECT string_agg(md5(value::text),'')::custom_values.message FROM generate_series(1,3000) value;
            SELECT 1
            """);
        Assert.IsTrue(await Scalar<bool>(connection, """
            SELECT custom_values.custom_message(value,4)::text = value::text
                AND length(value::text) = 96000
                AND pg_column_size(value) > 90000 FROM custom_toast
            """));
        await Scalar<object>(connection, """
            CREATE TEMP TABLE custom_compressed(value custom_values.message);
            INSERT INTO custom_compressed VALUES (repeat('compress',20000)::custom_values.message);
            SELECT 1
            """);
        Assert.IsTrue(await Scalar<bool>(connection,
            "SELECT pg_column_size(value) < 10000 AND custom_values.custom_message(value,1)::text = repeat('compress',20000) FROM custom_compressed"));
    }

    /// <summary>
    /// Executes binary receive and send with independently constructed COPY protocol bytes.
    /// </summary>
    /// <param name="value">An exact signed storage value.</param>
    [TestMethod]
    [DataRow(long.MinValue)]
    [DataRow(0L)]
    [DataRow(long.MaxValue)]
    public async Task CustomBinaryCopyPreservesExactBytes(long value)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        await Scalar<object>(connection, "CREATE TEMP TABLE custom_binary(value custom_values.number); SELECT 1");
        byte[] payload = new byte[8];
        BinaryPrimitives.WriteInt64BigEndian(payload, value);
        byte[] input = CopyPayload(payload);
        await Import(connection, input);
        Assert.AreEqual(value, await Scalar<long>(connection, "SELECT value::bigint FROM custom_binary"));
        await using var output = new MemoryStream();
        await using (Stream copy = await connection.BeginRawBinaryCopyAsync("COPY custom_binary TO STDOUT (FORMAT BINARY)", context.CancellationToken))
        {
            await copy.CopyToAsync(output, context.CancellationToken);
        }

        Assert.AreSequenceEqual(input, output.ToArray());
    }

    /// <summary>
    /// Rejects truncated and trailing binary bytes without inserting a row or losing the connection.
    /// </summary>
    /// <param name="length">The invalid binary payload size.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(7)]
    [DataRow(9)]
    public async Task CustomBinaryErrorsPreserveConnectionAndRows(int length)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        await Scalar<object>(connection, "CREATE TEMP TABLE custom_binary(value custom_values.number); SELECT 1");
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => Import(connection, CopyPayload(new byte[length])));
        Assert.AreEqual("22P03", error.SqlState);
        Assert.AreEqual("Invalid number payload.", error.MessageText);
        Assert.AreEqual(0L, await Scalar<long>(connection, "SELECT count(*) FROM custom_binary"));
        await Import(connection, CopyPayload(new byte[8]));
        Assert.AreEqual(0L, await Scalar<long>(connection, "SELECT value::bigint FROM custom_binary"));
        Assert.AreEqual(connection.ProcessID, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }

    /// <summary>
    /// Decodes domains without confusing another custom type with the same storage family.
    /// </summary>
    [TestMethod]
    public async Task CustomDomainsPreserveIdentityAndArrayShape()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        await Scalar<object>(connection, """
            CREATE TEMP TABLE custom_domain_anchor(value integer);
            CREATE DOMAIN pg_temp.number_domain AS custom_values.number;
            CREATE DOMAIN pg_temp.numbers_domain AS custom_values.number[];
            SELECT 1
            """);
        Assert.AreEqual("42", await Scalar<string>(connection,
            "SELECT custom_values.custom_query('SELECT ''42''::pg_temp.number_domain')::text"));
        Assert.AreEqual("[0:1]={42,NULL}", await Scalar<string>(connection,
            "SELECT custom_values.custom_array_query('SELECT ''[0:1]={42,NULL}''::pg_temp.number_domain[]')::text"));
        Assert.AreEqual("[0:1]={42,NULL}", await Scalar<string>(connection,
            "SELECT custom_values.custom_array_query('SELECT ''[0:1]={42,NULL}''::pg_temp.numbers_domain')::text"));
        PostgresException wrong = await Assert.ThrowsExactlyAsync<PostgresException>(() => Scalar<object>(connection,
            "SELECT custom_values.custom_query('SELECT ''42''::custom_values.message')"));
        Assert.AreEqual("38000", wrong.SqlState);
        Assert.AreEqual("42", await Scalar<string>(connection, "SELECT custom_values.custom_number('42',1)::text"));
    }

    /// <summary>
    /// Requires actual workers and partial aggregation while combining stored custom states.
    /// </summary>
    [TestMethod]
    public async Task CustomAggregateStatesWorkInParallelWorkers()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(context.CancellationToken);
        await Scalar<object>(connection, """
            CREATE TABLE custom_values.parallel_input AS SELECT value::text::custom_values.number value FROM generate_series(1,30000) value;
            ALTER TABLE custom_values.parallel_input SET (parallel_workers=2);
            ANALYZE custom_values.parallel_input;
            SET LOCAL max_parallel_workers_per_gather=2;
            SET LOCAL min_parallel_table_scan_size=0;
            SET LOCAL parallel_setup_cost=0;
            SET LOCAL parallel_tuple_cost=0;
            SET LOCAL parallel_leader_participation=off;
            SELECT 1
            """);
        const string query = "SELECT custom_values.custom_sum(value)::text FROM custom_values.parallel_input";
        using JsonDocument plan = JsonDocument.Parse(await Scalar<string>(connection, "EXPLAIN (ANALYZE, FORMAT JSON) " + query));
        (int workers, bool partial) = InspectPlan(plan.RootElement[0].GetProperty("Plan"));
        Assert.IsGreaterThan(0, workers);
        Assert.IsTrue(partial);
        Assert.AreEqual("450015000", await Scalar<string>(connection, query));
    }

    /// <summary>
    /// Counts actual workers and finds the partial aggregation phase recursively.
    /// </summary>
    private static (int Workers, bool Partial) InspectPlan(JsonElement plan)
    {
        int workers = plan.TryGetProperty("Workers Launched", out JsonElement launched) ? launched.GetInt32() : 0;
        bool partial = plan.TryGetProperty("Partial Mode", out JsonElement mode) && mode.GetString() == "Partial";
        if (plan.TryGetProperty("Plans", out JsonElement children))
        {
            foreach (JsonElement child in children.EnumerateArray())
            {
                (int count, bool found) = InspectPlan(child);
                workers += count;
                partial |= found;
            }
        }

        return (workers, partial);
    }

    /// <summary>
    /// Installs a type-only extension, relocates it and recreates its OIDs in one backend.
    /// </summary>
    [TestMethod]
    public async Task CustomTypeOnlyExtensionTracksRelocationAndReinstallation()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(context.CancellationToken);
        await Scalar<object>(connection, """
            CREATE SCHEMA custom_first;
            CREATE SCHEMA custom_second;
            CREATE EXTENSION ankus_custom_types WITH SCHEMA custom_first;
            SET LOCAL search_path = pg_catalog;
            SELECT 1
            """);
        uint before = await Scalar<uint>(connection, "SELECT 'custom_first.distance'::regtype::oid");
        Assert.AreEqual("125mm", await Scalar<string>(connection, "SELECT '125mm'::custom_first.distance::text"));
        await Scalar<object>(connection, "ALTER EXTENSION ankus_custom_types SET SCHEMA custom_second; SELECT 1");
        Assert.AreEqual("-125mm", await Scalar<string>(connection, "SELECT '-125mm'::custom_second.distance::text"));
        Assert.AreEqual(before, await Scalar<uint>(connection, "SELECT 'custom_second.distance'::regtype::oid"));
        await Scalar<object>(connection, "DROP EXTENSION ankus_custom_types; CREATE EXTENSION ankus_custom_types WITH SCHEMA custom_first; SELECT 1");
        Assert.AreNotEqual(before, await Scalar<uint>(connection, "SELECT 'custom_first.distance'::regtype::oid"));
        Assert.AreEqual("0mm", await Scalar<string>(connection, "SELECT '0mm'::custom_first.distance::text"));
    }

    /// <summary>
    /// Constructs one complete COPY binary row independently of the extension's codec.
    /// </summary>
    private static byte[] CopyPayload(byte[] payload)
    {
        byte[] copy = new byte[27 + payload.Length];
        new byte[] { 80, 71, 67, 79, 80, 89, 10, 255, 13, 10, 0 }.CopyTo(copy, 0);
        BinaryPrimitives.WriteInt16BigEndian(copy.AsSpan(19), 1);
        BinaryPrimitives.WriteInt32BigEndian(copy.AsSpan(21), payload.Length);
        payload.CopyTo(copy, 25);
        BinaryPrimitives.WriteInt16BigEndian(copy.AsSpan(25 + payload.Length), -1);
        return copy;
    }

    /// <summary>
    /// Completes the raw COPY operation so server-side receive errors surface before return.
    /// </summary>
    private async Task Import(NpgsqlConnection connection, byte[] data)
    {
        await using Stream copy = await connection.BeginRawBinaryCopyAsync("COPY custom_binary FROM STDIN (FORMAT BINARY)", context.CancellationToken);
        await copy.WriteAsync(data, context.CancellationToken);
    }

    /// <summary>
    /// Executes a scalar with cancellation and preserves the server's exact result type.
    /// </summary>
    private async Task<T> Scalar<T>(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        object? result = await command.ExecuteScalarAsync(context.CancellationToken);
        Assert.IsInstanceOfType<T>(result);
        return (T)result;
    }
}
