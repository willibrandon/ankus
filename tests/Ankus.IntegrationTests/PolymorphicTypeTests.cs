using System.Buffers.Binary;
using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies tagged custom types through Native AOT, SQL storage and PostgreSQL error recovery.
/// </summary>
/// <param name="context">The per-test cancellation context.</param>
[TestClass]
public sealed class PolymorphicTypeTests(TestContext context)
{
    /// <summary>
    /// Preserves concrete variants, nested nullable collections and SQL NULL through each ownership path.
    /// </summary>
    /// <param name="mode">The direct or SPI path.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    [DataRow(5)]
    [DataRow(6)]
    [DataRow(7)]
    public async Task PolymorphicValuesAndNullsCrossOwnershipPaths(int mode)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.AreEqual("number:-2147483648", await Scalar<string>(connection, $$"""
            SELECT tagged_values.tagged_kind(tagged_values.tagged_message('{"Number":-2147483648,"$type":7}',{{mode}}))
            """));
        Assert.AreEqual("text:héllo 😀", await Scalar<string>(connection, $$"""
            SELECT tagged_values.tagged_kind(tagged_values.tagged_message('{"$type":"text","Text":"héllo 😀"}',{{mode}}))
            """));
        Assert.IsTrue(await Scalar<bool>(connection, $$"""
            SELECT tagged_values.tagged_number_message('{"Number":42}',{{mode}})::text::jsonb = '{"Number":42}'::jsonb
                AND pg_typeof(tagged_values.tagged_number_message('{"Number":42}',{{mode}})) = 'tagged_values.number_message'::regtype
                AND pg_typeof(tagged_values.tagged_message('{"$type":7,"Number":42}',{{mode}})) = 'tagged_values.message'::regtype
                AND tagged_values.tagged_number_message(NULL,{{mode}}) IS NULL
            """));
        Assert.IsTrue(await Scalar<bool>(connection, $$$$"""
            SELECT tagged_values.tagged_message(NULL,{{{{mode}}}}) IS NULL
                AND tagged_values.tagged_message('{"$type":"link","Next":{"Number":42,"$type":7}}',{{{{mode}}}})::text::jsonb
                    = '{"$type":"link","Next":{"$type":7,"Number":42}}'::jsonb
                AND tagged_values.tagged_batch('{"Items":[null,{"$type":7,"Number":1}],"List":[{"$type":"text","Text":""},null],"Map":{"A":null,"a":{"$type":7,"Number":3}}}',{{{{mode}}}})::text::jsonb
                    = '{"Items":[null,{"$type":7,"Number":1}],"List":[{"$type":"text","Text":""},null],"Map":{"A":null,"a":{"$type":7,"Number":3}}}'::jsonb
                AND tagged_values.tagged_batch('{"List":[],"Map":{}}',{{{{mode}}}})::text::jsonb = '{"Items":null,"List":[],"Map":{}}'::jsonb
            """));
    }

    /// <summary>
    /// Preserves array bounds, mixed variants, NULL cells and ordered set-returning values.
    /// </summary>
    /// <param name="mode">The direct or SPI path.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(4)]
    [DataRow(7)]
    public async Task PolymorphicArraysAndSetsPreserveValues(int mode)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.IsTrue(await Scalar<bool>(connection, $$$"""
            WITH input AS (SELECT $array$[-1:0][3:4]={{"{\"$type\":7,\"Number\":1}",NULL},{"{\"$type\":\"text\",\"Text\":\"x\"}","{\"$type\":7,\"Number\":9}"}}$array$::tagged_values.message[] value),
            output AS (SELECT tagged_values.tagged_messages(value,{{{mode}}}) value FROM input)
            SELECT array_dims(value) = '[-1:0][3:4]'
                AND tagged_values.tagged_kind(value[-1][3]) = 'number:1' AND value[-1][4] IS NULL
                AND tagged_values.tagged_kind(value[0][3]) = 'text:x' AND tagged_values.tagged_kind(value[0][4]) = 'number:9'
                AND cardinality(tagged_values.tagged_messages('{}',{{{mode}}})) = 0
                AND tagged_values.tagged_messages(NULL,{{{mode}}}) IS NULL FROM output
            """));
        Assert.AreEqual("text:x,NULL,number:9", await Scalar<string>(connection, """
            SELECT string_agg(coalesce(tagged_values.tagged_kind(value),'NULL'),',' ORDER BY ordinal)
            FROM tagged_values.tagged_rows('{"$type":"text","Text":"x"}') WITH ORDINALITY row(value,ordinal)
            """));
    }

    /// <summary>
    /// Preserves distinct SQL mappings for the same runtime subtype in tuple scalar and array cells.
    /// </summary>
    /// <param name="mode">The direct or SPI path.</param>
    /// <param name="scenario">Present, SQL NULL, or empty-array cells.</param>
    [TestMethod]
    [DataRow(0, 0)]
    [DataRow(0, 1)]
    [DataRow(0, 2)]
    [DataRow(1, 0)]
    [DataRow(1, 1)]
    [DataRow(1, 2)]
    [DataRow(4, 0)]
    [DataRow(7, 0)]
    public async Task PolymorphicTupleCellsRetainDeclaredMappings(int mode, int scenario)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        string members = scenario switch
        {
            0 => """
                '{"$type":7,"Number":1}'::tagged_values.message, '{"Number":2}'::tagged_values.number_message,
                ARRAY['{"$type":7,"Number":3}'::tagged_values.message,NULL], ARRAY['{"Number":4}'::tagged_values.number_message,NULL]
                """,
            1 => "NULL::tagged_values.message,NULL::tagged_values.number_message,NULL::tagged_values.message[],NULL::tagged_values.number_message[]",
            2 => """
                '{"$type":7,"Number":1}'::tagged_values.message, '{"Number":2}'::tagged_values.number_message,
                ARRAY[]::tagged_values.message[],ARRAY[]::tagged_values.number_message[]
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };
        Assert.IsTrue(await Scalar<bool>(connection, $$"""
            WITH input AS (SELECT ROW({{members}}) value)
            SELECT record_send(value) = record_send(tagged_values.tagged_tuple(value,{{mode}})) FROM input
            """));
    }

    /// <summary>
    /// Keeps declared base array identity for covariant CLR vectors in SPI and erased tuple cells.
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
    public async Task PolymorphicCovariantVectorsRetainDeclaredMappings(int mode)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.IsTrue(await Scalar<bool>(connection, $$"""
            WITH output AS (SELECT tagged_values.tagged_covariant_vector({{mode}}) value)
            SELECT pg_typeof(value) = 'tagged_values.message[]'::regtype AND cardinality(value) = 2
                AND value[1]::text::jsonb = '{"$type":7,"Number":7}'::jsonb AND value[2] IS NULL FROM output
            """));
        Assert.IsTrue(await Scalar<bool>(connection, $$"""
            WITH input AS (SELECT ROW(ARRAY['{"$type":7,"Number":7}'::tagged_values.message,NULL]) value)
            SELECT record_send(value) = record_send(tagged_values.tagged_covariant_tuple(value,{{mode}})) FROM input
            """));
    }

    /// <summary>
    /// Reports invalid discriminators as input errors and leaves the same backend usable.
    /// </summary>
    /// <param name="input">The invalid tagged document.</param>
    [TestMethod]
    [DataRow("null")]
    [DataRow("{\"Number\":1}")]
    [DataRow("{\"$type\":null,\"Number\":1}")]
    [DataRow("{\"$type\":\"7\",\"Number\":1}")]
    [DataRow("{\"$type\":true,\"Number\":1}")]
    [DataRow("{\"$type\":7.5,\"Number\":1}")]
    [DataRow("{\"$type\":{},\"Number\":1}")]
    [DataRow("{\"$type\":7,\"Number\":1,\"$type\":7}")]
    [DataRow("{\"$type\":\"link\",\"Next\":{\"Number\":1}}")]
    [DataRow("{\"$type\":\"text\",\"Text\":null}")]
    [DataRow("{\"$type\":\"\\ud800\",\"Number\":1}")]
    public async Task PolymorphicInputErrorsPreserveBackend(string input)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        int process = connection.ProcessID;
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => Scalar<object>(connection,
            $"SELECT $value${input}$value$::tagged_values.message"));
        Assert.AreEqual("22P02", error.SqlState);
        Assert.AreEqual("number:42", await Scalar<string>(connection, "SELECT tagged_values.tagged_kind(tagged_values.tagged_message('{\"$type\":7,\"Number\":42}',1))"));
        Assert.AreEqual(process, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }

    /// <summary>
    /// Rejects unknown runtime types and cycles after managed unwinding without replacing the backend.
    /// </summary>
    /// <param name="function">The invalid-result function.</param>
    /// <param name="message">The diagnostic proving the intended writer failure occurred.</param>
    [TestMethod]
    [DataRow("tagged_unknown", "Unregistered runtime subtype.")]
    [DataRow("tagged_cycle", "Custom-type nesting exceeds 64 levels.")]
    public async Task PolymorphicWriteErrorsPreserveBackend(string function, string message)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        int process = connection.ProcessID;
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => Scalar<object>(connection,
            $"SELECT tagged_values.{function}()"));
        Assert.AreEqual("38000", error.SqlState);
        Assert.AreEqual(message, error.MessageText);
        Assert.AreEqual("number:42", await Scalar<string>(connection, "SELECT tagged_values.tagged_kind('{\"$type\":7,\"Number\":42}')"));
        Assert.AreEqual(process, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }

    /// <summary>
    /// Imports an independently specified tagged CBOR map and exports its exact binary COPY representation.
    /// </summary>
    [TestMethod]
    public async Task PolymorphicBinaryCopyUsesIndependentFixture()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        await Scalar<object>(connection, "CREATE TEMP TABLE tagged_binary(value tagged_values.message); SELECT 1");
        byte[] input = CopyPayload(Convert.FromHexString("A265247479706507664E756D626572182A"));
        await Import(connection, input);
        Assert.AreEqual("number:42", await Scalar<string>(connection, "SELECT tagged_values.tagged_kind(value) FROM tagged_binary"));
        await using var output = new MemoryStream();
        await using (Stream copy = await connection.BeginRawBinaryCopyAsync("COPY tagged_binary TO STDOUT (FORMAT BINARY)", context.CancellationToken))
        {
            await copy.CopyToAsync(output, context.CancellationToken);
        }

        Assert.AreSequenceEqual(input, output.ToArray());
    }

    /// <summary>
    /// Rejects malformed binary tags without inserting rows and permits a valid subsequent receive.
    /// </summary>
    /// <param name="hex">The malformed CBOR bytes.</param>
    [TestMethod]
    [DataRow("")]
    [DataRow("A1664E756D626572182A")]
    [DataRow("A2652474797065F6664E756D626572182A")]
    [DataRow("A2652474797065F5664E756D626572182A")]
    [DataRow("A26524747970656137664E756D626572182A")]
    [DataRow("A265247479706507664E756D626572182A00")]
    [DataRow("A365247479706507664E756D626572182A65247479706507")]
    public async Task PolymorphicBinaryErrorsPreserveBackendAndRows(string hex)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        int process = connection.ProcessID;
        await Scalar<object>(connection, "CREATE TEMP TABLE tagged_binary(value tagged_values.message); SELECT 1");
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => Import(connection, CopyPayload(Convert.FromHexString(hex))));
        Assert.AreEqual("22P03", error.SqlState);
        Assert.AreEqual(0L, await Scalar<long>(connection, "SELECT count(*) FROM tagged_binary"));
        await Import(connection, CopyPayload(Convert.FromHexString("A265247479706507664E756D626572182A")));
        Assert.AreEqual("number:42", await Scalar<string>(connection, "SELECT tagged_values.tagged_kind(value) FROM tagged_binary"));
        Assert.AreEqual(process, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }

    /// <summary>
    /// Retains exact variant text through external and compressed TOAST plus typed SPI decoding.
    /// </summary>
    [TestMethod]
    public async Task PolymorphicStorageSurvivesToast()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        await Scalar<object>(connection, """
            CREATE TEMP TABLE tagged_external(value tagged_values.message);
            ALTER TABLE tagged_external ALTER COLUMN value SET STORAGE EXTERNAL;
            INSERT INTO tagged_external SELECT json_build_object('$type','text','Text',string_agg(md5(value::text),''))::text::tagged_values.message FROM generate_series(1,3000) value;
            CREATE TEMP TABLE tagged_compressed(value tagged_values.message);
            INSERT INTO tagged_compressed VALUES (json_build_object('$type','text','Text',repeat('compress',20000))::text::tagged_values.message);
            SELECT 1
            """);
        Assert.IsTrue(await Scalar<bool>(connection, """
            SELECT pg_column_size(value) > 90000
                AND tagged_values.tagged_kind(tagged_values.tagged_message(value,4)) =
                    'text:' || (SELECT string_agg(md5(part::text),'') FROM generate_series(1,3000) part)
            FROM tagged_external
            """));
        Assert.IsTrue(await Scalar<bool>(connection, """
            SELECT pg_column_size(value) < 10000
                AND tagged_values.tagged_kind(tagged_values.tagged_message(value,1)) = 'text:' || repeat('compress',20000)
            FROM tagged_compressed
            """));
    }

    /// <summary>
    /// Constructs a complete binary COPY row independently of generated code.
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
    /// Completes COPY so receive errors are reported before return.
    /// </summary>
    private async Task Import(NpgsqlConnection connection, byte[] data)
    {
        await using Stream copy = await connection.BeginRawBinaryCopyAsync("COPY tagged_binary FROM STDIN (FORMAT BINARY)", context.CancellationToken);
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
