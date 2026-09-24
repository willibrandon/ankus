using System.Globalization;
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
public sealed partial class ToolCommandTests(TestContext context)
{
    private const string NativeAotRuntimeVersion = "10.0.11-ankus.2";

    private static string s_root = null!;
    private static string s_tool = null!;
    private static string s_home = null!;
    private static string s_published = null!;
    private static string s_project = null!;
    private static string s_version = null!;
    private static string s_postgresOption = null!;
    private static string s_postgresKey = null!;
    private static Dictionary<string, string?> s_environment = null!;
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
        string temporary = Directory.Exists("/tmp/opencode") ? "/tmp/opencode" : Path.GetTempPath();
        s_root = Path.Combine(temporary, "ankus package tests " + Guid.NewGuid().ToString("N"));
        s_home = Path.Combine(s_root, "Ankus home");
        s_published = Path.Combine(s_root, "published extension");
        s_installation = (await IntegrationEnvironment.CreateOptionsAsync(token)).Installation;
        s_postgresOption = "--pg" + s_installation.Version.Major.ToString(CultureInfo.InvariantCulture);
        s_postgresKey = "pg" + s_installation.Version.Major.ToString(CultureInfo.InvariantCulture);
        string feed = Path.Combine(s_root, "feed");
        Directory.CreateDirectory(feed);
        s_version = "0.0.0-test." + Guid.NewGuid().ToString("N");
        string runtimeIdentifier = RuntimeInformation.RuntimeIdentifier;
        string runtimeSdk = Path.Combine(repository, "artifacts", "nativeaot", runtimeIdentifier, "aotsdk") +
                            Path.DirectorySeparatorChar;
        string runtimeSource = Path.Combine(repository, "artifacts", "nativeaot", runtimeIdentifier, "source");
        await ProcessRunner.RunCheckedAsync("dotnet",
            ["pack", Path.Combine(repository, "src/Ankus.NativeAot.Runtime"), "-c", "Release", "-o", feed,
                "-p:AnkusRuntimeIdentifier=" + runtimeIdentifier, "-p:AnkusRuntimeSdkPath=" + runtimeSdk,
                "-p:AnkusRuntimeSourcePath=" + runtimeSource,
                "-bl:" + Path.Combine(repository, "artifacts", "runtime-package-pack-{}.binlog")],
            new Dictionary<string, string?>(), token);

        string[] projects = ["src/Ankus.Runtime", "src/Ankus.Generators", "src/Ankus.PgConfig",
            "src/Ankus.Sdk", "src/Ankus.Tool", "src/Ankus.Testing"];
        foreach (string project in projects)
        {
            await ProcessRunner.RunCheckedAsync("dotnet",
                ["pack", Path.Combine(repository, project), "-c", "Release", "-o", feed, "-p:Version=" + s_version,
                    "-bl:" + Path.Combine(repository, "artifacts", "package-pack-{}.binlog")],
                new Dictionary<string, string?>(), token);
        }

        s_environment = new Dictionary<string, string?> { ["NUGET_PACKAGES"] = Path.Combine(s_root, "NuGet packages") };
        File.Copy(Path.Combine(repository, "global.json"), Path.Combine(s_root, "global.json"));
        string config = Path.Combine(s_root, "NuGet.Config");
        new XDocument(new XElement("configuration", new XElement("packageSources", new XElement("clear"),
            new XElement("add", new XAttribute("key", "test"), new XAttribute("value", feed)),
            new XElement("add", new XAttribute("key", "nuget.org"), new XAttribute("value", "https://api.nuget.org/v3/index.json")))))
            .Save(config);
        string toolDirectory = Path.Combine(s_root, "installed tool");
        await ProcessRunner.RunCheckedAsync("dotnet",
            ["tool", "install", "Ankus.Tool", "--version", s_version, "--tool-path", toolDirectory, "--configfile", config],
            s_environment, token, workingDirectory: s_root);
        s_tool = Path.Combine(toolDirectory, OperatingSystem.IsWindows() ? "ankus.exe" : "ankus");
        (await InvokeAsync(["init", "--home", s_home, s_postgresOption, s_installation.PgConfigPath], token))
            .EnsureSuccess(s_tool, ["init"]);

