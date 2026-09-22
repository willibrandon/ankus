using System.Diagnostics;

namespace Ankus.PgConfig;

/// <summary>
/// Describes one PostgreSQL installation by querying its authoritative
/// <c>pg_config</c> executable.
/// </summary>
public sealed class PostgresInstallation
{
    private PostgresInstallation(
        string pgConfigPath,
        PostgresVersion version,
        string binDirectory,
        string libraryDirectory,
        string sharedDirectory,
        string serverIncludeDirectory)
    {
        PgConfigPath = pgConfigPath;
        Version = version;
        BinDirectory = binDirectory;
        LibraryDirectory = libraryDirectory;
        SharedDirectory = sharedDirectory;
        ServerIncludeDirectory = serverIncludeDirectory;
    }

    /// <summary>
    /// Gets the absolute path to <c>pg_config</c>.
    /// </summary>
    public string PgConfigPath { get; }

    /// <summary>
    /// Gets the PostgreSQL version reported by this installation.
    /// </summary>
    public PostgresVersion Version { get; }

    /// <summary>
    /// Gets the installation directory containing PostgreSQL executables.
    /// </summary>
    public string BinDirectory { get; }

    /// <summary>
    /// Gets PostgreSQL's native extension library directory.
    /// </summary>
    public string LibraryDirectory { get; }

    /// <summary>
    /// Gets PostgreSQL's architecture-independent shared-data directory.
    /// </summary>
    public string SharedDirectory { get; }

    /// <summary>
    /// Gets the server header directory for compiling native extension boundaries against this installation.
    /// </summary>
    public string ServerIncludeDirectory { get; }

    /// <summary>
    /// Gets the Ankus version selector for this installation, such as <c>pg18</c>.
    /// </summary>
    public string Label => Version.Label;

    /// <summary>
    /// Gets the platform-correct path to PostgreSQL's <c>initdb</c> executable.
    /// </summary>
    public string InitDbPath => GetExecutablePath("initdb");

    /// <summary>
    /// Gets the platform-correct path to PostgreSQL's <c>pg_ctl</c> executable.
    /// </summary>
    public string PgCtlPath => GetExecutablePath("pg_ctl");

    /// <summary>
    /// Gets the platform-correct path to PostgreSQL's <c>createdb</c> executable.
    /// </summary>
    public string CreateDbPath => GetExecutablePath("createdb");

    /// <summary>
    /// Gets the platform-correct path to PostgreSQL's <c>dropdb</c> executable.
    /// </summary>
    public string DropDbPath => GetExecutablePath("dropdb");

    /// <summary>
    /// Gets the platform-correct path to PostgreSQL's <c>psql</c> executable.
    /// </summary>
    public string PsqlPath => GetExecutablePath("psql");

    /// <summary>
    /// Discovers PostgreSQL 18 from persisted configuration, managed installations, PATH, and platform installation locations.
    /// </summary>
    /// <param name="cancellationToken">Cancels <c>pg_config</c> queries.</param>
    /// <returns>The discovered PostgreSQL installation.</returns>
    public static Task<PostgresInstallation> DiscoverAsync(CancellationToken cancellationToken = default)
        => DiscoverAsync(18, cancellationToken);

    /// <summary>
    /// Discovers a specific PostgreSQL major without requiring environment variables.
    /// Nonstandard installations can be registered as pgXX paths in <c>~/.ankus/config.json</c>.
    /// </summary>
    /// <param name="major">The required PostgreSQL major version.</param>
    /// <param name="cancellationToken">Cancels installation queries.</param>
    /// <returns>The first matching installation in discovery order.</returns>
    public static async Task<PostgresInstallation> DiscoverAsync(int major, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(major, 13);
        var registry = new PostgresRegistry();
        if (registry.GetPath(major) is not null)
        {
            return await registry.GetAsync(major, cancellationToken).ConfigureAwait(false);
        }

        foreach (string candidate in PostgresDiscovery.GetCandidates(major).Distinct(StringComparer.Ordinal))
        {
            if (File.Exists(candidate))
            {
                PostgresInstallation installation = await CreateAsync(candidate, cancellationToken).ConfigureAwait(false);
                if (installation.Version.Major == major)
                {
                    return installation;
                }
            }
        }

        throw new FileNotFoundException(
            $"PostgreSQL {major} was not found. Run 'ankus init --pg{major} /path/to/pg_config'.");
    }

    /// <summary>
    /// Creates an installation descriptor by querying an explicit
    /// <c>pg_config</c> executable.
    /// </summary>
    /// <param name="pgConfigPath">Path to <c>pg_config</c>.</param>
    /// <param name="cancellationToken">Cancels <c>pg_config</c> queries.</param>
    /// <returns>The queried PostgreSQL installation.</returns>
    public static async Task<PostgresInstallation> CreateAsync(
        string pgConfigPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pgConfigPath);
        string? resolvedPath = ExecutableLocator.Find(pgConfigPath);
        if (resolvedPath is null)
        {
            throw new FileNotFoundException("The specified pg_config executable was not found.", pgConfigPath);
        }

        Task<string> version = QueryAsync(resolvedPath, "--version", cancellationToken);
        Task<string> binDirectory = QueryAsync(resolvedPath, "--bindir", cancellationToken);
        Task<string> libraryDirectory = QueryAsync(resolvedPath, "--pkglibdir", cancellationToken);
        Task<string> sharedDirectory = QueryAsync(resolvedPath, "--sharedir", cancellationToken);
        Task<string> serverIncludeDirectory = QueryAsync(resolvedPath, "--includedir-server", cancellationToken);
        await Task.WhenAll(version, binDirectory, libraryDirectory, sharedDirectory, serverIncludeDirectory).ConfigureAwait(false);

        return new PostgresInstallation(
            resolvedPath,
            PostgresVersion.Parse(await version.ConfigureAwait(false)),
            Path.GetFullPath(await binDirectory.ConfigureAwait(false)),
            Path.GetFullPath(await libraryDirectory.ConfigureAwait(false)),
            Path.GetFullPath(await sharedDirectory.ConfigureAwait(false)),
            Path.GetFullPath(await serverIncludeDirectory.ConfigureAwait(false)));
    }

    private string GetExecutablePath(string name)
    {
        string executableName = OperatingSystem.IsWindows() ? $"{name}.exe" : name;
        return Path.Combine(BinDirectory, executableName);
    }

    private static async Task<string> QueryAsync(
        string pgConfigPath,
        string argument,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = pgConfigPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        process.StartInfo.ArgumentList.Add(argument);

        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start '{pgConfigPath}'.");
        }

        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        Task<string> standardError = process.StandardError.ReadToEndAsync(CancellationToken.None);
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) when (process.HasExited)
            {
                // The query exited while cancellation was being delivered.
            }

            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            await Task.WhenAll(standardOutput, standardError).ConfigureAwait(false);
            throw;
        }
        string output = (await standardOutput.ConfigureAwait(false)).Trim();
        string error = (await standardError.ConfigureAwait(false)).Trim();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"'{pgConfigPath} {argument}' exited with code {process.ExitCode}: {error}");
        }

        if (output.Length == 0)
        {
            throw new InvalidOperationException($"'{pgConfigPath} {argument}' returned no output.");
        }

        return output;
    }
}
