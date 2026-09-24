using System.Runtime.InteropServices;
using Ankus.PgConfig;

namespace Ankus.IntegrationTests;

/// <summary>
/// Compiles the emitted memory bridge with bounded, test-only native allocation failures.
/// </summary>
internal static class AllocatorFaultFixtureCompiler
{
    /// <summary>
    /// Gets the declaration for the native registry failure probe.
    /// </summary>
    internal const string InstallationSql = """
        CREATE FUNCTION tests.allocator_registry_fault(integer) RETURNS text
        AS 'Ankus.AllocatorFaultFixture', 'ankus_test_allocator_registry_fault' LANGUAGE c STRICT;
        """;

    /// <summary>
    /// Extracts exact emitted bridge sections and compiles them with native test controls.
    /// </summary>
    /// <param name="installation">The installation whose backend loads the test module.</param>
    /// <param name="cancellationToken">Cancels source reads and compilation.</param>
    internal static async Task CompileAsync(PostgresInstallation installation, CancellationToken cancellationToken)
    {
        string root = IntegrationEnvironment.RepositoryRoot;
        string suffix = Path.Combine(RuntimeInformation.RuntimeIdentifier, "native", "ankus", "bridge.c");
        string emittedPath = Directory.EnumerateFiles(Path.Combine(root, "tests", "Ankus.TestExtension", "obj", "Release"),
            "bridge.c", SearchOption.AllDirectories).Single(path => path.EndsWith(suffix, StringComparison.Ordinal));
        string emitted = (await File.ReadAllTextAsync(emittedPath, cancellationToken)).ReplaceLineEndings("\n");
        int preambleEnd = FindBoundary(emitted, "static inline void\nankus_read_buffer(");
        int diagnosticsStart = FindBoundary(emitted, "#include \"utils/memutils.h\"\n#include \"miscadmin.h\"\n#include \"tcop/dest.h\"");
        int memoryStart = FindBoundary(emitted, "#include <stdint.h>\n#include <stdlib.h>\n#include <string.h>\n#include \"utils/memutils.h\"");
        int memoryEnd = FindBoundary(emitted, "#include \"access/xact.h\"\n#include \"utils/snapmgr.h\"\n\n" +
            "typedef int (*AnkusTransactionManaged)");
        if (preambleEnd >= diagnosticsStart || diagnosticsStart >= memoryStart || memoryStart >= memoryEnd)
        {
            throw new InvalidOperationException("The emitted native bridge section order changed.");
        }

        string fixtures = Path.Combine(root, "tests", "Ankus.IntegrationTests", "Native");
        string prefix = await File.ReadAllTextAsync(Path.Combine(fixtures, "allocator_fault_prefix.c"), cancellationToken);
        string probe = await File.ReadAllTextAsync(Path.Combine(fixtures, "allocator_fault_probe.c"), cancellationToken);
        string source = emitted[..preambleEnd] + emitted[diagnosticsStart..memoryStart] + prefix +
            emitted[memoryStart..memoryEnd] + probe;
        string output = IntegrationEnvironment.NativeOutputDirectory;
        string sourcePath = Path.Combine(output, "allocator_fault_fixture.c");
        await File.WriteAllTextAsync(sourcePath, source, cancellationToken);
        string extension = OperatingSystem.IsWindows() ? ".dll" : OperatingSystem.IsMacOS() ? ".dylib" : ".so";
        await AllocatorFixtureCompiler.CompileModuleAsync(installation, sourcePath,
            Path.Combine(output, "Ankus.AllocatorFaultFixture" + extension), cancellationToken);
    }

    /// <summary>
    /// Requires an unambiguous emitted declaration boundary instead of accepting a partial bridge.
    /// </summary>
    /// <param name="source">The emitted production native source.</param>
    /// <param name="boundary">The exact declaration beginning a section.</param>
    /// <returns>The unique boundary's position.</returns>
    private static int FindBoundary(string source, string boundary)
    {
        int position = source.IndexOf(boundary, StringComparison.Ordinal);
        if (position < 0 || source.IndexOf(boundary, position + boundary.Length, StringComparison.Ordinal) >= 0)
        {
            throw new InvalidOperationException($"Expected one emitted native bridge boundary: {boundary}");
        }

        return position;
    }
}
