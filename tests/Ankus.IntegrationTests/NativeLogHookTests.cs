using System.Data;
using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Proves the generated emit_log_hook binding uses the live backend global and exact reporting callback signature.
/// </summary>
/// <param name="context">The current test context.</param>
[TestClass]
public sealed class NativeLogHookTests(TestContext context)
{
    /// <summary>
    /// Persistent managed callbacks preserve nested reports and owned errors, remain usable after failure, and restore the previous hook.
    /// </summary>
    /// <param name="mode">Capture, rejection, nested capture, or nested rejection.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public async Task GeneratedEmitLogHookChainsRestoresAndRecovers(int mode)
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        int backend = connection.ProcessID;
        const string Marker = "generated emit_log_hook café 100%";
        const string NestedClientMarker = "generated emit_log_hook caf?? 100% nested";
        var notices = new List<PostgresNotice>();
        connection.Notice += (_, eventArgs) => notices.Add(eventArgs.Notice);
        await using var command = new NpgsqlCommand("SET log_min_messages = 'notice'; SET client_min_messages = 'notice'", connection);
        await command.ExecuteNonQueryAsync(token);
        command.CommandText = "SELECT emit_log_values.native_log_hook_install($1, $2)";
        command.Parameters.AddWithValue(Marker);
        command.Parameters.AddWithValue(mode);
        bool reject = mode is 1 or 3;
        int[] expectedOrder = mode switch
        {
            0 => [1, 2, 3, 4],
            1 => [1, 2, 4],
            2 => [1, 2, 5, 6, 7, 8, 3, 4],
            _ => [1, 2, 5, 6, 8, 4],
        };
        try
        {
            Assert.IsTrue(Assert.IsInstanceOfType<bool>(await command.ExecuteScalarAsync(token)));
            command.Parameters.Clear();
            command.CommandText = "SELECT emit_log_values.native_log_hook_report()";
            if (reject)
            {
                PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(token));
                Assert.AreEqual(mode == 1 ? "P7520" : "P7522", error.SqlState);
                Assert.AreEqual(mode == 1 ? "managed log hook failure" : "nested managed log hook failure", error.MessageText);
                Assert.AreEqual(mode == 1 ? "owned callback detail" : "owned nested failure detail", error.Detail);
                Assert.AreEqual(mode == 1 ? "retry report" : "retry nested report", error.Hint);
            }
            else
            {
                Assert.AreEqual(42, await command.ExecuteScalarAsync(token));
            }

            command.CommandText = "SELECT emit_log_values.native_log_hook_order()";
            Assert.AreSequenceEqual(expectedOrder,
                Assert.IsInstanceOfType<int[]>(await command.ExecuteScalarAsync(token)));
            command.CommandText = "SELECT emit_log_values.native_log_hook_snapshot()";
            Assert.AreSequenceEqual<string>(["18", "64", Marker, "owned hook detail", "owned hook hint", "True", "True"],
                Assert.IsInstanceOfType<string[]>(await command.ExecuteScalarAsync(token)));
            command.CommandText = "SELECT emit_log_values.native_log_hook_messages()";
            string[] messages = Assert.IsInstanceOfType<string[]>(await command.ExecuteScalarAsync(token));
            Assert.HasCount(1, messages.Where(static message => message == Marker));
            Assert.HasCount(mode >= 2 ? 1 : 0, messages.Where(static message => message == Marker + " nested"));
            if (reject)
            {
                Assert.Contains(mode == 1 ? "managed log hook failure" : "nested managed log hook failure", messages);
            }

            command.CommandText = "SELECT emit_log_values.native_log_hook_report()";
            Assert.AreEqual(42, await command.ExecuteScalarAsync(token));
            int[] retriedOrder = [.. expectedOrder, 1, 2, 3, 4];
            command.CommandText = "SELECT emit_log_values.native_log_hook_order()";
            Assert.AreSequenceEqual(retriedOrder, Assert.IsInstanceOfType<int[]>(await command.ExecuteScalarAsync(token)));
            command.CommandText = "SELECT emit_log_values.native_log_hook_restore()";
            Assert.IsTrue(Assert.IsInstanceOfType<bool>(await command.ExecuteScalarAsync(token)));
            command.CommandText = "SELECT emit_log_values.native_log_hook_report()";
            Assert.AreEqual(42, await command.ExecuteScalarAsync(token));
            command.CommandText = "SELECT emit_log_values.native_log_hook_order()";
            Assert.AreSequenceEqual(retriedOrder,
                Assert.IsInstanceOfType<int[]>(await command.ExecuteScalarAsync(token)));
            PostgresNotice[] markerNotices = [.. notices.Where(static notice => notice.MessageText == Marker)];
            Assert.HasCount(reject ? 2 : 3, markerNotices);
            // PostgreSQL's recursive reporter replaces each non-ASCII UTF-8 byte before sending to the client.
            PostgresNotice[] nestedNotices = [.. notices.Where(static notice => notice.MessageText == NestedClientMarker)];
            Assert.HasCount(mode == 2 ? 1 : 0, nestedNotices);
            if (mode == 2)
            {
                Assert.AreEqual("01000", nestedNotices[0].SqlState);
                Assert.AreEqual("owned nested detail", nestedNotices[0].Detail);
                Assert.AreEqual("owned nested hint", nestedNotices[0].Hint);
            }

