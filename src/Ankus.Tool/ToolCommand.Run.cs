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
            PostgresInstallation installation = await SelectAsync(result, home, token);
            string database = result.GetValue<string?>("--database") ??
                await ExtensionBuilder.GetExtensionNameAsync(result.GetValue<string?>("--project"), GetConfiguration(result), installation, token);
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
            PostgresInstallation installation = await SelectAsync(result, home, token);
            string output = GetOutputDirectory(result, installation);
            PostgresDevelopmentOptions options = ReadServerOptions(result);
            var cluster = new PostgresDevelopmentCluster(installation, result.GetValue(home));
            await cluster.StopAsync(token);
            if (!result.GetValue(noBuild))
            {
                int code = await PublishAsync(result, installation, output, token);
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
        command.Options.Add(new Option<string?>("--database", "-d") { Description = "Literal database name (default: the extension name)." });
        command.Options.Add(new Option<bool>("--pgcli") { Description = "Use pgcli from PATH instead of the selected installation's psql." });
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
            Settings = ParseServerSettings(result.GetValue<string[]>("--postgresql-conf") ?? []),
        };

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
