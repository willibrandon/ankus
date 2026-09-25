using System.Buffers.Binary;
using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies independent custom text and generated storage in a published Native AOT extension.
/// </summary>
/// <param name="context">The per-test cancellation context.</param>
[TestClass]
public sealed class CustomTextTypeTests(TestContext context)
{
    /// <summary>
    /// Preserves field values, enum labels, concrete variants and SQL NULL across all ownership paths.
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
    public async Task CustomTextValuesAndNullsCrossOwnershipPaths(int mode)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.IsTrue(await Scalar<bool>(connection, $$"""
            SELECT custom_text.text_value_echo('-2147483648|héllo 😀',{{mode}})::text = '-2147483648|héllo 😀'
                AND custom_text.text_value_echo('2147483647|',{{mode}})::text = '2147483647|'
                AND custom_text.text_value_echo('0|a|b',{{mode}})::text = '0|a|b'
                AND custom_text.text_value_echo(NULL,{{mode}}) IS NULL
                AND custom_text.text_mode_echo('on',{{mode}})::text = 'on'
                AND custom_text.text_mode_echo('off',{{mode}})::text = 'off'
                AND custom_text.text_mode_echo(NULL,{{mode}}) IS NULL
                AND custom_text.text_message_kind(custom_text.text_message_echo('N:-7',{{mode}})) = 'number:-7'
                AND custom_text.text_message_echo('T:héllo 😀',{{mode}})::text = 'T:héllo 😀'
                AND custom_text.text_message_kind(custom_text.text_message_echo('T:',{{mode}})) = 'text:'
                AND custom_text.text_message_echo(NULL,{{mode}}) IS NULL
                AND pg_typeof(custom_text.text_value_echo('7|ok',{{mode}})) = 'custom_text.value'::regtype
                AND pg_typeof(custom_text.text_mode_echo('on',{{mode}})) = 'custom_text.mode'::regtype
                AND pg_typeof(custom_text.text_message_echo('N:7',{{mode}})) = 'custom_text.message'::regtype
            """));
    }

    /// <summary>
    /// Preserves shaped arrays, NULL and empty containers, set values and heterogeneous tuple cells.
    /// </summary>
    /// <param name="mode">The direct or SPI path.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(4)]
    [DataRow(7)]
    public async Task CustomTextArraysSetsAndTuplePreserveValues(int mode)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.IsTrue(await Scalar<bool>(connection, $$$"""
            SELECT custom_text.text_values_echo('[2:3][-1:0]={{1|a,NULL},{0|,9|z}}',{{{mode}}})::text = '[2:3][-1:0]={{1|a,NULL},{0|,9|z}}'
                AND custom_text.text_values_echo('{}',{{{mode}}})::text = '{}'
                AND custom_text.text_values_echo(NULL,{{{mode}}}) IS NULL
                AND custom_text.text_modes_echo('[3:5]={on,NULL,off}',{{{mode}}})::text = '[3:5]={on,NULL,off}'
                AND custom_text.text_modes_echo('{}',{{{mode}}})::text = '{}'
                AND custom_text.text_modes_echo(NULL,{{{mode}}}) IS NULL
            """));
        Assert.AreEqual("{7|first,NULL,9|row}", await Scalar<string>(connection,
            "SELECT array_agg(value::text)::text FROM custom_text.text_rows('7|first') value"));
        Assert.AreEqual("{NULL,NULL,9|row}", await Scalar<string>(connection,
            "SELECT array_agg(value::text)::text FROM custom_text.text_rows(NULL) value"));
        foreach (string row in new[]
        {
            "ROW('7|ok'::custom_text.value,'on'::custom_text.mode,'N:9'::custom_text.message,'[3:4]={7|ok,NULL}'::custom_text.value[])",
            "ROW(NULL::custom_text.value,NULL::custom_text.mode,NULL::custom_text.message,'{}'::custom_text.value[])",
            "ROW(NULL::custom_text.value,NULL::custom_text.mode,NULL::custom_text.message,NULL::custom_text.value[])",
        })
        {
            Assert.IsTrue(await Scalar<bool>(connection, $"WITH input AS (SELECT {row} value) SELECT record_send(value) = record_send(custom_text.text_tuple(value,{mode})) FROM input"));
        }
    }

    /// <summary>
    /// Nested attributed values use the containing structural contract without text-codec calls.
    /// </summary>
    [TestMethod]
    public async Task CustomTextNestedContractsRemainStructural()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.AreEqual("0:0:0:0", await Scalar<string>(connection, "SELECT custom_text.text_counters()"));
        Assert.IsTrue(await Scalar<bool>(connection, """
            SELECT custom_text.text_envelope_echo('{"Value":{"Number":7,"Text":"ok"},"Mode":"Ready","Message":{"$type":7,"Number":42}}',7)::text::jsonb
                = '{"Value":{"Number":7,"Text":"ok"},"Mode":"Ready","Message":{"$type":7,"Number":42}}'::jsonb
                AND custom_text.text_envelope_echo('{"Mode":"Off"}',1)::text::jsonb = '{"Value":null,"Mode":"Off","Message":null}'::jsonb
            """));
        Assert.AreEqual("0:0:0:0", await Scalar<string>(connection, "SELECT custom_text.text_counters()"));
    }

    /// <summary>
    /// Only first text conversion constructs the codec, and later native/SPI storage never invokes text callbacks.
    /// </summary>
    /// <param name="formatFirst">Whether formatting is the first custom text operation.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CustomTextCodecConstructionIsDeferredAndCached(bool formatFirst)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.AreEqual("0:0:0:0", await Scalar<string>(connection, "SELECT custom_text.text_counters()"));
        Assert.AreEqual("a2664e756d626572182a6454657874626f6b", await Scalar<string>(connection,
            "SELECT encode(custom_text.value_send(custom_text.text_value_echo(custom_text.text_make_value(42,'ok'),7)),'hex')"));
        Assert.AreEqual("0:0:0:0", await Scalar<string>(connection, "SELECT custom_text.text_counters()"));
        Assert.AreEqual("7|hello", await Scalar<string>(connection, formatFirst
            ? "SELECT custom_text.text_make_value(7,'hello')::text" : "SELECT custom_text.value_in('7|hello'::cstring)::text"));
        Assert.AreEqual(formatFirst ? "1:0:1:0" : "1:1:1:0", await Scalar<string>(connection, "SELECT custom_text.text_counters()"));
        Assert.AreEqual("9|again", await Scalar<string>(connection, "SELECT custom_text.text_value_echo(custom_text.value_in('9|again'::cstring),7)::text"));
        Assert.AreEqual(formatFirst ? "1:1:2:0" : "1:2:2:0", await Scalar<string>(connection, "SELECT custom_text.text_counters()"));
    }

    /// <summary>
    /// A failed text constructor is cached and guarded while generated CBOR remains usable before and after failure.
    /// </summary>
    [TestMethod]
    public async Task CustomTextFactoryErrorsLeaveBinaryStorageUsable()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        int process = connection.ProcessID;
        Assert.AreEqual(42, await Scalar<int>(connection, "SELECT custom_text.text_fault_number(custom_text.text_make_fault(42))"));
        Assert.AreEqual("a1664e756d626572182a", await Scalar<string>(connection, "SELECT encode(custom_text.fault_send(custom_text.text_make_fault(42)),'hex')"));
        Assert.AreEqual("0:0:0:0", await Scalar<string>(connection, "SELECT custom_text.text_counters()"));
        foreach (string sql in new[] { "SELECT custom_text.text_make_fault(7)::text", "SELECT '7'::custom_text.fault" })
        {
            PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => Scalar<object>(connection, sql));
            Assert.AreEqual("P7910", error.SqlState);
            Assert.AreEqual("custom text factory failed", error.MessageText);
            Assert.AreEqual("0:0:0:1", await Scalar<string>(connection, "SELECT custom_text.text_counters()"));
        }

        Assert.AreEqual(-7, await Scalar<int>(connection, "SELECT custom_text.text_fault_number(custom_text.text_make_fault(-7))"));
        Assert.AreEqual("a1664e756d62657226", await Scalar<string>(connection, "SELECT encode(custom_text.fault_send(custom_text.text_make_fault(-7)),'hex')"));
        Assert.AreEqual(process, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }

    /// <summary>
    /// Rejects unknown NULL coercion while preserving typed SQL NULL and converter metadata.
    /// </summary>
    /// <param name="type">The custom type name.</param>
    /// <param name="message">The exact configured error message.</param>
    [TestMethod]
    [DataRow("required_default", "default value isn't optional 😀")]
    [DataRow("required_empty", "")]
    [DataRow("required_full", "full value is required")]
    [DataRow("fault", "fault text needs input")]
    public async Task CustomTextNullInputOptionsPreserveSqlNull(string type, string message)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        int process = connection.ProcessID;
        string echo = type switch
        {
            "required_default" => "text_default_echo",
            "required_full" => "text_full_echo",
            "required_empty" => "text_empty_echo",
            _ => "text_fault_echo",
        };

        Assert.IsTrue(await Scalar<bool>(connection, $$"""
            SELECT NOT input.proisstrict AND output.proisstrict AND receive.proisstrict AND send.proisstrict
            FROM pg_type t JOIN pg_proc input ON input.oid=t.typinput JOIN pg_proc output ON output.oid=t.typoutput
                JOIN pg_proc receive ON receive.oid=t.typreceive JOIN pg_proc send ON send.oid=t.typsend
            WHERE t.oid='custom_text.{{type}}'::regtype
            """));
        Assert.IsTrue(await Scalar<bool>(connection, $$"""
            SELECT custom_text.text_null_{{type}}() IS NULL
                AND custom_text.{{echo}}(custom_text.text_null_{{type}}()) IS NULL
                AND custom_text.text_value_echo(NULL,7) IS NULL
            """));
        Assert.AreEqual("0:0:0:0", await Scalar<string>(connection, "SELECT custom_text.text_counters()"));
        foreach (string sql in new[] { $"SELECT custom_text.{type}_in(NULL)", $"SELECT NULL::custom_text.{type}", $"SELECT custom_text.{echo}(NULL)" })
        {
            PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => Scalar<object>(connection, sql));
            Assert.AreEqual("22004", error.SqlState);
            Assert.AreEqual(message, error.MessageText);
        }

        Assert.AreEqual("0:0:0:0", await Scalar<string>(connection, "SELECT custom_text.text_counters()"));
        Assert.IsTrue(await Scalar<bool>(connection, """
            SELECT custom_text.mode_in(NULL) IS NULL AND custom_text.envelope_in(NULL) IS NULL
                AND custom_text.value_in(NULL) IS NULL AND custom_values.number_in(NULL) IS NULL
                AND (SELECT bool_and(input.proisstrict) FROM pg_type t JOIN pg_proc input ON input.oid=t.typinput
                    WHERE t.oid IN ('custom_text.value'::regtype,'custom_text.mode'::regtype,'custom_text.envelope'::regtype,'custom_values.number'::regtype))
                AND custom_text.text_default_echo('{"Number":7}')::text::jsonb = '{"Number":7}'::jsonb
                AND custom_text.text_full_echo('9')::text = '9'
            """));
        Assert.AreEqual(process, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }

    /// <summary>
    /// Opted-in input policies reject native NULL pointers from text array and COPY conversion without affecting stored SQL NULL.
    /// </summary>
    /// <param name="type">The configured type name.</param>
    /// <param name="message">The exact null-input diagnostic.</param>
    [TestMethod]
    [DataRow("required_default", "default value isn't optional 😀")]
    [DataRow("required_empty", "")]
    [DataRow("required_full", "full value is required")]
    [DataRow("fault", "fault text needs input")]
    public async Task CustomTextNullInputPolicyAppliesToTextArraysAndCopy(string type, string message)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        int process = connection.ProcessID;
        Assert.IsTrue(await Scalar<bool>(connection, $$"""
            SELECT (ARRAY[custom_text.text_null_{{type}}()])[1] IS NULL
                AND cardinality(ARRAY[custom_text.text_null_{{type}}()]) = 1
                AND cardinality('{}'::custom_text.{{type}}[]) = 0
                AND NULL::custom_text.{{type}}[] IS NULL
            """));
        PostgresException arrayError = await Assert.ThrowsExactlyAsync<PostgresException>(() => Scalar<object>(connection,
            $"SELECT '{{NULL}}'::custom_text.{type}[]"));
        Assert.AreEqual("22004", arrayError.SqlState);
        Assert.AreEqual(message, arrayError.MessageText);
        await Scalar<object>(connection, $"CREATE TEMP TABLE custom_text_null(value custom_text.{type}); SELECT 1");
        PostgresException copyError = await Assert.ThrowsExactlyAsync<PostgresException>(() => ImportTextNull(connection));
        Assert.AreEqual("22004", copyError.SqlState);
        Assert.AreEqual(message, copyError.MessageText);
        Assert.AreEqual(0L, await Scalar<long>(connection, "SELECT count(*) FROM custom_text_null"));
        Assert.AreEqual("0:0:0:0", await Scalar<string>(connection, "SELECT custom_text.text_counters()"));
        await Scalar<object>(connection, $"INSERT INTO custom_text_null SELECT custom_text.text_null_{type}(); SELECT 1");
        await Import(connection, Convert.FromHexString("5047434F50590AFF0D0A0000000000000000000001FFFFFFFFFFFF"), "custom_text_null");
        Assert.AreEqual(2L, await Scalar<long>(connection, "SELECT count(*) FROM custom_text_null WHERE value IS NULL"));
        Assert.AreEqual("0:0:0:0", await Scalar<string>(connection, "SELECT custom_text.text_counters()"));
        Assert.AreEqual(process, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }

    /// <summary>
    /// Preserves user error identity and rejects null conversion results before recovering on the same backend.
    /// </summary>
    /// <param name="sql">The failing text operation.</param>
    /// <param name="state">The exact expected SQLSTATE.</param>
    /// <param name="message">The exact diagnostic.</param>
    [TestMethod]
    [DataRow("SELECT '!pgparse'::custom_text.value", "P7911", "custom text parse failed")]
    [DataRow("SELECT '!managedparse'::custom_text.value", "38000", "ordinary text parse failed")]
    [DataRow("SELECT '!nullparse'::custom_text.value", "38000", "A custom text codec returned null for a present text input.")]
    [DataRow("SELECT 'invalid'::custom_text.value", "22P02", "Expected integer|text.")]
    [DataRow("SELECT custom_text.text_make_value(7,'!pgformat')::text", "P7912", "custom text format failed")]
    [DataRow("SELECT custom_text.text_make_value(7,'!managedformat')::text", "38000", "ordinary text format failed")]
    [DataRow("SELECT custom_text.text_make_value(7,'!nullformat')::text", "38000", "A custom text codec returned null text for a present value.")]
    [DataRow("SELECT 'Ready'::custom_text.mode", "22P02", "Expected on or off.")]
    [DataRow("SELECT 'other'::custom_text.message", "22P02", "Expected N: or T:.")]
    public async Task CustomTextCodecErrorsPreserveBackend(string sql, string state, string message)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        int process = connection.ProcessID;
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => Scalar<object>(connection, sql));
        Assert.AreEqual(state, error.SqlState);
        Assert.AreEqual(message, error.MessageText);
        Assert.AreEqual("42|ok", await Scalar<string>(connection, "SELECT custom_text.text_value_echo('42|ok',7)::text"));
        Assert.AreEqual(process, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }

    /// <summary>
    /// Independent CBOR fixtures prove record, enum and tagged storage bytes are separate from custom text.
    /// </summary>
    /// <param name="type">The SQL type name.</param>
    /// <param name="hex">The independent CBOR payload.</param>
    /// <param name="expected">The expected domain text.</param>
    [TestMethod]
    [DataRow("value", "A2664E756D626572182A6454657874626F6B", "42|ok")]
    [DataRow("mode", "655265616479", "on")]
    [DataRow("message", "A265247479706507664E756D626572182A", "N:42")]
    public async Task CustomTextBinaryCopyUsesIndependentFixture(string type, string hex, string expected)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        await Scalar<object>(connection, $"CREATE TEMP TABLE custom_text_binary(value custom_text.{type}); SELECT 1");
        byte[] input = CopyPayload(Convert.FromHexString(hex));
        await Import(connection, input);
        Assert.AreEqual(hex.ToLowerInvariant(), await Scalar<string>(connection, $"SELECT encode(custom_text.{type}_send(value),'hex') FROM custom_text_binary"));
        Assert.AreEqual("0:0:0:0", await Scalar<string>(connection, "SELECT custom_text.text_counters()"));
        await using var output = new MemoryStream();
        await using (Stream copy = await connection.BeginRawBinaryCopyAsync("COPY custom_text_binary TO STDOUT (FORMAT BINARY)", context.CancellationToken))
        {
            await copy.CopyToAsync(output, context.CancellationToken);
        }

        Assert.AreSequenceEqual(input, output.ToArray());
        Assert.AreEqual(expected, await Scalar<string>(connection, "SELECT value::text FROM custom_text_binary"));
    }

    /// <summary>
    /// Invalid CBOR and domain text masquerading as binary cannot create rows or damage later operations.
    /// </summary>
    /// <param name="hex">The invalid record payload.</param>
    [TestMethod]
    [DataRow("")]
    [DataRow("34327C6F6B")]
    [DataRow("F6")]
    [DataRow("A1664E756D626572182A")]
    [DataRow("A2664E756D626572182A6454657874F6")]
    [DataRow("A2664E756D626572182A6454657874626F")]
    [DataRow("A2664E756D626572182A6454657874626F6B00")]
    public async Task CustomTextBinaryErrorsPreserveBackendAndRows(string hex)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        int process = connection.ProcessID;
        await Scalar<object>(connection, "CREATE TEMP TABLE custom_text_binary(value custom_text.value); SELECT 1");
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => Import(connection, CopyPayload(Convert.FromHexString(hex))));
        Assert.AreEqual("22P03", error.SqlState);
        Assert.AreEqual(0L, await Scalar<long>(connection, "SELECT count(*) FROM custom_text_binary"));
        Assert.AreEqual("0:0:0:0", await Scalar<string>(connection, "SELECT custom_text.text_counters()"));
        await Import(connection, CopyPayload(Convert.FromHexString("A2664E756D626572182A6454657874626F6B")));
        Assert.AreEqual("42|ok", await Scalar<string>(connection, "SELECT custom_text.text_value_echo(value,7)::text FROM custom_text_binary"));
        Assert.AreEqual(process, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }

    /// <summary>
    /// Generated CBOR survives external and compressed TOAST independently of text formatting.
    /// </summary>
    [TestMethod]
    public async Task CustomTextStorageSurvivesToast()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        await Scalar<object>(connection, """
            SET search_path = pg_catalog;
            CREATE TEMP TABLE text_external(value custom_text.value);
            ALTER TABLE text_external ALTER COLUMN value SET STORAGE EXTERNAL;
            INSERT INTO text_external SELECT ('42|' || string_agg(md5(part::text),''))::custom_text.value FROM generate_series(1,3000) part;
            CREATE TEMP TABLE text_compressed(value custom_text.value);
            INSERT INTO text_compressed VALUES (('7|' || repeat('compress',20000))::custom_text.value);
            SELECT 1
            """);
        Assert.IsTrue(await Scalar<bool>(connection, """
            SELECT pg_column_size(value) > 90000 AND custom_text.text_value_echo(value,4)::text
                = '42|' || (SELECT string_agg(md5(part::text),'') FROM generate_series(1,3000) part) FROM text_external
            """));
        Assert.IsTrue(await Scalar<bool>(connection, """
            SELECT pg_column_size(value) < 10000 AND custom_text.text_value_echo(value,7)::text = '7|' || repeat('compress',20000) FROM text_compressed
            """));
    }

    /// <summary>
    /// Constructs one complete COPY binary row independently of the generated serializer.
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
    /// Completes COPY so receive errors are observed before returning.
    /// </summary>
    private async Task Import(NpgsqlConnection connection, byte[] data, string table = "custom_text_binary")
    {
        await using Stream copy = await connection.BeginRawBinaryCopyAsync($"COPY {table} FROM STDIN (FORMAT BINARY)", context.CancellationToken);
        await copy.WriteAsync(data, context.CancellationToken);
    }

    /// <summary>
    /// Sends a text COPY NULL marker and waits for backend input conversion to finish.
    /// </summary>
    private async Task ImportTextNull(NpgsqlConnection connection)
    {
        await using TextWriter copy = await connection.BeginTextImportAsync("COPY custom_text_null FROM STDIN", context.CancellationToken);
        await copy.WriteAsync("\\N\n".AsMemory(), context.CancellationToken);
    }

    /// <summary>
    /// Executes an exact scalar with fixture cancellation.
    /// </summary>
    private async Task<T> Scalar<T>(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        object? result = await command.ExecuteScalarAsync(context.CancellationToken);
        return Assert.IsInstanceOfType<T>(result);
    }
}
