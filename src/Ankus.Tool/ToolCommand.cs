using System.CommandLine;
using System.Runtime.InteropServices;
using Ankus.PgConfig;

namespace Ankus.Tool;

/// <summary>
/// Defines the command-line interface for PostgreSQL registration and native extension builds.
/// </summary>
internal static class ToolCommand
{
    /// <summary>
    /// Parses and executes one command, reporting failures with a nonzero exit code.
    /// </summary>
    /// <param name="arguments">The command-line tokens.</param>
    /// <returns>The command exit code.</returns>
    internal static async Task<int> RunAsync(string[] arguments)
    {
        var home = new Option<string?>("--home")
        {
            Description = "Ankus home directory (default: ~/.ankus).",
            Recursive = true,
        };
        var root = new RootCommand("Build and manage .NET PostgreSQL extensions.") { home };
        root.Subcommands.Add(CreateInit(home));
        root.Subcommands.Add(CreateInfo(home));
        root.Subcommands.Add(CreateNew());
        root.Subcommands.Add(CreateBuild("build", "Build the native extension and SQL files.", home));
        root.Subcommands.Add(CreateBuild("publish", "Publish the native extension and SQL files to a directory.", home));
        root.Subcommands.Add(CreateInstall(home));
        try
        {
            return await root.Parse(arguments).InvokeAsync(new InvocationConfiguration
            {
                EnableDefaultExceptionHandler = false,
                ProcessTerminationTimeout = TimeSpan.FromSeconds(30),
            });
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Ankus: command canceled.");
            return 130;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"Ankus: {error.Message}");
            return 1;
        }
    }

    private static Command CreateNew()
    {
        var command = new Command("new", "Create an extension solution with managed and PostgreSQL tests.");
        var name = new Argument<string>("name") { Description = "C# project name, such as Acme.Search." };
        var output = new Option<string?>("--output", "-o") { Description = "New directory (default: the project name)." };
        var extension = new Option<string?>("--extension-name") { Description = "SQL extension name (default: snake_case project name)." };
        command.Arguments.Add(name);
        command.Options.Add(output);
        command.Options.Add(extension);
        command.SetAction(async (result, token) =>
        {
            string path = await ProjectScaffolder.CreateAsync(result.GetValue(name)!, result.GetValue(output), result.GetValue(extension), token);
            Console.WriteLine($"Created extension solution at {path}");
            Console.WriteLine("Run dotnet test from that directory to build and test the extension in PostgreSQL 18.");
            Console.WriteLine("Run ankus publish to publish using your registered PostgreSQL installation.");
        });
        return command;
    }

    private static Command CreateInit(Option<string?> home)
    {
        var command = new Command("init", "Register installed PostgreSQL versions for extension development.");
        var versions = new Dictionary<int, Option<string?>>();
        for (int major = 13; major <= 19; major++)
        {
            var option = new Option<string?>($"--pg{major}") { Description = $"Path to PostgreSQL {major}'s pg_config executable." };
            versions.Add(major, option);
            command.Options.Add(option);
        }

        command.SetAction(async (result, token) =>
        {
            var paths = new Dictionary<int, string>();
            foreach ((int major, Option<string?> option) in versions)
            {
                if (result.GetValue(option) is string path)
                {
                    paths.Add(major, path);
                }
            }

            var registry = new PostgresRegistry(result.GetValue(home));
            IReadOnlyList<PostgresInstallation> installations = await registry.RegisterAsync(paths, token);
            foreach (PostgresInstallation installation in installations)
            {
                Console.WriteLine($"Registered {installation.Label}: {installation.PgConfigPath}");
            }

            return 0;
        });
        return command;
    }

    private static Command CreateInfo(Option<string?> home)
    {
        var command = new Command("info", "Show the selected PostgreSQL installation.");
        AddSelectionOptions(command);
        command.SetAction(async (result, token) =>
        {
            PostgresInstallation installation = await SelectAsync(result, home, token);
            Console.WriteLine($"PostgreSQL: {installation.Version}");
            Console.WriteLine($"pg_config: {installation.PgConfigPath}");
            Console.WriteLine($"Binaries: {installation.BinDirectory}");
            Console.WriteLine($"Libraries: {installation.LibraryDirectory}");
            Console.WriteLine($"Shared files: {installation.SharedDirectory}");
            Console.WriteLine($"Server headers: {installation.ServerIncludeDirectory}");
            return 0;
        });
        return command;
    }

    private static Command CreateBuild(string name, string description, Option<string?> home)
    {
        var command = new Command(name, description);
        AddSelectionOptions(command);
        AddBuildOptions(command);
        command.Options.Add(new Option<string?>("--output", "-o") { Description = "Publish directory." });
        command.SetAction(async (result, token) =>
        {
            PostgresInstallation installation = await SelectAsync(result, home, token);
            string output = GetOutputDirectory(result, installation);
            return await PublishAsync(result, installation, output, token);
        });
        return command;
    }

    private static Command CreateInstall(Option<string?> home)
    {
        var command = new Command("install", "Build and copy an extension into a PostgreSQL installation.");
        AddSelectionOptions(command);
        AddBuildOptions(command);
        var from = new Option<string?>("--from") { Description = "Install an existing publish directory without rebuilding." };
        var destdir = new Option<string?>("--destdir")
        {
            Description = "Stage files under this root, preserving PostgreSQL's installation paths.",
        };
        command.Options.Add(from);
        command.Options.Add(destdir);
        command.SetAction(async (result, token) =>
        {
            if (result.GetValue(from) is not null && result.GetValue<string?>("--project") is not null)
            {
                throw new ArgumentException("Use either --from or --project.");
            }

            PostgresInstallation installation = await SelectAsync(result, home, token);
            string? source = result.GetValue(from);
            if (source is null)
            {
                source = GetOutputDirectory(result, installation);
                int exitCode = await PublishAsync(result, installation, source, token);
                if (exitCode != 0)
                {
                    return exitCode;
                }
            }

            foreach (string path in ExtensionInstaller.Install(source, installation, result.GetValue(destdir), token))
            {
                Console.WriteLine($"Installed {path}");
            }

            return 0;
        });
        return command;
    }

    private static void AddSelectionOptions(Command command)
    {
        command.Options.Add(new Option<int>("--pg")
        {
            Description = "PostgreSQL major version (13–19).",
            DefaultValueFactory = _ => 18,
        });
        command.Options.Add(new Option<string?>("--pg-config")
        {
            Description = "Use this pg_config instead of a registered installation.",
        });
    }

    private static void AddBuildOptions(Command command)
    {
        command.Options.Add(new Option<string?>("--project")
        {
            Description = "Extension project or directory (default: current directory).",
        });
        command.Options.Add(new Option<string>("--configuration", "-c")
        {
            Description = "Build configuration.",
            DefaultValueFactory = _ => "Release",
        });
    }

    private static async Task<PostgresInstallation> SelectAsync(ParseResult result, Option<string?> home, CancellationToken token)
    {
        int major = result.GetValue<int>("--pg");
        ArgumentOutOfRangeException.ThrowIfLessThan(major, 13);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(major, 19);
        if (result.GetValue<string?>("--pg-config") is not string path)
        {
            return await new PostgresRegistry(result.GetValue(home)).GetAsync(major, token);
        }

        PostgresInstallation installation = await PostgresInstallation.CreateAsync(path, token);
        if (installation.Version.Major != major)
        {
            throw new ArgumentException($"Expected PostgreSQL {major}, but '{path}' is {installation.Label}.");
        }

        return installation;
    }

    private static string GetOutputDirectory(ParseResult result, PostgresInstallation installation)
    {
        string? output = result.CommandResult.Command.Name == "install" ? null : result.GetValue<string?>("--output");
        string project = ExtensionBuilder.ResolveProject(result.GetValue<string?>("--project"));
        string configuration = GetConfiguration(result);
        return Path.GetFullPath(output ?? Path.Combine(Path.GetDirectoryName(project)!, "bin", "ankus",
            installation.Label, RuntimeInformation.RuntimeIdentifier, configuration));
    }

    private static async Task<int> PublishAsync(ParseResult result, PostgresInstallation installation,
        string output, CancellationToken token)
    {
        int code = await ExtensionBuilder.PublishAsync(result.GetValue<string?>("--project"),
            GetConfiguration(result), installation, output, token);
        if (code == 0)
        {
            Console.WriteLine($"Published {installation.Label} extension to {output}");
        }

        return code;
    }

    private static string GetConfiguration(ParseResult result)
    {
        string configuration = result.GetValue<string>("--configuration")!;
        if (configuration is not ("Debug" or "Release"))
        {
            throw new ArgumentException("Configuration must be Debug or Release.");
        }

        return configuration;
    }
}
