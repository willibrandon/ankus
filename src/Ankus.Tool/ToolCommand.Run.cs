using System.CommandLine;
using Ankus.PgConfig;

namespace Ankus.Tool;

internal static partial class ToolCommand
{
    private static Command CreateConnect(Option<string?> home, InteractiveCommandCancellation cancellation)
    {
        var command = new Command("connect", "Start PostgreSQL, create or reuse a database, and open a SQL client.");
        AddConnectionOptions(command);
        command.SetAction(async (result, token) =>
        {
            _ = result.GetValue<bool>("--pgcli");
            PostgresSelection selection = await SelectWithProjectAsync(result, home, token);
            PostgresInstallation installation = selection.Installation;
            string? environmentDatabase = Environment.GetEnvironmentVariable("DBNAME");
            string database = result.GetValue<string?>("--database") ??
                (environmentDatabase is { Length: > 0 } ? environmentDatabase : null) ??
                await ExtensionBuilder.GetExtensionNameAsync(selection.ProjectPath ?? result.GetValue<string?>("--project"),
                    GetConfiguration(result), installation, token, BuildProperties(result));
            var cluster = new PostgresDevelopmentCluster(installation, result.GetValue(home));
            await cluster.StartAsync(ReadServerOptions(result), token);
            return await OpenDatabaseAsync(result, installation, cluster, database, cancellation, token);
        });
        return command;
    }

    private static Command CreateRun(Option<string?> home, InteractiveCommandCancellation cancellation)
    {
        var command = new Command("run", "Build and install an extension, start PostgreSQL, and open a SQL client.");
        AddConnectionOptions(command);
        var noBuild = new Option<bool>("--no-build") { Description = "Install the project's existing publication without rebuilding." };
        var installOnly = new Option<bool>("--install-only") { Description = "Stop PostgreSQL and install the extension without starting or connecting." };
        command.Options.Add(noBuild);
        command.Options.Add(installOnly);
        command.SetAction(async (result, token) =>
        {
            _ = result.GetValue<bool>("--pgcli");
            ExtensionSelection selection = await SelectExtensionAsync(result, home, token);
            PostgresInstallation installation = selection.Installation;
            string output = GetOutputDirectory(result, selection);
            PostgresDevelopmentOptions options = ReadServerOptions(result);
            var cluster = new PostgresDevelopmentCluster(installation, result.GetValue(home));
            await cluster.StopAsync(token);
            if (!result.GetValue(noBuild))
            {
                int code = await PublishAsync(selection, output, token);
                if (code != 0)
                {
                    return code;
                }
            }

            foreach (string path in ExtensionInstaller.Install(output, installation, null, token))
            {
                Console.WriteLine($"Installed {path}");
            }

            if (result.GetValue(installOnly))
            {
                return 0;
            }

            string database = result.GetValue<string?>("--database") ?? Path.GetFileNameWithoutExtension(PublishedExtension.Read(output).Control);
            await cluster.StartAsync(options, token);
            return await OpenDatabaseAsync(result, installation, cluster, database, cancellation, token);
        });
        return command;
    }

    private static void AddConnectionOptions(Command command)
    {
        AddSelectionOptions(command);
        AddBuildOptions(command);
        AddValgrindOption(command);
        command.Options.Add(new Option<string?>("--database", "-d")
        {
            Description = command.Name == "connect" ? "Literal database name (default: DBNAME, otherwise the extension name)."
                : "Literal database name (default: the extension name).",
        });
        command.Options.Add(new Option<bool>("--pgcli")
        {
            Description = "Use pgcli from PATH instead of the selected installation's psql (default: ANKUS_PGCLI).",
            DefaultValueFactory = static _ => EnvironmentPgcli(),
        });
        command.Options.Add(new Option<int?>("--port") { Description = "Port when starting a stopped server; an existing server keeps its actual port." });
        command.Options.Add(new Option<int>("--timeout") { Description = "Startup timeout in seconds (1–600).", DefaultValueFactory = _ => 60 });
        command.Options.Add(new Option<string[]>("--postgresql-conf") { Description = "Literal name=value setting; repeat for multiple settings." });
        command.Arguments.Add(new Argument<string[]>("client-arguments")
        {
            Description = "Arguments for the SQL client, after --.",
            Arity = ArgumentArity.ZeroOrMore,
        });
    }

    private static PostgresDevelopmentOptions ReadServerOptions(ParseResult result)
        => new()
        {
            Port = result.GetValue<int?>("--port"),
            TimeoutSeconds = result.GetValue<int>("--timeout"),
            UseValgrind = result.GetValue<bool>("--valgrind"),
            Settings = ParseServerSettings(result.GetValue<string[]>("--postgresql-conf") ?? []),
        };

    /// <summary>
    /// Parses the optional environment choice without changing the process environment or overriding an explicit option.
    /// </summary>
    /// <returns>Whether pgcli is selected by default for interactive commands.</returns>
    private static bool EnvironmentPgcli()
    {
        string? value = Environment.GetEnvironmentVariable("ANKUS_PGCLI");
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        return bool.TryParse(value, out bool selected) ? selected
            : throw new ArgumentException("ANKUS_PGCLI must be true or false.");
    }

    private static async Task<int> OpenDatabaseAsync(ParseResult result, PostgresInstallation installation,
        PostgresDevelopmentCluster cluster, string database, InteractiveCommandCancellation cancellation, CancellationToken token)
    {
        bool created = await cluster.CreateDatabaseAsync(database, token);
        Console.WriteLine($"{(created ? "Created" : "Reusing")} database {database}");
        string connection = await cluster.GetConnectionStringAsync(database, token);
        string executable = result.GetValue<bool>("--pgcli") ? "pgcli" : installation.PsqlPath;
        return await cancellation.RunClientAsync(executable,
            ["--dbname", connection, .. result.GetValue<string[]>("client-arguments") ?? []], token);
    }
}
