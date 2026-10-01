using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using Ankus.Testing;

namespace Ankus.IntegrationTests;

public sealed partial class ToolCommandTests
{
    /// <summary>
    /// Plain dotnet test preserves project defaults and build overrides through publication and actual server execution.
    /// </summary>
    /// <param name="selection">The project or command-line property selecting the installation.</param>
    [TestMethod]
    [DataRow("project")]
    [DataRow("extension-project")]
    [DataRow("build-major")]
    [DataRow("build-path")]
    public async Task PlainTestHonorsProjectPostgresSelection(string selection)
    {
        CancellationToken token = context.CancellationToken;
        string output = await CreateSelectionProjectAsync(token);
        string properties = selection == "extension-project"
            ? Path.Combine(output, "src", "TestCommandProbe", "TestCommandProbe.csproj")
            : Path.Combine(output, "Directory.Build.props");
        XDocument document = XDocument.Load(properties);
        document.Root!.Add(new XElement("PropertyGroup",
            new XElement("AnkusPostgresMajor", selection == "build-major" ? DifferentMajor() : s_installation.Version.Major),
            new XElement("AnkusPgConfigPath", selection == "build-path" ? "missing-pg-config" : s_installation.PgConfigPath)));
        document.Save(properties);
        string[] arguments = selection switch
        {
            "build-major" => ["-p:AnkusPostgresMajor=" + MajorText()],
            "build-path" => ["-p:AnkusPgConfigPath=" + s_installation.PgConfigPath],
            _ => [],
        };
        Dictionary<string, string?> environment = SelectionEnvironment();
        ProcessResult result = await ProcessRunner.RunAsync("dotnet",
            ["test", .. arguments, "--filter", "FullyQualifiedName~SelectedVersionReachesPostgres", "--report-trx"],
            environment, token, workingDirectory: output);

        Assert.AreEqual(0, result.ExitCode, result.StandardOutput + result.StandardError);
        Assert.Contains("succeeded: 1", result.StandardOutput);
        string[] reports = Directory.GetFiles(output, "*.trx", SearchOption.AllDirectories);
        string report = Assert.ContainsSingle(reports);
        XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
        XElement outcome = Assert.ContainsSingle(XDocument.Load(report).Descendants(ns + "UnitTestResult"));
        Assert.AreEqual("SelectedVersionReachesPostgres", (string?)outcome.Attribute("testName"));
        Assert.AreEqual("Passed", (string?)outcome.Attribute("outcome"));
    }

    /// <summary>
    /// CLI defaults use imported project settings, while explicit major and path selections override them.
    /// </summary>
    /// <param name="selection">The implicit project or explicit command selector.</param>
    [TestMethod]
    [DataRow("project")]
    [DataRow("extension-project")]
    [DataRow("explicit-major")]
    [DataRow("explicit-path")]
    [DataRow("forwarded-major")]
    [DataRow("forwarded-path")]
    [DataRow("forwarded-properties")]
    [DataRow("forwarded-last-major")]
    [DataRow("forwarded-quoted-major")]
    public async Task CommandSelectionHonorsProjectPostgresVersion(string selection)
    {
        CancellationToken token = context.CancellationToken;
        string output = await CreateSelectionProjectAsync(token);
        string properties = selection == "extension-project"
            ? Path.Combine(output, "src", "TestCommandProbe", "TestCommandProbe.csproj")
            : Path.Combine(output, "Directory.Build.props");
        XDocument document = XDocument.Load(properties);
        document.Root!.Add(new XElement("PropertyGroup",
            new XElement("AnkusPostgresMajor", selection is "project" or "extension-project" ? s_installation.Version.Major : DifferentMajor())));
        document.Save(properties);
        string[] arguments = selection switch
        {
            "explicit-major" => ["--pg", MajorText()],
            "explicit-path" => ["--pg-config", s_installation.PgConfigPath],
            _ => [],
        };
        string project = Path.Combine(output, "tests", "TestCommandProbe.Tests", "TestCommandProbe.Tests.csproj");
        string[] forwarded = selection switch
        {
            "forwarded-major" => ["-p:AnkusPostgresMajor=" + MajorText()],
            "forwarded-path" => ["-p:AnkusPgConfigPath=" + s_installation.PgConfigPath],
            "forwarded-properties" => ["/property:AnkusPostgresMajor=" + MajorText() + ";Configuration=Debug"],
            "forwarded-last-major" => ["-p:AnkusPostgresMajor=" + DifferentMajor(), "-p:AnkusPostgresMajor=" + MajorText()],
            "forwarded-quoted-major" => ["-p:AnkusPostgresMajor=\"" + MajorText() + "\""],
            _ => [],
        };
        ProcessResult result = await ProcessRunner.RunAsync(s_tool,
            ["test", "--home", s_home, .. arguments, "--", "--project", project,
                .. forwarded, "--filter", "FullyQualifiedName~SelectedVersionReachesPostgres"],
            SelectionEnvironment(), token, workingDirectory: CreateDirectory());

        Assert.AreEqual(0, result.ExitCode, result.StandardOutput + result.StandardError);
        Assert.Contains($"Testing PostgreSQL {s_installation.Version}", result.StandardOutput);
        Assert.Contains(s_postgresKey + ": dotnet test exited 0.", result.StandardOutput);
        Assert.Contains("succeeded: 1", result.StandardOutput);
    }

