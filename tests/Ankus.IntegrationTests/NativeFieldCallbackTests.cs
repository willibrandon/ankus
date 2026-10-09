using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies field-named callbacks, native method-table storage and managed unwind through the real PostgreSQL boundary.
/// </summary>
/// <param name="context">The current test context.</param>
[TestClass]
public sealed class NativeFieldCallbackTests(TestContext context)
{
    /// <summary>
    /// Exact native addresses and mutations survive both alias and canonical callback invocation.
    /// </summary>
    /// <param name="mode">Ordinary execution or recovery from a caught SPI error inside each callback.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(3)]
    public async Task NativeFieldCallbackRoundtripPreservesStorage(int mode)
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        await using var command = new NpgsqlCommand("SELECT datatype.native_field_callback_roundtrip(@mode)", connection);
        command.Parameters.AddWithValue("mode", mode);
        Assert.AreEqual("9|True|True|True", await command.ExecuteScalarAsync(token));
        command.Parameters.Clear();
        command.CommandText = "SELECT datatype.native_field_callback_state()";
        Assert.AreEqual(mode == 3 ? "2|2|1|True" : "2|2|1|False", await command.ExecuteScalarAsync(token));
        await AssertHealthyAsync(connection, token);
    }

    /// <summary>
    /// An ordinary exception from a callback reaches its managed caller as a PostgreSQL error with its message after
    /// crossing the native frame, as pgrx's panics in extern C callbacks do. Caught from a subtransaction, which rolls
    /// the error back, the caller keeps using SPI in the same function.
    /// </summary>
    [TestMethod]
    public async Task OrdinaryCallbackExceptionsCanBeCaughtAcrossTheNativeFrame()
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        await using var command = new NpgsqlCommand("SELECT datatype.native_field_callback_roundtrip(5)", connection);
        Assert.AreEqual("PgException|38000|ordinary field callback failure", await command.ExecuteScalarAsync(token));
        command.CommandText = "SELECT datatype.native_field_callback_state()";
        Assert.AreEqual("1|1|1|True", await command.ExecuteScalarAsync(token));
        await AssertHealthyAsync(connection, token);
    }

    /// <summary>
    /// Managed and native callback failures unwind both managed frames and recover the original PostgreSQL session.
    /// </summary>
    /// <param name="mode">The controlled managed or uncaught native error.</param>
    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(4)]
    public async Task NativeFieldCallbackErrorsUnwindAndRecover(int mode)
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        int backend = connection.ProcessID;
        await using var command = new NpgsqlCommand("SELECT datatype.native_field_callback_roundtrip(@mode)", connection);
        command.Parameters.AddWithValue("mode", mode);
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(token));
        Assert.AreEqual(mode switch { 1 => "P7511", 2 => "22012", _ => "38000" }, error.SqlState);
        Assert.AreEqual(mode switch { 1 => "managed field callback failure", 2 => "division by zero", _ => "ordinary field callback failure" },
            error.MessageText);
        Assert.AreEqual(mode == 1 ? "field callback detail" : null, error.Detail);
        Assert.AreEqual(mode == 1 ? "field callback hint" : null, error.Hint);
        command.Parameters.Clear();
        command.CommandText = "SELECT datatype.native_field_callback_state()";
        Assert.AreEqual("1|1|1|False", await command.ExecuteScalarAsync(token));
        await AssertHealthyAsync(connection, token);
        command.CommandText = "SELECT pg_backend_pid(), 6 * 7";
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
        Assert.IsTrue(await reader.ReadAsync(token));
        Assert.AreEqual(backend, reader.GetInt32(0));
        Assert.AreEqual(42, reader.GetInt32(1));
        Assert.IsFalse(await reader.ReadAsync(token));
    }

    /// <summary>
    /// Requires exact native callback results and exactly one unwind for every managed invocation.
    /// </summary>
    private static async Task AssertHealthyAsync(NpgsqlConnection connection, CancellationToken token)
    {
        await using var command = new NpgsqlCommand("SELECT datatype.native_field_callback_roundtrip(0)", connection);
        Assert.AreEqual("9|True|True|True", await command.ExecuteScalarAsync(token));
        command.CommandText = "SELECT datatype.native_field_callback_state()";
        Assert.AreEqual("2|2|1|False", await command.ExecuteScalarAsync(token));
    }
}
