using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;
using Ankus.PgConfig;
using Ankus.Testing;
using Npgsql;

namespace Ankus.IntegrationTests;

public sealed partial class ToolCommandTests
{
    /// <summary>
    /// Native creation and client connections preserve unusual names and reject lossy identifier truncation.
    /// </summary>
    [TestMethod]
    public async Task DevelopmentDatabasesPreserveExactNamesAndRejectTruncation()
    {
        CancellationToken token = context.CancellationToken;
        string home = CreateDirectory();
        var cluster = new PostgresDevelopmentCluster(s_installation, home);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => cluster.GetConnectionStringAsync("postgres", token));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => cluster.CreateDatabaseAsync("missing", token));
        Assert.IsEmpty(Directory.GetFileSystemEntries(home));
        using PortReservation reservation = PortReservation.Create();
        int port = reservation.Port;
        reservation.Dispose();
        try
        {
            Assert.IsTrue(await cluster.StartAsync(new PostgresDevelopmentOptions { Port = port }, token));
            string[] names = ["ordinary", " café'\\\"; # ", "--help", "host=elsewhere dbname=other", "postgresql://elsewhere/db", " ", "line\n\\! echo forbidden", ".", "..", "%2F?a#b",
                new string('a', 63), new string('é', 31) + "a"];
            foreach (string name in names)
            {
                Assert.IsTrue(await cluster.CreateDatabaseAsync(name, token));
                Assert.IsFalse(await cluster.CreateDatabaseAsync(name, token));
                string connection = await cluster.GetConnectionStringAsync(name, token);
                ProcessResult result = await ProcessRunner.RunAsync(s_installation.PsqlPath,
                    ["-X", "-A", "-t", "--dbname", connection, "-c", "SELECT encode(convert_to(current_database(), 'UTF8'), 'base64')"],
                    s_environment, token);
                Assert.AreEqual(0, result.ExitCode, result.StandardError);
                Assert.AreEqual(Convert.ToBase64String(Encoding.UTF8.GetBytes(name)), result.StandardOutput.ReplaceLineEndings("").Trim());
            }

            foreach (string name in new[] { new string('a', 64), new string('é', 32) })
            {
                ArgumentException error = await Assert.ThrowsExactlyAsync<ArgumentException>(() => cluster.CreateDatabaseAsync(name, token));
                Assert.Contains("63-byte", error.Message);
            }

            await using NpgsqlConnection connectionToServer = await OpenDevelopmentConnectionAsync(port, token);
            await using var count = new NpgsqlCommand("SELECT count(*) FROM pg_database WHERE NOT datistemplate AND datname <> 'postgres'", connectionToServer);
            Assert.AreEqual((long)names.Length, await count.ExecuteScalarAsync(token));
        }
        finally
        {
            await cluster.StopAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// Connect starts a missing cluster, reuses live custom-port state and forwards a client's nonzero exit.
    /// </summary>
    [TestMethod]
    public async Task ConnectPreservesDatabaseAndUsesRunningPort()
    {
        CancellationToken token = context.CancellationToken;
        string home = CreateDirectory();
        var cluster = new PostgresDevelopmentCluster(s_installation, home);
        using PortReservation reservation = PortReservation.Create();
        int port = reservation.Port;
        reservation.Dispose();
        const string Database = "host=elsewhere café'\\";
        string[] options = ["connect", "--home", home, "--pg", MajorText(), "--pg-config", s_installation.PgConfigPath, "--database", Database];
        try
        {
            var environment = new Dictionary<string, string?>(s_environment)
            {
                ["PGHOSTADDR"] = "192.0.2.1", ["PGDATABASE"] = "wrong", ["PGSERVICE"] = "missing-service",
            };
            ProcessResult first = await ProcessRunner.RunAsync(s_tool,
                [.. options, "--port", port.ToString(CultureInfo.InvariantCulture), "--", "-X", "-A", "-t", "-v", "ON_ERROR_STOP=1",
                    "-c", "CREATE TABLE retained(value integer); INSERT INTO retained VALUES (42); SELECT value FROM retained"], environment, token,
                workingDirectory: s_root);
            Assert.AreEqual(0, first.ExitCode, first.StandardError);
            Assert.Contains("Created database " + Database, first.StandardOutput);
            Assert.EndsWith("42" + Environment.NewLine, first.StandardOutput);
            Assert.IsTrue(await cluster.IsRunningAsync(token));
            ProcessResult second = await InvokeAsync([.. options, "--port", "1", "--", "-X", "-A", "-t", "-c", "SELECT value FROM retained"], token);
            Assert.AreEqual(0, second.ExitCode, second.StandardError);
            Assert.Contains("Reusing database " + Database, second.StandardOutput);
            Assert.EndsWith("42" + Environment.NewLine, second.StandardOutput);
            string script = Path.Combine(home, "error.sql");
            await File.WriteAllTextAsync(script, "SELECT 1 / 0;", token);
            ProcessResult failure = await InvokeAsync([.. options, "--", "-X", "-v", "ON_ERROR_STOP=1", "-f", script], token);
            Assert.AreEqual(3, failure.ExitCode, failure.StandardError);
            Assert.Contains("division by zero", failure.StandardError);
            Assert.IsTrue(await cluster.IsRunningAsync(token));
        }
        finally
        {
            await cluster.StopAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// Database defaults follow evaluated imported configuration and the SDK's normalized assembly-name fallback.
    /// </summary>
    /// <param name="explicitName">Whether the project imports an explicit extension name.</param>
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task ConnectEvaluatesDefaultDatabaseName(bool explicitName)
    {
        CancellationToken token = context.CancellationToken;
        string home = CreateDirectory();
        string projectRoot = CreateDirectory();
        string project = Path.Combine(projectRoot, "NameProbe.csproj");
        new XDocument(new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
            new XElement("PropertyGroup", new XElement("TargetFramework", "net10.0"), new XElement("AssemblyName", "Different.Target")),
            new XElement("Import", new XAttribute("Project", "name.props")))).Save(project);
        new XDocument(new XElement("Project", new XElement("PropertyGroup", new XAttribute("Condition", "'$(Configuration)' == 'Release'"),
            new XElement("AnkusExtensionName", explicitName ? "imported_name" : "")))).Save(Path.Combine(projectRoot, "name.props"));
        string expected = explicitName ? "imported_name" : "different_target";
        var cluster = new PostgresDevelopmentCluster(s_installation, home);
        using PortReservation reservation = PortReservation.Create();
        int port = reservation.Port;
        reservation.Dispose();
        try
        {
            ProcessResult result = await InvokeAsync(["connect", "--home", home, "--pg", MajorText(), "--pg-config", s_installation.PgConfigPath,
                "--project", project, "--port", port.ToString(CultureInfo.InvariantCulture), "--", "-X", "-A", "-t", "-c", "SELECT current_database()"], token);
            Assert.AreEqual(0, result.ExitCode, result.StandardError);
            Assert.Contains("Created database " + expected, result.StandardOutput);
            Assert.EndsWith(expected + Environment.NewLine, result.StandardOutput);
            Assert.IsFalse(Directory.Exists(Path.Combine(projectRoot, "bin")));
        }
        finally
        {
            await cluster.StopAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// Run publishes and installs into an isolated installation, then no-build starts the retained publication and executes real extension SQL.
    /// </summary>
    [TestMethod]
    public async Task RunBuildsInstallsAndLoadsNativeExtension()
    {
        CancellationToken token = context.CancellationToken;
        await using PostgresTestInstallation owner = await PostgresTestInstallation.StageAsync(s_installation, CreateDirectory(), token);
        PostgresInstallation installation = owner.Installation;
        string home = CreateDirectory();
        string projectRoot = CreateDirectory();
        string project = Path.Combine(projectRoot, "RunProbe.csproj");
        XDocument definition = XDocument.Load(s_project);
        definition.Descendants("AnkusExtensionName").Single().Value = "ankus_run_probe";
        definition.Save(project);
        File.Copy(Path.Combine(Path.GetDirectoryName(s_project)!, "Hello.cs"), Path.Combine(projectRoot, "Hello.cs"));
        var cluster = new PostgresDevelopmentCluster(installation, home);
        using PortReservation reservation = PortReservation.Create();
        int port = reservation.Port;
        reservation.Dispose();
        string[] options = ["run", "--home", home, "--pg", MajorText(), "--pg-config", installation.PgConfigPath, "--project", project];
        try
        {
            ProcessResult install = await InvokeAsync([.. options, "--install-only"], token);
            Assert.AreEqual(0, install.ExitCode, install.StandardOutput + install.StandardError);
            Assert.IsFalse(await cluster.IsRunningAsync(token));
            Assert.IsFalse(Directory.Exists(cluster.DataDirectory));
            string control = Path.Combine(installation.SharedDirectory, "extension", "ankus_run_probe.control");
            Assert.IsTrue(File.Exists(control));
            string publication = Path.Combine(projectRoot, "bin", "ankus", s_postgresKey, RuntimeInformation.RuntimeIdentifier, "Release");
            PublishedExtension manifest = PublishedExtension.Read(publication);
            Assert.IsTrue(File.Exists(Path.Combine(installation.LibraryDirectory, manifest.Library)));
            ProcessResult run = await InvokeAsync([.. options, "--port", port.ToString(CultureInfo.InvariantCulture), "--",
                "-X", "-A", "-t", "-v", "ON_ERROR_STOP=1", "-c", "CREATE EXTENSION ankus_run_probe; SELECT add(19, 23)"], token);
            Assert.AreEqual(0, run.ExitCode, run.StandardOutput + run.StandardError);
            Assert.Contains("Created database ankus_run_probe", run.StandardOutput);
            Assert.EndsWith("42" + Environment.NewLine, run.StandardOutput);
            Assert.IsTrue(await cluster.IsRunningAsync(token));
            ProcessResult repeated = await InvokeAsync([.. options, "--no-build", "--install-only"], token);
            Assert.AreEqual(0, repeated.ExitCode, repeated.StandardError);
            Assert.IsFalse(await cluster.IsRunningAsync(token));
            Assert.IsTrue(Directory.Exists(cluster.DataDirectory));
            await File.WriteAllTextAsync(Path.Combine(projectRoot, "Broken.cs"), "this is not C#;", token);
            ProcessResult existing = await InvokeAsync([.. options, "--no-build", "--port", port.ToString(CultureInfo.InvariantCulture), "--",
                "-X", "-A", "-t", "-c", "SELECT add(19, 23)"], token);
            Assert.AreEqual(0, existing.ExitCode, existing.StandardError);
            Assert.Contains("Reusing database ankus_run_probe", existing.StandardOutput);
            Assert.EndsWith("42" + Environment.NewLine, existing.StandardOutput);
            ProcessResult failed = await InvokeAsync([.. options, "--install-only"], token);
            Assert.AreNotEqual(0, failed.ExitCode);
            Assert.IsFalse(await cluster.IsRunningAsync(token));
            Assert.IsFalse(File.Exists(Path.Combine(publication, PublishedExtension.FileName)));
            Assert.IsTrue(File.Exists(control));
            ProcessResult missing = await InvokeAsync([.. options, "--no-build"], token);
            Assert.AreEqual(1, missing.ExitCode);
            Assert.Contains(PublishedExtension.FileName, missing.StandardError);
            Assert.IsFalse(await cluster.IsRunningAsync(token));
        }
        finally
        {
            await cluster.StopAsync(CancellationToken.None);
        }
    }
}
