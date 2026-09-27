using System.Globalization;
using System.Net.Sockets;
using Ankus.Testing;
using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies prepared and parallel transaction callbacks using actual PostgreSQL transaction completion and recovery.
/// </summary>
/// <param name="context">The current test context.</param>
[TestClass]
[DoNotParallelize]
public sealed class TransactionBoundaryTests(TestContext context)
{
    private const string PreparedState = "pre:1,pre-finally,pre-second,prepare,spi-blocked,late-prepare|0|False";

    /// <summary>
    /// Preparation ends callback ownership before another backend commits or rolls back the durable transaction.
    /// </summary>
    /// <param name="commit">Whether to commit the prepared writes.</param>
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task PreparedTransactionCallbacksEndWithPreparation(bool commit)
    {
        CancellationToken token = context.CancellationToken;
        await using PostgresTestCluster cluster = await StartClusterAsync(token);
        await using NpgsqlConnection connection = await cluster.OpenConnectionAsync(token);
        await using NpgsqlConnection observer = await cluster.OpenConnectionAsync(token);
        Assert.AreNotEqual(connection.ProcessID, observer.ProcessID);
        string identity = "ankus-prepare-" + Guid.NewGuid().ToString("N");
        await BeginAsync(connection, 0, token);
        Assert.IsTrue(await ScalarAsync<bool>(connection, "SELECT datatype.transaction_callback_root_alive()", token));
        await ExecuteAsync(connection, $"PREPARE TRANSACTION '{identity}'", token);
        try
        {
            Assert.AreEqual(PreparedState, await StateAsync(connection, token));
            Assert.IsFalse(await ScalarAsync<bool>(connection, "SELECT datatype.transaction_callback_root_alive()", token));
            await AssertPreparedAsync(observer, identity, token);
            Assert.AreEqual(0L, await ScalarAsync<long>(observer, "SELECT count(*) FROM callback_prepared", token));
            await ExecuteAsync(observer, $"{(commit ? "COMMIT" : "ROLLBACK")} PREPARED '{identity}'", token);
            await AssertRowsAsync(observer, commit, token);
            Assert.AreEqual(0L, await PreparedCountAsync(observer, identity, token));
            Assert.AreEqual(PreparedState, await StateAsync(connection, token));
            await AssertHealthyAsync(connection, token);
        }
        finally
        {
            await RollbackPreparedAsync(observer, identity);
        }
    }

    /// <summary>
    /// Managed and native pre-prepare errors unwind callbacks, discard writes and recover the same backend.
    /// </summary>
    /// <param name="mode">Managed rejection, native failure, or a caught native failure.</param>
    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public async Task PrePrepareFailureAbortsAndRecovers(int mode)
    {
        CancellationToken token = context.CancellationToken;
        await using PostgresTestCluster cluster = await StartClusterAsync(token);
        await using NpgsqlConnection connection = await cluster.OpenConnectionAsync(token);
        int backend = connection.ProcessID;
        string identity = "ankus-prepare-error-" + Guid.NewGuid().ToString("N");
        await BeginAsync(connection, mode, token);
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(
            () => ExecuteAsync(connection, $"PREPARE TRANSACTION '{identity}'", token));
        Assert.AreEqual(mode == 1 ? "P7501" : "22012", error.SqlState);
        Assert.AreEqual(mode == 1 ? "managed pre-prepare failure" : "division by zero", error.MessageText);
        Assert.AreEqual(mode == 1 ? "prepare detail" : null, error.Detail);
        Assert.AreEqual(mode == 1 ? "prepare hint" : null, error.Hint);
        await ExecuteAsync(connection, "ROLLBACK", token);
        Assert.AreEqual(backend, connection.ProcessID);
        Assert.AreEqual(mode == 3 ? "pre:1,caught-native,pre-finally,pre-second,abort|0|False" :
            "pre:1,pre-finally,abort|0|False", await StateAsync(connection, token));
        Assert.IsFalse(await ScalarAsync<bool>(connection, "SELECT datatype.transaction_callback_root_alive()", token));
        Assert.AreEqual(0L, await PreparedCountAsync(connection, identity, token));
        await AssertRowsAsync(connection, false, token);
        await AssertHealthyAsync(connection, token);
    }