    /// <summary>
    /// Implicit test selection follows solution files and evaluated SDK imports rather than literal SDK attributes.
    /// </summary>
    /// <param name="layout">The ordinary test project or solution layout.</param>
    [TestMethod]
    [DataRow("directory")]
    [DataRow("slnx")]
    [DataRow("sln")]
    [DataRow("sdk-element")]
    public async Task CommandSelectionAcceptsOrdinaryProjectLayouts(string layout)
    {
        CancellationToken token = context.CancellationToken;
        string output = await CreateSelectionProjectAsync(token);
        string extension = Path.Combine(output, "src", "TestCommandProbe", "TestCommandProbe.csproj");
        XDocument document = XDocument.Load(extension);
        document.Root!.Add(new XElement("PropertyGroup", new XElement("AnkusPostgresMajor", s_installation.Version.Major),
            new XElement("AnkusPgConfigPath", s_installation.PgConfigPath)));
        if (layout == "sdk-element")
        {
            document.Root.Attribute("Sdk")!.Remove();
            document.Root.AddFirst(new XElement("Sdk", new XAttribute("Name", "Ankus.Sdk")));
        }

        document.Save(extension);
        string project = Path.Combine(output, "tests", "TestCommandProbe.Tests", "TestCommandProbe.Tests.csproj");
        string[] selection = layout switch
        {
            "directory" => [],
            "slnx" => ["--solution", Path.Combine(output, "TestCommandProbe.slnx")],
            "sdk-element" => ["--project", project],
            _ => ["--solution", Path.Combine(output, "Selection.sln")],
        };
        if (layout == "sln")
        {
            (await RunDotnetAsync(["new", "sln", "--format", "sln", "--name", "Selection", "--output", output], token))
                .EnsureSuccess("dotnet", ["new", "sln"]);
            (await RunDotnetAsync(["sln", selection[1], "add", extension, project], token))
                .EnsureSuccess("dotnet", ["sln", "add"]);
        }

        ProcessResult result = await ProcessRunner.RunAsync(s_tool,
            ["test", "--home", s_home, "--", .. selection, "--filter", "FullyQualifiedName~SelectedVersionReachesPostgres"],
            SelectionEnvironment(), token, workingDirectory: layout == "directory" ? output : CreateDirectory());

        Assert.AreEqual(0, result.ExitCode, result.StandardOutput + result.StandardError);
        Assert.Contains($"Testing PostgreSQL {s_installation.Version}", result.StandardOutput);
        Assert.Contains("succeeded: 1", result.StandardOutput);
    }

    /// <summary>
    /// Conflicting CLI and build selectors fail before a test process starts or rewrites the caller's properties.
    /// </summary>
    /// <param name="conflict">The conflicting selection.</param>
    [TestMethod]
    [DataRow("major")]
    [DataRow("path")]
    [DataRow("configuration")]
    [DataRow("all")]
    [DataRow("empty-property")]
    public async Task CommandSelectionRejectsConflictingBuildProperties(string conflict)
    {
        string[] arguments = conflict switch
        {
            "major" => ["--pg", MajorText(), "--", "-p:AnkusPostgresMajor=" + DifferentMajor()],
            "path" => ["--pg-config", s_installation.PgConfigPath, "--", "-p:AnkusPgConfigPath=missing-pg-config"],
            "configuration" => ["-c", "Release", "--", "-p:Configuration=Debug"],
            "empty-property" => ["--", "-p:;AnkusPostgresMajor=" + MajorText() + ";;"],
            _ => ["--all", "--", "-p:AnkusPostgresMajor=" + MajorText()],
        };
        ProcessResult result = await ProcessRunner.RunAsync(s_tool, ["test", "--home", s_home, .. arguments],
            SelectionEnvironment(), context.CancellationToken, workingDirectory: CreateDirectory());

        Assert.AreNotEqual(0, result.ExitCode);
        string expected = conflict switch
        {
            "all" => "Use --all without",
            "empty-property" => "Forwarded MSBuild properties must contain name=value",
            _ => "Select the same",
        };
        Assert.Contains(expected, result.StandardOutput + result.StandardError);
        Assert.DoesNotContain("Testing PostgreSQL", result.StandardOutput);
    }

