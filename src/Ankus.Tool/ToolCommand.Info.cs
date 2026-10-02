using System.CommandLine;
using System.Diagnostics;
using System.Globalization;
using Ankus.PgConfig;

namespace Ankus.Tool;

internal static partial class ToolCommand
{
    /// <summary>
    /// Builds the installation report and single-value information commands.
    /// </summary>
    /// <param name="home">The shared registry home option.</param>
    /// <returns>The PostgreSQL information command.</returns>
    private static Command CreateInfo(Option<string?> home)
    {
        var command = new Command("info", "Show the selected PostgreSQL installation.");
        AddSelectionOptions(command);
        foreach (Option option in command.Options)
        {
            option.Recursive = true;
        }

        command.Subcommands.Add(CreateInfoValue("path", home));
        command.Subcommands.Add(CreateInfoValue("pg-config", home));
        command.Subcommands.Add(CreateInfoValue("version", home));
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

    /// <summary>
    /// Prints a selected installation value suitable for ordinary command substitution.
    /// </summary>
    /// <param name="name">The value selector.</param>
    /// <param name="home">The shared registry home option.</param>
    /// <returns>A command that prints exactly one value on success.</returns>
    private static Command CreateInfoValue(string name, Option<string?> home)
    {
        var command = new Command(name, name switch
        {
            "path" => "Print the installation root above the registered pg_config directory.",
            "pg-config" => "Print the absolute path to the selected pg_config.",
            "version" => "Print the selected PostgreSQL release version.",
            _ => throw new UnreachableException(),
        });
        var version = new Argument<string?>("postgres")
        {
            Description = "PostgreSQL major, such as 18 or pg18 (default: project selection, otherwise 18).",
            Arity = ArgumentArity.ZeroOrOne,
        };
        command.Arguments.Add(version);
        command.SetAction(async (result, token) =>
        {
            PostgresInstallation installation = await SelectAsync(result, home, token,
                positionalMajor: ParseInfoMajor(result.GetValue(version)));
            string value = name switch
            {
                "path" => Path.GetDirectoryName(Path.GetDirectoryName(installation.PgConfigPath)!) ??
                    throw new InvalidOperationException("The selected pg_config has no installation root above its directory."),
                "pg-config" => installation.PgConfigPath,
                "version" => installation.Version.ToString(),
                _ => throw new UnreachableException(),
            };
            Console.WriteLine(value);
            return 0;
        });
        return command;
    }

    /// <summary>
    /// Validates the ordinary or pgrx-style major label before installation lookup.
    /// </summary>
    /// <param name="value">The optional positional version label.</param>
    /// <returns>The explicitly selected major, or null when omitted.</returns>
    private static int? ParseInfoMajor(string? value)
    {
        if (value is null)
        {
            return null;
        }

        ReadOnlySpan<char> number = value.StartsWith("pg", StringComparison.Ordinal) ? value.AsSpan(2) : value.AsSpan();
        if (!int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out int major) || major is < 13 or > 19)
        {
            throw new ArgumentException("Select a PostgreSQL major from 13 through 19, such as 18 or pg18.");
        }

        return major;
    }
}