            PostgresNotice notice = markerNotices[^1];
            Assert.AreEqual("01000", notice.SqlState);
            Assert.AreEqual("owned hook detail", notice.Detail);
            Assert.AreEqual("owned hook hint", notice.Hint);
            command.CommandText = "SELECT pg_backend_pid(), 6 * 7, tests.raw_call_holdoffs()";
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
            Assert.IsTrue(await reader.ReadAsync(token));
            Assert.AreEqual(backend, reader.GetInt32(0));
            Assert.AreEqual(42, reader.GetInt32(1));
            Assert.AreEqual(0L, reader.GetInt64(2));
            Assert.IsFalse(await reader.ReadAsync(token));
        }
        finally
        {
            if (connection.State == ConnectionState.Open)
            {
                command.CommandText = "SELECT emit_log_values.native_log_hook_restore()";
                command.Parameters.Clear();
                Assert.IsTrue(Assert.IsInstanceOfType<bool>(await command.ExecuteScalarAsync(token)));
            }
        }
    }

    /// <summary>
    /// A native prefix fails before invoking the generated managed hook, keeps the hook chain installed, and then succeeds in the same backend.
    /// </summary>
    [TestMethod]
    public async Task PersistentNativePrefixFailsBeforeManagedEntryAndRecovers()
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        int backend = connection.ProcessID;
        const string Marker = "persistent native prefix café 100%";
        await using var command = new NpgsqlCommand("SET log_min_messages = 'notice'; SET client_min_messages = 'notice'", connection);
        await command.ExecuteNonQueryAsync(token);
        command.CommandText = "SELECT emit_log_values.native_log_hook_install($1, 0)";
        command.Parameters.AddWithValue(Marker);
        bool prefixArmed = false;
        try
        {
            Assert.IsTrue(Assert.IsInstanceOfType<bool>(await command.ExecuteScalarAsync(token)));
            command.CommandText = "SELECT tests.log_prefix_arm($1)";
            await command.ExecuteNonQueryAsync(token);
            prefixArmed = true;
            command.Parameters.Clear();
            command.CommandText = "SELECT emit_log_values.native_log_hook_report()";
            PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(token));
            Assert.AreEqual("P7521", error.SqlState);
            Assert.AreEqual("native failure before managed log handler", error.MessageText);
            Assert.AreEqual("owned native prefix detail", error.Detail);
            Assert.AreEqual("retry native prefix report", error.Hint);
            command.CommandText = "SELECT tests.log_prefix_active()";
            Assert.IsTrue(Assert.IsInstanceOfType<bool>(await command.ExecuteScalarAsync(token)));
            command.CommandText = "SELECT tests.log_prefix_calls()";
            Assert.AreEqual(0x100000000L, await command.ExecuteScalarAsync(token));
            command.CommandText = "SELECT emit_log_values.native_log_hook_order()";
            Assert.IsEmpty(Assert.IsInstanceOfType<int[]>(await command.ExecuteScalarAsync(token)));
            command.CommandText = "SELECT emit_log_values.native_log_hook_messages()";
            string[] messages = Assert.IsInstanceOfType<string[]>(await command.ExecuteScalarAsync(token));
            Assert.DoesNotContain(Marker, messages);
            Assert.Contains("native failure before managed log handler", messages);
            command.CommandText = "SELECT emit_log_values.native_log_hook_report()";
            Assert.AreEqual(42, await command.ExecuteScalarAsync(token));
            command.CommandText = "SELECT tests.log_prefix_calls()";
            Assert.AreEqual(0x200000001L, await command.ExecuteScalarAsync(token));
            command.CommandText = "SELECT emit_log_values.native_log_hook_order()";
            Assert.AreSequenceEqual<int>([1, 2, 3, 4], Assert.IsInstanceOfType<int[]>(await command.ExecuteScalarAsync(token)));
            command.CommandText = "SELECT tests.log_prefix_active()";
            Assert.IsTrue(Assert.IsInstanceOfType<bool>(await command.ExecuteScalarAsync(token)));
            command.CommandText = "SELECT emit_log_values.native_log_hook_restore()";
            Assert.IsFalse(Assert.IsInstanceOfType<bool>(await command.ExecuteScalarAsync(token)));
            command.CommandText = "SELECT tests.log_prefix_restore()";
            Assert.IsTrue(Assert.IsInstanceOfType<bool>(await command.ExecuteScalarAsync(token)));
            prefixArmed = false;
            command.CommandText = "SELECT emit_log_values.native_log_hook_restore()";
            Assert.IsTrue(Assert.IsInstanceOfType<bool>(await command.ExecuteScalarAsync(token)));
            command.CommandText = "SELECT pg_backend_pid(), 6 * 7, tests.raw_call_holdoffs()";
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
            Assert.IsTrue(await reader.ReadAsync(token));
            Assert.AreEqual(backend, reader.GetInt32(0));
            Assert.AreEqual(42, reader.GetInt32(1));
            Assert.AreEqual(0L, reader.GetInt64(2));
            Assert.IsFalse(await reader.ReadAsync(token));
        }
        finally
        {
            if (connection.State == ConnectionState.Open)
            {
                command.Parameters.Clear();
                if (prefixArmed)
                {
                    command.CommandText = "SELECT tests.log_prefix_restore()";
                    Assert.IsTrue(Assert.IsInstanceOfType<bool>(await command.ExecuteScalarAsync(token)));
                }

                command.CommandText = "SELECT emit_log_values.native_log_hook_restore()";
                Assert.IsTrue(Assert.IsInstanceOfType<bool>(await command.ExecuteScalarAsync(token)));
            }
        }
    }
}