    /// <summary>
    /// Cluster inspection uses the current project's major and installation without requiring --pg.
    /// </summary>
    /// <param name="command">The non-starting cluster command.</param>
    [TestMethod]
    [DataRow("info")]
    [DataRow("status")]
    [DataRow("stop")]
    public async Task ClusterCommandsHonorProjectSelection(string command)
    {
        string directory = CreateDirectory();
        new XDocument(new XElement("Project", new XElement("PropertyGroup",
            new XElement("AnkusPostgresMajor", s_installation.Version.Major),
            new XElement("AnkusPgConfigPath", s_installation.PgConfigPath)))).Save(Path.Combine(directory, "Selection.csproj"));
        ProcessResult result = await ProcessRunner.RunAsync(s_tool, [command, "--home", directory],
            SelectionEnvironment(), context.CancellationToken, workingDirectory: directory);

        Assert.AreEqual(0, result.ExitCode, result.StandardOutput + result.StandardError);
        Assert.Contains(command == "info" ? "PostgreSQL: " + s_installation.Version : s_postgresKey + ":", result.StandardOutput);
        if (command == "info")
        {
            Assert.Contains(s_installation.PgConfigPath, result.StandardOutput);
        }
        else
        {
            Assert.Contains("stopped", result.StandardOutput);
        }

        Assert.IsFalse(File.Exists(Path.Combine(directory, "config.json")));
    }

