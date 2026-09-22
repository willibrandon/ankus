using Ankus.Testing;
using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies native library initialization, backend state, and errors with published Native AOT libraries.
/// </summary>
/// <param name="context">The test cancellation and diagnostic context.</param>
[TestClass]
public sealed class InitializationTests(TestContext context)
{
    /// <summary>
    /// Initializes before the first function or explicit load, only once, independently in each backend.
    /// </summary>
    /// <param name="explicitLoad">Whether LOAD precedes the first function call.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task InitializationRunsOncePerBackend(bool explicitLoad)
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection first = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        await using NpgsqlConnection second = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        Assert.AreNotEqual(first.ProcessID, second.ProcessID);
        foreach (NpgsqlConnection connection in new[] { first, second })
        {
            await using var command = new NpgsqlCommand("LOAD 'Ankus.TestExtension'", connection);
            if (explicitLoad)
            {
                await command.ExecuteNonQueryAsync(token);
            }

            command.CommandText = "SELECT datatype.initialization_state()";
            Assert.AreEqual("1|1|default|42", await command.ExecuteScalarAsync(token));
            command.CommandText = "LOAD 'Ankus.TestExtension'; LOAD 'Ankus.TestExtension'";
            await command.ExecuteNonQueryAsync(token);
            command.CommandText = "SELECT datatype.initialization_state()";
            Assert.AreEqual("1|1|default|42", await command.ExecuteScalarAsync(token));
        }
    }

    /// <summary>
    /// Repeated failures unwind managed finally blocks, preserve diagnostics, and permit same-session retry.
    /// </summary>
    /// <param name="mode">The managed, PostgreSQL, or recursive initialization failure.</param>
    /// <param name="sqlState">The expected SQLSTATE.</param>
    [TestMethod]
    [DataRow("managed-error", "38000")]
    [DataRow("postgres-error", "22023")]
    [DataRow("recursive", "55000")]
    public async Task InitializationFailureUnwindsAndCanRetry(string mode, string sqlState)
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        await using var command = new NpgsqlCommand("SELECT set_config('ankus_test.initialization', $1, false)", connection);
        command.Parameters.AddWithValue(mode);
        await command.ExecuteNonQueryAsync(token);
        command.Parameters.Clear();
        for (int attempt = 0; attempt < 50; attempt++)
        {
            command.CommandText = "LOAD 'Ankus.TestExtension'";
            PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteNonQueryAsync(token));
            Assert.AreEqual(sqlState, error.SqlState);
            if (mode == "postgres-error")
            {
                Assert.AreEqual(new string('x', 4096) + " initialisation 🐘", error.MessageText);
                Assert.AreEqual("Owned initialization détail", error.Detail);
                Assert.AreEqual("Change the initialization mode and retry.", error.Hint);
            }
            else if (mode == "managed-error")
            {
                Assert.AreEqual("Managed initialization failed.", error.MessageText);
            }
            else
            {
                Assert.Contains("initialization is already in progress", error.MessageText);
            }

            command.CommandText = "SELECT 42";
            Assert.AreEqual(42, await command.ExecuteScalarAsync(token));
        }

        command.CommandText = "SET ankus_test.initialization = 'default'; LOAD 'Ankus.TestExtension'";
        await command.ExecuteNonQueryAsync(token);
        command.CommandText = "SELECT datatype.initialization_state()";
        Assert.AreEqual("51|51|default|42", await command.ExecuteScalarAsync(token));
    }

    /// <summary>
    /// A failed initializer's SQL rolls back with its savepoint while managed counters survive for a retry.
    /// </summary>
    [TestMethod]
    public async Task InitializationFailureRollsBackSqlAndPreservesCallerState()
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token);
        await using var command = new NpgsqlCommand(
            "CREATE TEMP TABLE initialization_probe(value int); INSERT INTO initialization_probe VALUES (7); " +
            "SET ankus_test.initialization = 'sql-rollback'", connection, transaction);
        await command.ExecuteNonQueryAsync(token);
        await transaction.SaveAsync("before_initialization", token);
        command.CommandText = "LOAD 'Ankus.TestExtension'";
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteNonQueryAsync(token));
        Assert.AreEqual("P0001", error.SqlState);
        Assert.AreEqual("Rollback initialization SQL.", error.MessageText);
        await transaction.RollbackAsync("before_initialization", token);
        command.CommandText = "SELECT array_agg(value ORDER BY value) FROM initialization_probe";
        Assert.AreEqual(7, Assert.ContainsSingle(Assert.IsInstanceOfType<int[]>(await command.ExecuteScalarAsync(token))));
        command.CommandText = "SET ankus_test.initialization = 'default'; LOAD 'Ankus.TestExtension'";
        await command.ExecuteNonQueryAsync(token);
        command.CommandText = "SELECT datatype.initialization_state()";
        Assert.AreEqual("2|2|default|42", await command.ExecuteScalarAsync(token));
        await transaction.RollbackAsync(token);
        command.Transaction = null;
        Assert.AreEqual("2|2|default|42", await command.ExecuteScalarAsync(token));
    }

    /// <summary>
    /// A caught native error during initialization preserves an existing prepared statement, transaction, and later SPI work.
    /// </summary>
    /// <param name="mode">The guarded native failure to catch during initialization.</param>
    /// <param name="result">The initialization status after catching the native error.</param>
    [TestMethod]
    [DataRow("spi-error", "spi-recovered")]
    [DataRow("recursive-caught", "recursive-recovered")]
    public async Task InitializationSpiErrorsPreserveCallerState(string mode, string result)
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token);
        await using var prepared = new NpgsqlCommand("SELECT $1::integer + 2", connection, transaction);
        prepared.Parameters.AddWithValue(40);
        await prepared.PrepareAsync(token);
        await using var command = new NpgsqlCommand(
            "CREATE TEMP TABLE initialization_probe(value int); INSERT INTO initialization_probe VALUES (7)", connection, transaction);
        await command.ExecuteNonQueryAsync(token);
        command.CommandText = "SELECT set_config('ankus_test.initialization', $1, true)";
        command.Parameters.AddWithValue(mode);
        await command.ExecuteNonQueryAsync(token);
        command.Parameters.Clear();
        command.CommandText = "LOAD 'Ankus.TestExtension'";
        await command.ExecuteNonQueryAsync(token);
        command.CommandText = "SELECT datatype.initialization_state()";
        Assert.AreEqual($"1|1|{result}|42", await command.ExecuteScalarAsync(token));
        Assert.AreEqual(42, await prepared.ExecuteScalarAsync(token));
        command.CommandText = "INSERT INTO initialization_probe VALUES (9); SELECT sum(value) FROM initialization_probe";
        Assert.AreEqual(16L, await command.ExecuteScalarAsync(token));
    }

    /// <summary>
    /// Nested initialization of another Native AOT library restores the caller's PostgreSQL binding.
    /// </summary>
    [TestMethod]
    public async Task InitializationCanLoadAnotherExtension()
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        var notices = new List<PostgresNotice>();
        connection.Notice += (_, args) => notices.Add(args.Notice);
        await using var command = new NpgsqlCommand(
            "SET ankus_test.initialization = 'nested'; LOAD 'Ankus.TestExtension'", connection);
        await command.ExecuteNonQueryAsync(token);
        command.CommandText = "SELECT datatype.initialization_state()";
        Assert.AreEqual("1|1|nested|42", await command.ExecuteScalarAsync(token));
        Assert.AreEqual("Ankus initialization sample loaded.", Assert.ContainsSingle(notices).MessageText);
    }

    /// <summary>
    /// An assembly with no SQL functions still publishes and loads its native initializer exactly once.
    /// </summary>
    [TestMethod]
    public async Task InitializationOnlyExtensionLoadsWithoutSqlFunctions()
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        var notices = new List<PostgresNotice>();
        connection.Notice += (_, args) => notices.Add(args.Notice);
        await using var command = new NpgsqlCommand("LOAD 'Ankus.Examples.Initialization'; LOAD 'Ankus.Examples.Initialization'", connection);
        await command.ExecuteNonQueryAsync(token);
        Assert.AreEqual("Ankus initialization sample loaded.", Assert.ContainsSingle(notices).MessageText);
        command.CommandText = "SELECT 42";
        Assert.AreEqual(42, await command.ExecuteScalarAsync(token));
    }

    /// <summary>
    /// Session preload runs the initializer with database access before the first client command in each backend.
    /// </summary>
    [TestMethod]
    public async Task SessionPreloadInitializesBeforeFirstFunction()
    {
        CancellationToken token = context.CancellationToken;
        PostgresTestClusterOptions options = await PreloadOptionsAsync("session_preload_libraries", "Ankus.TestExtension");
        await using PostgresTestCluster cluster = await PostgresTestCluster.StartAsync(options, token);
        await using NpgsqlConnection first = await cluster.OpenConnectionAsync(token);
        await using var setup = new NpgsqlCommand("CREATE EXTENSION ankus_test", first);
        await setup.ExecuteNonQueryAsync(token);
        await using NpgsqlConnection second = await cluster.OpenConnectionAsync(token);
        foreach (NpgsqlConnection connection in new[] { first, second })
        {
            await using var command = new NpgsqlCommand("SET ankus_test.initialization = 'managed-error'; SELECT initialization_state()", connection);
            Assert.AreEqual("1|1|default|42", await command.ExecuteScalarAsync(token));
        }
    }

    /// <summary>
    /// A failed session initializer unwinds before rejecting its connection and leaves other backends and later connections usable.
    /// </summary>
    [TestMethod]
    public async Task SessionPreloadFailureUnwindsAndPreservesServer()
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection observer = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        var builder = new NpgsqlConnectionStringBuilder(observer.ConnectionString)
        {
            Options = "-c session_preload_libraries=Ankus.TestExtension -c ankus_test.initialization=startup-error",
        };
        await using (var failing = new NpgsqlConnection(builder.ConnectionString))
        {
            PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => failing.OpenAsync(token));
            Assert.AreEqual("38000", error.SqlState);
            Assert.AreEqual("Startup initialization failed.", error.MessageText);
            Assert.AreEqual("FATAL", error.InvariantSeverity);
        }

        string log = PostgresFixture.Cluster.ReadServerLog();
        Assert.Contains("Startup initialization finally completed.", log);
        await using var peer = new NpgsqlCommand("SELECT 42", observer);
        Assert.AreEqual(42, await peer.ExecuteScalarAsync(token));
        builder.Options = "-c session_preload_libraries=Ankus.TestExtension -c ankus_test.initialization=default";
        await using var recovered = new NpgsqlConnection(builder.ConnectionString);
        await recovered.OpenAsync(token);
        await using var command = new NpgsqlCommand("SELECT datatype.initialization_state()", recovered);
        Assert.AreEqual("1|1|default|42", await command.ExecuteScalarAsync(token));
    }

    /// <summary>
    /// The native postmaster guard rejects shared preload before invoking the managed initializer.
    /// </summary>
    [TestMethod]
    public async Task SharedPreloadRejectsManagedInitialization()
    {
        PostgresTestClusterOptions options = await PreloadOptionsAsync("shared_preload_libraries", "Ankus.Examples.Initialization");
        InvalidOperationException error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => PostgresTestCluster.StartAsync(options, context.CancellationToken));
        Assert.Contains("shared_preload_libraries", error.Message);
        Assert.Contains("session_preload_libraries", error.Message);
        Assert.DoesNotContain("Ankus initialization sample loaded.", error.Message);
    }

    private async Task<PostgresTestClusterOptions> PreloadOptionsAsync(string setting, string library)
    {
        PostgresTestClusterOptions defaults = await IntegrationEnvironment.CreateOptionsAsync(context.CancellationToken);
        return new PostgresTestClusterOptions
        {
            Installation = defaults.Installation,
            DataDirectoryBase = defaults.DataDirectoryBase,
            LogDirectory = defaults.LogDirectory,
            PostgreSqlConfiguration = [.. defaults.PostgreSqlConfiguration, $"{setting} = '{library}'"],
        };
    }
}
