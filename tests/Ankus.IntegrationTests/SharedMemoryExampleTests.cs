using Ankus.Testing;
using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Executes the ported pgrx shmem sample through its published Native AOT extension.
/// </summary>
/// <param name="context">The current cancellation and diagnostic context.</param>
[TestClass]
public sealed class SharedMemoryExampleTests(TestContext context)
{
    /// <summary>
    /// Without shared preload, registration fails with pgrx's message each time it is retried, and the backend continues.
    /// </summary>
    [TestMethod]
    public Task SharedMemorySampleRequiresSharedPreload()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(SharedMemorySampleRequiresSharedPreload),
            async (connection, transaction, token) =>
            {
                await ErrorTrap.InstallAsync(connection, transaction, token);
                const string Expected = "XX000: this extension must be loaded via shared_preload_libraries.";
                Assert.AreEqual(Expected, await ErrorTrap.RunAsync(connection, transaction, "CREATE EXTENSION ankus_shared_memory", token));
                Assert.AreEqual(Expected, await ErrorTrap.RunAsync(connection, transaction, "LOAD 'Ankus.Examples.SharedMemory'", token));
                await using var recovered = new NpgsqlCommand(
                    "SELECT count(*) FROM pg_extension WHERE extname = 'ankus_shared_memory'", connection, transaction);
                Assert.AreEqual(0L, await recovered.ExecuteScalarAsync(token));
            }, context.CancellationToken);

    /// <summary>
    /// Preloaded backends share every pgrx collection, value and atomic, including full-capacity behavior and rollback.
    /// </summary>
    [TestMethod]
    public async Task SharedMemorySampleSharesValuesAcrossBackends()
    {
        CancellationToken token = context.CancellationToken;
        PostgresTestClusterOptions defaults = await IntegrationEnvironment.CreateOptionsAsync(token);
        PostgresTestClusterOptions options = new()
        {
            Installation = defaults.Installation,
            DataDirectoryBase = defaults.DataDirectoryBase,
            LogDirectory = defaults.LogDirectory,
            PostgreSqlConfiguration = [.. defaults.PostgreSqlConfiguration, "shared_preload_libraries = 'Ankus.Examples.SharedMemory'"],
        };
        await using PostgresTestCluster cluster = await PostgresTestCluster.StartAsync(options, token);
        await using NpgsqlConnection first = await cluster.OpenConnectionAsync(token);
        await using NpgsqlConnection second = await cluster.OpenConnectionAsync(token);
        Assert.AreNotEqual(first.ProcessID, second.ProcessID);
        await ExecuteAsync(first, "CREATE EXTENSION ankus_shared_memory", token);
        Assert.AreEqual("{\"value1\":5,\"value2\":-6}", await ScalarAsync<string>(first, "SELECT '{\"value2\":-6,\"value1\":5}'::pgtest::text", token));

        // Initial values are zero, empty or false in every backend.
        Assert.AreEqual("0|0|t|t|t|t|{\"value1\":0,\"value2\":0}|0|f", await ScalarAsync<string>(second, """
            SELECT concat_ws('|', vec_count(), deque_count(), vec_pop() IS NULL, deque_pop_back() IS NULL,
                deque_pop_front() IS NULL, hash_get(1) IS NULL, struct_get()::text, primitive_get(), atomic_get())
            """, token));

        // heapless::Vec: push from either backend, select, pop the newest, then drain the rest.
        await ExecuteAsync(first, "SELECT vec_push('{\"value1\":1,\"value2\":2}')", token);
        await ExecuteAsync(second, "SELECT vec_push('{\"value1\":3,\"value2\":4}')", token);
        Assert.AreEqual("1:2,3:4", await ScalarAsync<string>(first, Rows("vec_select()"), token));
        Assert.AreEqual(2, await ScalarAsync<int>(second, "SELECT vec_count()", token));
        Assert.AreEqual("{\"value1\":3,\"value2\":4}", await ScalarAsync<string>(second, "SELECT vec_pop()::text", token));
        Assert.AreEqual("1:2", await ScalarAsync<string>(first, Rows("vec_drain()"), token));
        Assert.AreEqual(0, await ScalarAsync<int>(second, "SELECT vec_count()", token));

        // heapless::Deque: both ends from both backends.
        await ExecuteAsync(first, "SELECT deque_push_back('{\"value1\":1,\"value2\":1}')", token);
        await ExecuteAsync(second, "SELECT deque_push_front('{\"value1\":0,\"value2\":0}')", token);
        await ExecuteAsync(first, "SELECT deque_push_back('{\"value1\":2,\"value2\":2}')", token);
        Assert.AreEqual("0:0,1:1,2:2", await ScalarAsync<string>(second, Rows("deque_select()"), token));
        Assert.AreEqual(3, await ScalarAsync<int>(first, "SELECT deque_count()", token));
        Assert.AreEqual("{\"value1\":0,\"value2\":0}", await ScalarAsync<string>(first, "SELECT deque_pop_front()::text", token));
        Assert.AreEqual("{\"value1\":2,\"value2\":2}", await ScalarAsync<string>(second, "SELECT deque_pop_back()::text", token));
        Assert.AreEqual("1:1", await ScalarAsync<string>(first, Rows("deque_drain()"), token));
        Assert.AreEqual(0, await ScalarAsync<int>(second, "SELECT deque_count()", token));

        // Full collections warn and discard the update without changing their contents.
        await AssertFullAsync(first, second, "vec_push", "vec_count()", "Vector is full, discarding update", token);
        Assert.AreEqual("{\"value1\":400,\"value2\":-400}", await ScalarAsync<string>(second, "SELECT vec_pop()::text", token));
        Assert.AreEqual(399L, await ScalarAsync<long>(first, "SELECT count(*) FROM vec_drain()", token));
        await AssertFullAsync(second, first, "deque_push_back", "deque_count()", "Deque is full, discarding update", token);
        await AssertWarningAsync(first, "SELECT deque_push_front('{\"value1\":0,\"value2\":0}')", "Deque is full, discarding update", token);
        Assert.AreEqual("{\"value1\":1,\"value2\":-1}", await ScalarAsync<string>(first, "SELECT deque_pop_front()::text", token));
        Assert.AreEqual("{\"value1\":400,\"value2\":-400}", await ScalarAsync<string>(second, "SELECT deque_pop_back()::text", token));
        Assert.AreEqual(398L, await ScalarAsync<long>(first, "SELECT count(*) FROM deque_drain()", token));

        // FnvIndexMap<i32, i32, 4>: replacements fit at capacity; a fifth key fails and leaves the map unchanged.
        await ExecuteAsync(first, "SELECT hash_insert(1, 10), hash_insert(2, 20), hash_insert(3, 30), hash_insert(4, 40)", token);
        await ExecuteAsync(second, "SELECT hash_insert(2, 22)", token);
        Assert.AreEqual("10|22|30|40", await ScalarAsync<string>(first, "SELECT concat_ws('|', hash_get(1), hash_get(2), hash_get(3), hash_get(4))", token));
        PostgresException full = await Assert.ThrowsExactlyAsync<PostgresException>(() => ExecuteAsync(second, "SELECT hash_insert(5, 50)", token));
        Assert.AreEqual("38000", full.SqlState);
        Assert.AreEqual("The fixed dictionary is full.", full.MessageText);
        Assert.IsTrue(await ScalarAsync<bool>(second, "SELECT hash_get(5) IS NULL AND hash_get(4) = 40", token));

        // A lock-protected struct and primitive, and a lock-free atomic.
        await ExecuteAsync(first, "SELECT struct_set(7, -7), primitive_set(42)", token);
        Assert.AreEqual("{\"value1\":7,\"value2\":-7}|42", await ScalarAsync<string>(second, "SELECT struct_get()::text || '|' || primitive_get()", token));
        Assert.IsFalse(await ScalarAsync<bool>(first, "SELECT atomic_set(true)", token));
        Assert.IsTrue(await ScalarAsync<bool>(second, "SELECT atomic_get()", token));
        Assert.IsTrue(await ScalarAsync<bool>(second, "SELECT atomic_set(false)", token));
        Assert.IsFalse(await ScalarAsync<bool>(first, "SELECT atomic_get()", token));

        // Shared memory is not transactional: rolled-back writes remain visible.
        await ExecuteAsync(first, "BEGIN; SELECT primitive_set(99), vec_push('{\"value1\":9,\"value2\":9}'); ROLLBACK", token);
        Assert.AreEqual("99|9:9", await ScalarAsync<string>(second, "SELECT primitive_get() || '|' || (" + Rows("vec_select()") + ")", token));
        Assert.AreEqual(first.ProcessID, await ScalarAsync<int>(first, "SELECT pg_backend_pid()", token));
        Assert.AreEqual(second.ProcessID, await ScalarAsync<int>(second, "SELECT pg_backend_pid()", token));
    }

    /// <summary>
    /// Fills a 400-value collection from one backend, then rejects a 401st value from another with pgrx's warning.
    /// </summary>
    private static async Task AssertFullAsync(NpgsqlConnection writer, NpgsqlConnection reader, string push, string count,
        string warning, CancellationToken token)
    {
        await ExecuteAsync(writer, $"SELECT {push}(format('{{\"value1\":%s,\"value2\":%s}}', value, -value)::pgtest) FROM generate_series(1, 400) AS value",
            token);
        Assert.AreEqual(400, await ScalarAsync<int>(reader, "SELECT " + count, token));
        await AssertWarningAsync(reader, $"SELECT {push}('{{\"value1\":401,\"value2\":-401}}')", warning, token);
        Assert.AreEqual(400, await ScalarAsync<int>(writer, "SELECT " + count, token));
    }

    private static async Task AssertWarningAsync(NpgsqlConnection connection, string sql, string message, CancellationToken token)
    {
        var notices = new List<PostgresNotice>();
        void Record(object sender, NpgsqlNoticeEventArgs arguments) => notices.Add(arguments.Notice);
        connection.Notice += Record;
        try
        {
            await ExecuteAsync(connection, sql, token);
        }
        finally
        {
            connection.Notice -= Record;
        }

        PostgresNotice notice = Assert.ContainsSingle(notices);
        Assert.AreEqual("WARNING:01000:" + message, $"{notice.InvariantSeverity}:{notice.SqlState}:{notice.MessageText}");
    }

    /// <summary>
    /// Formats a pgtest set as ordered value pairs.
    /// </summary>
    private static string Rows(string function) =>
        $"SELECT coalesce(string_agg((value::text::jsonb->>'value1') || ':' || (value::text::jsonb->>'value2'), ',' ORDER BY ordinal), '') FROM {function} WITH ORDINALITY AS item(value, ordinal)";

    private static async Task<T> ScalarAsync<T>(NpgsqlConnection connection, string sql, CancellationToken token)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return Assert.IsInstanceOfType<T>(await command.ExecuteScalarAsync(token));
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, CancellationToken token)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(token);
    }
}
