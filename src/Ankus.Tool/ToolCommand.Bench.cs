using System.CommandLine;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Ankus.PgConfig;

namespace Ankus.Tool;

internal static partial class ToolCommand
{
    private static Command CreateBench(Option<string?> home)
    {
        var command = new Command("bench", "Run in-process benchmarks inside PostgreSQL.");
        AddSelectionOptions(command);
        AddBuildOptions(command);
        AddValgrindOption(command);
        var filter = new Argument<string?>("filter")
        {
            Description = "Optional ordinal substring of the managed benchmark name.",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var database = new Option<string?>("--database", "--dbname") { Description = "Benchmark database (default: <extension>_benches)." };
        var group = new Option<string?>("--group-name") { Description = "Name for this benchmark run group (default: timestamp and Git commit)." };
        var compare = new Option<string?>("--compare-group") { Description = "Named comparison baseline (default: latest compatible retained group)." };
        var reset = new Option<bool>("--resetdb") { Description = "Recreate the benchmark database before running." };
        var cascade = new Option<bool>("--cascade") { Description = "Use CASCADE while refreshing the extension." };
        var list = new Option<bool>("--list") { Description = "List discovered benchmarks without measuring them." };
        var report = new Option<bool>("--report") { Description = "Render recent persisted benchmark history without rebuilding." };
        var json = new Option<bool>("--json") { Description = "Write the final benchmark summary as JSON." };
        var wait = new Option<int>("--wait") { Description = "Seconds to wait after printing the backend PID (0–3600)." };
        var noBuild = new Option<bool>("--no-build") { Description = "Use the existing benchmark publication without rebuilding." };
        var port = new Option<int?>("--port") { Description = "Port when starting a stopped server." };
        var timeout = new Option<int>("--timeout") { Description = "Startup timeout in seconds (1–600).", DefaultValueFactory = _ => 60 };
        var settings = new Option<string[]>("--postgresql-conf") { Description = "Literal name=value setting; repeat for multiple settings." };
        command.Arguments.Add(filter);
        command.Options.Add(database);
        command.Options.Add(group);
        command.Options.Add(compare);
        command.Options.Add(reset);
        command.Options.Add(cascade);
        command.Options.Add(list);
        command.Options.Add(report);
        command.Options.Add(json);
        command.Options.Add(wait);
        command.Options.Add(noBuild);
        command.Options.Add(port);
        command.Options.Add(timeout);
        command.Options.Add(settings);
        command.SetAction(async (result, token) =>
        {
            int waitSeconds = result.GetValue(wait);
            ArgumentOutOfRangeException.ThrowIfNegative(waitSeconds);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(waitSeconds, 3_600);
            if (result.GetValue(report))
            {
                string[] incompatible = ["--group-name", "--compare-group", "--resetdb", "--cascade", "--list", "--wait", "--no-build"];
                string[] selected = [.. incompatible.Where(option => result.GetResult(option) is System.CommandLine.Parsing.OptionResult { Implicit: false })];
                if (selected.Length != 0)
                {
                    throw new ArgumentException("--report cannot be combined with " + string.Join(", ", selected) + ".");
                }
            }

            ExtensionSelection selection = await SelectExtensionAsync(result, home, token);
            PostgresInstallation installation = selection.Installation;
            var properties = new Dictionary<string, string>(selection.Properties, StringComparer.OrdinalIgnoreCase);
            if (properties.TryGetValue("AnkusIncludeTests", out string? includeTests) &&
                string.Equals(includeTests, "true", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("ankus bench cannot combine AnkusIncludeTests=true with benchmark publication.");
            }

            properties["AnkusIncludeBenchmarks"] = "true";
            selection = selection with { Properties = properties };
            string extension = await ExtensionBuilder.GetExtensionNameAsync(selection.Project, selection.Configuration,
                installation, token, properties);
            string benchmarkDatabase = result.GetValue(database) ?? BenchmarkDatabaseName(extension);
            bool jsonOutput = result.GetValue(json);
            var cluster = new PostgresDevelopmentCluster(installation, result.GetValue(home));
            string output = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(selection.Project)!, "bin", "ankus-bench",
                installation.Label, RuntimeInformation.RuntimeIdentifier, selection.Configuration));
            Dictionary<string, string> serverSettings = ParseServerSettings(result.GetValue(settings) ?? []);
            if (!result.GetValue(report))
            {
                await cluster.StopAsync(token);
                if (!result.GetValue(noBuild))
                {
                    int code = await PublishAsync(selection, output, token,
                        diagnosticsToStandardError: jsonOutput, announce: !jsonOutput);
                    if (code != 0)
                    {
                        return code;
                    }
                }

                IReadOnlyList<string> installed;
                if (installation.Version.Major >= 18)
                {
                    string controlBase = StageBenchmarkExtension(output, token, out installed);
                    char separator = OperatingSystem.IsWindows() ? ';' : ':';
                    PrependSearchPath(serverSettings, "extension_control_path", controlBase, "$system", separator);
                    PrependSearchPath(serverSettings, "dynamic_library_path", output, "$libdir", separator);
                }
                else
                {
                    installed = ExtensionInstaller.Install(output, installation, null, token);
                }

                foreach (string path in installed)
                {
                    if (jsonOutput)
                    {
                        Console.Error.WriteLine($"Installed {path}");
                    }
                    else
                    {
                        Console.WriteLine($"Installed {path}");
                    }
                }
            }

            var server = new PostgresDevelopmentOptions
            {
                Port = result.GetValue(port),
                TimeoutSeconds = result.GetValue(timeout),
                UseValgrind = result.GetValue<bool>("--valgrind"),
                Settings = serverSettings,
            };
            bool reportOnly = result.GetValue(report);
            await cluster.StartAsync(server, token);
            if (result.GetValue(reset))
            {
                await cluster.DropDatabaseAsync(benchmarkDatabase, force: true, token);
            }

            if (!reportOnly)
            {
                _ = await cluster.CreateDatabaseAsync(benchmarkDatabase, token);
            }

            string connection = await cluster.GetConnectionStringAsync(benchmarkDatabase, token);
            await using BenchmarkSqlSession sql = await BenchmarkSqlSession.StartAsync(installation.PsqlPath, connection, token);
            if (reportOnly)
            {
                return await ReportBenchmarksAsync(sql, extension, result.GetValue(filter), jsonOutput, token);
            }

            await EnsureBenchmarkStoreAsync(sql, token);
            string drop = "DROP EXTENSION IF EXISTS " + QuoteIdentifier(extension) +
                (result.GetValue(cascade) ? " CASCADE" : string.Empty) + ";\n";
            await sql.ExecuteAsync(drop + "CREATE EXTENSION " + QuoteIdentifier(extension) +
                (result.GetValue(cascade) ? " CASCADE" : string.Empty) + ";\n", token);
            List<BenchmarkDescriptor> benchmarks = await DiscoverBenchmarksAsync(sql, result.GetValue(filter), token);
            if (result.GetValue(list))
            {
                foreach (BenchmarkDescriptor benchmark in benchmarks)
                {
                    Console.WriteLine($"{benchmark.Name} [{benchmark.Transaction}; {benchmark.SampleSize} samples]");
                }

                return 0;
            }

            if (benchmarks.Count == 0)
            {
                throw new InvalidOperationException("No benchmarks were discovered in the benches schema.");
            }

            string groupName = result.GetValue(group) ?? await DefaultBenchmarkGroupAsync(sql, selection.Project, token);
            string? compareGroup = await ResolveBenchmarkComparisonAsync(sql, result.GetValue(compare),
                groupName, selection.Configuration, token);
            long runGroup = await InsertBenchmarkGroupAsync(sql, groupName, compareGroup,
                extension, selection.Configuration, token);
            string backendPid = (await sql.ExecuteAsync("SELECT pg_backend_pid();\n", token)).Trim();
            Stream attachmentOutput = jsonOutput ? Console.OpenStandardError() : Console.OpenStandardOutput();
            byte[] attachmentAnnouncement = Encoding.UTF8.GetBytes("Benchmark backend PID: " + backendPid + Environment.NewLine);
            await attachmentOutput.WriteAsync(attachmentAnnouncement, token);
            await attachmentOutput.FlushAsync(token);

            if (waitSeconds != 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(waitSeconds), token);
            }

            var results = new List<JsonElement>();
            int failures = 0;
            foreach (BenchmarkDescriptor benchmark in benchmarks)
            {
                if (!jsonOutput)
                {
                    Console.WriteLine("Benchmarking " + benchmark.Name);
                }

                string? baseline = compareGroup is null ? null : await LoadBenchmarkBaselineAsync(
                    sql, compareGroup, benchmark.Name, token);
                string baselineSql = baseline is null ? "NULL" : TextExpression(baseline) + "::jsonb";
                string measured = (await sql.ExecuteAsync(
                    "BEGIN;\nSELECT benches." + QuoteIdentifier(benchmark.FunctionName) + "(" + baselineSql + ")::text;\nROLLBACK;\n", token)).Trim();
                using JsonDocument document = JsonDocument.Parse(measured);
                JsonElement resultJson = document.RootElement.Clone();
                results.Add(resultJson);
                bool passed = resultJson.GetProperty("status").GetString() == "ok";
                if (!passed)
                {
                    failures++;
                }

                await PersistBenchmarkAsync(sql, runGroup, benchmark.Name, measured, token);
                if (!jsonOutput)
                {
                    PrintBenchmarkResult(resultJson);
                }
            }

            string status = failures == 0 ? "completed" : failures == benchmarks.Count ? "failed" : "partial";
            await sql.ExecuteAsync("UPDATE ankus_bench.run_group SET status = " +
                TextExpression(status) + " WHERE id = " +
                runGroup.ToString(CultureInfo.InvariantCulture) + ";\n", token);
            if (jsonOutput)
            {
                WriteBenchmarkSummary(groupName, compareGroup, results);
            }

            return failures == 0 ? 0 : 1;
        });
        return command;
    }

    private static string StageBenchmarkExtension(string output, CancellationToken token,
        out IReadOnlyList<string> installed)
    {
        PublishedExtension manifest = PublishedExtension.Read(output);
        string source = Path.Combine(output, "extension");
        string stage = Path.Combine(output, ".ankus-bench");
        string temporary = stage + "." + Guid.NewGuid().ToString("N") + ".tmp";
        string controls = Path.Combine(temporary, "extension");
        string scripts = manifest.ScriptDirectory is null ? controls : Path.Combine(temporary, "scripts");
        string[] scriptNames = [manifest.Sql, .. manifest.UpgradeScripts, .. manifest.VersionControlFiles];
        string[] sourceFiles =
        [
            Path.Combine(output, manifest.Library),
            Path.Combine(source, manifest.Control),
            .. scriptNames.Select(name => Path.Combine(source, name)),
        ];
        foreach (string path in sourceFiles)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("The published extension is incomplete.", path);
            }
        }

        _ = manifest.GetScriptDirectory(output, temporary);
        IReadOnlyDictionary<string, string> authoredControl = ExtensionControlFile.Read(sourceFiles[1]);
        string? stagedControl = null;
        if (manifest.ScriptDirectory is not null)
        {
            var values = new Dictionary<string, string>(authoredControl, StringComparer.Ordinal)
            {
                ["directory"] = "scripts",
            };
            stagedControl = ExtensionControlFile.Format(values);
        }

        try
        {
            Directory.CreateDirectory(controls);
            Directory.CreateDirectory(scripts);
            var destinations = new List<string>(scriptNames.Length + 1);
            foreach (string name in scriptNames)
            {
                token.ThrowIfCancellationRequested();
                string destination = Path.Combine(scripts, name);
                File.Copy(Path.Combine(source, name), destination);
                destinations.Add(destination);
            }

            string control = Path.Combine(controls, manifest.Control);
            if (stagedControl is null)
            {
                File.Copy(sourceFiles[1], control);
            }
            else
            {
                File.WriteAllText(control, stagedControl);
            }

            destinations.Add(control);
            token.ThrowIfCancellationRequested();
            if (Directory.Exists(stage))
            {
                Directory.Delete(stage, recursive: true);
            }

            Directory.Move(temporary, stage);
            installed = [.. destinations.Select(path => Path.Combine(stage, Path.GetRelativePath(temporary, path)))];
            return stage;
        }
        finally
        {
            if (Directory.Exists(temporary))
            {
                Directory.Delete(temporary, recursive: true);
            }
        }
    }

    private static void PrependSearchPath(Dictionary<string, string> settings, string name,
        string path, string defaultValue, char separator)
    {
        string existing = settings.GetValueOrDefault(name, defaultValue);
        settings[name] = path + separator + existing;
    }

    private static async Task EnsureBenchmarkStoreAsync(BenchmarkSqlSession sql, CancellationToken token)
        => _ = await sql.ExecuteAsync("""
            CREATE SCHEMA IF NOT EXISTS ankus_bench;
            CREATE TABLE IF NOT EXISTS ankus_bench.run_group (
                id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                group_name text NOT NULL UNIQUE,
                compare_group_name text,
                extension_name text NOT NULL,
                configuration text NOT NULL,
                postgres_version text NOT NULL,
                runtime_identifier text NOT NULL,
                pg_settings jsonb NOT NULL,
                created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
                status text NOT NULL DEFAULT 'running'
            );
            CREATE TABLE IF NOT EXISTS ankus_bench.benchmark_run (
                id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                group_id bigint NOT NULL REFERENCES ankus_bench.run_group(id) ON DELETE CASCADE,
                benchmark_name text NOT NULL,
                result jsonb NOT NULL
            );
            """, token);

    private static async Task<List<BenchmarkDescriptor>> DiscoverBenchmarksAsync(
        BenchmarkSqlSession sql,
        string? filter,
        CancellationToken token)
    {
        string functions = await sql.ExecuteAsync("""
            SELECT p.proname
            FROM pg_catalog.pg_proc AS p
            JOIN pg_catalog.pg_namespace AS n ON n.oid = p.pronamespace
            WHERE n.nspname = 'benches' AND p.pronargs = 0 AND p.proname LIKE 'ankus_bench_%_describe'
            ORDER BY p.proname;
            """, token);
        var descriptors = new List<BenchmarkDescriptor>();
        foreach (string function in functions.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string json = (await sql.ExecuteAsync(
                "SELECT benches." + QuoteIdentifier(function) + "()::text;\n", token)).Trim();
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            string name = root.GetProperty("bench_name").GetString()!;
            if (filter is not null && !name.Contains(filter, StringComparison.Ordinal))
            {
                continue;
            }

            descriptors.Add(new(name, root.GetProperty("function_name").GetString()!,
                root.GetProperty("transaction_mode").GetString()!, root.GetProperty("config").GetProperty("sample_size").GetInt32()));
        }

        return descriptors;
    }

    private static async Task<long> InsertBenchmarkGroupAsync(
        BenchmarkSqlSession sql,
        string group,
        string? compareGroup,
        string extension,
        string configuration,
        CancellationToken token)
    {
        string statement = "INSERT INTO ankus_bench.run_group (group_name, compare_group_name, extension_name, configuration, " +
            "postgres_version, runtime_identifier, pg_settings) SELECT " + TextExpression(group) + ", " +
            (compareGroup is null ? "NULL" : TextExpression(compareGroup)) + ", " + TextExpression(extension) + ", " +
            TextExpression(configuration) + ", current_setting('server_version'), " + TextExpression(RuntimeInformation.RuntimeIdentifier) +
            ", (SELECT jsonb_object_agg(name, setting ORDER BY name) FROM pg_catalog.pg_settings) RETURNING id;\n";
        string value = (await sql.ExecuteAsync(statement, token)).Trim();
        return long.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture);
    }

    private static async Task<string?> LoadBenchmarkBaselineAsync(
        BenchmarkSqlSession sql,
        string group,
        string benchmark,
        CancellationToken token)
    {
        string result = await sql.ExecuteAsync(
            "SELECT r.result::text FROM ankus_bench.benchmark_run AS r JOIN ankus_bench.run_group AS g ON g.id = r.group_id " +
            "WHERE g.group_name = " + TextExpression(group) + " AND r.benchmark_name = " + TextExpression(benchmark) +
            " AND r.result->>'status' = 'ok' ORDER BY g.created_at DESC, r.id DESC LIMIT 1;\n", token);
        return string.IsNullOrWhiteSpace(result) ? null : result.Trim();
    }

    private static async Task PersistBenchmarkAsync(
        BenchmarkSqlSession sql,
        long group,
        string benchmark,
        string result,
        CancellationToken token)
        => _ = await sql.ExecuteAsync(
            "INSERT INTO ankus_bench.benchmark_run (group_id, benchmark_name, result) VALUES (" +
            group.ToString(CultureInfo.InvariantCulture) + ", " + TextExpression(benchmark) + ", " +
            TextExpression(result) + "::jsonb);\n", token);

    private static async Task<int> ReportBenchmarksAsync(
        BenchmarkSqlSession sql,
        string extension,
        string? filter,
        bool json,
        CancellationToken token)
    {
        string available = (await sql.ExecuteAsync(
            "SELECT pg_catalog.to_regclass('ankus_bench.benchmark_run') IS NOT NULL " +
            "AND pg_catalog.to_regclass('ankus_bench.run_group') IS NOT NULL;\n", token)).Trim();
        if (available != "t")
        {
            throw new InvalidOperationException("No benchmark history is available in the selected database.");
        }

        string condition = filter is null ? string.Empty : " AND r.benchmark_name LIKE '%' || " + TextExpression(filter) + " || '%'";
        string rows = await sql.ExecuteAsync(
            "SELECT jsonb_build_object('group_name', g.group_name, 'compare_group_name', g.compare_group_name, " +
            "'created_at', g.created_at, 'benchmark_name', r.benchmark_name, 'result', r.result)::text " +
            "FROM ankus_bench.benchmark_run AS r JOIN ankus_bench.run_group AS g ON g.id = r.group_id " +
            "WHERE g.extension_name = " + TextExpression(extension) + condition +
            " ORDER BY g.created_at DESC, r.id LIMIT 10;\n", token);
        string[] entries = rows.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (entries.Length == 0)
        {
            throw new InvalidOperationException(filter is null
                ? "No benchmark history is available in the selected database."
                : "No benchmark history matches '" + filter + "'.");
        }

        if (json)
        {
            Console.WriteLine("[" + string.Join(",", entries) + "]");
        }
        else
        {
            foreach (string entry in entries)
            {
                using JsonDocument document = JsonDocument.Parse(entry);
                JsonElement root = document.RootElement;
                Console.Write(root.GetProperty("group_name").GetString() + "  " + root.GetProperty("benchmark_name").GetString() + "  ");
                PrintBenchmarkResult(root.GetProperty("result"));
            }
        }

        return 0;
    }

    private static async Task<string> DefaultBenchmarkGroupAsync(
        BenchmarkSqlSession sql,
        string project,
        CancellationToken token)
    {
        string? github = Environment.GetEnvironmentVariable("GITHUB_SHA");
        string commit;
        if (string.IsNullOrWhiteSpace(github))
        {
            using var output = new MemoryStream();
            int code = await ToolProcess.RunAsync("git", ["rev-parse", "--short", "HEAD"], token,
                outputStream: output, workingDirectory: Path.GetDirectoryName(project));
            commit = code == 0 ? Encoding.UTF8.GetString(output.ToArray()).Trim() : string.Empty;
        }
        else
        {
            commit = github;
        }

        string suffix = commit.Length == 0 ? "nogit" : commit[..Math.Min(7, commit.Length)];
        string timestamp = (await sql.ExecuteAsync(
            "SELECT to_char(clock_timestamp(), 'YYYYMMDD_HH24MISS');\n", token)).Trim();
        return timestamp + "_" + suffix;
    }

    private static async Task<string?> ResolveBenchmarkComparisonAsync(
        BenchmarkSqlSession sql,
        string? requested,
        string currentGroup,
        string configuration,
        CancellationToken token)
    {
        string condition;
        if (requested is not null)
        {
            condition = "group_name = " + TextExpression(requested);
        }
        else
        {
            condition = "status IN ('completed', 'partial') AND configuration = " + TextExpression(configuration) +
                " AND group_name <> " + TextExpression(currentGroup);
        }

        string resolved = (await sql.ExecuteAsync(
            "SELECT group_name FROM ankus_bench.run_group WHERE " + condition +
            " ORDER BY created_at DESC, id DESC LIMIT 1;\n", token)).Trim();
        if (requested is not null && resolved.Length == 0)
        {
            throw new InvalidOperationException("Benchmark comparison group '" + requested + "' was not found.");
        }

        return resolved.Length == 0 ? null : resolved;
    }

    private static void PrintBenchmarkResult(JsonElement result)
    {
        if (result.GetProperty("status").GetString() != "ok")
        {
            Console.WriteLine("FAILED: " + result.GetProperty("error_text").GetString());
            return;
        }

        JsonElement mean = result.GetProperty("estimates").EnumerateArray().First(static value =>
            value.GetProperty("estimate_kind").GetString() == "mean");
        Console.WriteLine(mean.GetProperty("point_estimate_ns").GetDouble().ToString("N2", CultureInfo.InvariantCulture) + " ns");
    }

    private static void WriteBenchmarkSummary(string group, string? comparison, List<JsonElement> results)
    {
        using Stream output = Console.OpenStandardOutput();
        using var writer = new Utf8JsonWriter(output, new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        writer.WriteString("group_name", group);
        if (comparison is null)
        {
            writer.WriteNull("compare_group_name");
        }
        else
        {
            writer.WriteString("compare_group_name", comparison);
        }

        writer.WriteStartArray("benchmarks");
        foreach (JsonElement result in results)
        {
            result.WriteTo(writer);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.Flush();
        Console.WriteLine();
    }

    private static string BenchmarkDatabaseName(string extension)
    {
        const string Suffix = "_benches";
        int available = 63 - Suffix.Length;
        string prefix = extension.Length <= available ? extension : extension[..available];
        return prefix + Suffix;
    }

    private static string QuoteIdentifier(string value) => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private static string TextExpression(string value)
        => "convert_from(decode('" + Convert.ToHexString(Encoding.UTF8.GetBytes(value)) + "', 'hex'), 'UTF8')";

    private sealed class BenchmarkSqlSession(Process process) : IAsyncDisposable
    {
        private readonly Process _process = process;
        private readonly Task _errorPump = PumpErrorsAsync(process);
        private bool _disposed;

        internal static async Task<BenchmarkSqlSession> StartAsync(
            string executable,
            string connection,
            CancellationToken token)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo(executable)
                {
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardInputEncoding = Encoding.UTF8,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                },
            };
            string[] arguments =
            [
                "--no-psqlrc", "--no-password", "--quiet", "--tuples-only", "--no-align", "--set=ON_ERROR_STOP=1",
                "--dbname=" + connection,
            ];
            foreach (string argument in arguments)
            {
                process.StartInfo.ArgumentList.Add(argument);
            }

            foreach (string key in process.StartInfo.Environment.Keys
                .Where(static key => key.StartsWith("PG", StringComparison.OrdinalIgnoreCase)).ToArray())
            {
                process.StartInfo.Environment.Remove(key);
            }

            process.StartInfo.Environment["PGCLIENTENCODING"] = "UTF8";
            if (!process.Start())
            {
                process.Dispose();
                throw new InvalidOperationException("Failed to start the PostgreSQL benchmark session.");
            }

            var session = new BenchmarkSqlSession(process);
            try
            {
                _ = await session.ExecuteAsync("SET client_min_messages = warning;\n", token);
                return session;
            }
            catch
            {
                await session.DisposeAsync();
                throw;
            }
        }

        internal async Task<string> ExecuteAsync(string command, CancellationToken token)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            string marker = "__ankus_benchmark_" + Guid.NewGuid().ToString("N");
            try
            {
                await _process.StandardInput.WriteAsync(command.AsMemory(), token);
                if (!command.EndsWith('\n'))
                {
                    await _process.StandardInput.WriteAsync("\n".AsMemory(), token);
                }

                await _process.StandardInput.WriteLineAsync(("\\echo " + marker).AsMemory(), token);
                await _process.StandardInput.FlushAsync(token);
                var output = new StringBuilder();
                while (await _process.StandardOutput.ReadLineAsync(token) is string line)
                {
                    if (line == marker)
                    {
                        return output.ToString().ReplaceLineEndings("\n");
                    }

                    output.AppendLine(line);
                }
            }
            catch (OperationCanceledException)
            {
                Stop();
                await _process.WaitForExitAsync(CancellationToken.None);
                await _errorPump;
                throw;
            }

            await _process.WaitForExitAsync(CancellationToken.None);
            await _errorPump;
            throw new InvalidOperationException("PostgreSQL benchmark session failed with exit code " +
                _process.ExitCode.ToString(CultureInfo.InvariantCulture) + ".");
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (!_process.HasExited)
            {
                _process.StandardInput.Close();
            }

            await _process.WaitForExitAsync(CancellationToken.None);
            await _errorPump;
            _process.Dispose();
        }

        private static async Task PumpErrorsAsync(Process process)
        {
            while (await process.StandardError.ReadLineAsync(CancellationToken.None) is string line)
            {
                await Console.Error.WriteLineAsync(line);
            }
        }

        private void Stop()
        {
            try
            {
                _process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) when (_process.HasExited)
            {
                // The client exited while cancellation was delivered.
            }
        }
    }

    private sealed record BenchmarkDescriptor(string Name, string FunctionName, string Transaction, int SampleSize);
}
