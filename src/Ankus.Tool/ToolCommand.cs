using System.CommandLine;
using System.Runtime.InteropServices;
using Ankus.PgConfig;

namespace Ankus.Tool;

/// <summary>
/// Defines the command-line interface for PostgreSQL registration and native extension builds.
/// </summary>
internal static partial class ToolCommand
{
    /// <summary>
    /// Parses and executes one command, reporting failures with a nonzero exit code.
    /// </summary>
    /// <param name="arguments">The command-line tokens.</param>
    /// <returns>The command exit code.</returns>
    internal static async Task<int> RunAsync(string[] arguments)
    {
        using var interactiveCancellation = new InteractiveCommandCancellation();
        var home = new Option<string?>("--home")
        {
            Description = "Ankus home directory (default: ~/.ankus).",
            Recursive = true,
        };
        var root = new RootCommand("Build and manage .NET PostgreSQL extensions.") { home };
        root.Subcommands.Add(CreateInit(home));
        root.Subcommands.Add(CreateInfo(home));
        root.Subcommands.Add(CreateCluster("start", home));
        root.Subcommands.Add(CreateCluster("stop", home));
        root.Subcommands.Add(CreateCluster("status", home));
        root.Subcommands.Add(CreateConnect(home, interactiveCancellation));
        root.Subcommands.Add(CreateRun(home, interactiveCancellation));
        root.Subcommands.Add(CreateNew());
        root.Subcommands.Add(CreateBuild("build", "Build the native extension and SQL files.", home));
        root.Subcommands.Add(CreateBuild("publish", "Publish the native extension and SQL files to a directory.", home));
        root.Subcommands.Add(CreateInstall(home));
        root.Subcommands.Add(CreateInstall(home, package: true));
        root.Subcommands.Add(CreateSchema(home));
        root.Subcommands.Add(CreateGet(home));
        root.Subcommands.Add(CreateRegress(home));
        root.Subcommands.Add(CreateTest(home));
        root.Subcommands.Add(CreateUpgrade());
        try
        {
            if (arguments.Length != 0 && arguments[0] == RegressionDriver.ClientSwitch)
            {
                return await RegressionDriver.RunClientAsync(arguments[1..], interactiveCancellation.Token);
            }

            ParseResult result = root.Parse(arguments);
            bool interactive = result.CommandResult.Command.Name is "run" or "connect" or "test";
            if (interactive)
            {
                interactiveCancellation.Enable();
            }

            return await result.InvokeAsync(new InvocationConfiguration
            {
                EnableDefaultExceptionHandler = false,
                ProcessTerminationTimeout = interactive ? null : TimeSpan.FromSeconds(30),
            }, interactiveCancellation.Token);
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
        var worker = new Option<bool>("--background-worker") { Description = "Include a preloaded PostgreSQL worker and its backend test." };
        command.Arguments.Add(name);
        command.Options.Add(output);
        command.Options.Add(extension);
        command.Options.Add(worker);
        command.SetAction(async (result, token) =>
        {
            string path = await ProjectScaffolder.CreateAsync(result.GetValue(name)!, result.GetValue(output),
                result.GetValue(extension), result.GetValue(worker), token);
            Console.WriteLine($"Created extension solution at {path}");
            Console.WriteLine("Run dotnet test from that directory to build and test the extension in PostgreSQL 18.");
            Console.WriteLine("Run ankus publish to publish using your registered PostgreSQL installation.");
        });
        return command;
    }

    private static Command CreateInit(Option<string?> home)
    {
        var command = new Command("init", "Download or register PostgreSQL versions for extension development.");
        var versions = new Dictionary<int, Option<string?>>();
        for (int major = 13; major <= 19; major++)
        {
            var option = new Option<string?>($"--pg{major}")
            {
                Description = $"Path to PostgreSQL {major}'s pg_config executable, or 'download' to install it locally.",
            };
            versions.Add(major, option);
            command.Options.Add(option);
        }

        var jobs = new Option<int>("--jobs", "-j")
        {
            Description = "Maximum parallel PostgreSQL source-build jobs.",
            DefaultValueFactory = _ => Environment.ProcessorCount,
        };
        var configure = new Option<string[]>("--configure-flag")
        {
            Description = "Additional Unix configure argument; repeat for multiple arguments.",
        };
        var valgrind = new Option<bool>("--valgrind") { Description = "Enable PostgreSQL's Valgrind instrumentation on Unix." };
        var basePort = new Option<int?>("--base-port") { Description = "Development port base, added to each major (0–65516; default 28800)." };
        var baseTestingPort = new Option<int?>("--base-testing-port") { Description = "Port base for explicitly configured test fixtures (0–65516; default 32200)." };
        command.Options.Add(jobs);
        command.Options.Add(configure);
        command.Options.Add(valgrind);
        command.Options.Add(basePort);
        command.Options.Add(baseTestingPort);
        command.SetAction(async (result, token) =>
        {
            var ports = new PostgresPortOptions(result.GetValue(basePort), result.GetValue(baseTestingPort));
            var paths = new Dictionary<int, string>();
            foreach ((int major, Option<string?> option) in versions)
            {
                if (result.GetValue(option) is string path)
                {
                    paths.Add(major, path);
                }
            }

            var registry = new PostgresRegistry(result.GetValue(home));
            if (paths.Count == 0)
            {
                foreach (int major in versions.Keys)
                {
                    paths.Add(major, "download");
                }
            }

            int[] downloads = [.. paths.Where(static pair => pair.Value == "download").Select(static pair => pair.Key)];
            if (downloads.Length != 0)
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
                var provisioner = new PostgresProvisioner(client, registry.HomeDirectory);
                var options = new PostgresProvisionOptions
                {
                    Jobs = result.GetValue(jobs),
                    ConfigureFlags = result.GetValue(configure) ?? [],
                    EnableValgrind = result.GetValue(valgrind),
                };
                IReadOnlyList<PostgresInstallation> downloaded = await provisioner.InstallAsync(downloads, options,
                    new ConsoleProgress(), token);
                foreach (PostgresInstallation installation in downloaded)
                {
                    paths[installation.Version.Major] = installation.PgConfigPath;
                }
            }

            IReadOnlyList<PostgresInstallation> installations = await registry.RegisterAsync(paths, ports, token);
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
            var registry = new PostgresRegistry(result.GetValue(home));
            Console.WriteLine($"Development port: {registry.GetPort(installation.Version.Major)}");
            Console.WriteLine($"Test port: {registry.GetTestPort(installation.Version.Major)}");
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

    private static Command CreateInstall(Option<string?> home, bool package = false)
    {
        Command command = package
            ? new Command("package", "Build an extension installation tree under a separate output directory.")
            : new Command("install", "Build and copy an extension into a PostgreSQL installation.");
        AddSelectionOptions(command);
        AddBuildOptions(command);
        var from = new Option<string?>("--from") { Description = "Use an existing publish directory without rebuilding." };
        Option<string?> destination = package
            ? new Option<string?>("--output", "-o") { Description = "Package root (default: extension-pgMAJOR beneath the publish directory)." }
            : new Option<string?>("--destdir") { Description = "Stage files under this root, preserving PostgreSQL's installation paths." };
        command.Options.Add(from);
        command.Options.Add(destination);
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

            string? root = result.GetValue(destination);
            if (package && root is null)
            {
                string name = Path.GetFileNameWithoutExtension(PublishedExtension.Read(source).Control);
                root = Path.Combine(Path.GetFullPath(source), name + "-" + installation.Label);
            }

            foreach (string path in ExtensionInstaller.Install(source, installation, root, token, packageLayout: package))
            {
                Console.WriteLine($"{(package ? "Packaged" : "Installed")} {path}");
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
        AddConfigurationOption(command, "Release");
    }

    private static void AddConfigurationOption(Command command, string defaultConfiguration)
    {
        var configuration = new Option<string>("--configuration", "-c")
        {
            Description = $"MSBuild configuration, including custom configurations (default: {defaultConfiguration}).",
            DefaultValueFactory = _ => defaultConfiguration,
        };
        configuration.Validators.Add(result =>
        {
            string? value = result.GetValueOrDefault<string>();
            if (ConfigurationError(value) is string error)
            {
                result.AddError(error);
            }
        });
        command.Options.Add(configuration);
    }

    private static string? ConfigurationError(string? value)
        => string.IsNullOrWhiteSpace(value) || value is "." or ".." ||
            value.IndexOfAny(['/', '\\', ':', '<', '>', '"', '|', '?', '*']) >= 0 ||
            value.Any(char.IsControl) || value.EndsWith(' ') || value.EndsWith('.')
            ? "Configuration must be a nonempty directory name without path separators, invalid filename characters, or a trailing dot or space."
            : null;

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
        string? output = result.CommandResult.Command.Name is "install" or "package" or "run" or "regress" ? null : result.GetValue<string?>("--output");
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

    private static string GetConfiguration(ParseResult result) => result.GetValue<string>("--configuration")!;
}
