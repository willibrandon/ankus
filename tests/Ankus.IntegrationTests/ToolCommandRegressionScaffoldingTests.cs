using Ankus.PgConfig;
using Ankus.Testing;
using Npgsql;

namespace Ankus.IntegrationTests;

public sealed partial class ToolCommandTests
{
    /// <summary>
    /// Generated regression setup creates the named native extension, survives database reuse and retains expectations.
    /// </summary>
    /// <param name="projectName">The generated managed project name.</param>
    /// <param name="extensionName">An explicit SQL name, or null to use the derived name.</param>
    /// <param name="expectedName">The independently expected SQL extension name.</param>
    /// <param name="template">Whether to create through the installed dotnet-new template.</param>
    [TestMethod]
    [DataRow("Acme.RegressProbe", null, "acme_regress_probe", false)]
    [DataRow("class.select", "regression_named", "regression_named", false)]
    [DataRow("Acme.RegressProbe", null, "acme_regress_probe", true)]
    [DataRow("class.select", null, "class_select", true)]
    [DataRow("1Ext", null, "_1_ext", false)]
    [DataRow("1Ext", null, "_1_ext", true)]
    [DataRow("Hello-World", null, "hello_world", false)]
    [DataRow("Hello-World", null, "hello_world", true)]
    [DataRow("Café.Δelta", null, "caf___elta", false)]
    [DataRow("Café.Δelta", null, "caf___elta", true)]
    public async Task NewSolutionScaffoldsRunnableRegressionSetup(string projectName, string? extensionName, string expectedName, bool template)
    {
        CancellationToken token = context.CancellationToken;
        string output = Path.Combine(CreateDirectory(), "regression solution with spaces");
        string[] arguments = ["new", projectName, "--output", output,
            .. extensionName is null ? Array.Empty<string>() : ["--extension-name", extensionName]];
        if (template)
        {
            await CreateTemplateAsync("ankus", projectName, output, token);
        }
        else
        {
            (await InvokeAsync(arguments, token)).EnsureSuccess(s_tool, arguments);
        }

        string project = Path.Combine(output, "src", projectName, projectName + ".csproj");
        string suite = Path.Combine(Path.GetDirectoryName(project)!, "pg_regress");
        string expectedSetup = "-- This setup file runs when the regression database is created or recreated.\n" +
            "-- Create the extension before running the ordinary SQL tests.\nCREATE EXTENSION " + expectedName + ";\n";
        string setup = Path.Combine(suite, "sql", "setup.sql");
        string setupOutput = Path.Combine(suite, "expected", "setup.out");
        Assert.AreEqual(expectedSetup, (await File.ReadAllTextAsync(setup, token)).ReplaceLineEndings("\n"));
        Assert.AreEqual(expectedSetup, (await File.ReadAllTextAsync(setupOutput, token)).ReplaceLineEndings("\n"));
        Assert.IsLessThanOrEqualTo(File.GetLastWriteTimeUtc(setupOutput), File.GetLastWriteTimeUtc(setup));
        byte[] originalSql = await File.ReadAllBytesAsync(setup, token);
        byte[] originalExpected = await File.ReadAllBytesAsync(setupOutput, token);
        await WriteRegressionCaseAsync(suite, "addition", "SELECT add(17, 25);", "42\n", token);

        await using PostgresTestInstallation owner = await PostgresTestInstallation.StageAsync(s_installation, CreateDirectory(), token);
        string home = CreateDirectory();
        var cluster = new PostgresDevelopmentCluster(owner.Installation, home);
        using PortReservation reservation = PortReservation.Create();
        int port = reservation.Port;
        reservation.Dispose();
        string[] options = RegressionOptions(owner.Installation, home, project, port);
        try
        {
            ProcessResult initial = await InvokeAsync(options, token);
            Assert.AreEqual(0, initial.ExitCode, initial.StandardOutput + initial.StandardError);
            Assert.Contains("Created database " + expectedName + "_regress", initial.StandardOutput);
            Assert.Contains("Selected 2 tests; skipped 0.", initial.StandardOutput);
            await using (NpgsqlConnection connection = await OpenRegressionConnectionAsync(port, expectedName + "_regress", token))
            {
                await using var command = new NpgsqlCommand("SELECT extname FROM pg_extension WHERE extname = $1", connection);
                command.Parameters.Add(new NpgsqlParameter { Value = expectedName });
                Assert.AreEqual(expectedName, await command.ExecuteScalarAsync(token));
                command.CommandText = "SELECT add(17, 25)";
                command.Parameters.Clear();
                Assert.AreEqual(42, await command.ExecuteScalarAsync(token));
            }

            ProcessResult reused = await InvokeAsync([.. options, "--no-build"], token);
            Assert.AreEqual(0, reused.ExitCode, reused.StandardOutput + reused.StandardError);
            Assert.Contains("Reusing database " + expectedName + "_regress", reused.StandardOutput);
            Assert.Contains("Selected 1 tests; skipped 0.", reused.StandardOutput);
            Assert.AreSequenceEqual(originalSql, await File.ReadAllBytesAsync(setup, token));
            Assert.AreSequenceEqual(originalExpected, await File.ReadAllBytesAsync(setupOutput, token));
            string expectedResult = $"\\set ECHO none{Environment.NewLine}42{Environment.NewLine}";
            Assert.AreEqual(expectedResult, await File.ReadAllTextAsync(Path.Combine(suite, "results", "addition.out"), token));
        }
        finally
        {
            await cluster.StopAsync(CancellationToken.None);
        }
    }
}
