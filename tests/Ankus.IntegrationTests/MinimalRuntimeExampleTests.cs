using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Executes the minimal-runtime counterpart of pgrx's nostd example.
/// </summary>
/// <param name="context">The current cancellation and diagnostic context.</param>
[TestClass]
public sealed class MinimalRuntimeExampleTests(TestContext context)
{
    /// <summary>
    /// Functions, a hand-declared operator and generated comparison operators and index classes work with invariant
    /// globalization, resource keys and stack-trace metadata removed.
    /// </summary>
    [TestMethod]
    public Task MinimalRuntimeSampleRunsGeneratedDeclarations()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(MinimalRuntimeSampleRunsGeneratedDeclarations), async (connection, transaction, token) =>
        {
            await ExecuteAsync(connection, transaction, "CREATE EXTENSION ankus_minimal_runtime", token);
            Assert.AreEqual("Hello, nostd", await ScalarAsync<string>(connection, transaction, "SELECT hello_nostd()", token));
            Assert.AreEqual("café 🐘", await ScalarAsync<string>(connection, transaction, "SELECT echo('café 🐘')", token));
            Assert.AreEqual("true|false", await ScalarAsync<string>(connection, transaction,
                """SELECT ('{"value":1}'::my_type = '{"value":1}'::my_type) || '|' || ('{"value":1}'::my_type = '{"value":2}'::my_type)""", token));

            await ExecuteAsync(connection, transaction, """
                CREATE TEMP TABLE things(value thing);
                INSERT INTO things SELECT json_build_object('Value', v)::text::thing
                FROM unnest(ARRAY['b', 'a', 'é', chr(65536), chr(65535)]) AS v;
                CREATE INDEX things_btree ON things USING btree (value);
                CREATE INDEX things_hash ON things USING hash (value);
                """, token);

            // UTF-8 byte order, as Rust orders String, places U+FFFF before U+10000; UTF-16 ordinal order would not.
            Assert.AreSequenceEqual<string>(["a", "b", "é", "￿", "\U00010000"], await ScalarAsync<string[]>(connection, transaction,
                "SELECT array_agg(value::text::jsonb ->> 'Value' ORDER BY value) FROM things", token));
            Assert.IsTrue(await ScalarAsync<bool>(connection, transaction,
                """SELECT '{"Value":"a"}'::thing < '{"Value":"b"}'::thing AND '{"Value":"a"}'::thing = '{"Value":"a"}'::thing""", token));
            await ExecuteAsync(connection, transaction, "SET LOCAL enable_seqscan = off", token);
            Assert.AreEqual(1L, await ScalarAsync<long>(connection, transaction,
                """SELECT count(*) FROM things WHERE value = '{"Value":"é"}'::thing""", token));
        }, context.CancellationToken);

    private static async Task<T> ScalarAsync<T>(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, CancellationToken token)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        return Assert.IsInstanceOfType<T>(await command.ExecuteScalarAsync(token));
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, CancellationToken token)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync(token);
    }
}
