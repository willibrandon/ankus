using System.Globalization;
using Ankus.PgConfig;

namespace Ankus.Testing;

/// <summary>
/// Reads the child-only selections supplied by ankus test without changing direct fixture defaults.
/// </summary>
internal static class TestCommandContext
{
    /// <summary>
    /// Gets the requested publication configuration, or Release for a direct fixture invocation.
    /// </summary>
    internal static string Configuration => Environment.GetEnvironmentVariable("ANKUS_TEST_CONFIGURATION") ?? "Release";

    /// <summary>
    /// Gets the command-owned temporary root, or null outside ankus test.
    /// </summary>
    internal static string? SessionDirectory
        => ReadDirectory("ANKUS_TEST_SESSION_DIRECTORY");

    /// <summary>
    /// Gets the command-owned data root while retaining the separate temporary socket and publication root.
    /// </summary>
    internal static string? DataDirectory
    {
        get
        {
            string? session = SessionDirectory;
            string? data = ReadDirectory("ANKUS_TEST_DATA_DIRECTORY");
            if (data is not null && session is null)
            {
                throw new InvalidOperationException("The ankus test data directory requires an existing command session.");
            }

            return data ?? (session is null ? null : Path.Combine(session, "pgdata"));
        }
    }

    private static string? ReadDirectory(string variable)
    {
        string? directory = Environment.GetEnvironmentVariable(variable);
        if (directory is not null && (!Path.IsPathFullyQualified(directory) || !Directory.Exists(directory)))
        {
            throw new InvalidOperationException($"{variable} must be an existing absolute directory.");
        }

        return directory;
    }

    /// <summary>
    /// Rejects fixtures that would silently execute a different PostgreSQL target within a selected test run.
    /// </summary>
    /// <param name="installation">The installation the fixture intends to use.</param>
    internal static void ValidateInstallation(PostgresInstallation installation)
    {
        string? value = Environment.GetEnvironmentVariable("ANKUS_TEST_POSTGRES_MAJOR");
        if (value is null)
        {
            return;
        }

        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int major) || major is < 13 or > 19)
        {
            throw new InvalidOperationException("ANKUS_TEST_POSTGRES_MAJOR must select PostgreSQL 13–19.");
        }

        if (installation.Version.Major != major)
        {
            throw new InvalidOperationException($"ankus test selected PostgreSQL {major}, but the fixture selected PostgreSQL {installation.Version.Major}.");
        }
    }
}
