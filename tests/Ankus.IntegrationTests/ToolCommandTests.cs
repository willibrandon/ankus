using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Ankus.PgConfig;
using Ankus.Testing;
using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Exercises the packaged tool through its installed command, real PostgreSQL, and Native AOT publishing.
/// </summary>
/// <param name="context">The current test context.</param>
[TestClass]
[DoNotParallelize]
public sealed class ToolCommandTests(TestContext context)
{
    private static string s_root = null!;
    private static string s_tool = null!;
    private static string s_home = null!;
    private static string s_published = null!;
    private static string s_project = null!;
    private static PostgresInstallation s_installation = null!;

    /// <summary>
    /// Packs, installs, and invokes the tool in isolated directories, then publishes an extension through it.
    /// </summary>
    /// <param name="context">The class initialization context.</param>
    [ClassInitialize]
    public static async Task InitializeAsync(TestContext context)
    {
        CancellationToken token = context.CancellationToken;
        string repository = IntegrationEnvironment.RepositoryRoot;
        s_root = Path.Combine(repository, "artifacts", "tool-tests", Guid.NewGuid().ToString("N"));
        s_home = Path.Combine(s_root, "Ankus home");
        s_published = Path.Combine(s_root, "published extension");
        s_installation = await PostgresInstallation.DiscoverAsync(token);
        string feed = Path.Combine(s_root, "feed");
        Directory.CreateDirectory(feed);
        string version = "0.0.0-test." + Guid.NewGuid().ToString("N");
        await ProcessRunner.RunCheckedAsync("dotnet",
            ["pack", Path.Combine(repository, "src", "Ankus.Tool"), "-c", "Release", "-o", feed, "-p:Version=" + version],
            new Dictionary<string, string?>(), token);
        string config = Path.Combine(s_root, "NuGet.Config");
        new XDocument(new XElement("configuration", new XElement("packageSources", new XElement("clear"),
            new XElement("add", new XAttribute("key", "test"), new XAttribute("value", feed))))).Save(config);
        string toolDirectory = Path.Combine(s_root, "installed tool");
        await ProcessRunner.RunCheckedAsync("dotnet",
            ["tool", "install", "Ankus.Tool", "--version", version, "--tool-path", toolDirectory, "--configfile", config],
            new Dictionary<string, string?>(), token);
        s_tool = Path.Combine(toolDirectory, OperatingSystem.IsWindows() ? "ankus.exe" : "ankus");
        (await InvokeAsync(["init", "--home", s_home, "--pg18", s_installation.PgConfigPath], token))
            .EnsureSuccess(s_tool, ["init"]);

        string projectDirectory = Path.Combine(s_root, "author project");
        Directory.CreateDirectory(projectDirectory);
        s_project = Path.Combine(projectDirectory, "ToolProbe.csproj");
        new XDocument(new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
            new XElement("PropertyGroup", new XElement("AnkusExtensionName", "ankus_tool_probe")),
            new XElement("Import", new XAttribute("Project", Path.Combine(repository, "src", "Ankus.Sdk", "Ankus.Sdk.targets")))))
            .Save(s_project);
        File.Copy(Path.Combine(repository, "samples", "Ankus.Examples.Hello", "Hello.cs"), Path.Combine(projectDirectory, "Hello.cs"));
        (await InvokeAsync(["publish", "--home", s_home, "--project", s_project, "--output", s_published], token))
            .EnsureSuccess(s_tool, ["publish"]);
    }

    /// <summary>
    /// Removes the isolated tool installation and project after every child process has exited.
    /// </summary>
    [ClassCleanup]
    public static void Cleanup()
    {
        if (s_root is not null && Directory.Exists(s_root))
        {
            Directory.Delete(s_root, recursive: true);
        }
    }

    /// <summary>
    /// Verifies the installed command supplies System.CommandLine help for each implemented operation.
    /// </summary>
    /// <param name="command">The subcommand to inspect.</param>
    /// <param name="expected">An option or command that must appear in its help.</param>
    [TestMethod]
    [DataRow("", "init")]
    [DataRow("init", "--pg19")]
    [DataRow("info", "--pg-config")]
    [DataRow("build", "--project")]
    [DataRow("publish", "--output")]
    [DataRow("install", "--destdir")]
    public async Task InstalledToolProvidesHelp(string command, string expected)
    {
        string[] arguments = command.Length == 0 ? ["--help"] : [command, "--help"];
        ProcessResult result = await InvokeAsync(arguments, context.CancellationToken);
        Assert.AreEqual(0, result.ExitCode, result.StandardError);
        Assert.Contains("ankus", result.StandardOutput);
        Assert.Contains(expected, result.StandardOutput);
        Assert.IsEmpty(result.StandardError);
    }

    /// <summary>
    /// Verifies initialization preserves unrelated registrations and writes the executable's absolute path.
    /// </summary>
    [TestMethod]
    public async Task InitPreservesSettingsAndInfoUsesRegistration()
    {
        string home = CreateDirectory();
        string configuration = Path.Combine(home, "config.json");
        await File.WriteAllTextAsync(configuration, "{\"pg17\":\"other/pg_config\",\"port\":28800}", context.CancellationToken);
        ProcessResult registered = await InvokeAsync(["init", "--home", home, "--pg18", s_installation.PgConfigPath],
            context.CancellationToken);
        Assert.AreEqual(0, registered.ExitCode, registered.StandardError);
        using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(configuration, context.CancellationToken));
        Assert.AreEqual(s_installation.PgConfigPath, document.RootElement.GetProperty("pg18").GetString());
        Assert.AreEqual("other/pg_config", document.RootElement.GetProperty("pg17").GetString());
        Assert.AreEqual(28800, document.RootElement.GetProperty("port").GetInt32());
        ProcessResult info = await InvokeAsync(["info", "--home", home], context.CancellationToken);
        Assert.AreEqual(0, info.ExitCode, info.StandardError);
        Assert.Contains("pg_config: " + s_installation.PgConfigPath, info.StandardOutput);
        Assert.Contains("Server headers: " + s_installation.ServerIncludeDirectory, info.StandardOutput);
    }

    /// <summary>
    /// Verifies a bad registration leaves the previous configuration byte-for-byte intact, even after validating a good entry.
    /// </summary>
    [TestMethod]
    public async Task InitRejectsWrongMajorWithoutPartialUpdate()
    {
        string home = CreateDirectory();
        string configuration = Path.Combine(home, "config.json");
        const string Original = "{ \"pg13\": \"keep this\" }\n";
        await File.WriteAllTextAsync(configuration, Original, context.CancellationToken);
        ProcessResult result = await InvokeAsync(
            ["init", "--home", home, "--pg18", s_installation.PgConfigPath, "--pg19", s_installation.PgConfigPath],
            context.CancellationToken);
        Assert.AreEqual(1, result.ExitCode);
        Assert.Contains("not PostgreSQL 19", result.StandardError);
        Assert.AreEqual(Original, await File.ReadAllTextAsync(configuration, context.CancellationToken));
    }

    /// <summary>
    /// Verifies initialization does not overwrite malformed or non-object configuration.
    /// </summary>
    /// <param name="original">The invalid configuration text.</param>
    [TestMethod]
    [DataRow("{broken")]
    [DataRow("[]")]
    public async Task InitPreservesInvalidConfiguration(string original)
    {
        string home = CreateDirectory();
        string configuration = Path.Combine(home, "config.json");
        await File.WriteAllTextAsync(configuration, original, context.CancellationToken);
        ProcessResult result = await InvokeAsync(["init", "--home", home, "--pg18", s_installation.PgConfigPath],
            context.CancellationToken);
        Assert.AreEqual(1, result.ExitCode);
        Assert.AreEqual(original, await File.ReadAllTextAsync(configuration, context.CancellationToken));
    }

    /// <summary>
    /// Verifies registered selection never silently falls back to a discoverable system PostgreSQL.
    /// </summary>
    [TestMethod]
    public async Task MissingRegistrationRequiresInitButExplicitSelectionWorks()
    {
        string home = CreateDirectory();
        ProcessResult missing = await InvokeAsync(["info", "--home", home], context.CancellationToken);
        Assert.AreEqual(1, missing.ExitCode);
        Assert.Contains("ankus init --pg18", missing.StandardError);
        ProcessResult explicitPath = await InvokeAsync(["info", "--home", home, "--pg-config", s_installation.PgConfigPath],
            context.CancellationToken);
        Assert.AreEqual(0, explicitPath.ExitCode, explicitPath.StandardError);
        Assert.Contains(s_installation.PgConfigPath, explicitPath.StandardOutput);
        Assert.IsFalse(File.Exists(Path.Combine(home, "config.json")));
    }

    /// <summary>
    /// Verifies stale registrations fail rather than silently selecting a different installation.
    /// </summary>
    [TestMethod]
    public async Task StaleRegistrationFails()
    {
        string home = CreateDirectory();
        await File.WriteAllTextAsync(Path.Combine(home, "config.json"), "{\"pg18\":\"missing/pg_config\"}",
            context.CancellationToken);
        ProcessResult result = await InvokeAsync(["info", "--home", home], context.CancellationToken);
        Assert.AreEqual(1, result.ExitCode);
        Assert.Contains("pg_config executable was not found", result.StandardError);
    }

    /// <summary>
    /// Verifies parsing and selection failures return nonzero exit codes without creating configuration.
    /// </summary>
    /// <param name="command">The command to invoke.</param>
    /// <param name="option">An invalid option or incomplete selection.</param>
    [TestMethod]
    [DataRow("init", "--pg18")]
    [DataRow("init", "--unknown")]
    [DataRow("info", "--pg=12")]
    [DataRow("info", "--pg=20")]
    [DataRow("publish", "--output")]
    public async Task InvalidArgumentsDoNotMutateConfiguration(string command, string option)
    {
        string home = CreateDirectory();
        ProcessResult result = await InvokeAsync([command, "--home", home, option], context.CancellationToken);
        Assert.AreNotEqual(0, result.ExitCode);
        Assert.IsNotEmpty(result.StandardError);
        Assert.IsFalse(File.Exists(Path.Combine(home, "config.json")));
    }

    /// <summary>
    /// Verifies publishing and staging through the installed tool produce a loadable PostgreSQL extension.
    /// </summary>
    [TestMethod]
    public async Task PublishedAndInstalledExtensionExecutesInPostgres()
    {
        CancellationToken token = context.CancellationToken;
        PublishedExtension manifest = PublishedExtension.Read(s_published);
        Assert.AreEqual(18, manifest.PostgresMajor);
        Assert.AreEqual(RuntimeInformation.RuntimeIdentifier, manifest.RuntimeIdentifier);
        Assert.AreEqual("ankus_tool_probe.control", manifest.Control);
        Assert.AreEqual("ankus_tool_probe--1.0.0.sql", manifest.Sql);
        string stage = CreateDirectory();
        ProcessResult result = await InvokeAsync(["install", "--home", s_home, "--from", s_published, "--destdir", stage], token);
        Assert.AreEqual(0, result.ExitCode, result.StandardError);
        string libraries = StagedPath(stage, s_installation.LibraryDirectory);
        string shared = StagedPath(stage, s_installation.SharedDirectory);
        string installedLibrary = Path.Combine(libraries, manifest.Library);
        Assert.AreSequenceEqual(await File.ReadAllBytesAsync(Path.Combine(s_published, manifest.Library), token),
            await File.ReadAllBytesAsync(installedLibrary, token));
        Assert.AreEqual(await File.ReadAllTextAsync(Path.Combine(s_published, "extension", manifest.Control), token),
            await File.ReadAllTextAsync(Path.Combine(shared, "extension", manifest.Control), token));
        Assert.HasCount(3, Directory.GetFiles(stage, "*", SearchOption.AllDirectories));
        await using PostgresTestCluster cluster = await PostgresTestCluster.StartAsync(new PostgresTestClusterOptions
        {
            Installation = s_installation,
            DataDirectoryBase = Path.Combine(s_root, "pgdata"),
            LogDirectory = Path.Combine(IntegrationEnvironment.RepositoryRoot, "artifacts", "test-logs"),
            PostgreSqlConfiguration =
            [
                "extension_control_path = '" + EscapeSetting(shared) + "'",
                "dynamic_library_path = '" + EscapeSetting(libraries) + "'",
            ],
        }, token);
        await using NpgsqlConnection connection = await cluster.OpenConnectionAsync(token);
        await using var command = new NpgsqlCommand("CREATE EXTENSION ankus_tool_probe; SELECT public.add(40, 2)", connection);
        Assert.AreEqual(42, await command.ExecuteScalarAsync(token));
        command.CommandText = "SELECT public.greet('工具')";
        Assert.AreEqual("Hello, 工具!", await command.ExecuteScalarAsync(token));
    }

    /// <summary>
    /// Verifies incompatible targets, unsafe filenames, and missing payloads fail before any installation files are copied.
    /// </summary>
    /// <param name="failure">The artifact inconsistency to introduce.</param>
    [TestMethod]
    [DataRow("major")]
    [DataRow("runtime")]
    [DataRow("path")]
    [DataRow("missing")]
    public async Task InvalidArtifactDoesNotPartiallyInstall(string failure)
    {
        string source = CreateDirectory();
        Directory.CreateDirectory(Path.Combine(source, "extension"));
        foreach (string path in Directory.GetFiles(s_published, "*", SearchOption.AllDirectories))
        {
            string destination = Path.Combine(source, Path.GetRelativePath(s_published, path));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(path, destination);
        }

        string manifestPath = Path.Combine(source, PublishedExtension.FileName);
        JsonNode manifest = JsonNode.Parse(await File.ReadAllTextAsync(manifestPath, context.CancellationToken))!;
        switch (failure)
        {
            case "major": manifest["postgresMajor"] = 17; break;
            case "runtime": manifest["runtimeIdentifier"] = "wrong-architecture"; break;
            case "path": manifest["library"] = "../outside.so"; break;
            case "missing": File.Delete(Path.Combine(source, "extension", manifest["sql"]!.GetValue<string>())); break;
        }

        await File.WriteAllTextAsync(manifestPath, manifest.ToJsonString(), context.CancellationToken);
        string stage = CreateDirectory();
        ProcessResult result = await InvokeAsync(["install", "--home", s_home, "--from", source, "--destdir", stage],
            context.CancellationToken);
        Assert.AreEqual(1, result.ExitCode);
        Assert.IsNotEmpty(result.StandardError);
        Assert.IsEmpty(Directory.GetFiles(stage, "*", SearchOption.AllDirectories));
    }

    /// <summary>
    /// Verifies a compiler failure propagates to the CLI and invalidates any prior install manifest.
    /// </summary>
    [TestMethod]
    public async Task FailedBuildDoesNotLeaveInstallableManifest()
    {
        string project = CreateDirectory();
        File.Copy(s_project, Path.Combine(project, "Broken.csproj"));
        await File.WriteAllTextAsync(Path.Combine(project, "Broken.cs"), "#error Intentional tool test failure", context.CancellationToken);
        string output = CreateDirectory();
        File.Copy(Path.Combine(s_published, PublishedExtension.FileName), Path.Combine(output, PublishedExtension.FileName));
        ProcessResult result = await InvokeAsync(["build", "--home", s_home, "--project", project, "--output", output],
            context.CancellationToken);
        Assert.AreEqual(1, result.ExitCode);
        Assert.Contains("Intentional tool test failure", result.StandardOutput);
        Assert.IsFalse(File.Exists(Path.Combine(output, PublishedExtension.FileName)));
    }

    private static Task<ProcessResult> InvokeAsync(string[] arguments, CancellationToken token)
        => ProcessRunner.RunAsync(s_tool, arguments, new Dictionary<string, string?>(), token);

    private static string CreateDirectory()
    {
        string path = Path.Combine(s_root, "case " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string StagedPath(string stage, string absolute)
        => Path.Combine(stage, absolute[Path.GetPathRoot(absolute)!.Length..]);

    private static string EscapeSetting(string path)
        => path.Replace("\\", "/", StringComparison.Ordinal).Replace("'", "''", StringComparison.Ordinal);
}
