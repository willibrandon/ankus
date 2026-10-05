using System.Text.Json;
using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Executes pgrx's borrowed-array JSON example through a published Native AOT extension.
/// </summary>
/// <param name="context">The current test cancellation context.</param>
[TestClass]
[DoNotParallelize]
public sealed class JsonExampleTests(TestContext context)
{
    /// <summary>
    /// Preserves exact text cells, SQL NULL and row-major order without changing the document shape.
    /// </summary>
    /// <param name="input">The independently specified PostgreSQL array.</param>
    /// <param name="expected">The independently specified JSON values array.</param>
    [TestMethod]
    [DataRow("ARRAY['one',NULL,'two','three']", "[\"one\",null,\"two\",\"three\"]")]
    [DataRow("ARRAY[]::text[]", "[]")]
    [DataRow("ARRAY[NULL,NULL,NULL]::text[]", "[null,null,null]")]
    [DataRow("ARRAY['']", "[\"\"]")]
    [DataRow("ARRAY['中文','😀',E'a\\nb','\"quoted\"']", "[\"中文\",\"😀\",\"a\\nb\",\"\\\"quoted\\\"\"]")]
    [DataRow("ARRAY[$q$<>&/'\"\\$q$]", "[\"<>&/'\\\"\\\\\"]")]
    [DataRow("ARRAY[U&'\\2028\\2029\\0085\\00A0\\FEFF\\FDD0\\FFFF']", "[\"\u2028\u2029\u0085\u00A0\uFEFF\uFDD0\uFFFF\"]")]
    [DataRow("ARRAY[ARRAY['left',NULL],ARRAY['right','last']]", "[\"left\",null,\"right\",\"last\"]")]
    [DataRow("'[0:1]={first,last}'::text[]", "[\"first\",\"last\"]")]
    public Task JsonTextSamplePreservesCells(string input, string expected)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(JsonTextSamplePreservesCells), async (connection, transaction, token) =>
        {
            await InstallAsync(connection, transaction, token);
            string actual = await ScalarAsync<string>(connection, transaction,
                $"SELECT json_example.text_array_to_json_doc({input})::text", token);
            AssertDocument(actual, expected);
        }, context.CancellationToken);

    /// <summary>
    /// Preserves every PostgreSQL text control character with the upstream JSON escape spelling.
    /// </summary>
    [TestMethod]
    public Task JsonTextSamplePreservesEveryControlEscape()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(JsonTextSamplePreservesEveryControlEscape), async (connection, transaction, token) =>
        {
            await InstallAsync(connection, transaction, token);
            string actual = await ScalarAsync<string>(connection, transaction, """"
                SELECT json_example.text_array_to_json_doc(ARRAY[
                    (SELECT string_agg(chr(ordinal), '' ORDER BY ordinal) FROM generate_series(1,31) AS ordinal)])::text
                """", token);
            const string Expected = """"["\u0001\u0002\u0003\u0004\u0005\u0006\u0007\b\t\n\u000b\f\r\u000e\u000f\u0010\u0011\u0012\u0013\u0014\u0015\u0016\u0017\u0018\u0019\u001a\u001b\u001c\u001d\u001e\u001f"]"""";
            AssertDocument(actual, Expected);
            using JsonDocument parsed = JsonDocument.Parse(actual);
            Assert.AreEqual(new string([.. Enumerable.Range(1, 31).Select(static value => (char)value)]),
                AssertValues(parsed)[0].GetString());
        }, context.CancellationToken);

    /// <summary>
    /// Keeps all one thousand upstream large-case text values in their original order.
    /// </summary>
    [TestMethod]
    public Task JsonTextSamplePreservesEveryLargeCaseValue()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(JsonTextSamplePreservesEveryLargeCaseValue), async (connection, transaction, token) =>
        {
            await InstallAsync(connection, transaction, token);
            string actual = await ScalarAsync<string>(connection, transaction, """
                SELECT json_example.text_array_to_json_doc(
                    ARRAY(SELECT input.value::text FROM generate_series(1,1000) AS input(value) ORDER BY input.value))::text
                """, token);
            using JsonDocument document = JsonDocument.Parse(actual);
            JsonElement values = AssertValues(document);
            Assert.AreEqual(1000, values.GetArrayLength());
            for (int index = 0; index < 1000; index++)
            {
                Assert.AreEqual(FormattableString.Invariant($"{index + 1}"), values[index].GetString());
            }
        }, context.CancellationToken);

    /// <summary>
    /// Writes bytea as arrays of numeric bytes, retaining empty cells and SQL NULL separately.
    /// </summary>
    /// <param name="input">The independently specified PostgreSQL bytea array.</param>
    /// <param name="expected">The independently specified JSON values array.</param>
    [TestMethod]
    [DataRow("ARRAY[]::bytea[]", "[]")]
    [DataRow("ARRAY[NULL,NULL]::bytea[]", "[null,null]")]
    [DataRow("ARRAY[decode('','hex')]", "[[]]")]
    [DataRow("ARRAY[decode('00ff80a5','hex'),NULL,decode('','hex')]", "[[0,255,128,165],null,[]]")]
    [DataRow("ARRAY[ARRAY[decode('00','hex'),NULL],ARRAY[decode('ff','hex'),decode('','hex')]]", "[[0],null,[255],[]]")]
    [DataRow("array_fill(decode('80','hex'),ARRAY[2],ARRAY[-2])", "[[128],[128]]")]
    public Task JsonByteaSamplePreservesNumericBytes(string input, string expected)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(JsonByteaSamplePreservesNumericBytes), async (connection, transaction, token) =>
        {
            await InstallAsync(connection, transaction, token);
            string actual = await ScalarAsync<string>(connection, transaction,
                $"SELECT json_example.bytea_array_to_json_doc({input})::text", token);
            AssertDocument(actual, expected);
        }, context.CancellationToken);

    /// <summary>
    /// Preserves every byte value through repeated payloads that outlive their source array's query.
    /// </summary>
    [TestMethod]
    public Task JsonByteaSampleOwnsLargeResultsAfterSourceDeletion()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(JsonByteaSampleOwnsLargeResultsAfterSourceDeletion), async (connection, transaction, token) =>
        {
            await InstallAsync(connection, transaction, token);
            await ExecuteAsync(connection, transaction, """
                CREATE TEMP TABLE json_sample_source(payload bytea[]);
                INSERT INTO json_sample_source VALUES (ARRAY[
                    decode((SELECT string_agg(lpad(to_hex(value),2,'0'),'' ORDER BY value)
                        FROM generate_series(0,255) AS value), 'hex'),
                    NULL,
                    decode(repeat('ff',8192), 'hex'),
                    decode('', 'hex')]);
                CREATE TEMP TABLE json_sample_result AS
                    SELECT json_example.bytea_array_to_json_doc(payload) AS document FROM json_sample_source;
                DROP TABLE json_sample_source;
                """, token);
            string actual = await ScalarAsync<string>(connection, transaction,
                "SELECT document::text FROM json_sample_result", token);
            using JsonDocument document = JsonDocument.Parse(actual);
            JsonElement values = AssertValues(document);
            Assert.AreEqual(4, values.GetArrayLength());
            Assert.AreEqual(JsonValueKind.Array, values[0].ValueKind);
            Assert.AreEqual(256, values[0].GetArrayLength());
            for (int index = 0; index < 256; index++)
            {
                Assert.AreEqual(index, values[0][index].GetInt32());
            }

            Assert.AreEqual(JsonValueKind.Null, values[1].ValueKind);
            Assert.AreEqual(JsonValueKind.Array, values[2].ValueKind);
            Assert.AreEqual(8192, values[2].GetArrayLength());
            foreach (JsonElement value in values[2].EnumerateArray())
            {
                Assert.AreEqual(255, value.GetInt32());
            }

            Assert.AreEqual(JsonValueKind.Array, values[3].ValueKind);
            Assert.AreEqual(0, values[3].GetArrayLength());
            Assert.AreEqual(connection.ProcessID, await ScalarAsync<int>(connection, transaction, "SELECT pg_backend_pid()", token));
            Assert.AreEqual(42, await ScalarAsync<int>(connection, transaction, "SELECT 42", token));
        }, context.CancellationToken);

    /// <summary>
    /// Keeps the outer-array strictness distinct from nullable cells and retains pgrx's default declaration flags.
    /// </summary>
    [TestMethod]
    public Task JsonSamplePreservesStrictInputsAndNativeDeclarations()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(JsonSamplePreservesStrictInputsAndNativeDeclarations), async (connection, transaction, token) =>
        {
            await InstallAsync(connection, transaction, token);
            Assert.IsTrue(await ScalarAsync<bool>(connection, transaction, """
                SELECT json_example.text_array_to_json_doc(NULL::text[]) IS NULL
                    AND json_example.bytea_array_to_json_doc(NULL::bytea[]) IS NULL
                """, token));
            Assert.AreEqual(2L, await ScalarAsync<long>(connection, transaction, """
                SELECT count(*) FROM pg_proc JOIN pg_namespace ON pg_namespace.oid = pronamespace
                WHERE nspname = 'json_example'
                    AND proname IN ('text_array_to_json_doc', 'bytea_array_to_json_doc')
                    AND proisstrict AND provolatile = 'v' AND proparallel = 'u'
                    AND prorettype = 'json'::regtype
                """, token));
            Assert.IsFalse(await ScalarAsync<bool>(connection, transaction,
                "SELECT extrelocatable FROM pg_extension WHERE extname = 'ankus_json'", token));
        }, context.CancellationToken);

    /// <summary>
    /// Requires exactly the sample's single values property and its array representation.
    /// </summary>
    /// <param name="document">The detached native JSON output.</param>
    /// <returns>The independently checked values array.</returns>
    private static JsonElement AssertValues(JsonDocument document)
    {
        Assert.AreEqual(JsonValueKind.Object, document.RootElement.ValueKind);
        JsonProperty property = Assert.ContainsSingle(document.RootElement.EnumerateObject());
        Assert.AreEqual("values", property.Name);
        Assert.AreEqual(JsonValueKind.Array, property.Value.ValueKind);
        return property.Value;
    }

    /// <summary>
    /// Compares the native JSON text with an independent literal, including exact escaping.
    /// </summary>
    /// <param name="actual">The returned native JSON document.</param>
    /// <param name="expected">The independently specified JSON values array.</param>
    private static void AssertDocument(string actual, string expected)
    {
        using JsonDocument document = JsonDocument.Parse(actual);
        AssertValues(document);
        Assert.AreEqual("{\"values\":" + expected + "}", actual);
    }

    /// <summary>
    /// Installs the independently published sample into a deliberately named consuming schema.
    /// </summary>
    /// <param name="connection">The isolated backend connection.</param>
    /// <param name="transaction">The rollback-isolated sample test.</param>
    /// <param name="token">Cancels native work.</param>
    /// <returns>The installation completion.</returns>
    private static Task InstallAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken token)
        => ExecuteAsync(connection, transaction, "CREATE SCHEMA json_example; CREATE EXTENSION ankus_json WITH SCHEMA json_example", token);

    /// <summary>
    /// Executes an independently specified native fixture statement.
    /// </summary>
    /// <param name="connection">The isolated backend connection.</param>
    /// <param name="transaction">The rollback-isolated sample test.</param>
    /// <param name="sql">The fixture SQL.</param>
    /// <param name="token">Cancels native work.</param>
    /// <returns>The statement completion.</returns>
    private static async Task ExecuteAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, CancellationToken token)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync(token);
    }

    /// <summary>
    /// Reads a required detached scalar with an explicit type assertion.
    /// </summary>
    /// <typeparam name="T">The expected client value type.</typeparam>
    /// <param name="connection">The isolated backend connection.</param>
    /// <param name="transaction">The rollback-isolated sample test.</param>
    /// <param name="sql">The independent scalar query.</param>
    /// <param name="token">Cancels native work.</param>
    /// <returns>The required detached scalar.</returns>
    private static async Task<T> ScalarAsync<T>(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, CancellationToken token)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        return Assert.IsInstanceOfType<T>(await command.ExecuteScalarAsync(token));
    }
}
