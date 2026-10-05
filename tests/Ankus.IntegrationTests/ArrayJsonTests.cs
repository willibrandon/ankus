using System.Text.Json;
using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Preserves pgrx's exact nullable, temporal and embedded-JSON array serialization cases inside Native AOT.
/// </summary>
/// <param name="context">The current test cancellation context.</param>
[TestClass]
public sealed class ArrayJsonTests(TestContext context)
{
    /// <summary>
    /// Native-word iteration rejects each NULL partition and permits later native work after rollback.
    /// </summary>
    /// <param name="input">The native array containing first, interior, last or only NULL cells.</param>
    [TestMethod]
    [DataRow("ARRAY[NULL,1]::integer[]")]
    [DataRow("ARRAY[1,NULL,2]")]
    [DataRow("ARRAY[1,NULL]")]
    [DataRow("ARRAY[NULL,NULL]::integer[]")]
    public Task AnyArrayJsonIterationRejectsNullAndRecovers(string input)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(AnyArrayJsonIterationRejectsNullAndRecovers), async (connection, transaction, token) =>
        {
            await transaction.SaveAsync("anyarray_null", token);
            await using var command = new NpgsqlCommand($"SELECT array_json.anyarray_iter_arg({input})", connection, transaction);
            PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(token));
            Assert.AreEqual("38000", error.SqlState);
            Assert.AreEqual("element is null", error.MessageText);
            await transaction.RollbackAsync("anyarray_null", token);
            command.CommandText = "SELECT array_json.anyarray_iter_arg(ARRAY[42])::text";
            using JsonDocument result = JsonDocument.Parse(Assert.IsInstanceOfType<string>(await command.ExecuteScalarAsync(token)));
            Assert.AreEqual(42, Assert.ContainsSingle(result.RootElement.EnumerateArray()).GetInt32());
            command.CommandText = "SELECT pg_backend_pid()";
            Assert.AreEqual(connection.ProcessID, await command.ExecuteScalarAsync(token));
        }, context.CancellationToken);

    /// <summary>
    /// Preserves the exact upstream anyarray iteration result and independently specified by-value boundaries.
    /// </summary>
    /// <param name="input">The native polymorphic array with known by-value Datum words.</param>
    /// <param name="expected">The independently specified JSON array of signed native words.</param>
    [TestMethod]
    [DataRow("ARRAY[1,2,3]", "[1,2,3]")]
    [DataRow("ARRAY[]::integer[]", "[]")]
    [DataRow("ARRAY[-2147483648,0,2147483647]", "[-2147483648,0,2147483647]")]
    [DataRow("ARRAY[-9223372036854775808,9223372036854775807]::bigint[]", "[-9223372036854775808,9223372036854775807]")]
    [DataRow("ARRAY[false,true]", "[0,1]")]
    [DataRow("'[-2:-1][3:4]={{1,2},{3,4}}'::integer[]", "[1,2,3,4]")]
    public Task AnyArrayJsonIterationPreservesNativeWords(string input, string expected)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(AnyArrayJsonIterationPreservesNativeWords), async (connection, transaction, token) =>
        {
            await using var command = new NpgsqlCommand($"SELECT array_json.anyarray_iter_arg({input})::text", connection, transaction);
            string actual = Assert.IsInstanceOfType<string>(await command.ExecuteScalarAsync(token));
            using JsonDocument actualDocument = JsonDocument.Parse(actual);
            using JsonDocument expectedDocument = JsonDocument.Parse(expected);
            Assert.AreEqual(JsonValueKind.Array, actualDocument.RootElement.ValueKind);
            Assert.IsTrue(JsonElement.DeepEquals(expectedDocument.RootElement, actualDocument.RootElement), actual);
        }, context.CancellationToken);

    /// <summary>
    /// Compares complete native documents with independent upstream values and adjacent empty/NULL/shape partitions.
    /// </summary>
    /// <param name="function">The attributed native serializer function.</param>
    /// <param name="input">The independently specified native array.</param>
    /// <param name="expected">The independently specified values document.</param>
    [TestMethod]
    [DataRow("serde_serialize_array_i32", "ARRAY[1,NULL,2,3,NULL,4,5]", "{\"values\":[1,null,2,3,null,4,5]}")]
    [DataRow("serde_serialize_array_i32", "ARRAY[]::integer[]", "{\"values\":[]}")]
    [DataRow("serde_serialize_array_i32", "ARRAY[NULL,NULL]::integer[]", "{\"values\":[null,null]}")]
    [DataRow("serde_serialize_array_i32", "'[-2:-1][3:4]={{1,NULL},{2,3}}'::integer[]", "{\"values\":[1,null,2,3]}")]
    [DataRow("serde_serialize_array_i32_deny_null", "ARRAY[]::integer[]", "{\"values\":[]}")]
    [DataRow("serde_serialize_array_i32_deny_null", "ARRAY[-2147483648,0,2147483647]", "{\"values\":[-2147483648,0,2147483647]}")]
    [DataRow("serde_serialize_array_date", "ARRAY['1977-07-04',NULL,'2026-01-15']::date[]", "{\"values\":[\"1977-07-04\",null,\"2026-01-15\"]}")]
    [DataRow("serde_serialize_array_date", "ARRAY[]::date[]", "{\"values\":[]}")]
    [DataRow("serde_serialize_array_date", "ARRAY[NULL,NULL]::date[]", "{\"values\":[null,null]}")]
    [DataRow("serde_serialize_array_date", "ARRAY['0001-02-29 BC','5874897-12-31','infinity','-infinity']::date[]",
        "{\"values\":[\"0001-02-29 BC\",\"5874897-12-31\",\"infinity\",\"-infinity\"]}")]
    [DataRow("serde_serialize_array_timestamp", "ARRAY['2026-01-15 12:34:56',NULL]::timestamp[]", "{\"values\":[\"2026-01-15T12:34:56\",null]}")]
    [DataRow("serde_serialize_array_timestamp", "ARRAY[]::timestamp[]", "{\"values\":[]}")]
    [DataRow("serde_serialize_array_timestamp", "ARRAY[NULL,NULL]::timestamp[]", "{\"values\":[null,null]}")]
    [DataRow("serde_serialize_array_timestamp", "ARRAY['294276-12-31 23:59:59.999999','-infinity']::timestamp[]",
        "{\"values\":[\"294276-12-31T23:59:59.999999\",\"-infinity\"]}")]
    [DataRow("serde_serialize_array_json", "ARRAY['{\"a\":1}',NULL,'[true,false]']::json[]", "{\"values\":[{\"a\":1},null,[true,false]]}")]
    [DataRow("serde_serialize_array_json", "ARRAY[]::json[]", "{\"values\":[]}")]
    [DataRow("serde_serialize_array_json", "ARRAY[NULL,NULL]::json[]", "{\"values\":[null,null]}")]
    [DataRow("serde_serialize_array_json", "ARRAY['null','\"雪\"','123456789012345678901234567890.123456789','true','{}','[]']::json[]",
        "{\"values\":[null,\"雪\",123456789012345678901234567890.123456789,true,{},[]]}")]
    public Task ArrayJsonPreservesExactUpstreamValues(string function, string input, string expected)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(ArrayJsonPreservesExactUpstreamValues), async (connection, transaction, token) =>
        {
            await using var settings = new NpgsqlCommand("SET LOCAL DateStyle='German'", connection, transaction);
            await settings.ExecuteNonQueryAsync(token);
            await using var command = new NpgsqlCommand($"SELECT array_json.{function}({input})::text", connection, transaction);
            string actual = Assert.IsInstanceOfType<string>(await command.ExecuteScalarAsync(token));
            using JsonDocument actualDocument = JsonDocument.Parse(actual);
            using JsonDocument expectedDocument = JsonDocument.Parse(expected);
            Assert.AreEqual(JsonValueKind.Object, actualDocument.RootElement.ValueKind);
            Assert.AreEqual("values", Assert.ContainsSingle(actualDocument.RootElement.EnumerateObject()).Name);
            Assert.IsTrue(JsonElement.DeepEquals(expectedDocument.RootElement, actualDocument.RootElement), actual);
        }, context.CancellationToken);

    /// <summary>
    /// Rejects NULL cells throughout the required integer input and permits correct work after explicit rollback.
    /// </summary>
    /// <param name="input">The source array containing a NULL in the first, interior or last position.</param>
    [TestMethod]
    [DataRow("ARRAY[NULL,1]::integer[]")]
    [DataRow("ARRAY[1,2,3,NULL,4,5]")]
    [DataRow("ARRAY[1,2,NULL]::integer[]")]
    [DataRow("ARRAY[NULL,NULL]::integer[]")]
    public Task RequiredArrayJsonRejectsNullCellsAndRecovers(string input)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(RequiredArrayJsonRejectsNullCellsAndRecovers), async (connection, transaction, token) =>
        {
            int backend = connection.ProcessID;
            await transaction.SaveAsync("array_json_failure", token);
            await using var command = new NpgsqlCommand($"SELECT array_json.serde_serialize_array_i32_deny_null({input})", connection, transaction);
            PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(token));
            Assert.AreEqual("38000", error.SqlState);
            Assert.Contains("SQL NULL array cells", error.MessageText);
            await transaction.RollbackAsync("array_json_failure", token);
            command.CommandText = "SELECT array_json.serde_serialize_array_i32_deny_null(ARRAY[42])::text";
            using JsonDocument result = JsonDocument.Parse(Assert.IsInstanceOfType<string>(await command.ExecuteScalarAsync(token)));
            Assert.AreEqual(1, result.RootElement.GetProperty("values").GetArrayLength());
            Assert.AreEqual(42, result.RootElement.GetProperty("values")[0].GetInt32());
            command.CommandText = "SELECT set_values.set_interrupt_state()";
            Assert.AreSequenceEqual([0, 0], Assert.IsInstanceOfType<int[]>(await command.ExecuteScalarAsync(token)));
            command.CommandText = "SELECT pg_backend_pid()";
            Assert.AreEqual(backend, await command.ExecuteScalarAsync(token));
        }, context.CancellationToken);
}
