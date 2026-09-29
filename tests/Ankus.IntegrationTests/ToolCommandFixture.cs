using System.Collections.Concurrent;
using System.Globalization;
using Ankus.PgConfig;
using Ankus.Testing;

namespace Ankus.IntegrationTests;

/// <summary>
/// Bounds package-consumer concurrency and isolates pre-18 extension control files for each active test.
/// </summary>
public sealed partial class ToolCommandTests
{
    private static readonly int s_concurrentCases = ReadConcurrentCases();

    private static readonly SemaphoreSlim s_caseSlots = new(s_concurrentCases, s_concurrentCases);
    private static readonly SemaphoreSlim s_sampleProjectLock = new(1, 1);
    private static readonly ConcurrentQueue<PostgresTestInstallation> s_caseInstallations = new();

    private PostgresTestInstallation? _caseInstallation;
    private bool _ownsCaseSlot;

    /// <summary>
    /// Reserves an independent package-consumer slot before executing a test.
    /// </summary>
    [TestInitialize]
    public async Task ReserveCaseAsync()
    {
        await s_caseSlots.WaitAsync(context.CancellationToken);
        _ownsCaseSlot = true;
        if (s_installation.Version.Major < 18 && !s_caseInstallations.TryDequeue(out _caseInstallation))
        {
            throw new InvalidOperationException("The reserved package-consumer slot has no PostgreSQL installation.");
        }
    }

    /// <summary>
    /// Returns the installation only after the test's clusters and child processes have stopped.
    /// </summary>
    [TestCleanup]
    public void ReleaseCase()
    {
        if (!_ownsCaseSlot)
        {
            return;
        }

        if (_caseInstallation is not null)
        {
            s_caseInstallations.Enqueue(_caseInstallation);
            _caseInstallation = null;
        }

        _ownsCaseSlot = false;
        s_caseSlots.Release();
    }

    private static async Task InitializeCaseInstallationsAsync(CancellationToken token)
    {
        if (s_installation.Version.Major >= 18)
        {
            return;
        }

        for (int index = 0; index < s_concurrentCases; index++)
        {
            string root = Path.Combine(s_root, "postgres " + Guid.NewGuid().ToString("N"));
            s_caseInstallations.Enqueue(await PostgresTestInstallation.StageAsync(s_installation, root, token));
        }
    }

    private static int ReadConcurrentCases()
    {
        string? value = Environment.GetEnvironmentVariable("ANKUS_PACKAGE_TEST_CONCURRENCY");
        if (string.IsNullOrWhiteSpace(value))
        {
            return 3;
        }

        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int concurrency) || concurrency < 1)
        {
            throw new InvalidOperationException("ANKUS_PACKAGE_TEST_CONCURRENCY must be a positive integer.");
        }

        return concurrency;
    }

    private PostgresInstallation PrepareCaseInstallation(string output)
    {
        if (s_installation.Version.Major >= 18)
        {
            return s_installation;
        }

        PostgresTestInstallation installation = _caseInstallation
            ?? throw new InvalidOperationException("The package-consumer test has no reserved PostgreSQL installation.");
        installation.InstallExtensionFiles(output);
        return installation.Installation;
    }
}
