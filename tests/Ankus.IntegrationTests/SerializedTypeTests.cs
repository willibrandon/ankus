using System.Buffers.Binary;
using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Proves generated serialization through published Native AOT type I/O and PostgreSQL storage.
/// </summary>
/// <param name="context">The per-test cancellation context.</param>
[TestClass]
public sealed class SerializedTypeTests(TestContext context)
{
    /// <summary>
    /// Preserves exact nested values and SQL NULL through every scalar ownership path.
    /// </summary>
    /// <param name="mode">The direct or SPI ownership path.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    [DataRow(5)]
    [DataRow(6)]
    [DataRow(7)]
    public async Task SerializedValuesAndNullsCrossOwnershipPaths(int mode)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        const string input = """{"Name":"héllo 😀","Count":-9223372036854775808,"Items":[null,{"Number":0,"Text":""}],"Tags":["",null,"tag"],"Lookup":{"é😀":null,"item":{"Number":-1,"Text":null}}}""";
        Assert.IsTrue(await Scalar<bool>(connection, $$$"""
            SELECT serialized_values.serialized_envelope($value${{{input}}}$value$,{{{mode}}})::text::jsonb = $value${{{input}}}$value$::jsonb
                AND serialized_values.serialized_envelope(NULL,{{{mode}}}) IS NULL
                AND serialized_values.serialized_counter('{"Value":-2147483648}',{{{mode}}})::text::jsonb = '{"Value":-2147483648}'::jsonb
                AND serialized_values.serialized_counter('{"Value":2147483647}',{{{mode}}})::text::jsonb = '{"Value":2147483647}'::jsonb
                AND serialized_values.serialized_counter(NULL,{{{mode}}}) IS NULL
                AND pg_typeof(serialized_values.serialized_envelope($value${{{input}}}$value$,{{{mode}}})) = 'serialized_values.envelope'::regtype
            """));
        Assert.IsTrue(await Scalar<bool>(connection, $$$"""
            SELECT serialized_values.serialized_envelope('{"Name":"","Count":0}',{{{mode}}})::text::jsonb = '{"Name":"","Count":0,"Items":null,"Tags":null,"Lookup":null}'::jsonb
                AND serialized_values.serialized_envelope('{"Name":"","Count":0,"Items":[],"Tags":[],"Lookup":{}}',{{{mode}}})::text::jsonb = '{"Name":"","Count":0,"Items":[],"Tags":[],"Lookup":{}}'::jsonb
            """));
    }

    /// <summary>
    /// Preserves multidimensional custom arrays, nondefault bounds, NULL cells, and SETOF results.
    /// </summary>
    /// <param name="mode">The direct or SPI ownership path.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(4)]
    [DataRow(7)]
    public async Task SerializedArraysPreserveShape(int mode)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.IsTrue(await Scalar<bool>(connection, $$$"""
            WITH input AS (SELECT $array$[-1:0][3:4]={{"{\"Value\":1}",NULL},{"{\"Value\":0}","{\"Value\":9}"}}$array$::serialized_values.counter[] value),
            output AS (SELECT serialized_values.serialized_counters(value,{{{mode}}}) value FROM input)
            SELECT array_dims(value) = '[-1:0][3:4]'
                AND value[-1][3]::text::jsonb = '{"Value":1}'::jsonb
                AND value[-1][4] IS NULL
                AND value[0][3]::text::jsonb = '{"Value":0}'::jsonb
                AND value[0][4]::text::jsonb = '{"Value":9}'::jsonb
                AND cardinality(serialized_values.serialized_counters('{}',{{{mode}}})) = 0
                AND serialized_values.serialized_counters(NULL,{{{mode}}}) IS NULL FROM output
            """));
        Assert.AreEqual("7,NULL,9", await Scalar<string>(connection, """
            SELECT string_agg(coalesce(value::text::jsonb->>'Value','NULL'),',' ORDER BY ordinal)
            FROM serialized_values.serialized_rows('{"Value":7}') WITH ORDINALITY row(value,ordinal)
            """));
    }

    /// <summary>
    /// Exercises named enum values and the ordinary mutable JSON contract in PostgreSQL.
    /// </summary>
    [TestMethod]
    public async Task SerializedEnumsAndMutableMembersUseGeneratedContracts()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.AreEqual("\"Ready\"", await Scalar<string>(connection, "SELECT serialized_values.serialized_mode('\"Ready\"')::text"));
        Assert.AreEqual("\"Stopped\"", await Scalar<string>(connection, "SELECT serialized_values.serialized_mode('\"Stopped\"')::text"));
        Assert.AreEqual(9, await Scalar<int>(connection, "SELECT serialized_values.serialized_mutable_number('{\"n\":9,\"Text\":null,\"Ignored\":{\"ignored\":true}}')"));
        Assert.IsTrue(await Scalar<bool>(connection, "SELECT '{\"n\":9,\"Text\":\"value\"}'::serialized_values.mutable::text::jsonb = '{\"n\":9,\"Text\":\"value\"}'::jsonb"));
    }

    /// <summary>
    /// Rejects invalid documents after managed unwinding and preserves the same usable PostgreSQL backend.
    /// </summary>
    /// <param name="input">The invalid JSON input.</param>
    /// <param name="type">The target SQL type.</param>
    [TestMethod]
    [DataRow("null", "envelope")]
    [DataRow("{}", "envelope")]
    [DataRow("{\"Name\":null,\"Count\":0}", "envelope")]
    [DataRow("{\"Name\":\"x\",\"Count\":0,\"Count\":1}", "envelope")]
    [DataRow("{\"Name\":\"x\",\"Count\":9223372036854775808}", "envelope")]
    [DataRow("{\"Name\":\"x\",\"Count\":0,\"Items\":[{\"Number\":null}]}", "envelope")]
    [DataRow("{\"Name\":\"x\",\"Count\":0,\"Lookup\":{\"x\":null,\"x\":null}}", "envelope")]
    [DataRow("{\"Name\":\"x\",\"Count\":0", "envelope")]
    [DataRow("{\"Name\":\"x\",\"Count\":0} false", "envelope")]
    [DataRow("{\"Name\":\"\\ud800\",\"Count\":0}", "envelope")]
    [DataRow("{\"Value\":2147483648}", "counter")]
    [DataRow("\"Unknown\"", "mode")]
    [DataRow("7", "mode")]
    [DataRow("{\"n\":9}", "mutable")]
    public async Task SerializedInputErrorsPreserveBackend(string input, string type)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        int process = connection.ProcessID;
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => Scalar<object>(connection,
            $"SELECT $value${input}$value$::serialized_values.{type}"));
        Assert.AreEqual("22P02", error.SqlState);
        Assert.AreEqual(42, await Scalar<int>(connection, "SELECT (serialized_values.serialized_counter('{\"Value\":42}',1)::text::jsonb->>'Value')::integer"));
        Assert.AreEqual(process, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }

    /// <summary>
    /// Bounds cyclic graphs and invalid enum writes before reporting a recoverable PostgreSQL error.
    /// </summary>
    /// <param name="function">The function producing an invalid managed value.</param>
    [TestMethod]
    [DataRow("serialized_cycle")]
    [DataRow("serialized_invalid_mode")]
    public async Task SerializedWriteErrorsPreserveBackend(string function)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        int process = connection.ProcessID;
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => Scalar<object>(connection,
            $"SELECT serialized_values.{function}()"));
        Assert.AreEqual("38000", error.SqlState);
        Assert.AreEqual(42, await Scalar<int>(connection, "SELECT (serialized_values.serialized_counter('{\"Value\":42}',1)::text::jsonb->>'Value')::integer"));
        Assert.AreEqual(process, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }

    /// <summary>
    /// Uses independent CBOR bytes for binary receive and verifies exact byte-for-byte send output.
    /// </summary>
    [TestMethod]
    public async Task SerializedBinaryCopyUsesIndependentCborFixture()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        await Scalar<object>(connection, "CREATE TEMP TABLE serialized_binary(value serialized_values.counter); SELECT 1");
        byte[] input = CopyPayload(Convert.FromHexString("A16556616C7565182A"));
        await Import(connection, input);
        Assert.AreEqual(42, await Scalar<int>(connection, "SELECT (value::text::jsonb->>'Value')::integer FROM serialized_binary"));
        await using var output = new MemoryStream();
        await using (Stream copy = await connection.BeginRawBinaryCopyAsync("COPY serialized_binary TO STDOUT (FORMAT BINARY)", context.CancellationToken))
        {
            await copy.CopyToAsync(output, context.CancellationToken);
        }

        Assert.AreSequenceEqual(input, output.ToArray());
    }

    /// <summary>
    /// Rejects malformed CBOR without inserting rows and permits a subsequent valid binary receive.
    /// </summary>
    /// <param name="hex">The malformed storage payload.</param>
    [TestMethod]
    [DataRow("")]
    [DataRow("F6")]
    [DataRow("A0")]
    [DataRow("A16556616C7565")]
    [DataRow("A16556616C7565182A00")]
    [DataRow("A26556616C7565016556616C756502")]
    [DataRow("A16556616C75651A80000000")]
    [DataRow("A16556616C75656178")]
    public async Task SerializedBinaryErrorsPreserveBackendAndRows(string hex)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        int process = connection.ProcessID;
        await Scalar<object>(connection, "CREATE TEMP TABLE serialized_binary(value serialized_values.counter); SELECT 1");
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => Import(connection, CopyPayload(Convert.FromHexString(hex))));
        Assert.AreEqual("22P03", error.SqlState);
        Assert.AreEqual(0L, await Scalar<long>(connection, "SELECT count(*) FROM serialized_binary"));
        await Import(connection, CopyPayload(Convert.FromHexString("A16556616C7565182A")));
        Assert.AreEqual(42, await Scalar<int>(connection, "SELECT (value::text::jsonb->>'Value')::integer FROM serialized_binary"));
        Assert.AreEqual(process, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }

    /// <summary>
    /// Retains large immutable values through external and compressed TOAST and typed SPI materialization.
    /// </summary>
    [TestMethod]
    public async Task SerializedStorageSurvivesToast()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        await Scalar<object>(connection, """
            CREATE TEMP TABLE serialized_external(value serialized_values.envelope);
            ALTER TABLE serialized_external ALTER COLUMN value SET STORAGE EXTERNAL;
            INSERT INTO serialized_external SELECT json_build_object('Name',string_agg(md5(value::text),''),'Count',7)::text::serialized_values.envelope FROM generate_series(1,3000) value;
            CREATE TEMP TABLE serialized_compressed(value serialized_values.envelope);
            INSERT INTO serialized_compressed VALUES (json_build_object('Name',repeat('compress',20000),'Count',9)::text::serialized_values.envelope);
            SELECT 1
            """);
        Assert.IsTrue(await Scalar<bool>(connection, """
            SELECT serialized_values.serialized_envelope(value,4)::text::jsonb = value::text::jsonb
                AND length(value::text::jsonb->>'Name') = 96000 AND pg_column_size(value) > 90000
                AND value::text::jsonb->>'Name' = (SELECT string_agg(md5(part::text),'') FROM generate_series(1,3000) part)
            FROM serialized_external
            """));
        Assert.IsTrue(await Scalar<bool>(connection, """
            SELECT pg_column_size(value) < 10000
                AND serialized_values.serialized_envelope(value,1)::text::jsonb->>'Name' = repeat('compress',20000)
                AND value::text::jsonb->>'Count' = '9' FROM serialized_compressed
            """));
    }

    /// <summary>
    /// Constructs one complete COPY row independently of the generated serializer.
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
    /// Completes COPY before returning so receive errors are observed by the calling assertion.
    /// </summary>
    private async Task Import(NpgsqlConnection connection, byte[] data)
    {
        await using Stream copy = await connection.BeginRawBinaryCopyAsync("COPY serialized_binary FROM STDIN (FORMAT BINARY)", context.CancellationToken);
        await copy.WriteAsync(data, context.CancellationToken);
    }

    /// <summary>
    /// Executes an exact typed scalar with fixture cancellation.
    /// </summary>
    private async Task<T> Scalar<T>(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        object? result = await command.ExecuteScalarAsync(context.CancellationToken);
        return Assert.IsInstanceOfType<T>(result);
    }
}
