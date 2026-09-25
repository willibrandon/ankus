using Ankus.PgConfig;
using Ankus.Testing;

namespace Ankus.IntegrationTests;

/// <summary>
/// Compiles test-only native allocator constructors against the cluster's selected PostgreSQL headers.
/// </summary>
internal static class AllocatorFixtureCompiler
{
    /// <summary>
    /// Gets SQL that installs the native fixture functions in an existing tests schema.
    /// </summary>
    internal const string InstallationSql = """
        CREATE FUNCTION tests.function_address(regprocedure) RETURNS bigint
        AS 'Ankus.AllocatorFixture', 'ankus_test_function_address' LANGUAGE c STRICT;
        CREATE FUNCTION tests.native_nullable_sum(integer, integer) RETURNS integer
        AS 'Ankus.AllocatorFixture', 'ankus_test_nullable_sum' LANGUAGE c;
        CREATE FUNCTION tests.internal_invoke(regprocedure, integer) RETURNS bigint
        AS 'Ankus.AllocatorFixture', 'ankus_test_internal_invoke' LANGUAGE c STRICT;
        CREATE FUNCTION tests.internal_set_invoke(regprocedure, regprocedure, boolean, boolean) RETURNS integer
        AS 'Ankus.AllocatorFixture', 'ankus_test_internal_set_invoke' LANGUAGE c STRICT;
        CREATE FUNCTION tests.allocator_create(integer, regprocedure, text) RETURNS integer
        AS 'Ankus.AllocatorFixture', 'ankus_test_allocator_create' LANGUAGE c STRICT;
        CREATE FUNCTION tests.allocator_delete() RETURNS void
        AS 'Ankus.AllocatorFixture', 'ankus_test_allocator_delete' LANGUAGE c;
        CREATE FUNCTION tests.allocator_flags() RETURNS integer
        AS 'Ankus.AllocatorFixture', 'ankus_test_allocator_flags' LANGUAGE c;
        CREATE FUNCTION tests.stringinfo_cursor(bigint, integer) RETURNS integer
        AS 'Ankus.AllocatorFixture', 'ankus_test_stringinfo_cursor' LANGUAGE c STRICT;
        CREATE FUNCTION tests.stringinfo_borrow(regprocedure, integer) RETURNS text
        AS 'Ankus.AllocatorFixture', 'ankus_test_stringinfo_borrow' LANGUAGE c STRICT;
        """;

    /// <summary>
    /// Builds the fixture with the host C compiler without adding any production exports.
    /// </summary>
    /// <param name="installation">The installation used to start the backend.</param>
    /// <param name="cancellationToken">Cancels the compiler process.</param>
    internal static async Task BuildAsync(PostgresInstallation installation, CancellationToken cancellationToken)
    {
        string source = Path.Combine(IntegrationEnvironment.RepositoryRoot, "tests", "Ankus.IntegrationTests", "Native", "allocator_fixture.c");
        string extension = OperatingSystem.IsWindows() ? ".dll" : OperatingSystem.IsMacOS() ? ".dylib" : ".so";
        await CompileModuleAsync(installation, source,
            Path.Combine(IntegrationEnvironment.NativeOutputDirectory, "Ankus.AllocatorFixture" + extension), cancellationToken);
    }

    /// <summary>
    /// Compiles one standalone PostgreSQL test module using the selected installation's public headers.
    /// </summary>
    /// <param name="installation">The installation providing native headers and import libraries.</param>
    /// <param name="source">The complete C translation unit.</param>
    /// <param name="outputPath">The resulting shared library path.</param>
    /// <param name="cancellationToken">Cancels compilation.</param>
    internal static async Task CompileModuleAsync(PostgresInstallation installation, string source, string outputPath, CancellationToken cancellationToken)
    {
        string output = Path.GetDirectoryName(outputPath) ?? throw new ArgumentException("The module needs an output directory.", nameof(outputPath));
        Directory.CreateDirectory(output);
        List<string> arguments;
        string compiler;
        if (OperatingSystem.IsWindows())
        {
            compiler = "cl.exe";
            arguments = ["/nologo", "/LD", "/O2", "/MD", "/WX",
                "/I" + installation.ServerIncludeDirectory,
                "/I" + installation.IncludeDirectory,
                "/I" + Path.Combine(installation.ServerIncludeDirectory, "port", "win32"),
                "/I" + Path.Combine(installation.ServerIncludeDirectory, "port", "win32_msvc"),
                "/Fo" + Path.ChangeExtension(outputPath, ".obj"), source, "/link",
                "/OUT:" + outputPath,
                Path.Combine(installation.LibraryDirectory, "postgres.lib")];
        }
        else
        {
            compiler = "cc";
            arguments = [.. installation.PreprocessorArguments,
                "-O2", "-fPIC", "-Wall", "-Wextra", "-Werror",
                "-isystem", installation.ServerIncludeDirectory,
                "-isystem", installation.IncludeDirectory];
            if (OperatingSystem.IsMacOS())
            {
                arguments.AddRange(["-dynamiclib", "-Wl,-undefined,dynamic_lookup"]);
            }
            else
            {
                arguments.Add("-shared");
            }

            arguments.AddRange(["-o", outputPath, source]);
        }

        await ProcessRunner.RunCheckedAsync(compiler, arguments, new Dictionary<string, string?>(), cancellationToken,
            workingDirectory: output);
    }
}