        string projectDirectory = Path.Combine(s_root, "author project");
        Directory.CreateDirectory(projectDirectory);
        s_project = Path.Combine(projectDirectory, "ToolProbe.csproj");
        new XDocument(new XElement("Project", new XAttribute("Sdk", "Ankus.Sdk/" + s_version),
            new XElement("PropertyGroup", new XElement("TargetFramework", "net10.0"),
                new XElement("Nullable", "enable"), new XElement("ImplicitUsings", "enable"),
                new XElement("TreatWarningsAsErrors", "true"), new XElement("AnkusExtensionName", "ankus_tool_probe"))))
            .Save(s_project);
        File.Copy(Path.Combine(repository, "samples", "Ankus.Examples.Hello", "Hello.cs"), Path.Combine(projectDirectory, "Hello.cs"));
        File.Copy(Path.Combine(repository, "samples", "Ankus.Examples.Initialization", "Startup.cs"),
            Path.Combine(projectDirectory, "Startup.cs"));
        (await InvokeAsync(
            ["publish", "--home", s_home, "--pg", MajorText(), "--project", s_project, "--output", s_published], token))
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
    [DataRow("new", "--extension-name")]
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
        int otherMajor = DifferentMajor();
        string otherKey = "pg" + otherMajor.ToString(CultureInfo.InvariantCulture);
        await File.WriteAllTextAsync(configuration, $"{{\"{otherKey}\":\"other/pg_config\",\"port\":28800}}", context.CancellationToken);
        ProcessResult registered = await InvokeAsync(["init", "--home", home, s_postgresOption, s_installation.PgConfigPath],
            context.CancellationToken);
        Assert.AreEqual(0, registered.ExitCode, registered.StandardError);
        using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(configuration, context.CancellationToken));
        Assert.AreEqual(s_installation.PgConfigPath, document.RootElement.GetProperty(s_postgresKey).GetString());
        Assert.AreEqual("other/pg_config", document.RootElement.GetProperty(otherKey).GetString());
        Assert.AreEqual(28800, document.RootElement.GetProperty("port").GetInt32());
        ProcessResult info = await InvokeAsync(["info", "--home", home, "--pg", MajorText()], context.CancellationToken);
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
        int wrongMajor = DifferentMajor();
        string wrongOption = "--pg" + wrongMajor.ToString(CultureInfo.InvariantCulture);
        ProcessResult result = await InvokeAsync(
            ["init", "--home", home, s_postgresOption, s_installation.PgConfigPath, wrongOption, s_installation.PgConfigPath],
            context.CancellationToken);
        Assert.AreEqual(1, result.ExitCode);
        Assert.Contains("not PostgreSQL " + wrongMajor.ToString(CultureInfo.InvariantCulture), result.StandardError);
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
        ProcessResult result = await InvokeAsync(["init", "--home", home, s_postgresOption, s_installation.PgConfigPath],
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
        ProcessResult explicitPath = await InvokeAsync(
            ["info", "--home", home, "--pg", MajorText(), "--pg-config", s_installation.PgConfigPath],
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
    /// Verifies package publishing, staging, shared preload, and managed calls in PostgreSQL.
    /// </summary>
    [TestMethod]
    public async Task PublishedInstalledAndPreloadedExtensionExecutesInPostgres()
    {
        CancellationToken token = context.CancellationToken;
        PublishedExtension manifest = PublishedExtension.Read(s_published);
        Assert.AreEqual(s_installation.Version.Major, manifest.PostgresMajor);
        Assert.AreEqual(RuntimeInformation.RuntimeIdentifier, manifest.RuntimeIdentifier);
        Assert.AreEqual("ankus_tool_probe.control", manifest.Control);
        Assert.AreEqual("ankus_tool_probe--1.0.0.sql", manifest.Sql);
        string stage = CreateDirectory();
        ProcessResult result = await InvokeAsync(
            ["install", "--home", s_home, "--pg", MajorText(), "--from", s_published, "--destdir", stage], token);
        Assert.AreEqual(0, result.ExitCode, result.StandardError);
        string libraries = StagedPath(stage, s_installation.LibraryDirectory);
        string shared = StagedPath(stage, s_installation.SharedDirectory);
        string installedLibrary = Path.Combine(libraries, manifest.Library);
        Assert.AreSequenceEqual(await File.ReadAllBytesAsync(Path.Combine(s_published, manifest.Library), token),
            await File.ReadAllBytesAsync(installedLibrary, token));
        Assert.AreEqual(await File.ReadAllTextAsync(Path.Combine(s_published, "extension", manifest.Control), token),
            await File.ReadAllTextAsync(Path.Combine(shared, "extension", manifest.Control), token));
        Assert.HasCount(3, Directory.GetFiles(stage, "*", SearchOption.AllDirectories));
        s_installation = await IntegrationEnvironment.PrepareExtensionInstallationAsync(s_published, token);
        List<string> configuration = ["dynamic_library_path = '" + EscapeSetting(libraries) + "'"];
        if (s_installation.Version.Major >= 18)
        {
            configuration.Insert(0, "extension_control_path = '" + EscapeSetting(shared) + "'");
        }

        await using PostgresTestCluster cluster = await PostgresTestCluster.StartAsync(new PostgresTestClusterOptions
        {
            Installation = s_installation,
            DataDirectoryBase = Path.Combine(s_root, "pgdata"),
            LogDirectory = Path.Combine(IntegrationEnvironment.RepositoryRoot, "artifacts", "test-logs"),
            PostgreSqlConfiguration = [.. configuration, "shared_preload_libraries = '" + manifest.Library + "'"],
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
            case "major": manifest["postgresMajor"] = DifferentMajor(); break;
            case "runtime": manifest["runtimeIdentifier"] = "wrong-architecture"; break;
            case "path": manifest["library"] = "../outside.so"; break;
            case "missing": File.Delete(Path.Combine(source, "extension", manifest["sql"]!.GetValue<string>())); break;
        }

        await File.WriteAllTextAsync(manifestPath, manifest.ToJsonString(), context.CancellationToken);
        string stage = CreateDirectory();
        ProcessResult result = await InvokeAsync(
            ["install", "--home", s_home, "--pg", MajorText(), "--from", source, "--destdir", stage],
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
        ProcessResult result = await InvokeAsync(
            ["build", "--home", s_home, "--pg", MajorText(), "--project", project, "--output", output],
            context.CancellationToken);
        Assert.AreEqual(1, result.ExitCode);
        Assert.Contains("Intentional tool test failure", result.StandardOutput);
        Assert.IsFalse(File.Exists(Path.Combine(output, PublishedExtension.FileName)));
    }

    /// <summary>
    /// Verifies a cold SDK restore supplies only package dependencies, with Native AOT and the analyzer active.
    /// </summary>
    [TestMethod]
    public async Task SdkRestoresWithoutRepositoryReferences()
    {
        Assert.IsFalse(s_project.StartsWith(IntegrationEnvironment.RepositoryRoot, StringComparison.Ordinal));
        ProcessResult evaluated = await RunDotnetAsync(["msbuild", s_project, "-target:ResolveReferences", "-verbosity:quiet",
            "-bl:" + Path.Combine(s_root, "references-{}.binlog"),
            "-getProperty:PublishAot,IsAotCompatible,NativeLib,_AnkusBuildTool", "-getItem:ProjectReference,Analyzer"],
            context.CancellationToken);
        evaluated.EnsureSuccess("dotnet", ["msbuild"]);
        using JsonDocument document = JsonDocument.Parse(evaluated.StandardOutput);
        JsonElement properties = document.RootElement.GetProperty("Properties");
        Assert.AreEqual("true", properties.GetProperty("PublishAot").GetString());
        Assert.AreEqual("true", properties.GetProperty("IsAotCompatible").GetString());
        Assert.AreEqual("Shared", properties.GetProperty("NativeLib").GetString());
        string helper = Path.GetFullPath(properties.GetProperty("_AnkusBuildTool").GetString()!);
        Assert.StartsWith(s_environment["NUGET_PACKAGES"]!, helper);
        Assert.IsTrue(File.Exists(helper));
        Assert.AreEqual(0, document.RootElement.GetProperty("Items").GetProperty("ProjectReference").GetArrayLength());
        JsonElement analyzer = document.RootElement.GetProperty("Items").GetProperty("Analyzer").EnumerateArray()
            .Single(item => Path.GetFileName(item.GetProperty("Identity").GetString()) == "Ankus.Generators.dll");
        Assert.StartsWith(s_environment["NUGET_PACKAGES"]!, analyzer.GetProperty("FullPath").GetString());
        using JsonDocument assets = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(Path.GetDirectoryName(s_project)!, "obj", "project.assets.json"), context.CancellationToken));
        JsonElement libraries = assets.RootElement.GetProperty("libraries");
        Assert.AreEqual("package", libraries.GetProperty("Ankus.Runtime/" + s_version).GetProperty("type").GetString());
        Assert.AreEqual("package", libraries.GetProperty("Ankus.Generators/" + s_version).GetProperty("type").GetString());
        Assert.AreEqual("package", libraries.GetProperty("Ankus.NativeAot.Runtime." + RuntimeInformation.RuntimeIdentifier + "/" +
            NativeAotRuntimeVersion).GetProperty("type").GetString());
        foreach (JsonProperty library in libraries.EnumerateObject())
        {
            Assert.AreEqual("package", library.Value.GetProperty("type").GetString(), library.Name);
        }

        string[] deployedAssemblies = Directory.GetFiles(s_published, "Ankus.*.dll");
        Assert.IsEmpty(deployedAssemblies, "Build-time assemblies must not ship alongside the Native AOT extension.");
    }

    /// <summary>
    /// Verifies direct dotnet publish under central package management uses the SDK and requested extension version.
    /// </summary>
    [TestMethod]
    public async Task SdkSupportsDirectPublishWithCentralPackages()
    {
        string projectDirectory = CreateDirectory();
        string project = Path.Combine(projectDirectory, "DirectProbe.csproj");
        XDocument projectFile = XDocument.Load(s_project);
        projectFile.Root!.SetAttributeValue("Sdk", "Ankus.Sdk");
        projectFile.Save(project);
        JsonNode global = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(s_root, "global.json"), context.CancellationToken))!;
        global["msbuild-sdks"]!["Ankus.Sdk"] = s_version;
        await File.WriteAllTextAsync(Path.Combine(projectDirectory, "global.json"), global.ToJsonString(), context.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Directory.Packages.props"),
            "<Project><PropertyGroup><ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally></PropertyGroup></Project>",
            context.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Functions.cs"), """
            using Ankus;
            [assembly: PgSql("package-pair", "CREATE TYPE package_pair AS (name text, amount integer);")]
            public static class Functions
            {
                [PgFunction]
                public static string PackageEcho(string value) => value + " from NuGet";

                [PgFunction]
                public static PgArray<int?> PackageArray(PgArray<int?> value)
                    => Spi.ExecuteScalar<PgArray<int?>>("SELECT $1", SpiParameter.Create(value));

                [PgFunction]
                public static byte[]?[] PackageBinary(params byte[]?[] values) => values;

                [PgFunction(Requires = ["package-pair"])]
                [return: PgCompositeType("package_pair")]
                public static PgHeapTuple PackageComposite([PgCompositeType("package_pair")] PgHeapTuple value)
                {
                    SpiParameter parameter = SpiParameter.Create(value);
                    using SpiPreparedStatement plan = Spi.PrepareWithTypeOids("SELECT $1", parameter.TypeOid);
                    PgHeapTuple copy = plan.ExecuteScalar<PgHeapTuple>(parameter);
                    copy.Set("amount", copy.Get<int>("amount") + 1);
                    return copy;
                }

                [PgFunction(Requires = ["package-pair"])]
                [return: PgCompositeType("package_pair")]
                public static PgArray<PgHeapTuple?> PackageCompositeArray([PgCompositeType("package_pair")] PgArray<PgHeapTuple?> value)
                    => Spi.ExecuteScalar<PgArray<PgHeapTuple?>>("SELECT $1", SpiParameter.Create(value));

                [PgSchema("package_contract")]
                public static class Fixed
                {
                    [PgEnum]
                    public enum PackageCode { First = 17, Last = 25 }

                    [PgOperator("@+")]
                    public static int AddCode(PackageCode left, int right) => checked((int)left + right);

                    [PgCast]
                    public static int CodeValue(PackageCode value) => (int)value;

                    [PgFunction]
                    public static IEnumerable<(PackageCode Code, int NumericValue)> PackageRows()
                        => [(PackageCode.First, 17), (PackageCode.Last, 25)];

                    [PgFunction(Volatility = PgVolatility.Immutable, ParallelSafety = PgParallelSafety.Safe, Cost = 2.5)]
                    public static int PackageDefault(int inputValue = 41) => inputValue + 1;
                }
            }
            """, context.CancellationToken);
        string output = Path.Combine(projectDirectory, "published");
        ProcessResult result = await RunDotnetAsync(["publish", project, "-c", "Release", "-r", RuntimeInformation.RuntimeIdentifier,
            "-o", output, "-p:AnkusPostgresMajor=" + MajorText(), "-p:AnkusPgConfigPath=" + s_installation.PgConfigPath,
            "-p:AnkusExtensionVersion=2.3.4", "-bl:" + Path.Combine(projectDirectory, "publish-{}.binlog")], context.CancellationToken);
        result.EnsureSuccess("dotnet", ["publish"]);
        PublishedExtension manifest = PublishedExtension.Read(output);
        Assert.AreEqual("ankus_tool_probe--2.3.4.sql", manifest.Sql);
        await using PostgresTestCluster cluster = await StartPublishedClusterAsync(output, context.CancellationToken);
        await using NpgsqlConnection connection = await cluster.OpenConnectionAsync(context.CancellationToken);
        await using var command = new NpgsqlCommand("CREATE EXTENSION ankus_tool_probe; SELECT package_echo('直接')", connection);
        Assert.AreEqual("直接 from NuGet", await command.ExecuteScalarAsync(context.CancellationToken));
        command.CommandText = "SELECT extversion FROM pg_extension WHERE extname = 'ankus_tool_probe'";
        Assert.AreEqual("2.3.4", await command.ExecuteScalarAsync(context.CancellationToken));
        command.CommandText = "SELECT package_array('[-1:0][2:3]={{1,NULL},{-2,3}}'::int[])::text";
        Assert.AreEqual("[-1:0][2:3]={{1,NULL},{-2,3}}", await command.ExecuteScalarAsync(context.CancellationToken));
        command.CommandText = "SELECT encode((package_binary(decode('0001ff','hex'), NULL))[1], 'hex')";
        Assert.AreEqual("0001ff", await command.ExecuteScalarAsync(context.CancellationToken));
        command.CommandText = "SELECT name || ':' || amount FROM package_composite(ROW('owned',41)::package_pair)";
        Assert.AreEqual("owned:42", await command.ExecuteScalarAsync(context.CancellationToken));
        command.CommandText = """
            SELECT array_send(package_composite_array(ARRAY[NULL,ROW('nested',3)::package_pair]))
                = array_send(ARRAY[NULL,ROW('nested',3)::package_pair])
            """;
        Assert.IsTrue(Assert.IsInstanceOfType<bool>(await command.ExecuteScalarAsync(context.CancellationToken)));
        command.CommandText = "SELECT package_contract.package_default()";
        Assert.AreEqual(42, await command.ExecuteScalarAsync(context.CancellationToken));
        command.CommandText = "SELECT package_contract.package_default(input_value => 9)";
        Assert.AreEqual(10, await command.ExecuteScalarAsync(context.CancellationToken));
        command.CommandText = "SELECT 'First'::package_contract.package_code OPERATOR(package_contract.@+) 25";
        Assert.AreEqual(42, await command.ExecuteScalarAsync(context.CancellationToken));
        command.CommandText = "SELECT 'Last'::package_contract.package_code::integer";
        Assert.AreEqual(25, await command.ExecuteScalarAsync(context.CancellationToken));
        command.CommandText = "SELECT string_agg(code::text || ':' || numeric_value, ',' ORDER BY numeric_value) FROM package_contract.package_rows()";
        Assert.AreEqual("First:17,Last:25", await command.ExecuteScalarAsync(context.CancellationToken));
        command.CommandText = "SELECT extrelocatable FROM pg_extension WHERE extname = 'ankus_tool_probe'";
        Assert.IsFalse(Assert.IsInstanceOfType<bool>(await command.ExecuteScalarAsync(context.CancellationToken)));
    }

    /// <summary>
    /// Enum-only package consumers publish and install both inhabited and empty enum declarations without functions.
    /// </summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task EnumOnlyPackageCreatesOwnedType(bool empty)
    {
        CancellationToken token = context.CancellationToken;
        string directory = CreateDirectory();
        string project = Path.Combine(directory, "EnumOnly.csproj");
        File.Copy(s_project, project);
        await File.WriteAllTextAsync(Path.Combine(directory, "State.cs"),
            "using Ankus; [PgEnum] public enum State { " + (empty ? string.Empty : "Ready, Complete") + " }", token);
        string output = Path.Combine(directory, "published");
        ProcessResult result = await RunDotnetAsync(["publish", project, "-c", "Release", "-r", RuntimeInformation.RuntimeIdentifier,
            "-o", output, "-p:AnkusPostgresMajor=" + MajorText(), "-p:AnkusPgConfigPath=" + s_installation.PgConfigPath], token);
        result.EnsureSuccess("dotnet", ["publish"]);
        PublishedExtension manifest = PublishedExtension.Read(output);
        await using PostgresTestCluster cluster = await StartPublishedClusterAsync(output, token);
        await using NpgsqlConnection connection = await cluster.OpenConnectionAsync(token);
        await using var command = new NpgsqlCommand($"""
            LOAD '{manifest.Library}';
            CREATE EXTENSION ankus_tool_probe;
            SELECT enum_range(NULL::state)::text[]
            """, connection);
        string[] expected = empty ? [] : ["Ready", "Complete"];
        Assert.AreSequenceEqual(expected, Assert.IsInstanceOfType<string[]>(await command.ExecuteScalarAsync(token)));
        command.CommandText = "DROP EXTENSION ankus_tool_probe; SELECT to_regtype('state') IS NULL";
        Assert.IsTrue(Assert.IsInstanceOfType<bool>(await command.ExecuteScalarAsync(token)));
    }

    /// <summary>
    /// Publishes a package-backed extension containing only a schema declaration and verifies its ownership lifecycle.
    /// </summary>
    [TestMethod]
    public async Task SchemaOnlyPackageCreatesOwnedNamespace()
    {
        CancellationToken token = context.CancellationToken;
        string directory = CreateDirectory();
        string project = Path.Combine(directory, "SchemaOnly.csproj");
        File.Copy(s_project, project);
        await File.WriteAllTextAsync(Path.Combine(directory, "Schema.cs"), """
            using Ankus;
            [PgSchema("package_schema_only")]
            public static class SchemaOnly;
            """, token);
        string output = Path.Combine(directory, "published");
        ProcessResult result = await RunDotnetAsync(["publish", project, "-c", "Release", "-r", RuntimeInformation.RuntimeIdentifier,
            "-o", output, "-p:AnkusPostgresMajor=" + MajorText(), "-p:AnkusPgConfigPath=" + s_installation.PgConfigPath,
            "-bl:" + Path.Combine(directory, "schema-only-{}.binlog")], token);
        result.EnsureSuccess("dotnet", ["publish"]);
        PublishedExtension manifest = PublishedExtension.Read(output);
        await using PostgresTestCluster cluster = await StartPublishedClusterAsync(output, token);
        await using NpgsqlConnection connection = await cluster.OpenConnectionAsync(token);
        await using var command = new NpgsqlCommand($"""
            LOAD '{manifest.Library}';
            CREATE EXTENSION ankus_tool_probe;
            SELECT to_regnamespace('package_schema_only') IS NOT NULL
            """, connection);
        Assert.IsTrue(Assert.IsInstanceOfType<bool>(await command.ExecuteScalarAsync(token)));
        command.CommandText = "DROP EXTENSION ankus_tool_probe; SELECT to_regnamespace('package_schema_only') IS NULL";
        Assert.IsTrue(Assert.IsInstanceOfType<bool>(await command.ExecuteScalarAsync(token)));
    }

    /// <summary>
    /// SQL-only package consumers rebuild when tracked SQL files change, relocate their objects, and roll back failed installation.
    /// </summary>
    [TestMethod]
    public async Task SqlFilePackageRebuildsAndRollsBackFailedInstallation()
    {
        CancellationToken token = context.CancellationToken;
        string directory = CreateDirectory();
        string project = Path.Combine(directory, "SqlOnly.csproj");
        XDocument projectFile = XDocument.Load(s_project);
        projectFile.Root!.Add(new XElement("ItemGroup", new XElement("AdditionalFiles", new XAttribute("Include", "sql setup/seed.sql"))));
        projectFile.Save(project);
        await File.WriteAllTextAsync(Path.Combine(directory, "Installation.cs"), """
            using Ankus;
            [assembly: PgSql("view", "CREATE VIEW sql_view AS SELECT amount + 1 AS answer FROM sql_values;",
                Requires = ["seed"], Relocatable = true)]
            [assembly: PgSqlFile("seed", "sql setup/seed.sql", Relocatable = true)]
            """, token);
        string sqlDirectory = Path.Combine(directory, "sql setup");
        Directory.CreateDirectory(sqlDirectory);
        string sqlFile = Path.Combine(sqlDirectory, "seed.sql");
        string output = Path.Combine(directory, "published");
        foreach (int input in new[] { 41, 52 })
        {
            await File.WriteAllTextAsync(sqlFile, $"CREATE TABLE sql_values(amount int); INSERT INTO sql_values VALUES ({input});", token);
            await PublishAsync();
            PublishedExtension manifest = PublishedExtension.Read(output);
            await using PostgresTestCluster cluster = await StartPublishedClusterAsync(output, token);
            await using NpgsqlConnection connection = await cluster.OpenConnectionAsync(token);
            await using var command = new NpgsqlCommand($"LOAD '{manifest.Library}'; CREATE EXTENSION ankus_tool_probe; SELECT answer FROM sql_view", connection);
            Assert.AreEqual(input + 1, await command.ExecuteScalarAsync(token));
            command.CommandText = "CREATE SCHEMA sql_relocated; ALTER EXTENSION ankus_tool_probe SET SCHEMA sql_relocated; SELECT answer FROM sql_relocated.sql_view";
            Assert.AreEqual(input + 1, await command.ExecuteScalarAsync(token));
            command.CommandText = "DROP EXTENSION ankus_tool_probe; SELECT to_regclass('sql_relocated.sql_values') IS NULL, to_regclass('sql_relocated.sql_view') IS NULL";
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
            Assert.IsTrue(await reader.ReadAsync(token));
            Assert.IsTrue(reader.GetBoolean(0));
            Assert.IsTrue(reader.GetBoolean(1));
        }

        await File.WriteAllTextAsync(sqlFile, "CREATE TABLE sql_values(amount int); INSERT INTO sql_values VALUES (1 / 0);", token);
        await PublishAsync();
        await using (PostgresTestCluster cluster = await StartPublishedClusterAsync(output, token))
        await using (NpgsqlConnection connection = await cluster.OpenConnectionAsync(token))
        await using (var command = new NpgsqlCommand("CREATE EXTENSION ankus_tool_probe", connection))
        {
            PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteNonQueryAsync(token));
            Assert.AreEqual(PostgresErrorCodes.DivisionByZero, error.SqlState);
            command.CommandText = "SELECT to_regclass('sql_values') IS NULL, NOT EXISTS (SELECT FROM pg_extension WHERE extname = 'ankus_tool_probe')";
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
            Assert.IsTrue(await reader.ReadAsync(token));
            Assert.IsTrue(reader.GetBoolean(0));
            Assert.IsTrue(reader.GetBoolean(1));
        }

        async Task PublishAsync()
        {
            ProcessResult result = await RunDotnetAsync(["publish", project, "-c", "Release", "-r", RuntimeInformation.RuntimeIdentifier,
                "-o", output, "-p:AnkusPostgresMajor=" + MajorText(), "-p:AnkusPgConfigPath=" + s_installation.PgConfigPath,
                "-bl:" + Path.Combine(directory, "sql-only-{}.binlog")], token);
            result.EnsureSuccess("dotnet", ["publish"]);
        }
    }

    /// <summary>
    /// Verifies invalid output settings fail rather than producing an unloadable managed or static library.
    /// </summary>
    /// <param name="property">The invalid publish property.</param>
    /// <param name="message">The SDK diagnostic.</param>
    [TestMethod]
    [DataRow("PublishAot=false", "Ankus extensions require PublishAot=true.")]
    [DataRow("NativeLib=Static", "Ankus extensions require OutputType=Library and NativeLib=Shared.")]
    public async Task SdkRejectsNonExtensionPublishSettings(string property, string message)
    {
        string output = CreateDirectory();
        ProcessResult result = await RunDotnetAsync(["publish", s_project, "-c", "Release", "-r", RuntimeInformation.RuntimeIdentifier,
            "-o", output, "-p:" + property, "-bl:" + Path.Combine(output, "rejected-{}.binlog")], context.CancellationToken);
        Assert.AreNotEqual(0, result.ExitCode);
        Assert.Contains(message, result.StandardOutput);
        Assert.IsFalse(File.Exists(Path.Combine(output, PublishedExtension.FileName)));
    }

    /// <summary>
    /// Verifies the published testing package works in a separately restored MSTest project with ordinary discovery.
    /// </summary>
    [TestMethod]
    public async Task TestingPackageRunsInIndependentMSTestProject()
    {
        s_installation = await IntegrationEnvironment.PrepareExtensionInstallationAsync(
            s_published,
            context.CancellationToken);
        string projectDirectory = CreateDirectory();
        string project = Path.Combine(projectDirectory, "ConsumerTests.csproj");
        string controlSetting = s_installation.Version.Major >= 18
            ? JsonSerializer.Serialize("extension_control_path = '" + EscapeSetting(s_published) + "'") + ","
            : string.Empty;
        new XDocument(new XElement("Project", new XAttribute("Sdk", "MSTest.Sdk"),
            new XElement("PropertyGroup", new XElement("TargetFramework", "net10.0"),
                new XElement("ImplicitUsings", "enable"), new XElement("Nullable", "enable")),
            new XElement("ItemGroup", new XElement("PackageReference", new XAttribute("Include", "Ankus.Testing"),
                new XAttribute("Version", s_version))))).Save(project);
        await File.WriteAllTextAsync(Path.Combine(projectDirectory, "PackageTests.cs"), $$"""
            using Ankus.PgConfig;
            using Ankus.Testing;
            using Microsoft.VisualStudio.TestTools.UnitTesting;
            using Npgsql;
            [TestClass]
            public sealed class PackageTests
            {
                [TestMethod]
                public async Task PackagedClusterLoadsNativeExtension()
                {
                    var installation = await PostgresInstallation.CreateAsync({{JsonSerializer.Serialize(s_installation.PgConfigPath)}});
                    await using var cluster = await PostgresTestCluster.StartAsync(new PostgresTestClusterOptions
                    {
                        Installation = installation,
                        PostgreSqlConfiguration =
                        [
                            {{controlSetting}}
                            {{JsonSerializer.Serialize("dynamic_library_path = '" + EscapeSetting(s_published) + "'")}},
                        ],
                    });
                    await using var connection = await cluster.OpenConnectionAsync();
                    await using var command = new NpgsqlCommand("CREATE EXTENSION ankus_tool_probe; SELECT public.add(19, 23)", connection);
                    Assert.AreEqual(42, await command.ExecuteScalarAsync());
                    command.CommandText = "SELECT public.add(2147483647, 1)";
                    var error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync());
                    Assert.AreEqual("38000", error.SqlState);
                    command.CommandText = "SELECT public.greet('package')";
                    Assert.AreEqual("Hello, package!", await command.ExecuteScalarAsync());
                }
            }
            """, context.CancellationToken);
        ProcessResult result = await ProcessRunner.RunAsync("dotnet", ["test", "-bl:tests-{}.binlog", "--report-trx"],
            s_environment, context.CancellationToken, workingDirectory: projectDirectory);
        result.EnsureSuccess("dotnet", ["test"]);
        string trx = Directory.GetFiles(projectDirectory, "*.trx", SearchOption.AllDirectories).Single();
        XDocument report = XDocument.Load(trx);
        XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
        XElement counters = report.Descendants(ns + "Counters").Single();
        Assert.AreEqual("1", counters.Attribute("total")!.Value);
        Assert.AreEqual("1", counters.Attribute("passed")!.Value);
        Assert.AreEqual("0", counters.Attribute("failed")!.Value);
        Assert.AreEqual("PackagedClusterLoadsNativeExtension", report.Descendants(ns + "UnitTestResult").Single().Attribute("testName")!.Value);
    }

    /// <summary>
    /// Checks a generated package-only solution discovers and runs managed and native tests with plain dotnet test.
    /// </summary>
    [TestMethod]
    public async Task NewSolutionRunsManagedAndBackendTests()
    {
        CancellationToken token = context.CancellationToken;
        string output = Path.Combine(CreateDirectory(), "new solution with spaces");
        ProcessResult created = await InvokeAsync(["new", "Acme.HTTPProbe", "-o", output], token);
        created.EnsureSuccess(s_tool, ["new"]);
        using JsonDocument global = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output, "global.json"), token));
        Assert.AreEqual(s_version, global.RootElement.GetProperty("msbuild-sdks").GetProperty("Ankus.Sdk").GetString());
        Assert.AreEqual("Microsoft.Testing.Platform", global.RootElement.GetProperty("test").GetProperty("runner").GetString());
        string project = Path.Combine(output, "src", "Acme.HTTPProbe", "Acme.HTTPProbe.csproj");
        Assert.AreEqual("acme_http_probe", XDocument.Load(project).Descendants("AnkusExtensionName").Single().Value);
        XDocument packages = XDocument.Load(Path.Combine(output, "Directory.Packages.props"));
        Assert.AreEqual(s_version, packages.Descendants("PackageVersion").Single(element => (string?)element.Attribute("Include") == "Ankus.Testing").Attribute("Version")!.Value);
        Assert.IsFalse(File.Exists(Path.Combine(output, ".editorconfig")));
        Assert.IsTrue(File.Exists(Path.Combine(output, ".gitignore")));
        ProcessResult listing = await ProcessRunner.RunCheckedAsync("dotnet", ["sln", "Acme.HTTPProbe.slnx", "list"],
            s_environment, token, workingDirectory: output);
        Assert.Contains("Acme.HTTPProbe.Tests.csproj", listing.StandardOutput);

        ProcessResult tests = await ProcessRunner.RunAsync("dotnet",
            ["test", "--report-trx", "-bl:generated-tests-{}.binlog", "-p:AnkusPostgresMajor=" + MajorText()],
            s_environment, token, workingDirectory: output);
        tests.EnsureSuccess("dotnet", ["test"]);
        string trx = Directory.GetFiles(output, "*.trx", SearchOption.AllDirectories).Single();
        XDocument report = XDocument.Load(trx);
        XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
        XElement counters = report.Descendants(ns + "Counters").Single();
        Assert.AreEqual("5", counters.Attribute("total")!.Value);
        Assert.AreEqual("5", counters.Attribute("passed")!.Value);
        Assert.AreEqual("0", counters.Attribute("failed")!.Value);
        Assert.Contains("FunctionsExecuteInPostgres", report.Descendants(ns + "UnitTestResult").Select(element => element.Attribute("testName")!.Value));
        Assert.Contains("ManagedErrorsLeaveBackendUsable", report.Descendants(ns + "UnitTestResult").Select(element => element.Attribute("testName")!.Value));
        string projectDirectory = Path.GetDirectoryName(project)!;
        Assert.IsEmpty(Directory.GetDirectories(Path.Combine(projectDirectory, "bin", "ankus-test-pgdata")));
        Assert.IsEmpty(Directory.GetDirectories(Path.Combine(projectDirectory, "bin", "ankus-test-publish")));
        Assert.IsNotEmpty(Directory.GetFiles(Path.Combine(projectDirectory, "bin", "ankus-test-logs"), "*.log"));

        string published = Path.Combine(output, "published");
        ProcessResult publish = await ProcessRunner.RunAsync(s_tool,
            ["publish", "--home", s_home, "--pg", MajorText(), "-o", published],
            s_environment, token, workingDirectory: output);
        publish.EnsureSuccess(s_tool, ["publish"]);
        Assert.AreEqual("acme_http_probe.control", PublishedExtension.Read(published).Control);
        await using PostgresTestCluster cluster = await StartPublishedClusterAsync(published, token);
        await using NpgsqlConnection connection = await cluster.OpenConnectionAsync(token);
        await using var command = new NpgsqlCommand("CREATE EXTENSION acme_http_probe; SELECT add(17, 25)", connection);
        Assert.AreEqual(42, await command.ExecuteScalarAsync(token));

        string functions = Path.Combine(projectDirectory, "Functions.cs");
        string text = await File.ReadAllTextAsync(functions, token);
        await File.WriteAllTextAsync(functions, text.Replace("checked(left + right)", "checked(left - right)", StringComparison.Ordinal), token);
        ProcessResult changed = await ProcessRunner.RunAsync("dotnet",
            ["test", "--filter", "FullyQualifiedName~FunctionsExecuteInPostgres", "-bl:changed-tests-{}.binlog",
                "-p:AnkusPostgresMajor=" + MajorText()],
            s_environment, token, workingDirectory: output);
        Assert.AreNotEqual(0, changed.ExitCode);
        Assert.Contains("expected: 42", changed.StandardOutput);
        Assert.Contains("actual:   38", changed.StandardOutput);
        Assert.IsEmpty(Directory.GetDirectories(Path.Combine(projectDirectory, "bin", "ankus-test-pgdata")));
        Assert.IsEmpty(Directory.GetDirectories(Path.Combine(projectDirectory, "bin", "ankus-test-publish")));

    }

    /// <summary>
    /// Build and CREATE EXTENSION failures fail native test initialization and remove the owned publish/cluster directories.
    /// </summary>
    /// <param name="failure">The initialization stage to fail.</param>
    [TestMethod]
    [DataRow("build")]
    [DataRow("load")]
    public async Task NewSolutionReportsInitializationFailuresAndCleansUp(string failure)
    {
        CancellationToken token = context.CancellationToken;
        string output = Path.Combine(CreateDirectory(), "failure solution");
        (await InvokeAsync(["new", "FailureProbe", "-o", output], token)).EnsureSuccess(s_tool, ["new"]);
        string projectDirectory = Path.Combine(output, "src", "FailureProbe");
        if (failure == "build")
        {
            await File.WriteAllTextAsync(Path.Combine(projectDirectory, "FailPublish.cs"),
                "#if !DEBUG\n#error Intentional native publication failure\n#endif\n", token);
        }
        else
        {
            string project = Path.Combine(projectDirectory, "FailureProbe.csproj");
            XDocument document = XDocument.Load(project);
            document.Root!.Add(new XElement("Target", new XAttribute("Name", "FailExtensionLoad"),
                new XAttribute("AfterTargets", "_PublishAnkusSchema"),
                new XElement("WriteLinesToFile", new XAttribute("File", "$(PublishDir)extension/failure_probe--1.0.0.sql"),
                    new XAttribute("Lines", "SELECT 1 / 0%3B"))));
            document.Save(project);
        }

        ProcessResult result = await ProcessRunner.RunAsync("dotnet",
            ["test", "--filter", "FullyQualifiedName~FunctionsExecuteInPostgres", "-bl:initialization-failure-{}.binlog",
                "-p:AnkusPostgresMajor=" + MajorText()],
            s_environment, token, workingDirectory: output);
        Assert.AreNotEqual(0, result.ExitCode);
        Assert.Contains(failure == "build" ? "Intentional native publication failure" : "division by zero", result.StandardOutput);
        Assert.Contains("BackendTests.InitializeAsync", result.StandardOutput);
        string pgdata = Path.Combine(projectDirectory, "bin", "ankus-test-pgdata");
        Assert.IsTrue(!Directory.Exists(pgdata) || Directory.GetDirectories(pgdata).Length == 0);
        Assert.IsEmpty(Directory.GetDirectories(Path.Combine(projectDirectory, "bin", "ankus-test-publish")));
        Assert.IsNotEmpty(Directory.GetFiles(Path.Combine(projectDirectory, "bin", "ankus-test-logs"), "*.binlog"));
    }

    /// <summary>
    /// Checks keyword namespaces, explicit SQL names, and token-like project names generate valid C# without recursive replacement.
    /// </summary>
    /// <param name="name">The managed project name.</param>
    /// <param name="extension">The explicit SQL extension name.</param>
    [TestMethod]
    [DataRow("class.event", "custom_extension")]
    [DataRow("__VERSION__", "token_probe")]
    public async Task NewSolutionSupportsKeywordsAndExplicitNames(string name, string extension)
    {
        CancellationToken token = context.CancellationToken;
        string output = Path.Combine(CreateDirectory(), "solution");
        (await InvokeAsync(["new", name, "--output", output, "--extension-name", extension], token)).EnsureSuccess(s_tool, ["new"]);
        string project = Path.Combine(output, "src", name, name + ".csproj");
        Assert.AreEqual(extension, XDocument.Load(project).Descendants("AnkusExtensionName").Single().Value);
        ProcessResult build = await ProcessRunner.RunAsync("dotnet", ["build", "-bl:names-{}.binlog"],
            s_environment, token, workingDirectory: output);
        build.EnsureSuccess("dotnet", ["build"]);
    }

    /// <summary>
    /// Checks invalid names fail before creating destination directories or files.
    /// </summary>
    /// <param name="name">The candidate project name.</param>
    /// <param name="extension">The SQL extension override, or null.</param>
    [TestMethod]
    [DataRow("../outside", null)]
    [DataRow("1Project", null)]
    [DataRow("Acme..Search", null)]
    [DataRow("CON.Tools", null)]
    [DataRow("Project", "BadName")]
    [DataRow("Project", "1name")]
    [DataRow("Project", "bad/name")]
    [DataRow("Project", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task NewRejectsInvalidNamesWithoutFiles(string name, string? extension)
    {
        string output = Path.Combine(CreateDirectory(), "rejected");
        string[] arguments = extension is null ? ["new", name, "-o", output] : ["new", name, "-o", output, "--extension-name", extension];
        ProcessResult result = await InvokeAsync(arguments, context.CancellationToken);
        Assert.AreEqual(1, result.ExitCode);
        Assert.IsNotEmpty(result.StandardError);
        Assert.IsFalse(Path.Exists(output));
        Assert.IsEmpty(Directory.GetFileSystemEntries(Path.GetDirectoryName(output)!));
    }

    /// <summary>
    /// Checks an existing destination remains byte-for-byte unchanged.
    /// </summary>
    [TestMethod]
    public async Task NewPreservesExistingFiles()
    {
        string output = CreateDirectory();
        string file = Path.Combine(output, "user-data.txt");
        await File.WriteAllTextAsync(file, "keep this exact text", context.CancellationToken);
        ProcessResult result = await InvokeAsync(["new", "Existing", "-o", output], context.CancellationToken);
        Assert.AreEqual(1, result.ExitCode);
        Assert.Contains("already exists", result.StandardError);
        Assert.AreEqual("keep this exact text", await File.ReadAllTextAsync(file, context.CancellationToken));
        Assert.AreSequenceEqual([file], Directory.GetFileSystemEntries(output));
    }

    /// <summary>
    /// A solution with multiple extensions requires explicit selection rather than publishing an arbitrary project.
    /// </summary>
    [TestMethod]
    public async Task NewSolutionWithMultipleExtensionsRequiresSelection()
    {
        string output = Path.Combine(CreateDirectory(), "solution");
        CancellationToken token = context.CancellationToken;
        (await InvokeAsync(["new", "First", "-o", output], token)).EnsureSuccess(s_tool, ["new"]);
        string second = Path.Combine(output, "src", "Second");
        Directory.CreateDirectory(second);
        await File.WriteAllTextAsync(Path.Combine(second, "Second.csproj"), "<Project Sdk=\"Ankus.Sdk\" />", token);
        string solution = Path.Combine(output, "First.slnx");
        XDocument document = XDocument.Load(solution);
        document.Root!.Add(new XElement("Project", new XAttribute("Path", "src/Second/Second.csproj")));
        document.Save(solution);
        ProcessResult result = await ProcessRunner.RunAsync(s_tool,
            ["publish", "--home", s_home, "--pg", MajorText(), "-o", "published"],
            s_environment, token, workingDirectory: output);
        Assert.AreEqual(1, result.ExitCode);
        Assert.Contains("Specify --project", result.StandardError);
        Assert.IsFalse(Directory.Exists(Path.Combine(output, "published")));
    }

    private static async Task<PostgresTestCluster> StartPublishedClusterAsync(string output, CancellationToken token)
    {
        s_installation = await IntegrationEnvironment.PrepareExtensionInstallationAsync(output, token);
        List<string> configuration = ["dynamic_library_path = '" + EscapeSetting(output) + "'"];
        if (s_installation.Version.Major >= 18)
        {
            configuration.Insert(0, "extension_control_path = '" + EscapeSetting(output) + "'");
        }

        return await PostgresTestCluster.StartAsync(new PostgresTestClusterOptions
        {
            Installation = s_installation,
            DataDirectoryBase = Path.Combine(s_root, "pgdata"),
            LogDirectory = Path.Combine(IntegrationEnvironment.RepositoryRoot, "artifacts", "test-logs"),
            PostgreSqlConfiguration = configuration,
        }, token);
    }

    private static Task<ProcessResult> RunDotnetAsync(string[] arguments, CancellationToken token)
        => ProcessRunner.RunAsync("dotnet", arguments, s_environment, token, workingDirectory: s_root);

    private static Task<ProcessResult> InvokeAsync(string[] arguments, CancellationToken token)
        => ProcessRunner.RunAsync(s_tool, arguments, s_environment, token, workingDirectory: s_root);

    private static int DifferentMajor()
        => s_installation.Version.Major == 19 ? 18 : 19;

    private static string MajorText()
        => s_installation.Version.Major.ToString(CultureInfo.InvariantCulture);

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