    /// <summary>
    /// A cleanup exception after preparation preserves the durable transaction through PostgreSQL crash recovery.
    /// </summary>
    /// <param name="commit">Whether the recovered prepared transaction is committed.</param>
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task PrepareFailureRetainsDurablePreparedTransaction(bool commit)
    {
        CancellationToken token = context.CancellationToken;
        await using PostgresTestCluster cluster = await StartClusterAsync(token);
        await using NpgsqlConnection observer = await cluster.OpenConnectionAsync(token);
        await using NpgsqlConnection connection = await cluster.OpenConnectionAsync(token);
        string identity = "ankus-prepare-panic-" + Guid.NewGuid().ToString("N");
        await ExecuteAsync(connection, $"SET application_name = '{identity}'; SET log_error_verbosity = verbose", token);
        await BeginAsync(connection, 4, token);
        NpgsqlException failure = await Assert.ThrowsAsync<NpgsqlException>(
            () => ExecuteAsync(connection, $"PREPARE TRANSACTION '{identity}'", token));
        if (failure is PostgresException error)
        {
            Assert.AreEqual("38000", error.SqlState);
            Assert.AreEqual("managed post-prepare failure", error.MessageText);
            Assert.AreEqual("PANIC", error.InvariantSeverity);
        }
        else
        {
            Assert.IsTrue(OperatingSystem.IsWindows());
            IOException transport = Assert.IsInstanceOfType<IOException>(failure.InnerException);
            SocketException socket = Assert.IsInstanceOfType<SocketException>(transport.InnerException);
            Assert.AreEqual(SocketError.ConnectionReset, socket.SocketErrorCode);
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        while (!cluster.ReadServerLog().Contains("reinitializing", StringComparison.Ordinal))
        {
            await Task.Delay(50, deadline.Token);
        }

        string log = cluster.ReadServerLog();
        string diagnostic = $"[{identity}]: PANIC:  38000: managed post-prepare failure";
        string cleanup = $"[{identity}]: WARNING:  01000: managed post-prepare finally";
        Assert.Contains(diagnostic, log);
        Assert.Contains(cleanup, log);
        Assert.IsLessThan(log.IndexOf(diagnostic, StringComparison.Ordinal), log.IndexOf(cleanup, StringComparison.Ordinal));
        Assert.DoesNotContain("AbortTransaction while in PREPARE state", log);
        Assert.AreEqual(System.Data.ConnectionState.Closed, connection.State);
        await Assert.ThrowsAsync<NpgsqlException>(() => ScalarAsync<int>(observer, "SELECT 42", deadline.Token));
        await using NpgsqlConnection recovered = await RecoverAsync(cluster, deadline.Token);
        try
        {
            await AssertPreparedAsync(recovered, identity, deadline.Token);
            Assert.AreEqual(0L, await ScalarAsync<long>(recovered, "SELECT count(*) FROM callback_prepared", deadline.Token));
            await ExecuteAsync(recovered, $"{(commit ? "COMMIT" : "ROLLBACK")} PREPARED '{identity}'", deadline.Token);
            await AssertRowsAsync(recovered, commit, deadline.Token);
            Assert.AreEqual(0L, await PreparedCountAsync(recovered, identity, deadline.Token));
            await AssertHealthyAsync(recovered, deadline.Token);
        }
        finally
        {
            await RollbackPreparedAsync(recovered, identity);
        }
    }

    /// <summary>
    /// Actual workers report exact terminal callback sequences, released captures and recoverable query errors.
    /// </summary>
    /// <param name="mode">Successful execution, a managed row error, or a parallel pre-commit error.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public async Task ParallelTransactionCallbacksPreserveWorkerOutcomes(int mode)
    {
        CancellationToken token = context.CancellationToken;
        await using PostgresTestCluster cluster = await StartClusterAsync(token);
        await using NpgsqlConnection connection = await cluster.OpenConnectionAsync(token);
        int backend = connection.ProcessID;
        await ExecuteAsync(connection, """
            CREATE TABLE callback_parallel_input AS SELECT generate_series(1,30000) AS value;
            ALTER TABLE callback_parallel_input SET (parallel_workers=2);
            ANALYZE callback_parallel_input;
            SET max_parallel_workers_per_gather=2;
            SET min_parallel_table_scan_size=0;
            SET parallel_setup_cost=0;
            SET parallel_tuple_cost=0;
            SET parallel_leader_participation=off;
            """, token);
        string identity = Guid.NewGuid().ToString("N");
        HashSet<int> workers = [];
        if (mode == 0)
        {
            await ReadParallelRowsAsync(connection, identity, mode, workers, token);
        }
        else
        {
            PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(
                () => ReadParallelRowsAsync(connection, identity, mode, workers, token));
            Assert.AreEqual(mode == 1 ? "P7502" : "P7503", error.SqlState);
            Assert.AreEqual(mode == 1 ? "managed parallel row failure" : "managed parallel pre-commit failure", error.MessageText);
            Assert.AreEqual("parallel detail", error.Detail);
            Assert.AreEqual("parallel hint", error.Hint);
            Assert.Contains("parallel worker", error.Where ?? string.Empty);
        }

        await AssertParallelWitnessesAsync(cluster, backend, identity, mode, workers, token);
        Assert.AreEqual(backend, connection.ProcessID);
        await AssertHealthyAsync(connection, token);
        string retryIdentity = Guid.NewGuid().ToString("N");
        HashSet<int> retry = [];
        await ReadParallelRowsAsync(connection, retryIdentity, 0, retry, token);
        await AssertParallelWitnessesAsync(cluster, backend, retryIdentity, 0, retry, token);
    }

    /// <summary>
    /// Requires every executing worker's independent terminal record and released callback captures.
    /// </summary>
    private static async Task AssertParallelWitnessesAsync(PostgresTestCluster cluster, int backend, string identity,
        int mode, HashSet<int> workers, CancellationToken token)
    {
        string[] started = Directory.GetFiles(cluster.DataDirectory, $"ankus-parallel-callback-{identity}-*.started");
        Assert.IsNotEmpty(started);
        var observed = new HashSet<int>();
        var reports = new List<string>();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        foreach (string path in started)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            int process = int.Parse(name[(name.LastIndexOf('-') + 1)..], CultureInfo.InvariantCulture);
            Assert.IsGreaterThan(0, process);
            Assert.AreNotEqual(backend, process);
            Assert.IsTrue(observed.Add(process));
            Assert.AreEqual(identity + "|True", await File.ReadAllTextAsync(path, deadline.Token));
            string done = Path.ChangeExtension(path, ".done");
            while (!File.Exists(done))
            {
                await Task.Delay(25, deadline.Token);
            }

            reports.Add(await File.ReadAllTextAsync(done, deadline.Token));
        }

        string success = "registered,ParallelPreCommit,spi-blocked,pre-finally,ParallelCommit,spi-blocked|0|False";
        string rejected = "registered,ParallelPreCommit,spi-blocked,pre-finally,ParallelAbort,spi-blocked|0|False";
        foreach (string report in reports)
        {
            if (mode == 0)
            {
                Assert.AreEqual(success, report);
            }
            else if (mode == 1)
            {
                Assert.AreEqual("registered,body-finally,ParallelAbort,spi-blocked|0|False", report);
            }
            else
            {
                Assert.Contains(report, [rejected, "registered,ParallelAbort,spi-blocked|0|False"]);
            }
        }

        if (mode == 0)
        {
            Assert.AreSequenceEqual(workers.Order(), observed.Order());
        }
        else if (mode == 2)
        {
            Assert.Contains(rejected, reports);
        }
    }