    /// <summary>
    /// Standalone directories and conflicting project layouts use the documented PostgreSQL 18 fallback.
    /// </summary>
    /// <param name="layout">The absence or ambiguity of project selection.</param>
    [TestMethod]
    [DataRow("empty")]
    [DataRow("unrelated")]
    [DataRow("projects")]
    [DataRow("solution")]
    public async Task UnselectedProjectLayoutsRetainDefaultMajor(string layout)
    {
        string directory = CreateDirectory();
        if (layout == "unrelated")
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "Library.csproj"), "<Project />", context.CancellationToken);
        }
        else if (layout is "projects" or "solution")
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "First.csproj"),
                "<Project><PropertyGroup><AnkusPostgresMajor>13</AnkusPostgresMajor></PropertyGroup></Project>", context.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(directory, "Second.csproj"),
                "<Project><PropertyGroup><AnkusPostgresMajor>19</AnkusPostgresMajor></PropertyGroup></Project>", context.CancellationToken);
            if (layout == "solution")
            {
                await File.WriteAllTextAsync(Path.Combine(directory, "Selection.slnx"),
                    "<Solution><Project Path=\"First.csproj\" /><Project Path=\"Second.csproj\" /></Solution>", context.CancellationToken);
            }
        }

        ProcessResult result = await ProcessRunner.RunAsync(s_tool, ["info", "--home", directory],
            SelectionEnvironment(), context.CancellationToken, workingDirectory: directory);
        string output = result.StandardOutput + result.StandardError;
        Assert.AreNotEqual(0, result.ExitCode);
        Assert.Contains("PostgreSQL 18 is not registered", output);
        Assert.DoesNotContain("Specify --project or --solution", output);
        Assert.DoesNotContain("different PostgreSQL installations", output);
    }

    /// <summary>
    /// Runtime configuration follows property changes and removals on incremental builds of a packed consumer.
    /// </summary>
    [TestMethod]
    public async Task TestHostSelectionTracksBuildProperties()
    {
        CancellationToken token = context.CancellationToken;
        string directory = CreateDirectory();
        string project = Path.Combine(directory, "Host.csproj");
        new XDocument(new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
            new XElement("PropertyGroup", new XElement("TargetFramework", "net10.0"), new XElement("OutputType", "Exe")),
            new XElement("ItemGroup", new XElement("PackageReference", new XAttribute("Include", "Ankus.Testing"),
                new XAttribute("Version", s_version)))))
            .Save(project);
        await File.WriteAllTextAsync(Path.Combine(directory, "Program.cs"), "System.Console.WriteLine(42);", token);
        foreach (string major in new[] { "17", "18", "" })
        {
            string path = major.Length == 0 ? "" : "postgres " + major + "/pg_config";
            ProcessResult result = await RunDotnetAsync(["build", project, "-p:AnkusPostgresMajor=" + major,
                "-p:AnkusPgConfigPath=" + path], token);
            Assert.AreEqual(0, result.ExitCode, result.StandardOutput + result.StandardError);
            using JsonDocument config = JsonDocument.Parse(await File.ReadAllTextAsync(
                Path.Combine(directory, "bin", "Debug", "net10.0", "Host.runtimeconfig.json"), token));
            JsonElement settings = config.RootElement.GetProperty("runtimeOptions").GetProperty("configProperties");
            if (major.Length == 0)
            {
                Assert.IsFalse(settings.TryGetProperty("Ankus.Testing.PostgresMajor", out _));
                Assert.IsFalse(settings.TryGetProperty("Ankus.Testing.PgConfigPath", out _));
            }
            else
            {
                Assert.AreEqual(major, settings.GetProperty("Ankus.Testing.PostgresMajor").ToString());
                Assert.AreEqual(Path.GetFullPath(path, directory), settings.GetProperty("Ankus.Testing.PgConfigPath").GetString());
            }
        }
    }

    /// <summary>
    /// Metadata-only commands honor evaluated version defaults without requiring a registered server.
    /// </summary>
    /// <param name="command">The metadata-only command.</param>
    [TestMethod]
    [DataRow("get")]
    [DataRow("regress")]
    public async Task OfflineCommandsUseEvaluatedPostgresMajor(string command)
    {
        string root = CreateDirectory();
        string project = Path.Combine(root, "Metadata.csproj");
        await File.WriteAllTextAsync(project, """
            <Project>
              <PropertyGroup>
                <AnkusPostgresMajor>13</AnkusPostgresMajor>
                <AnkusPostgresMajor Condition="'$(Configuration)' == 'Shipping'">17</AnkusPostgresMajor>
                <AnkusExtensionName>selection_pg$(AnkusPostgresMajor)</AnkusExtensionName>
              </PropertyGroup>
            </Project>
            """, context.CancellationToken);
        string[] arguments = command == "get" ? ["get", "extname"] : ["regress", "--dry-run"];
        ProcessResult result = await InvokeAsync([.. arguments, "--home", root, "--project", project, "-c", "Shipping"], context.CancellationToken);

        Assert.AreEqual(0, result.ExitCode, result.StandardOutput + result.StandardError);
        Assert.Contains("selection_pg17", result.StandardOutput);
        Assert.DoesNotContain("selection_pg18", result.StandardOutput);
        Assert.IsFalse(File.Exists(Path.Combine(root, "config.json")));
        Assert.IsFalse(Directory.Exists(Path.Combine(root, "bin")));
    }

    /// <summary>
    /// Creates a packed consumer whose fixture and native SQL both observe the selected server major.
    /// </summary>
    /// <param name="token">Cancels scaffolding and writes.</param>
    /// <returns>The generated solution directory.</returns>
    private static async Task<string> CreateSelectionProjectAsync(CancellationToken token)
    {
        string output = Path.Combine(CreateDirectory(), "selection project");
        (await InvokeAsync(["new", "TestCommandProbe", "-o", output], token)).EnsureSuccess(s_tool, ["new"]);
        string hostDirectory = Path.Combine(output, "tests", "TestCommandProbe.Tests");
        await File.WriteAllTextAsync(Path.Combine(hostDirectory, "SelectionTests.cs"), $$"""
            using Microsoft.VisualStudio.TestTools.UnitTesting;
            using Npgsql;
            namespace TestCommandProbe.Tests;
            public sealed partial class BackendTests
            {
                [TestMethod]
                public async Task SelectedVersionReachesPostgres()
                {
                    Assert.AreEqual({{s_installation.Version.Major.ToString(CultureInfo.InvariantCulture)}}, s_extension!.Cluster.Installation.Version.Major);
                    await using NpgsqlConnection connection = await s_extension.Cluster.OpenConnectionAsync(context.CancellationToken);
                    await using var command = new NpgsqlCommand("SELECT current_setting('server_version_num')::integer / 10000, add(40, 2)", connection);
                    await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(context.CancellationToken);
                    Assert.IsTrue(await reader.ReadAsync(context.CancellationToken));
                    Assert.AreEqual({{s_installation.Version.Major.ToString(CultureInfo.InvariantCulture)}}, reader.GetInt32(0));
                    Assert.AreEqual(42, reader.GetInt32(1));
                }
            }
            """, token);
        return output;
    }

    /// <summary>
    /// Isolates a consumer from the repository validation host's own explicit installation selection.
    /// </summary>
    /// <returns>Child-process environment overrides without changing the current process.</returns>
    private static Dictionary<string, string?> SelectionEnvironment()
        => new(s_environment)
        {
            ["ANKUS_TEST_PG_CONFIG"] = null,
            ["ANKUS_TEST_POSTGRES_MAJOR"] = null,
            ["ANKUS_TEST_CONFIGURATION"] = null,
            ["AnkusPostgresMajor"] = null,
            ["AnkusPgConfigPath"] = null,
        };
}
