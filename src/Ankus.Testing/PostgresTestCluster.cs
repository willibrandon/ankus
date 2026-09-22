using System.Globalization;
using System.Text;
using Ankus.PgConfig;
using Npgsql;

namespace Ankus.Testing;

/// <summary>
/// Owns a pgrx-style, per-invocation PostgreSQL cluster and runs backend tests in rollback-only transactions.
/// Dispose the cluster after all parallel test sessions finish. Normal process exit also attempts shutdown.
/// </summary>
public sealed class PostgresTestCluster : IAsyncDisposable
{
    private readonly PostgresTestClusterOptions _options;
    private readonly IReadOnlyDictionary<string, string?> _environment;
    private readonly object _shutdownLock = new();
    private Task? _shutdownTask;
    private bool _startAttempted;

    private PostgresTestCluster(PostgresTestClusterOptions options, int port)
    {
        _options = options;
        _environment = new Dictionary<string, string?>(options.ProcessEnvironment);
        Port = port;
        string invocation = $"{options.Installation.Version.Major}-{Environment.ProcessId}-{Guid.NewGuid():N}";
        DataDirectory = Path.GetFullPath(Path.Combine(options.DataDirectoryBase, invocation));
        LogFilePath = Path.GetFullPath(Path.Combine(options.LogDirectory, $"{invocation}.log"));
        SocketDirectory = OperatingSystem.IsWindows()
            ? null
            : Path.Combine(OperatingSystem.IsMacOS() ? "/tmp" : Path.GetTempPath(), $"ak-{Guid.NewGuid():N}");
    }

    /// <summary>
    /// Gets the installation whose tools and backend serve this cluster.
    /// </summary>
    public PostgresInstallation Installation => _options.Installation;

    /// <summary>
    /// Gets the unique PGDATA directory, removed after a successful shutdown.
    /// </summary>
    public string DataDirectory { get; }

    /// <summary>
    /// Gets the Unix socket directory, or null when Windows uses loopback TCP only.
    /// </summary>
    public string? SocketDirectory { get; }

    /// <summary>
    /// Gets the loopback TCP port selected for this invocation.
    /// </summary>
    public int Port { get; }

    /// <summary>
    /// Gets the retained server log path.
    /// </summary>
    public string LogFilePath { get; }