    /// <summary>
    /// Starts an owned cluster with preparation enabled and installs the already published extension.
    /// </summary>
    private static async Task<PostgresTestCluster> StartClusterAsync(CancellationToken token)
    {
        PostgresTestClusterOptions defaults = await IntegrationEnvironment.CreateOptionsAsync(token);
        PostgresTestCluster cluster = await PostgresTestCluster.StartAsync(new()
        {
            Installation = defaults.Installation,
            DataDirectoryBase = defaults.DataDirectoryBase,
            LogDirectory = defaults.LogDirectory,
            StartupTimeout = defaults.StartupTimeout,
            PostgreSqlConfiguration = [.. defaults.PostgreSqlConfiguration,
                "max_prepared_transactions=8", "max_worker_processes=8", "max_parallel_workers=4"],
        }, token);
        try
        {
            await using NpgsqlConnection connection = await cluster.OpenConnectionAsync(token);
            await ExecuteAsync(connection,
                "CREATE SCHEMA datatype; CREATE EXTENSION ankus_test WITH SCHEMA datatype; CREATE TABLE callback_prepared(value integer)", token);
            return cluster;
        }
        catch
        {
            await cluster.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// Begins a real transaction with a durable-write candidate and the selected preparation callbacks.
    /// </summary>
    private static Task BeginAsync(NpgsqlConnection connection, int mode, CancellationToken token)
        => ExecuteAsync(connection, "BEGIN; INSERT INTO callback_prepared VALUES (42); SELECT datatype.prepared_callback_register(" +
            mode.ToString(CultureInfo.InvariantCulture) + ")", token);

    /// <summary>
    /// Checks the durable prepared identity and its original database and role.
    /// </summary>
    private static async Task AssertPreparedAsync(NpgsqlConnection connection, string identity, CancellationToken token)
        => Assert.AreEqual(1L, await ScalarAsync<long>(connection,
            $"SELECT count(*) FROM pg_prepared_xacts WHERE gid='{identity}' AND owner=current_user AND database=current_database()", token));

    /// <summary>
    /// Checks every write from the body and pre-prepare callback after resolution.
    /// </summary>
    private static async Task AssertRowsAsync(NpgsqlConnection connection, bool commit, CancellationToken token)
        => Assert.AreSequenceEqual(commit ? [42, 43] : [],
            await ScalarAsync<int[]>(connection, "SELECT ARRAY(SELECT value FROM callback_prepared ORDER BY value)", token));

    /// <summary>
    /// Requires a new ordinary transaction's callbacks and a healthy SQL result on the same connection.
    /// </summary>
    private static async Task AssertHealthyAsync(NpgsqlConnection connection, CancellationToken token)
    {
        Assert.AreEqual(connection.ProcessID, await ScalarAsync<int>(connection, "SELECT pg_backend_pid()", token));
        Assert.AreEqual(42, await ScalarAsync<int>(connection, "SELECT 42", token));
        await ExecuteAsync(connection, "SELECT datatype.transaction_callback_reset()", token);
        Assert.IsTrue(await ScalarAsync<bool>(connection, "SELECT datatype.transaction_callback_register_outer(true,false,false,false)", token));
        Assert.AreEqual("pre1:1,commit|False,False,False,False,null,null,null,null,null,null",
            await ScalarAsync<string>(connection, "SELECT datatype.transaction_callback_state()", token));
    }

    /// <summary>
    /// Reads every scanned value while independently retaining the actual evaluating process identities.
    /// </summary>
    private static async Task ReadParallelRowsAsync(NpgsqlConnection connection, string identity, int mode,
        HashSet<int> workers, CancellationToken token)
    {
        await using var command = new NpgsqlCommand("SELECT datatype.parallel_callback_value(value, '" + identity + "', " +
            mode.ToString(CultureInfo.InvariantCulture) + "), value FROM callback_parallel_input", connection);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
        var values = new HashSet<int>();
        while (await reader.ReadAsync(token))
        {
            int process = reader.GetInt32(0);
            Assert.AreNotEqual(connection.ProcessID, process);
            _ = workers.Add(process);
            int value = reader.GetInt32(1);
            Assert.IsInRange(1, 30000, value);
            Assert.IsTrue(values.Add(value));
        }

        Assert.HasCount(30000, values);
        Assert.IsNotEmpty(workers);
    }

    /// <summary>
    /// Waits for this owned server to accept a fresh session after its crash recovery.
    /// </summary>
    private static async Task<NpgsqlConnection> RecoverAsync(PostgresTestCluster cluster, CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                return await cluster.OpenConnectionAsync(token);
            }
            catch (NpgsqlException error) when (error is not PostgresException || error is PostgresException
                { SqlState: PostgresErrorCodes.CannotConnectNow or PostgresErrorCodes.AdminShutdown or PostgresErrorCodes.CrashShutdown })
            {
                await Task.Delay(50, token);
            }
        }
    }

