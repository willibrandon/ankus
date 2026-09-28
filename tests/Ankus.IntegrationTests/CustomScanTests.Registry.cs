using System.Text;
using System.Text.Json;
using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Proves predecessor-hook and method-registry contracts in real, independent PostgreSQL backends.
/// </summary>
public sealed partial class CustomScanTests
{
    /// <summary>
    /// Enabling and disabling tracing preserves the predecessor, its original arguments and its position before wrapping.
    /// </summary>
    [TestMethod]
    public async Task TraceScanChainsPredecessorBeforeWrapping()
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await OpenHookAsync(token);
        foreach (bool enabled in new[] { false, true, false })
        {
            await using var command = new NpgsqlCommand($"""
                SELECT datatype.custom_scan_hook_configure(0);
                SELECT customscan.trace_scan_reset();
                SET ankus_trace_scan.enabled = {(enabled ? "on" : "off")};
                """, connection);
            await command.ExecuteNonQueryAsync(token);
            using (JsonDocument plan = await ExplainAsync(connection, HookQuery, true, token))
            {
                Assert.HasCount(enabled ? 1 : 0, TraceNodes(plan.RootElement[0].GetProperty("Plan")));
            }

            await AssertHookRowsAsync(connection, token);
            command.CommandText = "SELECT datatype.custom_scan_hook_state()";
            Assert.AreSequenceEqual<long>([2, 2, 2, 1, 1], Assert.IsInstanceOfType<long[]>(await command.ExecuteScalarAsync(token)));
            Assert.AreSequenceEqual<long>(enabled ? [2, 2, 2, 6, 2, 0, 6, 2] : new long[8], await CountsAsync(connection, token));
        }
    }

    /// <summary>
    /// Errors cross both predecessor and trace callback boundaries without skipping unwind or replacing the backend.
    /// </summary>
    /// <param name="mode">One for a managed exception and two for a guarded native registry error.</param>
    /// <param name="enabled">Whether tracing is enabled while the predecessor rejects planning.</param>
    [TestMethod]
    [DataRow(1, false)]
    [DataRow(1, true)]
    [DataRow(2, false)]
    [DataRow(2, true)]
    public async Task TraceScanPredecessorErrorsUnwindAndRecover(int mode, bool enabled)
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await OpenHookAsync(token);
        int backend = connection.ProcessID;
        await using var command = new NpgsqlCommand($"""
            SELECT datatype.custom_scan_hook_configure({mode});
            SET ankus_trace_scan.enabled = {(enabled ? "on" : "off")};
            """, connection);
        await command.ExecuteNonQueryAsync(token);
        command.CommandText = HookQuery;
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteNonQueryAsync(token));
        Assert.AreEqual(mode == 1 ? "P7522" : "42704", error.SqlState);
        Assert.AreEqual(mode == 1 ? "managed predecessor failure" : "ExtensibleNodeMethods \"Ankus missing predecessor\" was not registered", error.MessageText);
        Assert.AreEqual(mode == 1 ? "predecessor detail" : null, error.Detail);
        Assert.AreEqual(mode == 1 ? "retry planning" : null, error.Hint);
        command.CommandText = "SELECT datatype.custom_scan_hook_state()";
        Assert.AreSequenceEqual<long>([1, 1, 2, 1, 1], Assert.IsInstanceOfType<long[]>(await command.ExecuteScalarAsync(token)));
        Assert.AreSequenceEqual(new long[8], await CountsAsync(connection, token));
        command.CommandText = "SELECT pg_backend_pid()";
        Assert.AreEqual(backend, await command.ExecuteScalarAsync(token));
        await AssertHookRowsAsync(connection, token);
        command.CommandText = "SELECT datatype.custom_scan_hook_state()";
        Assert.AreSequenceEqual<long>([2, 2, 2, 1, 1], Assert.IsInstanceOfType<long[]>(await command.ExecuteScalarAsync(token)));
        Assert.AreSequenceEqual<long>(enabled ? [1, 1, 1, 3, 1, 0, 3, 1] : new long[8], await CountsAsync(connection, token));
    }

    /// <summary>
    /// Successful registration retains exact native name bytes and method identity outside the registering transaction.
    /// </summary>
    /// <param name="bytes">The native byte count, below the selected headers' 64-byte registration limit.</param>
    /// <param name="multibyte">Whether the name includes two-byte UTF8 characters.</param>
    [TestMethod]
    [DataRow(0, false)]
    [DataRow(1, false)]
    [DataRow(63, false)]
    [DataRow(63, true)]
    public async Task TraceScanRegistryOwnsNamesBeyondTransactions(int bytes, bool multibyte)
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await OpenAsync(token);
        string name = RegistryName(bytes, multibyte);
        Assert.AreEqual(bytes, Encoding.UTF8.GetByteCount(name));
        await using var command = new NpgsqlCommand("SELECT datatype.custom_scan_registry_lookup(@name, true)", connection);
        command.Parameters.AddWithValue("name", name);
        Assert.AreEqual(0L, await command.ExecuteScalarAsync(token));
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token);
        command.Transaction = transaction;
        command.CommandText = "SELECT datatype.custom_scan_registry_register(@name)";
        long address = Assert.IsInstanceOfType<long>(await command.ExecuteScalarAsync(token));
        Assert.AreNotEqual(0L, address);
        await transaction.RollbackAsync(token);
        command.Transaction = null;
        command.CommandText = "SELECT datatype.custom_scan_collect()";
        await command.ExecuteNonQueryAsync(token);
        command.CommandText = "SELECT datatype.custom_scan_registry_lookup(@name, false)";
        Assert.AreEqual(address, await command.ExecuteScalarAsync(token));
        command.CommandText = "SELECT datatype.custom_scan_registry_name(@name)";
        Assert.AreEqual(name, await command.ExecuteScalarAsync(token));
        command.CommandText = RegistryOwnersQuery;
        Assert.AreEqual(1L, await command.ExecuteScalarAsync(token));
    }

    /// <summary>
    /// Native byte-length rejection cannot leak an attempted backend-lived table or damage an existing provider.
    /// </summary>
    /// <param name="bytes">The exact native name length at or immediately above the selected 64-byte limit.</param>
    /// <param name="multibyte">Whether the name has fewer managed characters than native UTF8 bytes.</param>
    [TestMethod]
    [DataRow(64, false)]
    [DataRow(65, false)]
    [DataRow(64, true)]
    [DataRow(65, true)]
    public async Task TraceScanRegistryRejectsLongNativeNames(int bytes, bool multibyte)
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await OpenAsync(token);
        int backend = connection.ProcessID;
        string name = RegistryName(bytes, multibyte);
        Assert.AreEqual(bytes, Encoding.UTF8.GetByteCount(name));
        // Npgsql closes connections for class XX errors; catch the real ERROR in PostgreSQL to prove same-backend recovery.
        await using var command = new NpgsqlCommand("""
            CREATE FUNCTION pg_temp.registry_failure(requested_name text) RETURNS text[] LANGUAGE plpgsql AS $$
            DECLARE code text; message text; detail text; hint text;
            BEGIN
                PERFORM datatype.custom_scan_registry_register(requested_name);
                RETURN NULL;
            EXCEPTION WHEN internal_error THEN
                GET STACKED DIAGNOSTICS code = RETURNED_SQLSTATE, message = MESSAGE_TEXT,
                    detail = PG_EXCEPTION_DETAIL, hint = PG_EXCEPTION_HINT;
                RETURN ARRAY[code,message,detail,hint];
            END
            $$;
            """, connection);
        await command.ExecuteNonQueryAsync(token);
        command.CommandText = "SELECT pg_temp.registry_failure(@name)";
        command.Parameters.AddWithValue("name", name);
        Assert.AreSequenceEqual<string>(["XX000", "extensible node name is too long", string.Empty, string.Empty],
            Assert.IsInstanceOfType<string[]>(await command.ExecuteScalarAsync(token)));
        command.CommandText = "SELECT pg_backend_pid()";
        Assert.AreEqual(backend, await command.ExecuteScalarAsync(token));
        command.CommandText = RegistryOwnersQuery;
        Assert.AreEqual(0L, await command.ExecuteScalarAsync(token));
        command.CommandText = "SELECT datatype.custom_scan_registry_lookup(@name, true)";
        Assert.AreEqual(0L, await command.ExecuteScalarAsync(token));
        command.CommandText = "SELECT sum(value::bigint) FROM trace_input WHERE value > 0";
        Assert.AreEqual(2147483650m, await command.ExecuteScalarAsync(token));
    }

    /// <summary>
    /// Distinct native keys preserve case while registry contents remain private to each backend.
    /// </summary>
    [TestMethod]
    public async Task TraceScanRegistryUsesCaseSensitiveBackendNames()
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await OpenAsync(token);
        await using var command = new NpgsqlCommand("SELECT datatype.custom_scan_registry_register('Trace Alias')", connection);
        long upper = Assert.IsInstanceOfType<long>(await command.ExecuteScalarAsync(token));
        command.CommandText = "SELECT datatype.custom_scan_registry_register('trace alias')";
        long lower = Assert.IsInstanceOfType<long>(await command.ExecuteScalarAsync(token));
        Assert.AreNotEqual(0L, upper);
        Assert.AreNotEqual(0L, lower);
        Assert.AreNotEqual(upper, lower);
        command.CommandText = "SELECT datatype.custom_scan_registry_lookup('Trace Alias', false)";
        Assert.AreEqual(upper, await command.ExecuteScalarAsync(token));
        command.CommandText = "SELECT datatype.custom_scan_registry_lookup('trace alias', false)";
        Assert.AreEqual(lower, await command.ExecuteScalarAsync(token));
        await using NpgsqlConnection other = await OpenAsync(token);
        Assert.AreNotEqual(connection.ProcessID, other.ProcessID);
        await using var lookup = new NpgsqlCommand("SELECT datatype.custom_scan_registry_lookup('Trace Alias', true)", other);
        Assert.AreEqual(0L, await lookup.ExecuteScalarAsync(token));
        lookup.CommandText = "SELECT datatype.custom_scan_registry_lookup('trace alias', true)";
        Assert.AreEqual(0L, await lookup.ExecuteScalarAsync(token));
        command.CommandText = RegistryOwnersQuery;
        Assert.AreEqual(2L, await command.ExecuteScalarAsync(token));
    }

    /// <summary>
    /// Optional lookup and required lookup preserve PostgreSQL's distinct missing-entry contracts and backend recovery.
    /// </summary>
    [TestMethod]
    public async Task TraceScanRegistryDistinguishesMissingMethods()
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await OpenAsync(token);
        int backend = connection.ProcessID;
        await using var command = new NpgsqlCommand("SELECT datatype.custom_scan_registry_lookup('Unregistered trace', true)", connection);
        Assert.AreEqual(0L, await command.ExecuteScalarAsync(token));
        command.CommandText = "SELECT datatype.custom_scan_registry_lookup('Unregistered trace', false)";
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(token));
        Assert.AreEqual("42704", error.SqlState);
        Assert.AreEqual("ExtensibleNodeMethods \"Unregistered trace\" was not registered", error.MessageText);
        Assert.IsNull(error.Detail);
        Assert.IsNull(error.Hint);
        command.CommandText = "SELECT pg_backend_pid()";
        Assert.AreEqual(backend, await command.ExecuteScalarAsync(token));
        command.CommandText = "SELECT value FROM trace_input WHERE value = 2";
        Assert.AreEqual(2, await command.ExecuteScalarAsync(token));
    }

    /// <summary>
    /// Duplicate registration leaves the original native table and already-cached real custom plan usable after an error.
    /// </summary>
    [TestMethod]
    public async Task TraceScanRegistryDuplicatesPreserveCachedPlans()
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await OpenAsync(token);
        await using var command = new NpgsqlCommand("SELECT datatype.custom_scan_registry_lookup('Ankus Trace', false)", connection);
        long original = Assert.IsInstanceOfType<long>(await command.ExecuteScalarAsync(token));
        Assert.AreNotEqual(0L, original);
        command.CommandText = "SET plan_cache_mode = force_generic_plan; PREPARE registered_trace AS SELECT sum(value::bigint) FROM trace_input WHERE value > 0";
        await command.ExecuteNonQueryAsync(token);
        command.CommandText = "EXECUTE registered_trace";
        Assert.AreEqual(2147483650m, await command.ExecuteScalarAsync(token));
        command.CommandText = "SELECT datatype.custom_scan_registry_register('Ankus Trace')";
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(token));
        Assert.AreEqual("42710", error.SqlState);
        Assert.AreEqual("extensible node type \"Ankus Trace\" already exists", error.MessageText);
        Assert.IsNull(error.Detail);
        Assert.IsNull(error.Hint);
        command.CommandText = "SELECT datatype.custom_scan_registry_lookup('Ankus Trace', false)";
        Assert.AreEqual(original, await command.ExecuteScalarAsync(token));
        command.CommandText = RegistryOwnersQuery;
        Assert.AreEqual(0L, await command.ExecuteScalarAsync(token));
        command.CommandText = "SELECT datatype.custom_scan_collect(); SET ankus_trace_scan.enabled = off";
        await command.ExecuteNonQueryAsync(token);
        command.CommandText = "EXECUTE registered_trace";
        Assert.AreEqual(2147483650m, await command.ExecuteScalarAsync(token));
        Assert.AreSequenceEqual<long>([1, 2, 2, 8, 2, 0, 8, 2], await CountsAsync(connection, token));
        command.CommandText = "DEALLOCATE registered_trace";
        await command.ExecuteNonQueryAsync(token);
    }

    /// <summary>
    /// A pre-existing name prevents module registration, reclaims its actual owner and preserves the predecessor on each retry.
    /// </summary>
    [TestMethod]
    public async Task TraceScanRegistryCollisionsRejectModuleLoad()
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await OpenHookAsync(token, false);
        int backend = connection.ProcessID;
        await using var command = new NpgsqlCommand("SELECT datatype.custom_scan_registry_register('Ankus Trace', false)", connection);
        long original = Assert.IsInstanceOfType<long>(await command.ExecuteScalarAsync(token));
        Assert.AreNotEqual(0L, original);
        for (int attempt = 0; attempt < 2; attempt++)
        {
            command.CommandText = "SELECT customscan.trace_scan_reset()";
            PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(token));
            Assert.AreEqual("42710", error.SqlState);
            Assert.AreEqual("extensible node type \"Ankus Trace\" already exists", error.MessageText);
            Assert.IsNull(error.Detail);
            Assert.IsNull(error.Hint);
            command.CommandText = "SELECT datatype.custom_scan_registry_lookup('Ankus Trace', false)";
            Assert.AreEqual(original, await command.ExecuteScalarAsync(token));
            command.CommandText = "SELECT count(*) FROM pg_backend_memory_contexts WHERE ident = 'Ankus Trace methods'";
            Assert.AreEqual(0L, await command.ExecuteScalarAsync(token));
            command.CommandText = RegistryOwnersQuery;
            Assert.AreEqual(1L, await command.ExecuteScalarAsync(token));
            using (JsonDocument plan = await ExplainAsync(connection, HookQuery, true, token))
            {
                Assert.IsEmpty(TraceNodes(plan.RootElement[0].GetProperty("Plan")));
            }

            await AssertHookRowsAsync(connection, token);
        }

        command.CommandText = "SELECT datatype.custom_scan_hook_state()";
        Assert.AreSequenceEqual<long>([4, 4, 2, 1, 1], Assert.IsInstanceOfType<long[]>(await command.ExecuteScalarAsync(token)));
        command.CommandText = "SELECT pg_backend_pid()";
        Assert.AreEqual(backend, await command.ExecuteScalarAsync(token));
    }

    /// <summary>
    /// Uses a second range-table entry to expose forwarding mistakes hidden by the common first-relation index.
    /// </summary>
    private const string HookQuery = "SELECT t.value,t.label FROM (VALUES (0)) AS seed(dummy) CROSS JOIN trace_hook_target t ORDER BY t.value";

    /// <summary>
    /// Observes retained native contexts rather than relying on managed callback counters for registration cleanup.
    /// </summary>
    private const string RegistryOwnersQuery = "SELECT count(*) FROM pg_backend_memory_contexts WHERE ident = 'Ankus registry probe'";

    /// <summary>
    /// Constructs independent ASCII and UTF8 byte boundaries without embedding zero bytes in PostgreSQL text.
    /// </summary>
    private static string RegistryName(int bytes, bool multibyte) => multibyte
        ? new string('é', bytes / 2) + (bytes % 2 == 0 ? string.Empty : "x")
        : new string('r', bytes);

    /// <summary>
    /// Installs the predecessor before loading the trace module in a fresh backend.
    /// </summary>
    private static async Task<NpgsqlConnection> OpenHookAsync(CancellationToken token, bool loadTrace = true)
    {
        NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        try
        {
            await using var command = new NpgsqlCommand("""
                CREATE TEMP TABLE trace_hook_target(value integer, label text);
                INSERT INTO trace_hook_target VALUES (1,NULL),(2,'café');
                ANALYZE trace_hook_target;
                SELECT datatype.custom_scan_hook_install('trace_hook_target'::regclass);
                SET max_parallel_workers_per_gather = 0;
                """, connection);
            await command.ExecuteNonQueryAsync(token);
            if (loadTrace)
            {
                command.CommandText = "SELECT customscan.trace_scan_reset()";
                await command.ExecuteNonQueryAsync(token);
            }

            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// Proves actual nullable and UTF8 tuple execution through the planned hook chain.
    /// </summary>
    private static async Task AssertHookRowsAsync(NpgsqlConnection connection, CancellationToken token)
    {
        await using var command = new NpgsqlCommand(HookQuery, connection);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
        var rows = new List<(int Value, string? Label)>();
        while (await reader.ReadAsync(token))
        {
            rows.Add((reader.GetInt32(0), reader.IsDBNull(1) ? null : reader.GetString(1)));
        }

        Assert.AreSequenceEqual<(int, string?)>([(1, null), (2, "café")], rows);
    }
}
