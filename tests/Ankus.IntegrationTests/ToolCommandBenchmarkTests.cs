using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using Ankus.PgConfig;
using Ankus.Testing;

namespace Ankus.IntegrationTests;

public sealed partial class ToolCommandTests
{
    /// <summary>
    /// Publishes benchmark-only wrappers, measures inside PostgreSQL, rolls author writes back, and retains history.
    /// </summary>
    [RetryPortCollisionTestMethod]
    [DoNotParallelize]
    public async Task BenchRunsInBackendAndPersistsResultsOutsideMeasurementTransaction()
    {
        CancellationToken token = context.CancellationToken;
        const string InsertBenchmark = "Ankus.Examples.Hello.BenchmarkProbe.SuccessInsertRow(Ankus.PgBencher)";
        const string AddNumericBenchmark = "Ankus.Examples.Hello.BenchmarkProbe.SuccessAddNumeric(Ankus.PgBencher)";
        string installationSql = await File.ReadAllTextAsync(
            Directory.GetFiles(Path.Combine(s_published, "extension"), "ankus_tool_probe--*.sql").Single(), token);
        Assert.DoesNotContain("CREATE SCHEMA benches", installationSql);
        Assert.DoesNotContain("ankus_bench_", installationSql);

        const string Group = "integration-benchmark";
        PostgresInstallation installation = await ReserveCaseInstallationAsync(token);
        string benchmarkDirectory = CreateDirectory();
        string benchmarkProject = Path.Combine(benchmarkDirectory, "BenchmarkProbe.csproj");
        XDocument definition = XDocument.Load(s_project);
        definition.Root!.Add(new XElement("PropertyGroup",
            new XElement("AnkusPostgresMajor", new XAttribute("Condition",
                "'$(AnkusIncludeBenchmarks)' == 'true' and '$(BenchmarkProbe)' == 'enabled'"), MajorText()),
            new XElement("AnkusPgConfigPath", new XAttribute("Condition",
                "'$(AnkusIncludeBenchmarks)' == 'true' and '$(BenchmarkProbe)' == 'enabled'"), installation.PgConfigPath),
            new XElement("AnkusPostgresMajor", new XAttribute("Condition",
                "'$(AnkusIncludeBenchmarks)' != 'true' or '$(BenchmarkProbe)' != 'enabled'"), DifferentMajor()),
            new XElement("AnkusPgConfigPath", new XAttribute("Condition",
                "'$(AnkusIncludeBenchmarks)' != 'true' or '$(BenchmarkProbe)' != 'enabled'"), string.Empty)));
        definition.Save(benchmarkProject);
        File.Copy(Path.Combine(Path.GetDirectoryName(s_project)!, "BenchmarkProbe.cs"),
            Path.Combine(benchmarkDirectory, "BenchmarkProbe.cs"));
        // The default development port can fall inside an operating-system exclusion, such as a Windows Hyper-V range.
        using PortReservation reservation = PortReservation.Create();
        int port = reservation.Port;
        reservation.Dispose();
        string[] options = ["--home", s_home, "--project", benchmarkProject, "--configuration", "Release",
            "--property", "BenchmarkProbe=enabled", "--port", port.ToString(CultureInfo.InvariantCulture)];
        var cluster = new PostgresDevelopmentCluster(installation, s_home);
        try
        {
            ProcessResult run = await InvokeAsync(
                ["bench", "Success", .. options, "--group-name", Group, "--resetdb"], token);
            Assert.AreEqual(0, run.ExitCode, run.StandardOutput + run.StandardError);
            Assert.Contains("     Running " + InsertBenchmark + " [transaction=", run.StandardOutput);
            Assert.Contains("Bench group " + Group + Environment.NewLine, run.StandardOutput);
            Assert.MatchesRegex(@"    Result (\d+) total, \1 ok, 0 failed", run.StandardOutput);
            Assert.Contains(new string(' ', 28) + "time:   [", run.StandardOutput);
            Assert.Contains(new string(' ', 28) + "mean:   [", run.StandardOutput);
            Assert.Contains("] std. dev. [", run.StandardOutput);
            Assert.Contains(new string(' ', 28) + "median: [", run.StandardOutput);
            Assert.Contains("] med. abs. dev. [", run.StandardOutput);
            Assert.IsEmpty(run.StandardError);
            if (s_installation.Version.Major >= 18)
            {
                string benchmarkOutput = Path.Combine(benchmarkDirectory, "bin", "ankus-bench",
                    s_installation.Label, System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier, "Release");
                string stagedControl = Path.Combine(benchmarkOutput, ".ankus-bench", "extension", "ankus_tool_probe.control");
                Assert.IsTrue(File.Exists(stagedControl), stagedControl);
                Assert.Contains(Path.GetDirectoryName(stagedControl)!, run.StandardOutput);
                Assert.DoesNotContain(s_installation.LibraryDirectory, run.StandardOutput);
            }

            string connectionString = await cluster.GetConnectionStringAsync("ankus_tool_probe_benches", token);
            await using (Npgsql.NpgsqlConnection connection = BenchmarkConnection(connectionString, "ankus_tool_probe_benches"))
            {
                await connection.OpenAsync(token);
                await using var command = new Npgsql.NpgsqlCommand(
                    "SELECT pg_catalog.to_regclass('public.ankus_benchmark_probe') IS NULL", connection);
                Assert.IsTrue((bool)(await command.ExecuteScalarAsync(token))!);
            }

            const string EmptyDatabase = "ankus_tool_probe_empty_benches";
            await cluster.DropDatabaseAsync(EmptyDatabase, force: true, token);
            _ = await cluster.CreateDatabaseAsync(EmptyDatabase, token);
            try
            {
                ProcessResult emptyReport = await InvokeAsync(
                    ["bench", .. options, "--database", EmptyDatabase, "--report"], token);
                Assert.AreEqual(1, emptyReport.ExitCode, emptyReport.StandardOutput + emptyReport.StandardError);
                Assert.Contains("No benchmark history is available", emptyReport.StandardError);
                string emptyConnection = await cluster.GetConnectionStringAsync(EmptyDatabase, token);
                await using Npgsql.NpgsqlConnection empty = BenchmarkConnection(emptyConnection, EmptyDatabase);
                await empty.OpenAsync(token);
                await using var schema = new Npgsql.NpgsqlCommand(
                    "SELECT pg_catalog.to_regnamespace('ankus_bench') IS NULL", empty);
                Assert.IsTrue((bool)(await schema.ExecuteScalarAsync(token))!);
            }
            finally
            {
                await cluster.DropDatabaseAsync(EmptyDatabase, force: true, CancellationToken.None);
            }

            using (var process = new Process
            {
                StartInfo = new ProcessStartInfo(s_tool)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = s_root,
                },
            })
            {
                string[] arguments = ["bench", "SuccessAddNumeric", .. options, "--group-name", "persistent-session",
                    "--no-build", "--wait", "10"];
                foreach (string argument in arguments)
                {
                    process.StartInfo.ArgumentList.Add(argument);
                }

                foreach ((string name, string? value) in s_environment)
                {
                    process.StartInfo.Environment[name] = value;
                }

                Assert.IsTrue(process.Start());
                Task<string> standardOutput = process.StandardOutput.ReadToEndAsync(token);
                Task<string> standardError = process.StandardError.ReadToEndAsync(token);
                try
                {
                    string observerConnection = await cluster.GetConnectionStringAsync("ankus_tool_probe_benches", token);
                    int backendPid = await WaitForBenchmarkBackendAsync(observerConnection, process, token);
                    Assert.IsFalse(process.HasExited, "The benchmark backend was observed only after the tool exited.");
                    await process.WaitForExitAsync(token);
                    string output = await standardOutput;
                    Assert.AreEqual(0, process.ExitCode, output + await standardError);
                    Assert.Contains("Benchmark backend PID: " + backendPid.ToString(CultureInfo.InvariantCulture), output);
                    Assert.Contains(new string(' ', 28) + "change: [", output);
                    Assert.Contains("   Compared " + Group + Environment.NewLine, output);
                    Assert.Contains("Missing From Current Run" + Environment.NewLine + "  " + InsertBenchmark, output);
                    Assert.MatchesRegex(@"\] \(p = \d\.\d\d [<>] \d\.\d\d\)", output);
                }
                finally
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                        await process.WaitForExitAsync(CancellationToken.None);
                    }
                }
            }

            ProcessResult report = await InvokeAsync(
                ["bench", .. options, "--report", "--json"], token);
            Assert.AreEqual(0, report.ExitCode, report.StandardOutput + report.StandardError);
            using JsonDocument history = JsonDocument.Parse(report.StandardOutput);
            JsonElement entry = history.RootElement.EnumerateArray().Single(value =>
                value.GetProperty("benchmark_name").GetString() == InsertBenchmark);
            Assert.AreEqual(Group, entry.GetProperty("group_name").GetString());
            Assert.AreEqual(InsertBenchmark, entry.GetProperty("benchmark_name").GetString());
            JsonElement result = entry.GetProperty("result");
            Assert.AreEqual("ok", result.GetProperty("status").GetString());
            Assert.IsGreaterThan(1, result.GetProperty("source_line").GetInt32());
            Assert.AreEqual(10, result.GetProperty("samples").GetArrayLength());
            Assert.IsNull(result.GetProperty("error_text").GetString());
            string? samplingMode = result.GetProperty("sampling_mode").GetString();
            Assert.IsTrue(samplingMode is "linear" or "flat");
            JsonElement[] estimates = [.. result.GetProperty("estimates").EnumerateArray()];
            JsonElement primary = estimates.FirstOrDefault(static value =>
                value.GetProperty("estimate_kind").GetString() == "slope");
            if (primary.ValueKind == JsonValueKind.Undefined)
            {
                primary = estimates.First(static value => value.GetProperty("estimate_kind").GetString() == "mean");
            }

            Assert.Contains(Environment.NewLine + InsertBenchmark + Environment.NewLine +
                new string(' ', 28) + "time:   [" + BenchmarkTime(primary.GetProperty("ci_lower_bound_ns").GetDouble()) + " " +
                BenchmarkTime(primary.GetProperty("point_estimate_ns").GetDouble()) + " ", run.StandardOutput);
            Assert.HasCount(3, history.RootElement.EnumerateArray());
            JsonElement automatic = history.RootElement.EnumerateArray().Single(value =>
                value.GetProperty("group_name").GetString() == "persistent-session");
            Assert.AreEqual(Group, automatic.GetProperty("compare_group_name").GetString());
            Assert.AreEqual(JsonValueKind.Object,
                automatic.GetProperty("result").GetProperty("comparison").ValueKind);

            ProcessResult comparisonRun = await InvokeAsync(
                ["bench", "SuccessAddNumeric", .. options, "--group-name", "comparison", "--compare-group", Group,
                    "--no-build", "--json"], token);
            Assert.AreEqual(0, comparisonRun.ExitCode, comparisonRun.StandardOutput + comparisonRun.StandardError);
            using JsonDocument comparisonOutput = JsonDocument.Parse(comparisonRun.StandardOutput);
            JsonElement benchmark = Assert.ContainsSingle(comparisonOutput.RootElement.GetProperty("benchmarks").EnumerateArray());
            JsonElement comparison = benchmark.GetProperty("comparison");
            Assert.IsTrue(double.IsFinite(comparison.GetProperty("p_value").GetDouble()));
            Assert.AreEqual(0.95, comparison.GetProperty("mean").GetProperty("confidence_level").GetDouble(), 0.000_001);
            Assert.Contains(InsertBenchmark, comparisonOutput.RootElement.GetProperty("missing_from_current").EnumerateArray()
                .Select(static name => name.GetString()));

            ProcessResult newBenchmarkRun = await InvokeAsync(
                ["bench", "SuccessInsertRow", .. options, "--group-name", "new-benchmark", "--compare-group", "comparison", "--no-build"], token);
            Assert.AreEqual(0, newBenchmarkRun.ExitCode, newBenchmarkRun.StandardOutput + newBenchmarkRun.StandardError);
            Assert.Contains(new string(' ', 28) + "New benchmark; no baseline comparison available.", newBenchmarkRun.StandardOutput);
            Assert.Contains("Missing From Current Run" + Environment.NewLine + "  " + AddNumericBenchmark, newBenchmarkRun.StandardOutput);

            ProcessResult failureRun = await InvokeAsync(
                ["bench", "Failure", .. options, "--group-name", "failure", "--no-build", "--json"], token);
            Assert.AreEqual(1, failureRun.ExitCode, failureRun.StandardOutput + failureRun.StandardError);
            using JsonDocument failureOutput = JsonDocument.Parse(failureRun.StandardOutput);
            JsonElement failed = Assert.ContainsSingle(failureOutput.RootElement.GetProperty("benchmarks").EnumerateArray());
            Assert.AreEqual("failed", failed.GetProperty("status").GetString());
            string? errorText = failed.GetProperty("error_text").GetString();
            Assert.IsNotNull(errorText);
            Assert.Contains("division by zero", errorText);

            ProcessResult listed = await InvokeAsync(["bench", "Success", .. options, "--no-build", "--list"], token);
            Assert.AreEqual(0, listed.ExitCode, listed.StandardOutput + listed.StandardError);
            Assert.Contains(InsertBenchmark + " [", listed.StandardOutput);
            Assert.Contains(AddNumericBenchmark + " [", listed.StandardOutput);
            Assert.DoesNotContain("Running", listed.StandardOutput);
            Assert.DoesNotContain("Failure", listed.StandardOutput);
            ProcessResult unknown = await InvokeAsync(["bench", "SuccessAddNumeric", .. options, "--no-build",
                "--group-name", "unknown-comparison", "--compare-group", "no-such-group"], token);
            Assert.AreNotEqual(0, unknown.ExitCode);
            Assert.Contains("Benchmark comparison group 'no-such-group' was not found.", unknown.StandardOutput + unknown.StandardError);
            ProcessResult none = await InvokeAsync(["bench", "NoSuchBenchmark", .. options, "--no-build"], token);
            Assert.AreNotEqual(0, none.ExitCode);
            Assert.Contains("No benchmarks were discovered in the benches schema.", none.StandardOutput + none.StandardError);
            ProcessResult combined = await InvokeAsync(["bench", .. options, "--report", "--list"], token);
            Assert.AreNotEqual(0, combined.ExitCode);
            Assert.Contains("--report cannot be combined with --list.", combined.StandardOutput + combined.StandardError);
        }
        finally
        {
            await cluster.StopAsync(CancellationToken.None);
        }
    }

    private static Npgsql.NpgsqlConnection BenchmarkConnection(
        string libpqConnection,
        string database,
        string? applicationName = null,
        int timeout = 15)
    {
        var endpoint = new Uri(libpqConnection);
        return new(new Npgsql.NpgsqlConnectionStringBuilder
        {
            Host = endpoint.Host,
            Port = endpoint.Port,
            Username = "postgres",
            Database = database,
            ApplicationName = applicationName,
            Pooling = false,
            Timeout = timeout,
            CommandTimeout = timeout,
        }.ConnectionString);
    }

    private static async Task<int> WaitForBenchmarkBackendAsync(
        string connectionString,
        Process process,
        CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        while (!process.HasExited)
        {
            try
            {
                await using Npgsql.NpgsqlConnection observer = BenchmarkConnection(connectionString,
                    "ankus_tool_probe_benches", "ankus benchmark integration observer", timeout: 1);
                await observer.OpenAsync(timeout.Token);
                await using var active = new Npgsql.NpgsqlCommand(
                    "SELECT pid FROM pg_catalog.pg_stat_activity WHERE datname = current_database() " +
                    "AND application_name = 'psql' ORDER BY backend_start DESC LIMIT 1", observer);
                if (await active.ExecuteScalarAsync(timeout.Token) is int backendPid)
                {
                    return backendPid;
                }
            }
            catch (Npgsql.NpgsqlException)
            {
            }

            await Task.Delay(100, timeout.Token);
        }

        throw new InvalidOperationException("The benchmark tool exited before its backend could be observed.");
    }

    /// <summary>
    /// Formats a duration independently of the tool, with the unit and digits that cargo pgrx bench prints.
    /// </summary>
    private static string BenchmarkTime(double nanoseconds)
    {
        (double value, string unit) = nanoseconds >= 1e9 ? (nanoseconds / 1e9, "s") : nanoseconds >= 1e6 ? (nanoseconds / 1e6, "ms")
            : nanoseconds >= 1e3 ? (nanoseconds / 1e3, "us") : nanoseconds >= 1 ? (nanoseconds, "ns") : (nanoseconds * 1e3, "ps");
        string text = value.ToString(value >= 100 ? "0.00" : value >= 10 ? "0.000" : "0.0000", CultureInfo.InvariantCulture).TrimEnd('0');
        return (text.EndsWith('.') ? text + "0" : text) + " " + unit;
    }
}
