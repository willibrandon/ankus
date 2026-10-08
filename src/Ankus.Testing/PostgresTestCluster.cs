using System.Globalization;
using System.Runtime.ExceptionServices;
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
    private readonly PostgresTestLog _log;
    private readonly Lock _shutdownLock = new();
    private Task? _shutdownTask;

    /// <summary>
    /// Requires definitive native shutdown until pg_ctl confirms the attempted server is stopped.
    /// </summary>
    private bool _requiresShutdown;

    private PostgresTestCluster(PostgresTestClusterOptions options, int port)
    {
        _options = options;
        _environment = new Dictionary<string, string?>(options.ProcessEnvironment);
        Port = port;
        string invocation = $"{options.Installation.Version.Major}-{Environment.ProcessId}-{Guid.NewGuid():N}";
        string? session = TestCommandContext.SessionDirectory;
        DataDirectory = Path.GetFullPath(Path.Combine(TestCommandContext.DataDirectory ?? options.DataDirectoryBase, invocation));
        LogFilePath = Path.GetFullPath(Path.Combine(options.LogDirectory, $"{invocation}.log"));
        _log = new(LogFilePath);
        SocketDirectory = OperatingSystem.IsWindows()
            ? null
            : session is null
                ? Path.Combine(OperatingSystem.IsMacOS() ? "/tmp" : Path.GetTempPath(), $"ak-{Guid.NewGuid():N}")
                : Path.Combine(session, $"s-{Guid.NewGuid():N}");
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
    /// <remarks>
    /// On Windows, <see cref="ReadServerLog"/> refreshes this file with native file and Event Log messages.
    /// Disposal also refreshes the file to retain shutdown diagnostics.
    /// </remarks>
    public string LogFilePath { get; }

    /// <summary>
    /// Initializes a fresh cluster, waits for readiness, and creates its test database.
    /// A failed startup attempts shutdown and retains the server log in the reported diagnostic.
    /// </summary>
    /// <remarks>
    /// If another process claims an automatically selected port before PostgreSQL binds it, startup retries with a new
    /// cluster and port, up to three attempts within the same startup timeout. Explicit ports are never replaced;
    /// collisions and other startup failures are reported immediately.
    /// Failed attempts remove their data and socket directories and retain their server logs.
    /// </remarks>
    /// <param name="options">The installation and invocation settings.</param>
    /// <param name="cancellationToken">Cancels startup.</param>
    /// <returns>The ready cluster, owned by the caller.</returns>
    public static Task<PostgresTestCluster> StartAsync(
        PostgresTestClusterOptions options,
        CancellationToken cancellationToken = default)
        => StartAsync(options, beforeStart: null, cancellationToken);

    /// <summary>
    /// Starts a cluster with a per-invocation handoff callback for deterministic startup contention tests.
    /// </summary>
    /// <param name="options">The installation and invocation settings.</param>
    /// <param name="beforeStart">Runs before startup with the automatic-port reservation, or null for an explicit port.</param>
    /// <param name="cancellationToken">Cancels the complete initialization and readiness operation.</param>
    /// <returns>The ready cluster, owned by the caller.</returns>
    internal static async Task<PostgresTestCluster> StartAsync(
        PostgresTestClusterOptions options,
        Action<PostgresTestCluster, PortReservation?>? beforeStart,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Installation);
        TestCommandContext.ValidateInstallation(options.Installation);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.DatabaseName);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.UserName);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.StartupTimeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.ShutdownTimeout, TimeSpan.Zero);
        if (options.Port is int port)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);
        }

        cancellationToken.ThrowIfCancellationRequested();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.StartupTimeout);
        const int MaximumAttempts = 3;
        for (int attempt = 1; ; attempt++)
        {
            timeout.Token.ThrowIfCancellationRequested();
            using PortReservation? reservation = options.Port is null ? PortReservation.Create() : null;
            int selectedPort = options.Port ?? reservation?.Port
                ?? throw new InvalidOperationException("No PostgreSQL test port was selected.");
            var cluster = new PostgresTestCluster(options, selectedPort);
            AppDomain.CurrentDomain.ProcessExit += cluster.OnProcessExit;
            try
            {
                await cluster.InitializeAsync(reservation, beforeStart, timeout.Token).ConfigureAwait(false);
                return cluster;
            }
            catch (Exception error)
            {
                string log = string.Empty;
                Exception? diagnosticFailure = null;
                try
                {
                    log = cluster.ReadServerLog();
                }
                catch (Exception logError)
                {
                    diagnosticFailure = logError;
                }

                try
                {
                    await cluster.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception cleanupError)
                {
                    Exception[] failures = diagnosticFailure is null ? [error, cleanupError] : [error, diagnosticFailure, cleanupError];
                    throw new AggregateException($"Startup and cleanup failed. Log: {cluster.LogFilePath}\n{log}", failures);
                }

                if (diagnosticFailure is not null)
                {
                    throw new AggregateException($"Startup and diagnostic collection failed. Log: {cluster.LogFilePath}", error, diagnosticFailure);
                }

                if (error is OperationCanceledException)
                {
                    throw;
                }

                if (options.Port is null && attempt < MaximumAttempts && cluster.HasPortCollision(log))
                {
                    timeout.Token.ThrowIfCancellationRequested();
                    continue;
                }

                throw new InvalidOperationException(
                    $"PostgreSQL startup failed: {error.Message}\nLog: {cluster.LogFilePath}\n{log}", error);
            }
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
        ArgumentException.ThrowIfNullOrEmpty(schema);
        ArgumentException.ThrowIfNullOrEmpty(functionName);
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
    public string ReadServerLog() => _log.Read();

    /// <summary>
    /// Stops PostgreSQL in fast mode and removes owned data and socket directories, retaining logs.
    /// Shutdown uses its own timeout. If shutdown fails, directories remain available for recovery.
    /// A later disposal retries a faulted or canceled shutdown after the native server recovers.
    /// After successful shutdown, a diagnostic read failure is reported after owned directories are removed.
    /// </summary>
    /// <returns>A task that completes after shutdown and cleanup.</returns>
    public ValueTask DisposeAsync()
    {
        lock (_shutdownLock)
        {
            if (_shutdownTask is null || _shutdownTask.IsFaulted || _shutdownTask.IsCanceled)
            {
                _shutdownTask = StopAsync();
            }

            return new ValueTask(_shutdownTask);
        }
    }

    private async Task InitializeAsync(PortReservation? reservation, Action<PostgresTestCluster, PortReservation?>? beforeStart, CancellationToken cancellationToken)
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

        beforeStart?.Invoke(this, reservation);
        reservation?.Dispose();
        cancellationToken.ThrowIfCancellationRequested();
        _requiresShutdown = true;
        string[] startupArguments =
        [
            "start", "-D", DataDirectory, "-l", _log.NativeFilePath, "-w", "-t", GetTimeoutSeconds(_options.StartupTimeout),
        ];
        if (OperatingSystem.IsWindows())
        {
            // Command-line configuration takes effect before loading configuration files,
            // retaining startup errors as well as the service token's Event Log routing.
            startupArguments = [.. startupArguments, "-o", "-c event_source=" + _log.EventSource];
        }

        await ProcessRunner.RunCheckedAsync(
            Installation.PgCtlPath,
            startupArguments,
            _environment,
            cancellationToken,
            captureOutput: !OperatingSystem.IsWindows()).ConfigureAwait(false);

        await using NpgsqlConnection connection = await OpenConnectionAsync("postgres", "ankus-setup", cancellationToken)
            .ConfigureAwait(false);
        // PostgreSQL 13 and 14 copy the template database through a checkpoint, which can exceed an ordinary
        // command timeout on a busy disk. Bootstrap shares the configured startup budget.
        await using var command = new NpgsqlCommand($"CREATE DATABASE {QuoteIdentifier(_options.DatabaseName)}", connection)
        {
            CommandTimeout = GetConnectionTimeoutSeconds(_options.StartupTimeout),
        };
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
            Timeout = GetConnectionTimeoutSeconds(_options.StartupTimeout),
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
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
        if (_requiresShutdown)
        {
            using var timeout = new CancellationTokenSource(_options.ShutdownTimeout);
            ProcessResult status = await ProcessRunner.RunAsync(
                Installation.PgCtlPath, ["status", "-D", DataDirectory], _environment, timeout.Token).ConfigureAwait(false);
            if (status.ExitCode == 0)
            {
                TimeSpan fastDuration = TimeSpan.FromTicks(Math.Min(
                    _options.ShutdownTimeout.Ticks / 2,
                    TimeSpan.FromSeconds(5).Ticks));
                using var fastTimeout = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
                fastTimeout.CancelAfter(fastDuration);
                ProcessResult? fast = null;
                try
                {
                    fast = await ProcessRunner.RunAsync(
                        Installation.PgCtlPath,
                        ["stop", "-D", DataDirectory, "-m", "fast", "-w", "-t", GetTimeoutSeconds(fastDuration)],
                        _environment,
                        fastTimeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!timeout.IsCancellationRequested)
                {
                }

                if (fast?.ExitCode != 0)
                {
                    string[] immediateArguments =
                    [
                        "stop", "-D", DataDirectory, "-m", "immediate", "-w", "-t", GetTimeoutSeconds(_options.ShutdownTimeout),
                    ];
                    ProcessResult immediate = await ProcessRunner.RunAsync(
                        Installation.PgCtlPath,
                        immediateArguments,
                        _environment,
                        timeout.Token).ConfigureAwait(false);
                    if (immediate.ExitCode != 0)
                    {
                        ProcessResult stopped = await ProcessRunner.RunAsync(
                            Installation.PgCtlPath, ["status", "-D", DataDirectory], _environment, timeout.Token).ConfigureAwait(false);
                        if (stopped.ExitCode != 3)
                        {
                            immediate.EnsureSuccess(Installation.PgCtlPath, immediateArguments);
                        }
                    }
                }
            }
            else if (status.ExitCode != 3)
            {
                status.EnsureSuccess(Installation.PgCtlPath, ["status", "-D", DataDirectory]);
            }
        }

        _requiresShutdown = false;
        var failures = new List<Exception>();
        try
        {
            _ = ReadServerLog();
        }
        catch (Exception error)
        {
            failures.Add(error);
        }

        DeleteOwnedDirectory(DataDirectory, failures);
        if (SocketDirectory is not null)
        {
            DeleteOwnedDirectory(SocketDirectory, failures);
        }

        if (failures.Count == 1)
        {
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }

        if (failures.Count > 1)
        {
            throw new AggregateException("PostgreSQL stopped, but diagnostic collection or owned-directory cleanup failed.", failures);
        }
    }

    /// <summary>
    /// Removes one stopped invocation's directory while allowing its other owned resources to be reclaimed.
    /// </summary>
    /// <param name="path">The invocation's data or socket directory.</param>
    /// <param name="failures">Receives original cleanup exceptions without replacing diagnostic failures.</param>
    private static void DeleteOwnedDirectory(string path, List<Exception> failures)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception error)
        {
            failures.Add(error);
        }
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

    private bool HasPortCollision(string log)
        => log.Contains("could not bind IPv4 address \"127.0.0.1\":", StringComparison.Ordinal) &&
            log.Contains($"Is another postmaster already running on port {Port}?", StringComparison.Ordinal) &&
            log.Contains("could not create any TCP/IP sockets", StringComparison.Ordinal);

    private static string QuoteSetting(string value)
        => $"'{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "''", StringComparison.Ordinal)}'";

    private static string GetTimeoutSeconds(TimeSpan timeout)
        => Math.Ceiling(timeout.TotalSeconds).ToString(CultureInfo.InvariantCulture);

    private static int GetConnectionTimeoutSeconds(TimeSpan timeout)
        => (int)Math.Clamp(Math.Ceiling(timeout.TotalSeconds), 1, int.MaxValue);
}
