using System.Globalization;
using System.Text;

namespace Ankus.PgConfig;

public sealed partial class PostgresDevelopmentCluster
{
    /// <summary>
    /// Gets a libpq connection string for the running cluster's actual port and an exact database name.
    /// This does not start the server or create the database.
    /// </summary>
    /// <param name="database">The literal database name, not a connection string.</param>
    /// <param name="cancellationToken">Cancels status and port discovery.</param>
    /// <returns>Connection settings suitable for psql or another libpq client.</returns>
    public async Task<string> GetConnectionStringAsync(string database, CancellationToken cancellationToken = default)
    {
        ValidateDatabaseName(database);
        if (!await IsRunningAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("The development server is stopped. Run 'ankus start' first.");
        }

        // PostgreSQL writes the active TCP port on line four, independently of later configuration edits.
        string[] lines = await File.ReadAllLinesAsync(Path.Combine(DataDirectory, "postmaster.pid"), cancellationToken).ConfigureAwait(false);
        if (lines.Length < 4 || !int.TryParse(lines[3], NumberStyles.None, CultureInfo.InvariantCulture, out int port) || port is < 1 or > 65535)
        {
            throw new FormatException("The running PostgreSQL server has no valid TCP port in postmaster.pid.");
        }

        return CreateConnectionString(port, database);
    }

    /// <summary>
    /// Creates a database in the running cluster, or reuses an existing database without modifying it.
    /// Names retain their exact UTF-8 spelling; names exceeding the server's identifier limit are rejected.
    /// </summary>
    /// <param name="database">The literal database name.</param>
    /// <param name="cancellationToken">Cancels database queries and creation.</param>
    /// <returns>True when a database was created, or false when it already existed.</returns>
    public async Task<bool> CreateDatabaseAsync(string database, CancellationToken cancellationToken = default)
    {
        int byteCount = ValidateDatabaseName(database);
        string connection = await GetConnectionStringAsync("postgres", cancellationToken).ConfigureAwait(false);
        using FileStream operationLock = AcquireLock();
        // Windows native argv may use an ANSI code page. Send names as UTF-8 SQL input,
        // and use percent-encoded connection parameters rather than Unicode arguments.
        string[] arguments = ["--no-psqlrc", "--no-password", "--quiet", "--tuples-only", "--no-align",
            "--set=ON_ERROR_STOP=1", "--dbname=" + connection, "--file=-"];
        if (await DatabaseExistsAsync(database, byteCount, arguments, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        (int code, string output) = await RunAsync(_installation.PsqlPath, arguments, cancellationToken,
            input: "CREATE DATABASE \"" + database.Replace("\"", "\"\"", StringComparison.Ordinal) + "\";\n", postgresClient: true).ConfigureAwait(false);
        RequireDatabaseCommand(code, output);
        return true;
    }

    /// <summary>
    /// Drops an exact database from the running development cluster, or leaves an absent database unchanged.
    /// Other databases and the server remain running. PostgreSQL errors preserve their diagnostics.
    /// </summary>
    /// <param name="database">The literal database name. Names exceeding the server's identifier limit are rejected.</param>
    /// <param name="force">Whether PostgreSQL should terminate connections to the selected database before dropping it.</param>
    /// <param name="cancellationToken">Cancels database queries and removal.</param>
    /// <returns>True when a database was dropped, or false when it was already absent.</returns>
    public async Task<bool> DropDatabaseAsync(string database, bool force = false, CancellationToken cancellationToken = default)
    {
        int byteCount = ValidateDatabaseName(database);
        string connection = await GetConnectionStringAsync("postgres", cancellationToken).ConfigureAwait(false);
        using FileStream operationLock = AcquireLock();
        string[] arguments = ["--no-psqlrc", "--no-password", "--quiet", "--tuples-only", "--no-align",
            "--set=ON_ERROR_STOP=1", "--dbname=" + connection, "--file=-"];
        if (!await DatabaseExistsAsync(database, byteCount, arguments, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        string command = "DROP DATABASE \"" + database.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" +
            (force ? " WITH (FORCE)" : "") + ";\n";
        (int code, string output) = await RunAsync(_installation.PsqlPath, arguments, cancellationToken,
            input: command, postgresClient: true).ConfigureAwait(false);
        RequireDatabaseCommand(code, output);
        return true;
    }

    /// <summary>
    /// Formats exact database names as ASCII libpq URIs, preserving Unicode across native Windows command lines.
    /// </summary>
    internal static string CreateConnectionString(int port, string database)
    {
        ValidateDatabaseName(database);
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);
        return string.Create(CultureInfo.InvariantCulture,
            $"postgresql://postgres@127.0.0.1:{port}/{Uri.EscapeDataString(database)}?hostaddr=127.0.0.1&connect_timeout=10");
    }

    private static int ValidateDatabaseName(string database)
    {
        ArgumentException.ThrowIfNullOrEmpty(database);
        if (database.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException("Database names cannot contain NUL.", nameof(database));
        }

        return new UTF8Encoding(false, true).GetByteCount(database);
    }

    private async Task<bool> DatabaseExistsAsync(string database, int byteCount, string[] arguments, CancellationToken cancellationToken)
    {
        string literal = "E'" + database.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "''", StringComparison.Ordinal) + "'";
        (int code, string output) = await RunAsync(_installation.PsqlPath, arguments, cancellationToken,
            input: $"SELECT current_setting('max_identifier_length'), EXISTS (SELECT FROM pg_catalog.pg_database WHERE datname = {literal});\n",
            postgresClient: true).ConfigureAwait(false);
        RequireDatabaseCommand(code, output);
        string[] fields = output.Trim().Split('|');
        if (fields.Length != 2 || !int.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out int limit) ||
            fields[1] is not ("t" or "f"))
        {
            throw new FormatException($"Unexpected PostgreSQL database metadata: {output}");
        }

        if (byteCount > limit)
        {
            throw new ArgumentException($"The database name exceeds PostgreSQL's {limit}-byte identifier limit.", nameof(database));
        }

        return fields[1] == "t";
    }

    private static void RequireDatabaseCommand(int code, string output)
    {
        if (code != 0)
        {
            throw new InvalidOperationException($"PostgreSQL database command failed ({code}): {output}");
        }
    }
}
