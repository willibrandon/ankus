using System.CommandLine;
using System.Globalization;
using Ankus.PgConfig;

namespace Ankus.Tool;

internal static partial class ToolCommand
{
    private static Command CreateTest(Option<string?> home)
    {
        var command = new Command("test", "Run dotnet test using a selected PostgreSQL installation or all registered versions.");
        AddSelectionOptions(command);
        AddConfigurationOption(command, "Debug");
        var all = new Option<bool>("--all") { Description = "Run sequentially against every registered PostgreSQL version." };
        var noSchema = new Option<bool>("--no-schema") { Description = "Reuse the last successful test schema while rebuilding compatible native code." };
        var reports = new Option<string?>("--results-directory") { Description = "Test report root; each PostgreSQL major gets its own subdirectory." };
        var dataDirectory = new Option<string?>("--pgdata") { Description = "Base directory for isolated, per-invocation PostgreSQL cluster data." };
        var forwarded = new Argument<string[]>("test-arguments")
        {
            Description = "Ordinary dotnet test arguments after --, including project selection, filters and report options.",
            Arity = ArgumentArity.ZeroOrMore,
        };
        command.Options.Add(all);
        command.Options.Add(noSchema);
        command.Options.Add(reports);
        command.Options.Add(dataDirectory);
        command.Arguments.Add(forwarded);
        command.SetAction(async (result, token) =>
        {
            (string configuration, string[] arguments) = TestConfiguration(GetConfiguration(result),
                result.GetResult("--configuration") is System.CommandLine.Parsing.OptionResult { Implicit: false },
                result.GetValue(forwarded) ?? []);
            var installations = new List<PostgresInstallation>();
            if (result.GetValue(all))
            {
                if (result.GetResult("--pg") is System.CommandLine.Parsing.OptionResult { Implicit: false } ||
                    result.GetValue<string?>("--pg-config") is not null)
                {
                    throw new ArgumentException("Use --all without --pg or --pg-config.");
                }

                var registry = new PostgresRegistry(result.GetValue(home));
                for (int major = 13; major <= 19; major++)
                {
                    if (registry.GetPath(major) is not null)
                    {
                        installations.Add(await registry.GetAsync(major, token));
                    }
                }

                if (installations.Count == 0)
                {
                    throw new InvalidOperationException("No PostgreSQL versions are registered. Run 'ankus init' first.");
                }
            }
            else
            {
                installations.Add(await SelectAsync(result, home, token));
            }

            string root = Path.GetFullPath(result.GetValue(reports) ?? Path.Combine("TestResults", "ankus", Guid.NewGuid().ToString("N")));
            int separator = Array.IndexOf(arguments, "--");
            if (separator < 0)
            {
                separator = arguments.Length;
            }

            int firstFailure = 0;
            foreach (PostgresInstallation installation in installations)
            {
                token.ThrowIfCancellationRequested();
                string major = installation.Version.Major.ToString(CultureInfo.InvariantCulture);
                await using var session = new ExtensionTestCommandSession(installation, result.GetValue(dataDirectory));
                string resultsDirectory = Path.Combine(root, installation.Label);
                Console.WriteLine($"Testing PostgreSQL {installation.Version} ({configuration}). Results: {resultsDirectory}");
                int code = await ToolProcess.RunAsync("dotnet",
                [
                    "test", .. arguments[..separator],
                    "-p:Configuration=" + ExtensionBuilder.EscapeProperty(configuration),
                    "-p:AnkusPostgresMajor=" + major,
                    "-p:AnkusPgConfigPath=" + ExtensionBuilder.EscapeProperty(installation.PgConfigPath),
                    "--results-directory", resultsDirectory,
                    .. arguments[separator..],
                ], token, environment: new Dictionary<string, string?>
                {
                    ["ANKUS_TEST_PG_CONFIG"] = installation.PgConfigPath,
                    ["ANKUS_TEST_POSTGRES_MAJOR"] = major,
                    ["ANKUS_TEST_CONFIGURATION"] = configuration,
                    ["ANKUS_TEST_REUSE_SCHEMA"] = result.GetValue(noSchema) ? "true" : "false",
                    ["ANKUS_TEST_SESSION_DIRECTORY"] = session.DirectoryPath,
                    ["ANKUS_TEST_DATA_DIRECTORY"] = session.DataDirectoryPath,
                });
                Console.WriteLine($"{installation.Label}: dotnet test exited {code}.");
                if (firstFailure == 0)
                {
                    firstFailure = code;
                }
            }

            return firstFailure;
        });
        return command;
    }

    private static (string Configuration, string[] Arguments) TestConfiguration(string configuration, bool explicitlySelected, string[] arguments)
    {
        var forwarded = new List<string>();
        for (int index = 0; index < arguments.Length; index++)
        {
            string argument = arguments[index];
            if (argument == "--")
            {
                forwarded.AddRange(arguments[index..]);
                break;
            }

            string? value = null;
            if (argument is "--configuration" or "-c")
            {
                if (++index == arguments.Length || arguments[index] == "--")
                {
                    throw new ArgumentException("The forwarded configuration option requires a value.");
                }

                value = arguments[index];
            }
            else if (argument.StartsWith("--configuration=", StringComparison.Ordinal) || argument.StartsWith("--configuration:", StringComparison.Ordinal))
            {
                value = argument[16..];
            }
            else if (argument.StartsWith("-c=", StringComparison.Ordinal) || argument.StartsWith("-c:", StringComparison.Ordinal))
            {
                value = argument[3..];
            }

            if (value is null)
            {
                forwarded.Add(argument);
                continue;
            }

            if (ConfigurationError(value) is string error)
            {
                throw new ArgumentException(error);
            }

            if (explicitlySelected && !string.Equals(configuration, value, StringComparison.Ordinal))
            {
                throw new ArgumentException("Select the same configuration for ankus test and forwarded dotnet test arguments.");
            }

            configuration = value;
            explicitlySelected = true;
        }

        return (configuration, [.. forwarded]);
    }
}