    /// <summary>
    /// Initializes a fresh cluster, waits for readiness, and creates its test database.
    /// A failed startup attempts shutdown and retains the server log in the reported diagnostic.
    /// </summary>
    /// <param name="options">The installation and invocation settings.</param>
    /// <param name="cancellationToken">Cancels startup.</param>
    /// <returns>The ready cluster, owned by the caller.</returns>
    public static async Task<PostgresTestCluster> StartAsync(
        PostgresTestClusterOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Installation);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.DatabaseName);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.UserName);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.StartupTimeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.ShutdownTimeout, TimeSpan.Zero);
        cancellationToken.ThrowIfCancellationRequested();

        using PortReservation reservation = PortReservation.Create();
        var cluster = new PostgresTestCluster(options, reservation.Port);
        AppDomain.CurrentDomain.ProcessExit += cluster.OnProcessExit;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.StartupTimeout);

        try
        {
            await cluster.InitializeAsync(reservation, timeout.Token).ConfigureAwait(false);
            return cluster;
        }
        catch (Exception error)
        {
            string log = cluster.ReadServerLog();
            try
            {
                await cluster.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception cleanupError)
            {
                throw new AggregateException($"Startup and cleanup failed. Log: {cluster.LogFilePath}\n{log}", error, cleanupError);
            }

            if (error is OperationCanceledException)
            {
                throw;
            }

            throw new InvalidOperationException(
                $"PostgreSQL startup failed: {error.Message}\nLog: {cluster.LogFilePath}\n{log}", error);
        }
    }

    /// <summary>
    /// Opens an unpooled connection to the test database. The caller owns the connection.
    /// </summary>
    /// <param name="cancellationToken">Cancels connection establishment.</param>
    /// <returns>An open connection.</returns>
    public Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
        => OpenConnectionAsync(_options.DatabaseName, "ankus-setup", cancellationToken);

    /// <summary>
    /// Executes a SQL test function inside PostgreSQL and rolls its transaction back afterward.
    /// Expected errors must match the server's primary message exactly, as in pgrx.
    /// </summary>
    /// <param name="schema">The SQL schema containing the test function.</param>
    /// <param name="functionName">The zero-argument SQL test function.</param>
    /// <param name="expectedError">An expected server error message, or null for success.</param>
    /// <param name="cancellationToken">Cancels the test.</param>
    /// <returns>A task that completes when the backend test and rollback finish.</returns>
    public Task RunTestAsync(
        string schema,
        string functionName,
        string? expectedError = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);
        ArgumentException.ThrowIfNullOrWhiteSpace(functionName);
        return RunInTransactionAsync($"{schema}.{functionName}", async (connection, transaction, token) =>
        {
            string sql = $"SELECT {QuoteIdentifier(schema)}.{QuoteIdentifier(functionName)}()";
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            try
            {
                await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }
            catch (PostgresException error) when (expectedError is not null && error.MessageText == expectedError)
            {
                return;
            }

            if (expectedError is not null)
            {
                throw new InvalidOperationException($"Expected PostgreSQL error '{expectedError}', but the test succeeded.");
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Runs a test with its own connection and transaction, rolling back on success, failure, or cancellation.
    /// The callback must not commit or replace the transaction. Parallel callbacks use independent sessions.
    /// </summary>
    /// <param name="testName">The test name used in failure diagnostics.</param>
    /// <param name="test">The test operation.</param>
    /// <param name="cancellationToken">Cancels the connection and test operation.</param>
    /// <returns>A task that completes after rollback.</returns>
    public async Task RunInTransactionAsync(
        string testName,
        Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, Task> test,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(testName);
        ArgumentNullException.ThrowIfNull(test);
        string sessionName = $"ankus-{Guid.NewGuid():N}";
        try
        {
            await using NpgsqlConnection connection = await OpenConnectionAsync(
                _options.DatabaseName, sessionName, cancellationToken).ConfigureAwait(false);
            await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await test(connection, transaction, cancellationToken).ConfigureAwait(false);
            // Npgsql's transaction disposal rolls back without the canceled test token.
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception error)
        {
            string sessionLog = ReadSessionLog(sessionName);
            throw new PostgresTestException(testName, sessionLog, error);
        }
    }

    /// <summary>
    /// Reads the server log, including startup and shutdown diagnostics.
    /// </summary>
    /// <returns>The available log text.</returns>
    public string ReadServerLog()
    {
        if (!File.Exists(LogFilePath))
        {
            return string.Empty;
        }

        using var stream = new FileStream(LogFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Stops PostgreSQL in fast mode and removes owned data and socket directories, retaining logs.
    /// Shutdown uses its own timeout. If shutdown fails, directories remain available for recovery.
    /// </summary>
    /// <returns>A task that completes after shutdown and cleanup.</returns>
    public ValueTask DisposeAsync()
    {
        lock (_shutdownLock)
        {
            if (_shutdownTask is null || _shutdownTask.IsFaulted)
            {
                _shutdownTask = StopAsync();
            }

            return new ValueTask(_shutdownTask);
        }
    }

    private async Task InitializeAsync(PortReservation reservation, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DataDirectory)!);
        Directory.CreateDirectory(Path.GetDirectoryName(LogFilePath)!);
        if (SocketDirectory is not null)
        {
            Directory.CreateDirectory(SocketDirectory);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(SocketDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }

        var arguments = new List<string>
        {
            "-D", DataDirectory, "--auth=trust", "--encoding=UTF8", "--no-sync", "--username", _options.UserName,
            "-L", _options.SharedDirectory ?? Installation.SharedDirectory,
        };
        arguments.AddRange(await PostgresLocale.GetInitDbArgumentsAsync(_environment, cancellationToken).ConfigureAwait(false));
        await ProcessRunner.RunCheckedAsync(Installation.InitDbPath, arguments, _environment, cancellationToken).ConfigureAwait(false);

        var configuration = new StringBuilder();
        configuration.AppendLine("log_min_messages = info");
        configuration.AppendLine("log_min_duration_statement = 1000");
        configuration.AppendLine("log_statement = 'all'");
        foreach (string setting in _options.PostgreSqlConfiguration)
        {
            configuration.AppendLine(setting);
        }

        // Routing and diagnostic identity belong to the harness, independent of extension settings.
        configuration.AppendLine(CultureInfo.InvariantCulture, $"port = {Port}");
        configuration.AppendLine("listen_addresses = '127.0.0.1'");
        configuration.AppendLine(CultureInfo.InvariantCulture,
            $"unix_socket_directories = {QuoteSetting(SocketDirectory ?? string.Empty)}");
        configuration.AppendLine("log_destination = 'stderr'");
        configuration.AppendLine("logging_collector = off");
        configuration.AppendLine("log_line_prefix = '[%m] [%p] [%c] [%a]: '");
        await File.WriteAllTextAsync(
            Path.Combine(DataDirectory, "postgresql.auto.conf"), configuration.ToString(), cancellationToken).ConfigureAwait(false);

        reservation.Dispose();
        _startAttempted = true;
        await ProcessRunner.RunCheckedAsync(
            Installation.PgCtlPath,
            ["start", "-D", DataDirectory, "-l", LogFilePath, "-w", "-t", GetTimeoutSeconds(_options.StartupTimeout)],
            _environment,
            cancellationToken,
            captureOutput: !OperatingSystem.IsWindows()).ConfigureAwait(false);

        await using NpgsqlConnection connection = await OpenConnectionAsync("postgres", "ankus-setup", cancellationToken)
            .ConfigureAwait(false);
        await using var command = new NpgsqlCommand($"CREATE DATABASE {QuoteIdentifier(_options.DatabaseName)}", connection);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync(string database, string sessionName, CancellationToken cancellationToken)
    {
        lock (_shutdownLock)
        {
            ObjectDisposedException.ThrowIf(_shutdownTask is not null, this);
        }

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = "127.0.0.1",
            Port = Port,
            Database = database,
            Username = _options.UserName,
            Pooling = false,
            IncludeErrorDetail = true,
            Enlist = false,
            ApplicationName = sessionName,
            Timeout = 10,
            CommandTimeout = 30,
            SslMode = SslMode.Disable,
        };
        var connection = new NpgsqlConnection(builder.ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task StopAsync()
    {
        if (_startAttempted)
        {
            using var timeout = new CancellationTokenSource(_options.ShutdownTimeout);
            ProcessResult status = await ProcessRunner.RunAsync(
                Installation.PgCtlPath, ["status", "-D", DataDirectory], _environment, timeout.Token).ConfigureAwait(false);
            if (status.ExitCode == 0)
            {
                await ProcessRunner.RunCheckedAsync(
                    Installation.PgCtlPath,
                    ["stop", "-D", DataDirectory, "-m", "fast", "-w", "-t", GetTimeoutSeconds(_options.ShutdownTimeout)],
                    _environment,
                    timeout.Token).ConfigureAwait(false);
            }
            else if (status.ExitCode != 3)
            {
                status.EnsureSuccess(Installation.PgCtlPath, ["status", "-D", DataDirectory]);
            }
        }

        if (Directory.Exists(DataDirectory))
        {
            Directory.Delete(DataDirectory, recursive: true);
        }

        if (SocketDirectory is not null && Directory.Exists(SocketDirectory))
        {
            Directory.Delete(SocketDirectory, recursive: true);
        }

        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
    }

    private void OnProcessExit(object? sender, EventArgs arguments)
    {
        try
        {
            DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"PostgreSQL cleanup failed for '{DataDirectory}': {error}");
        }
    }

    private string ReadSessionLog(string sessionName)
    {
        string marker = $"[{sessionName}]: ";
        var result = new StringBuilder();
        bool include = false;
        foreach (string line in ReadServerLog().Split('\n'))
        {
            if (line.StartsWith('['))
            {
                include = line.Contains(marker, StringComparison.Ordinal);
            }

            if (include)
            {
                result.AppendLine(line);
            }
        }

        return result.ToString();
    }

    private static string QuoteIdentifier(string value) => $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private static string QuoteSetting(string value)
        => $"'{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "''", StringComparison.Ordinal)}'";

    private static string GetTimeoutSeconds(TimeSpan timeout)
        => Math.Ceiling(timeout.TotalSeconds).ToString(CultureInfo.InvariantCulture);
}
