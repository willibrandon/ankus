using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using Ankus.PgConfig;
using Ankus.Testing;

namespace Ankus.IntegrationTests;

public sealed partial class ToolCommandTests
{
    /// <summary>
    /// An environment major overrides project defaults while retaining a read-only installation lookup.
    /// </summary>
    /// <param name="prefix">Whether the environment uses a pgrx-style version label.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task EnvironmentMajorPrecedesProjectDefault(bool prefix)
    {
        CancellationToken token = context.CancellationToken;
        string directory = CreateDirectory();
        string home = Path.Combine(directory, "home");
        await RegisterEnvironmentInstallationAsync(home, token);
        new XDocument(new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
            new XElement("PropertyGroup", new XElement("TargetFramework", "net10.0"),
                new XElement("UsingAnkusSdk", "true"), new XElement("AnkusPostgresMajor", DifferentMajor()))))
            .Save(Path.Combine(directory, "Selection.csproj"));
        Dictionary<string, string?> environment = EnvironmentDefaultsConfiguration(home);
        environment["PG_VERSION"] = (prefix ? "pg" : "") + MajorText();
        ProcessResult result = await ProcessRunner.RunAsync(s_tool, ["info", "version"], environment, token, workingDirectory: directory);

        Assert.AreEqual(0, result.ExitCode, result.StandardError);
        Assert.AreEqual(s_installation.Version + Environment.NewLine, result.StandardOutput);
        Assert.IsEmpty(Directory.GetDirectories(directory, "obj", SearchOption.AllDirectories));
        Assert.IsEmpty(Directory.GetDirectories(directory, "bin", SearchOption.AllDirectories));
        Assert.HasCount(1, Directory.GetFileSystemEntries(home));
    }

    /// <summary>
    /// An absent or empty version default retains the project's explicit installation without consulting a broken registry.
    /// </summary>
    /// <param name="value">The absent or empty environment selection.</param>
    [TestMethod]
    [DataRow((string?)null)]
    [DataRow("")]
    public async Task EmptyEnvironmentMajorRetainsProjectInstallation(string? value)
    {
        CancellationToken token = context.CancellationToken;
        string directory = CreateDirectory();
        string home = Directory.CreateDirectory(Path.Combine(directory, "home")).FullName;
        string configuration = Path.Combine(home, "config.json");
        string original = JsonSerializer.Serialize(new Dictionary<string, string> { ["pg" + MajorText()] = "missing/bin/pg_config" });
        await File.WriteAllTextAsync(configuration, original, token);
        new XDocument(new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
            new XElement("PropertyGroup", new XElement("TargetFramework", "net10.0"),
                new XElement("UsingAnkusSdk", "true"), new XElement("AnkusPostgresMajor", MajorText()),
                new XElement("AnkusPgConfigPath", s_installation.PgConfigPath))))
            .Save(Path.Combine(directory, "Selection.csproj"));
        Dictionary<string, string?> environment = EnvironmentDefaultsConfiguration(home);
        environment["PG_VERSION"] = value;
        ProcessResult result = await ProcessRunner.RunAsync(s_tool, ["info", "version"], environment, token, workingDirectory: directory);

        Assert.AreEqual(0, result.ExitCode, result.StandardError);
        Assert.AreEqual(s_installation.Version + Environment.NewLine, result.StandardOutput);
        Assert.AreEqual(original, await File.ReadAllTextAsync(configuration, token));
        Assert.IsEmpty(Directory.GetDirectories(directory, "obj", SearchOption.AllDirectories));
        Assert.IsEmpty(Directory.GetDirectories(directory, "bin", SearchOption.AllDirectories));
    }

    /// <summary>
    /// Invalid version defaults fail before a registry mutation or installation download.
    /// </summary>
    /// <param name="value">The invalid version label.</param>
    [TestMethod]
    [DataRow("12")]
    [DataRow("pg20")]
    [DataRow(" 18")]
    [DataRow("18.6")]
    [DataRow("banana")]
    [DataRow("pg")]
    public async Task InvalidEnvironmentMajorPreservesRegistry(string value)
    {
        CancellationToken token = context.CancellationToken;
        string home = CreateDirectory();
        await RegisterEnvironmentInstallationAsync(home, token);
        string configuration = Path.Combine(home, "config.json");
        string original = await File.ReadAllTextAsync(configuration, token);
        Dictionary<string, string?> environment = EnvironmentDefaultsConfiguration(home);
        environment["PG_VERSION"] = value;
        ProcessResult result = await ProcessRunner.RunAsync(s_tool, ["info", "version"], environment, token, workingDirectory: s_root);

        Assert.AreEqual(1, result.ExitCode);
        Assert.IsEmpty(result.StandardOutput);
        Assert.Contains("PG_VERSION must select PostgreSQL 13–19", result.StandardError);
        Assert.AreEqual(original, await File.ReadAllTextAsync(configuration, token));
        Assert.HasCount(1, Directory.GetFileSystemEntries(home));
    }

    /// <summary>
    /// Explicit version options bypass an invalid environment default instead of treating it as a conflict.
    /// </summary>
    /// <param name="selection">The explicit selection form.</param>
    [TestMethod]
    [DataRow("option")]
    [DataRow("argument")]
    [DataRow("path")]
    public async Task ExplicitMajorPrecedesEnvironment(string selection)
    {
        CancellationToken token = context.CancellationToken;
        string home = CreateDirectory();
        await RegisterEnvironmentInstallationAsync(home, token);
        Dictionary<string, string?> environment = EnvironmentDefaultsConfiguration(home);
        environment["PG_VERSION"] = "invalid";
        string[] selected = selection switch
        {
            "option" => ["--pg", MajorText()],
            "argument" => ["pg" + MajorText()],
            "path" => ["--pg-config", s_installation.PgConfigPath],
            _ => throw new ArgumentException("Unknown selection.", nameof(selection)),
        };
        ProcessResult result = await ProcessRunner.RunAsync(s_tool, ["info", "version", .. selected], environment, token, workingDirectory: s_root);

        Assert.AreEqual(0, result.ExitCode, result.StandardError);
        Assert.AreEqual(s_installation.Version + Environment.NewLine, result.StandardOutput);
    }

    /// <summary>
    /// A valid unavailable environment version is rejected instead of falling back to a registered major.
    /// </summary>
    [TestMethod]
    public async Task UnregisteredEnvironmentMajorDoesNotFallBack()
    {
        CancellationToken token = context.CancellationToken;
        string home = CreateDirectory();
        await RegisterEnvironmentInstallationAsync(home, token);
        Dictionary<string, string?> environment = EnvironmentDefaultsConfiguration(home);
        int major = DifferentMajor();
        environment["PG_VERSION"] = "pg" + major.ToString(CultureInfo.InvariantCulture);
        ProcessResult result = await ProcessRunner.RunAsync(s_tool, ["info", "version"], environment, token, workingDirectory: s_root);

        Assert.AreEqual(1, result.ExitCode);
        Assert.IsEmpty(result.StandardOutput);
        Assert.Contains("PostgreSQL " + major.ToString(CultureInfo.InvariantCulture) + " is not registered", result.StandardError);
        Assert.HasCount(1, Directory.GetFileSystemEntries(home));
    }

    /// <summary>
    /// Connect transports DBNAME literally, and an explicit database option retains priority.
    /// </summary>
    /// <param name="explicitDatabase">Whether the command overrides the environment database.</param>
    /// <param name="explicitClient">Whether an explicit client option overrides an invalid environment default.</param>
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    public async Task ConnectUsesLiteralEnvironmentDatabase(bool explicitDatabase, bool explicitClient)
    {
        CancellationToken token = context.CancellationToken;
        string home = CreateDirectory();
        var cluster = new PostgresDevelopmentCluster(s_installation, home);
        using PortReservation reservation = PortReservation.Create();
        int port = reservation.Port;
        reservation.Dispose();
        const string EnvironmentDatabase = "host=elsewhere café'\\";
        const string ExplicitDatabase = "explicit café database";
        Dictionary<string, string?> environment = EnvironmentDefaultsConfiguration(home);
        environment["PG_VERSION"] = "invalid";
        environment["DBNAME"] = EnvironmentDatabase;
        environment["ANKUS_PGCLI"] = explicitClient ? "invalid" : "false";
        string expected = explicitDatabase ? ExplicitDatabase : EnvironmentDatabase;
        string[] selected = explicitDatabase ? ["--database", ExplicitDatabase] : [];
        string[] client = explicitClient ? ["--pgcli", "false"] : [];
        try
        {
            ProcessResult result = await ProcessRunner.RunAsync(s_tool,
                ["connect", "--pg-config", s_installation.PgConfigPath, "--port", port.ToString(CultureInfo.InvariantCulture),
                    .. selected, .. client, "--", "-X", "-A", "-t", "-c", "SELECT current_database()"],
                environment, token, workingDirectory: s_root);

            Assert.AreEqual(0, result.ExitCode, result.StandardError);
            Assert.EndsWith(expected + Environment.NewLine, result.StandardOutput);
            Assert.Contains("Created database " + expected, result.StandardOutput);
            Assert.IsTrue(await cluster.IsRunningAsync(token));
        }
        finally
        {
            await cluster.StopAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// Invalid client defaults fail before creating or starting any development server.
    /// </summary>
    /// <param name="command">The interactive preparation command.</param>
    [TestMethod]
    [DataRow("connect")]
    [DataRow("run")]
    public async Task InvalidClientEnvironmentDoesNotStartServer(string command)
    {
        string directory = CreateDirectory();
        string home = Path.Combine(directory, "unused home");
        Dictionary<string, string?> environment = EnvironmentDefaultsConfiguration(home);
        environment["ANKUS_PGCLI"] = "invalid";
        ProcessResult result = await ProcessRunner.RunAsync(s_tool,
            [command, "--pg-config", s_installation.PgConfigPath], environment,
            context.CancellationToken, workingDirectory: s_root);

        Assert.AreEqual(1, result.ExitCode);
        Assert.IsEmpty(result.StandardOutput);
        Assert.Contains("ANKUS_PGCLI must be true or false", result.StandardError);
        Assert.IsFalse(Directory.Exists(home));
    }

    /// <summary>
    /// The true client default selects pgcli, while an explicit false option selects the installation's psql.
    /// </summary>
    /// <param name="overrideClient">Whether the command explicitly selects psql.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task EnvironmentClientSelectsPgcliUnlessOverridden(bool overrideClient)
    {
        CancellationToken token = context.CancellationToken;
        string directory = CreateDirectory();
        string home = Path.Combine(directory, "home");
        string clientDirectory = Directory.CreateDirectory(Path.Combine(directory, "empty client path")).FullName;
        var cluster = new PostgresDevelopmentCluster(s_installation, home);
        using PortReservation reservation = PortReservation.Create();
        int port = reservation.Port;
        reservation.Dispose();
        Dictionary<string, string?> environment = EnvironmentDefaultsConfiguration(home);
        environment["ANKUS_PGCLI"] = "true";
        environment["PATH"] = clientDirectory;
        string[] client = overrideClient ? ["--pgcli", "false"] : [];
        try
        {
            ProcessResult result = await ProcessRunner.RunAsync(s_tool,
                ["connect", "--pg-config", s_installation.PgConfigPath, "--port", port.ToString(CultureInfo.InvariantCulture),
                    "--database", "client_selection", .. client, "--", "-X", "-A", "-t", "-c", "SELECT 42"],
                environment, token, workingDirectory: directory);

            if (overrideClient)
            {
                Assert.AreEqual(0, result.ExitCode, result.StandardError);
                Assert.EndsWith("42" + Environment.NewLine, result.StandardOutput);
            }
            else
            {
                Assert.AreEqual(1, result.ExitCode);
                Assert.Contains("pgcli", result.StandardError);
                Assert.Contains("Created database client_selection", result.StandardOutput);
            }

            Assert.IsTrue(await cluster.IsRunningAsync(token));
            Assert.IsEmpty(Directory.GetFileSystemEntries(clientDirectory));
        }
        finally
        {
            await cluster.StopAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// Existing native publications select their compiled major before any new environment default.
    /// </summary>
    /// <param name="operation">The staged installation command.</param>
    /// <param name="invalid">Whether the environment default is malformed rather than unregistered.</param>
    [TestMethod]
    [DataRow("package", false)]
    [DataRow("package", true)]
    [DataRow("install", false)]
    [DataRow("install", true)]
    public async Task PublishedArtifactMajorPrecedesEnvironment(string operation, bool invalid)
    {
        CancellationToken token = context.CancellationToken;
        string directory = CreateDirectory();
        string home = Path.Combine(directory, "home");
        await RegisterEnvironmentInstallationAsync(home, token);
        string configuration = Path.Combine(home, "config.json");
        string original = await File.ReadAllTextAsync(configuration, token);
        PublishedExtension manifest = PublishedExtension.Read(s_published);
        Assert.AreEqual(s_installation.Version.Major, manifest.PostgresMajor);
        string output = Path.Combine(directory, "staged assets");
        Dictionary<string, string?> environment = EnvironmentDefaultsConfiguration(home);
        environment["PG_VERSION"] = invalid ? "invalid" : "pg" + DifferentMajor().ToString(CultureInfo.InvariantCulture);
        string[] staging = operation == "package" ? ["--output", output, "--prefix-dir", "flat"] : ["--destdir", output];
        ProcessResult result = await ProcessRunner.RunAsync(s_tool, [operation, "--from", s_published, .. staging],
            environment, token, workingDirectory: directory);

        Assert.AreEqual(0, result.ExitCode, result.StandardError);
        string libraries = operation == "install" ? StagedPath(output, s_installation.LibraryDirectory)
            : Path.Combine(output, OperatingSystem.IsWindows() ? "lib" : "flat");
        string scripts = operation == "install" ? StagedPath(output, Path.Combine(s_installation.SharedDirectory, "extension"))
            : OperatingSystem.IsWindows() ? Path.Combine(output, "share", "extension") : Path.Combine(output, "flat");
        (string Source, string Destination)[] assets =
        [
            (Path.Combine(s_published, manifest.Library), Path.Combine(libraries, manifest.Library)),
            (Path.Combine(s_published, "extension", manifest.Control), Path.Combine(scripts, manifest.Control)),
            (Path.Combine(s_published, "extension", manifest.Sql), Path.Combine(scripts, manifest.Sql)),
        ];
        foreach ((string source, string destination) in assets)
        {
            Assert.AreSequenceEqual(await File.ReadAllBytesAsync(source, token), await File.ReadAllBytesAsync(destination, token));
        }

        Assert.AreEqual(original, await File.ReadAllTextAsync(configuration, token));
        Assert.HasCount(1, Directory.GetFileSystemEntries(home));
        Assert.IsEmpty(Directory.GetDirectories(directory, "obj", SearchOption.AllDirectories));
        Assert.IsEmpty(Directory.GetDirectories(directory, "bin", SearchOption.AllDirectories));
    }

    /// <summary>
    /// Removes fixture build overrides so each process observes the authored project or its explicit command selection.
    /// </summary>
    /// <param name="home">The case-owned registry directory.</param>
    /// <returns>Isolated tool defaults with ordinary consumer build properties.</returns>
    private static Dictionary<string, string?> EnvironmentDefaultsConfiguration(string home)
    {
        Dictionary<string, string?> environment = EnvironmentConfiguration(home);
        environment["AnkusPostgresMajor"] = null;
        environment["AnkusPgConfigPath"] = null;
        return environment;
    }

    /// <summary>
    /// Creates only the test-owned registry needed for selection without changing the user's home.
    /// </summary>
    /// <param name="home">The owned registry directory.</param>
    /// <param name="token">Cancels writing before invocation.</param>
    private static async Task RegisterEnvironmentInstallationAsync(string home, CancellationToken token)
    {
        Directory.CreateDirectory(home);
        await File.WriteAllTextAsync(Path.Combine(home, "config.json"),
            JsonSerializer.Serialize(new Dictionary<string, string> { ["pg" + MajorText()] = s_installation.PgConfigPath }), token);
    }
}
