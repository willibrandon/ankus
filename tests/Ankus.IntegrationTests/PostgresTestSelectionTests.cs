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
        ProcessResult result = await ProcessRunner.RunAsync(s_tool,
            ["test", "--home", s_home, .. arguments, "--", "--project", project,
                "--filter", "FullyQualifiedName~SelectedVersionReachesPostgres"],
            SelectionEnvironment(), token, workingDirectory: CreateDirectory());

        Assert.AreEqual(0, result.ExitCode, result.StandardOutput + result.StandardError);
        Assert.Contains($"Testing PostgreSQL {s_installation.Version}", result.StandardOutput);
        Assert.Contains(s_postgresKey + ": dotnet test exited 0.", result.StandardOutput);
        Assert.Contains("succeeded: 1", result.StandardOutput);
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
