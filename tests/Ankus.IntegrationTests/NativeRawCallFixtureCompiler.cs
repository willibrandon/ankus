using System.Globalization;
using Ankus.PgConfig;
using Ankus.Testing;

namespace Ankus.IntegrationTests;

/// <summary>
/// Compiles actual generated call bodies into a native fixture used by published managed callbacks.
/// </summary>
internal static class NativeRawCallFixtureCompiler
{
    /// <summary>
    /// Gets the SQL entry points for native body addresses, backend state and deliberate errors.
    /// </summary>
    internal const string InstallationSql = """
        CREATE FUNCTION tests.raw_call_address(integer) RETURNS bigint
        AS 'Ankus.RawCallFixture', 'ankus_test_raw_call_address' LANGUAGE c STRICT;
        CREATE FUNCTION tests.raw_call_holdoffs() RETURNS bigint
        AS 'Ankus.RawCallFixture', 'ankus_test_raw_call_holdoffs' LANGUAGE c STRICT;
        CREATE FUNCTION tests.raw_call_error(integer) RETURNS integer
        AS 'Ankus.RawCallFixture', 'ankus_test_raw_call_error' LANGUAGE c STRICT;
        CREATE FUNCTION tests.raw_call_control(integer) RETURNS bigint
        AS 'Ankus.RawCallFixture', 'ankus_test_raw_call_control' LANGUAGE c STRICT;
        """;

    /// <summary>
    /// Generates selected-header bodies with the production command and compiles their complete native implementation.
    /// </summary>
    /// <param name="installation">The same installation used by the test backend.</param>
    /// <param name="cancellationToken">Cancels collection and compilation.</param>
    internal static async Task CompileAsync(PostgresInstallation installation, CancellationToken cancellationToken)
    {
        string output = IntegrationEnvironment.NativeOutputDirectory;
        string temporary = Path.Combine(output, "raw-call-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            string symbols = Path.Combine(temporary, "symbols.txt");
            await File.WriteAllTextAsync(symbols, "FullTransactionIdFromU64\npg_strtoint32\nOidFunctionCall1Coll\nProcessInterrupts\npfree\n", cancellationToken);
            string helper = Path.Combine(IntegrationEnvironment.RepositoryRoot, "src", "Ankus.Build", "bin", "Release", "net10.0", "Ankus.Build.dll");
            await ProcessRunner.RunCheckedAsync("dotnet",
                [helper, "binding-call-sources", symbols, installation.Version.Major.ToString(CultureInfo.InvariantCulture), installation.PgConfigPath, temporary],
                new Dictionary<string, string?>(), cancellationToken);
            string fixture = await File.ReadAllTextAsync(Path.Combine(IntegrationEnvironment.RepositoryRoot,
                "tests", "Ankus.IntegrationTests", "Native", "raw_call_fixture.c"), cancellationToken);
            string generated = await File.ReadAllTextAsync(Path.Combine(temporary, "native-calls.c"), cancellationToken);
            string source = Path.Combine(output, "raw_call_fixture.c");
            await File.WriteAllTextAsync(source, generated + "\n" + fixture, cancellationToken);
            string extension = OperatingSystem.IsWindows() ? ".dll" : OperatingSystem.IsMacOS() ? ".dylib" : ".so";
            await AllocatorFixtureCompiler.CompileModuleAsync(installation, source,
                Path.Combine(output, "Ankus.RawCallFixture" + extension), true, cancellationToken);
        }
        finally
        {
            Directory.Delete(temporary, recursive: true);
        }
    }
}
