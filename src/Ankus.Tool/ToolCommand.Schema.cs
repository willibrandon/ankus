using System.CommandLine;
using System.CommandLine.Parsing;
using System.Runtime.InteropServices;
using Ankus.PgConfig;

namespace Ankus.Tool;

internal static partial class ToolCommand
{
    private static readonly string[] s_schemaBuildOptions = ["--project", "--pg", "--pg-config", "--configuration", "--skip-build"];

    private static Command CreateSchema(Option<string?> home)
    {
        var command = new Command("schema", "Extract installation SQL from a Native AOT extension.");
        AddSelectionOptions(command);
        AddBuildOptions(command);
        var from = new Option<string?>("--from")
        {
            Description = "Read this native library without building or requiring a PostgreSQL installation.",
        };
        var skipBuild = new Option<bool>("--skip-build")
        {
            Description = "Read the project's existing default publish directory without rebuilding.",
        };
        var runtime = new Option<string?>("--runtime", "-r")
        {
            Description = "Required library RID; selects an architecture in a universal macOS library. Builds use the host RID.",
        };
        var output = new Option<string?>("--output", "-o") { Description = "SQL output file (default: stdout)." };
        command.Options.Add(from);
        command.Options.Add(skipBuild);
        command.Options.Add(runtime);
        command.Options.Add(output);
        command.SetAction(async (result, token) =>
        {
            string? library = result.GetValue(from);
            string? rid = result.GetValue(runtime);
            if (library is not null)
            {
                foreach (string option in s_schemaBuildOptions)
                {
                    if (result.GetResult(option) is OptionResult { Implicit: false })
                    {
                        throw new ArgumentException($"Use either --from or {option}.");
                    }
                }
            }
            else
            {
                if (rid is not null && rid != RuntimeInformation.RuntimeIdentifier)
                {
                    throw new ArgumentException("Project schema builds use the host runtime identifier; use --from for another target.");
                }

                int major = result.GetValue<int>("--pg");
                ArgumentOutOfRangeException.ThrowIfLessThan(major, 13);
                ArgumentOutOfRangeException.ThrowIfGreaterThan(major, 19);
                string project = ExtensionBuilder.ResolveProject(result.GetValue<string?>("--project"));
                string configuration = GetConfiguration(result);
                string directory = Path.Combine(Path.GetDirectoryName(project)!, "bin", "ankus",
                    "pg" + major.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    RuntimeInformation.RuntimeIdentifier, configuration);
                if (!result.GetValue(skipBuild))
                {
                    PostgresInstallation installation = await SelectAsync(result, home, token);
                    int code = await ExtensionBuilder.PublishAsync(project, configuration, installation, directory, token,
                        diagnosticsToStandardError: true);
                    if (code != 0)
                    {
                        return code;
                    }
                }
                else if (result.GetValue<string?>("--pg-config") is not null)
                {
                    throw new ArgumentException("--pg-config requires a build; use --pg to select an existing publication.");
                }

                PublishedExtension publication = PublishedExtension.Read(directory);
                if (publication.PostgresMajor != major || publication.RuntimeIdentifier != RuntimeInformation.RuntimeIdentifier)
                {
                    throw new FormatException("The published extension does not match the selected PostgreSQL target.");
                }

                library = Path.Combine(directory, publication.Library);
                ExtensionSchema existing = ExtensionSchema.Read(library, RuntimeInformation.RuntimeIdentifier);
                if (existing.Artifacts.PostgresMajor != major || existing.Artifacts.Library != publication.Library ||
                    existing.Artifacts.Control != publication.Control || existing.Artifacts.Sql != publication.Sql)
                {
                    throw new FormatException("The published manifest disagrees with the embedded schema identity.");
                }

                await WriteSchemaAsync(existing, library, result.GetValue(output), token);
                return 0;
            }

            ExtensionSchema schema = ExtensionSchema.Read(library, rid);
            await WriteSchemaAsync(schema, library, result.GetValue(output), token);
            return 0;
        });
        return command;
    }

    private static async Task WriteSchemaAsync(ExtensionSchema schema, string library, string? output, CancellationToken token)
    {
        if (output is null)
        {
            await Console.Out.WriteAsync(schema.Sql.AsMemory(), token);
            return;
        }

        string path = Path.GetFullPath(output);
        if (string.Equals(path, Path.GetFullPath(library), OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            throw new ArgumentException("The SQL output must differ from the native library path.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, schema.Sql, token);
            token.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}