    /// <summary>
    /// Removes only this test's prepared transaction, independently of test cancellation.
    /// </summary>
    private static async Task RollbackPreparedAsync(NpgsqlConnection connection, string identity)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        if (await PreparedCountAsync(connection, identity, deadline.Token) != 0)
        {
            await ExecuteAsync(connection, $"ROLLBACK PREPARED '{identity}'", deadline.Token);
        }
    }

    /// <summary>
    /// Counts only the prepared transaction owned by this test invocation.
    /// </summary>
    private static Task<long> PreparedCountAsync(NpgsqlConnection connection, string identity, CancellationToken token)
        => ScalarAsync<long>(connection, $"SELECT count(*) FROM pg_prepared_xacts WHERE gid='{identity}'", token);

    /// <summary>
    /// Reads exact managed preparation events and receipt states from the originating backend.
    /// </summary>
    private static Task<string> StateAsync(NpgsqlConnection connection, CancellationToken token)
        => ScalarAsync<string>(connection, "SELECT datatype.prepared_callback_state()", token);

    /// <summary>
    /// Executes transaction control or fixture setup on a particular owned connection.
    /// </summary>
    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, CancellationToken token)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(token);
    }

    /// <summary>
    /// Reads a strongly typed observation without accepting database null or an unexpected representation.
    /// </summary>
    private static async Task<T> ScalarAsync<T>(NpgsqlConnection connection, string sql, CancellationToken token)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return Assert.IsInstanceOfType<T>(await command.ExecuteScalarAsync(token));
    }
}
