using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Ankus.Testing;
using Npgsql;

namespace Ankus.IntegrationTests;

public sealed partial class ToolCommandTests
{
    private static readonly JsonSerializerOptions s_bindingJsonOptions = new() { MaxDepth = 512 };

    /// <summary>
    /// Binding discovery builds its companion without capturing native linker inputs before the extension is compiled.
    /// </summary>
    [TestMethod]
    public async Task SdkBindingDiscoveryLeavesNativeLinkInputsDeferred()
    {
        string root = CreateBindingDirectory();
        string project = Path.Combine(root, "BindingDiscovery.csproj");
        File.Copy(s_project, project);
        ProcessResult result = await RunDotnetAsync(
            ["msbuild", project, "-restore", "-target:_ResolveAnkusBindings", "-verbosity:quiet",
                "-property:Configuration=Release", "-property:RuntimeIdentifier=" + RuntimeInformation.RuntimeIdentifier,
                "-property:AnkusPostgresMajor=" + MajorText(), "-property:AnkusPgConfigPath=" + s_installation.PgConfigPath,
                "-getItem:ManagedBinary,LinkerArg,NativeLibrary,_AnkusBindingAssembly",
                "-bl:" + Path.Combine(root, "binding-discovery-{}.binlog")], context.CancellationToken);
        result.EnsureSuccess("dotnet", ["msbuild"]);
        using JsonDocument document = JsonDocument.Parse(result.StandardOutput);
        JsonElement items = document.RootElement.GetProperty("Items");
        Assert.AreEqual(0, items.GetProperty("ManagedBinary").GetArrayLength());
        Assert.AreEqual(0, items.GetProperty("LinkerArg").GetArrayLength());
        Assert.AreEqual(0, items.GetProperty("NativeLibrary").GetArrayLength());
        JsonElement companion = Assert.ContainsSingle(items.GetProperty("_AnkusBindingAssembly").EnumerateArray());
        string? assembly = companion.GetProperty("FullPath").GetString();
        Assert.IsNotNull(assembly);
        Assert.IsTrue(File.Exists(assembly));
        Assert.StartsWith("Ankus.Postgres.Pg", Path.GetFileName(assembly));
    }

    /// <summary>
    /// Separate SDK projects exchange the same native types and retain them through Native AOT publication, rebuild and clean.
    /// </summary>
    [TestMethod]
    public async Task SdkSharesNativeTypesAcrossProjectsAndPublishesThem()
    {
        CancellationToken token = context.CancellationToken;
        string root = CreateBindingDirectory();
        string provider = Path.Combine(root, "provider");
        string consumer = Path.Combine(root, "consumer");
        Directory.CreateDirectory(provider);
        Directory.CreateDirectory(consumer);
        LinkBindingIntermediates(provider);
        LinkBindingIntermediates(consumer);
        string providerProject = Path.Combine(provider, "BindingProvider.csproj");
        string consumerProject = Path.Combine(consumer, "BindingConsumer.csproj");
        File.Copy(s_project, providerProject);
        XDocument project = XDocument.Load(s_project);
        project.Root!.Add(new XElement("ItemGroup", new XElement("ProjectReference",
            new XAttribute("Include", providerProject))));
        string nativeProvider = await CompileBindingProviderAsync(root, token);
        project.Root.Add(new XElement("ItemGroup", new XElement("NativeLibrary", new XAttribute("Include", nativeProvider))));
        project.Save(consumerProject);
        await File.WriteAllTextAsync(Path.Combine(provider, "BindingProvider.cs"), """
            using Ankus.Postgres;
            public static class BindingProvider
            {
                public static RangeTblRef Create(int value) => new() { type = NodeTag.T_RangeTblRef, rtindex = value };
                public static void Increment(ref RangeTblRef value) => value.rtindex++;
                public static ErrorData CreateError(int value) => new() { elevel = value, output_to_client = true };
            }
            """, token);
        await File.WriteAllTextAsync(Path.Combine(consumer, "BindingConsumer.cs"), """
            using Ankus;
            using Ankus.Postgres;
            public static class BindingConsumer
            {
                [PgFunction]
                public static int NativeBindingRoundTrip(int input)
                {
                    RangeTblRef value = BindingProvider.Create(input);
                    BindingProvider.Increment(ref value);
                    return value.rtindex;
                }

                [PgFunction]
                public static bool NativeBindingTag() => BindingProvider.Create(9).type == NodeTag.T_RangeTblRef;

                [PgFunction]
                public static unsafe int NativeBindingSize() => sizeof(RangeTblRef);

                [PgFunction]
                public static string NativeBindingRid() => NativeBinding.RuntimeIdentifier;

                [PgFunction]
                public static int NativeDependencyRoundTrip(int input)
                {
                    ErrorData value = BindingProvider.CreateError(input);
                    return value.output_to_client ? value.elevel : -1;
                }

                [PgFunction]
                public static string NativeRecordResult()
                {
                    FullTransactionId value = NativeMethods.FullTransactionIdFromU64(0xFEDCBA9876543210UL);
                    return value.value.ToString("X16", System.Globalization.CultureInfo.InvariantCulture);
                }

                [PgFunction]
                public static unsafe bool NativeLinkedProvider()
                {
                    OutputPluginCallbacks callbacks = new() { startup_cb = 17, shutdown_cb = 23 };
                    NativeMethods._PG_output_plugin_init((nint)(&callbacks));
                    return callbacks.startup_cb == 0 && callbacks.shutdown_cb == 23;
                }

                [PgFunction]
                public static unsafe int NativeNodeCall(int input)
                {
                    nint address = NativeMethods.makeInteger(input);
                    try
                    {
                        Integer value = *(Integer*)address;
                        if (value.type != NodeTag.T_Integer) { throw new System.InvalidOperationException("Unexpected native node tag."); }
                        return value.ival;
                    }
                    finally { NativeMethods.pfree(address); }
                }

                [PgFunction]
                public static string NativeTypedError(uint functionOid, bool swallow)
                {
                    PgMemoryContext owner = PgMemoryContext.Current;
                    PgMemoryContext? failedContext = null;
                    PgException failure;
                    try
                    {
                        PgTransaction.RunInSubtransaction(() =>
                        {
                            failedContext = PgMemoryContext.Current;
                            if (swallow)
                            {
                                Spi.Connect(session =>
                                {
                                    session.Execute("INSERT INTO native_typed_effects VALUES (1)");
                                    try { NativeMethods.OidFunctionCall1Coll(functionOid, 0, 0); }
                                    catch (PgException) { }
                                });
                            }
                            else
                            {
                                Spi.Execute("INSERT INTO native_typed_effects VALUES (1)");
                                NativeMethods.OidFunctionCall1Coll(functionOid, 0, 0);
                            }
                        });
                        throw new System.InvalidOperationException("The deliberate native error did not occur.");
                    }
                    catch (PgException exception) { failure = exception; }
                    bool restored = owner.Id == PgMemoryContext.Current.Id;
                    bool expired = failedContext is not null && !failedContext.IsAlive;
                    bool nestedEntered = false;
                    ulong recovered = Spi.Connect(session =>
                    {
                        ulong value = PgTransaction.RunInSubtransaction(() =>
                        {
                            Spi.Execute("INSERT INTO native_typed_effects VALUES (2)");
                            try
                            {
                                PgTransaction.RunInSubtransaction(() =>
                                {
                                    Spi.Execute("INSERT INTO native_typed_effects VALUES (3)");
                                    nestedEntered = true;
                                    NativeMethods.OidFunctionCall1Coll(functionOid, 0, 0);
                                });
                            }
                            catch (PgException) { }
                            return NativeMethods.OidFunctionCall1Coll(functionOid, 0, 25);
                        });
                        if (session.ExecuteScalar<int>("SELECT sum(value)::integer FROM native_typed_effects") != 2)
                        {
                            throw new System.InvalidOperationException("The enclosing SPI session did not recover.");
                        }
                        return value;
                    });
                    return $"{failure.SqlState}|{failure.Message}|{failure.Detail}|{failure.Hint}|{restored}|{expired}|{nestedEntered}|{recovered}";
                }

                [PgFunction]
                public static bool NativeManagedRecovery()
                {
                    var original = new System.InvalidOperationException("managed recovery");
                    try
                    {
                        PgTransaction.RunInSubtransaction(() =>
                        {
                            Spi.Execute("INSERT INTO native_typed_effects VALUES (5)");
                            throw original;
                        });
                    }
                    catch (System.InvalidOperationException error) when (object.ReferenceEquals(original, error))
                    {
                        PgMemoryContext owner = PgMemoryContext.Current;
                        PgMemoryContext success = PgTransaction.RunInSubtransaction(() => PgMemoryContext.Current);
                        return success.Id != owner.Id && success.IsAlive &&
                            Spi.ExecuteScalar<int>("SELECT sum(value)::integer FROM native_typed_effects") == 2;
                    }
                    return false;
                }

                [PgFunction]
                public static void NativeForbiddenRecovery()
                {
                    _ = PgTransaction.RegisterCallback(PgTransactionEvent.PreCommit, () =>
                    {
                        try
                        {
                            PgTransaction.RunInSubtransaction(() =>
                                Spi.Execute("INSERT INTO native_typed_effects VALUES (6)"));
                        }
                        catch (PgException) { }
                    });
                }
            }
            """, token);
        string[] options = ["-c", "Release", "-r", RuntimeInformation.RuntimeIdentifier,
            "-p:AnkusPostgresMajor=" + MajorText(), "-p:AnkusPgConfigPath=" + s_installation.PgConfigPath];
        string published = Path.Combine(root, "published");
        (await RunDotnetAsync(["publish", consumerProject, .. options, "-o", published], token))
            .EnsureSuccess("dotnet", ["publish"]);

        string providerAssembly = Assert.ContainsSingle(Directory.GetFiles(Path.Combine(provider, "bin"), "Ankus.Postgres.*.dll", SearchOption.AllDirectories));
        string consumerAssembly = Assert.ContainsSingle(Directory.GetFiles(Path.Combine(consumer, "bin"), "Ankus.Postgres.*.dll", SearchOption.AllDirectories));
        Assert.AreEqual(Path.GetFileName(providerAssembly), Path.GetFileName(consumerAssembly));
        byte[] expected = SHA256.HashData(await File.ReadAllBytesAsync(providerAssembly, token));
        Assert.AreSequenceEqual(expected, SHA256.HashData(await File.ReadAllBytesAsync(consumerAssembly, token)));
        Assert.IsEmpty(Directory.GetFiles(published, "Ankus.Postgres.*.dll"));
        DateTime written = File.GetLastWriteTimeUtc(consumerAssembly);
        ProcessResult repeated = await RunDotnetAsync(["build", consumerProject, .. options, "--no-restore"], token);
        repeated.EnsureSuccess("dotnet", ["build"]);
        Assert.Contains("Native binding sources: reused after native verification.", repeated.StandardOutput);
        Assert.AreEqual(written, File.GetLastWriteTimeUtc(consumerAssembly));

        string driver = Path.Combine(root, "driver");
        Directory.CreateDirectory(driver);
        string driverProject = Path.Combine(driver, "BindingDriver.csproj");
        new XDocument(new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
            new XElement("PropertyGroup", new XElement("TargetFramework", "net10.0"),
                new XElement("OutputType", "Exe"), new XElement("TreatWarningsAsErrors", "true")),
            new XElement("ItemGroup", new XElement("ProjectReference", new XAttribute("Include", consumerProject)))))
            .Save(driverProject);
        await File.WriteAllTextAsync(Path.Combine(driver, "Program.cs"), """
            Ankus.Postgres.RangeTblRef value = BindingProvider.Create(41);
            System.Console.WriteLine($"{value.rtindex}|{BindingConsumer.NativeBindingRoundTrip(value.rtindex)}");
            """, token);
        ProcessResult managed = await RunDotnetAsync(["run", "--project", driverProject, .. options], token);
        managed.EnsureSuccess("dotnet", ["run"]);
        Assert.EndsWith("41|42", managed.StandardOutput.Trim());
        await File.WriteAllTextAsync(Path.Combine(driver, "Program.cs"), """
            Ankus.Postgres.RangeTblRef value = BindingProvider.Create(51);
            System.Console.WriteLine($"{value.rtindex}|{BindingConsumer.NativeBindingRoundTrip(value.rtindex)}");
            """, token);
        (await RunDotnetAsync(["build", driverProject, .. options, "--no-restore", "-p:BuildProjectReferences=false"], token))
            .EnsureSuccess("dotnet", ["build"]);
        string driverAssembly = Path.Combine(driver, "bin", "Release", "net10.0", RuntimeInformation.RuntimeIdentifier, "BindingDriver.dll");
        ProcessResult existing = await RunDotnetAsync([driverAssembly], token);
        existing.EnsureSuccess("dotnet", [driverAssembly]);
        Assert.AreEqual("51|52", existing.StandardOutput.Trim());

        await using (PostgresTestCluster cluster = await StartPublishedClusterAsync(published, token))
        {
            await using NpgsqlConnection connection = await cluster.OpenConnectionAsync(token);
            await using var command = new NpgsqlCommand("CREATE EXTENSION ankus_tool_probe; SELECT native_binding_round_trip(9)", connection);
            Assert.AreEqual(10, await command.ExecuteScalarAsync(token));
            command.CommandText = "SELECT native_binding_tag()";
            Assert.IsTrue(Assert.IsInstanceOfType<bool>(await command.ExecuteScalarAsync(token)));
            command.CommandText = "SELECT native_binding_size()";
            Assert.AreEqual(8, await command.ExecuteScalarAsync(token));
            command.CommandText = "SELECT native_binding_rid()";
            Assert.AreEqual(RuntimeInformation.RuntimeIdentifier, await command.ExecuteScalarAsync(token));
            command.CommandText = "SELECT native_dependency_round_trip(37)";
            Assert.AreEqual(37, await command.ExecuteScalarAsync(token));
            command.CommandText = "SELECT native_record_result()";
            Assert.AreEqual("FEDCBA9876543210", await command.ExecuteScalarAsync(token));
            command.CommandText = "SELECT native_linked_provider()";
            Assert.IsTrue(Assert.IsInstanceOfType<bool>(await command.ExecuteScalarAsync(token)));
            command.CommandText = "SELECT native_node_call(-2147483648)";
            Assert.AreEqual(int.MinValue, await command.ExecuteScalarAsync(token));
            command.CommandText = "SELECT native_node_call(2147483647)";
            Assert.AreEqual(int.MaxValue, await command.ExecuteScalarAsync(token));
            command.CommandText = "SELECT set_config('dynamic_library_path', current_setting('dynamic_library_path') || $1, false)";
            command.Parameters.AddWithValue(Path.PathSeparator + "$libdir");
            await command.ExecuteNonQueryAsync(token);
            command.Parameters.Clear();
            var notices = new List<PostgresNotice>();
            connection.Notice += (_, arguments) => notices.Add(arguments.Notice);
            command.CommandText = """
                CREATE TEMP TABLE native_typed_effects(value integer);
                CREATE FUNCTION native_typed_target(value integer) RETURNS integer LANGUAGE plpgsql AS $$
                BEGIN
                    IF value = 0 THEN
                        INSERT INTO native_typed_effects VALUES (4);
                        RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'typed native failure',
                            DETAIL = 'owned typed detail', HINT = 'retry typed call';
                    END IF;
                    RETURN value + 17;
                END;
                $$;
                SELECT native_typed_error('native_typed_target(integer)'::regprocedure::oid, false)
                """;
            Assert.AreEqual("22023|typed native failure|owned typed detail|retry typed call|True|True|True|42", await command.ExecuteScalarAsync(token));
            command.CommandText = "SELECT array_agg(value) FROM native_typed_effects";
            Assert.AreSequenceEqual([2], Assert.IsInstanceOfType<int[]>(await command.ExecuteScalarAsync(token)));
            command.CommandText = "TRUNCATE native_typed_effects; SELECT native_typed_error('native_typed_target(integer)'::regprocedure::oid, true)";
            Assert.AreEqual("22023|typed native failure|owned typed detail|retry typed call|True|True|True|42", await command.ExecuteScalarAsync(token));
            command.CommandText = "SELECT array_agg(value) FROM native_typed_effects";
            Assert.AreSequenceEqual([2], Assert.IsInstanceOfType<int[]>(await command.ExecuteScalarAsync(token)));
            command.CommandText = "SELECT native_managed_recovery()";
            Assert.IsTrue(Assert.IsInstanceOfType<bool>(await command.ExecuteScalarAsync(token)));
            command.CommandText = "SELECT native_forbidden_recovery()";
            PostgresException forbidden = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteNonQueryAsync(token));
            Assert.AreEqual("55000", forbidden.SqlState);
            Assert.AreEqual("Explicit recovery scopes are unavailable during transaction callbacks", forbidden.MessageText);
            command.CommandText = "SELECT array_agg(value) FROM native_typed_effects";
            Assert.AreSequenceEqual([2], Assert.IsInstanceOfType<int[]>(await command.ExecuteScalarAsync(token)));
            Assert.IsEmpty(notices, "Native error recovery must not leave PostgreSQL's SPI stack unbalanced.");
            command.CommandText = "SELECT native_node_call(42)";
            Assert.AreEqual(42, await command.ExecuteScalarAsync(token));
        }

        (await RunDotnetAsync(["clean", consumerProject, .. options], token)).EnsureSuccess("dotnet", ["clean"]);
        Assert.IsFalse(File.Exists(providerAssembly));
        Assert.IsFalse(File.Exists(consumerAssembly));
        Assert.IsEmpty(Directory.GetFiles(root, "native-binding.g.cs", SearchOption.AllDirectories));
        Assert.IsEmpty(Directory.GetFiles(root, "Ankus.NativeBindings.csproj", SearchOption.AllDirectories));
        (await RunDotnetAsync(["build", consumerProject, .. options], token)).EnsureSuccess("dotnet", ["build"]);
        Assert.AreSequenceEqual(expected, SHA256.HashData(await File.ReadAllBytesAsync(consumerAssembly, token)));

        string artifacts = Path.Combine(root, "isolated artifacts");
        (await RunDotnetAsync(["build", consumerProject, .. options, "--artifacts-path", artifacts], token))
            .EnsureSuccess("dotnet", ["build"]);
        string[] references = Directory.GetFiles(artifacts, "native-binding.assembly-path", SearchOption.AllDirectories);
        Assert.HasCount(2, references);
        foreach (string reference in references)
        {
            string assembly = (await File.ReadAllTextAsync(reference, token)).Trim();
            Assert.StartsWith(Path.GetDirectoryName(reference)! + Path.DirectorySeparatorChar, Path.GetFullPath(assembly));
            Assert.AreSequenceEqual(expected, SHA256.HashData(await File.ReadAllBytesAsync(assembly, token)));
        }
    }

    private static async Task<string> CompileBindingProviderAsync(string root, CancellationToken token)
    {
        string directory = Directory.CreateDirectory(Path.Combine(root, "native provider")).FullName;
        string file = Path.Combine(directory, "provider.c");
        string artifact = Path.Combine(directory, OperatingSystem.IsWindows() ? "provider.obj" : "provider.o");
        string library = Path.Combine(directory, OperatingSystem.IsWindows() ? "provider.lib" : "libprovider.a");
        await File.WriteAllTextAsync(file, """
            #include "postgres.h"
            #include "replication/output_plugin.h"
            PGDLLEXPORT void _PG_output_plugin_init(OutputPluginCallbacks *callbacks)
            {
                callbacks->startup_cb = NULL;
            }
            """, token);
        string compiler = OperatingSystem.IsWindows() ? "cl.exe" : "clang";
        string[] options = OperatingSystem.IsWindows()
            ? ["/nologo", "/std:c11", "/WX", "/O2", "/c", "/I" + s_installation.ServerIncludeDirectory, "/I" + s_installation.IncludeDirectory,
                "/I" + Path.Combine(s_installation.ServerIncludeDirectory, "port", "win32"),
                "/I" + Path.Combine(s_installation.ServerIncludeDirectory, "port", "win32_msvc"), "/Fo" + artifact, file]
            : [.. s_installation.PreprocessorArguments, "-std=c11", "-Wall", "-Wextra", "-Werror", "-O2", "-fPIC", "-c",
                "-isystem", s_installation.ServerIncludeDirectory, "-isystem", s_installation.IncludeDirectory, file, "-o", artifact];
        await ProcessRunner.RunCheckedAsync(compiler, options, s_environment, token, workingDirectory: directory);
        string archiver = OperatingSystem.IsWindows() ? "lib.exe" : "ar";
        string[] archive = OperatingSystem.IsWindows() ? ["/nologo", "/OUT:" + library, artifact] : ["rcs", library, artifact];
        await ProcessRunner.RunCheckedAsync(archiver, archive, s_environment, token, workingDirectory: directory);
        return library;
    }

    /// <summary>
    /// A compiler targeting a different runtime cannot emit a managed binding contract for the requested runtime.
    /// </summary>
    [TestMethod]
    public async Task PackagedBuildToolRejectsMismatchedBindingRuntime()
    {
        string helper = await ReadPackagedBuildToolAsync();
        string output = Path.Combine(s_root, "mismatched binding runtime");
        string otherRuntime = RuntimeInformation.RuntimeIdentifier == "linux-x64" ? "linux-arm64" : "linux-x64";
        ProcessResult result = await RunDotnetAsync(
            [helper, "binding-sources", MajorText(), s_installation.PgConfigPath, output, "", "", otherRuntime],
            context.CancellationToken);
        Assert.AreEqual(1, result.ExitCode);
        Assert.Contains($"Native bindings target {RuntimeInformation.RuntimeIdentifier}", result.StandardError);
        Assert.Contains($"requested runtime is {otherRuntime}", result.StandardError);
        Assert.IsFalse(File.Exists(Path.Combine(output, "native-binding.g.cs")));
        Assert.IsFalse(File.Exists(Path.Combine(output, "Ankus.NativeBindings.csproj")));
    }

    /// <summary>
    /// Long output paths retain a complete companion, clean temporary ASTs after success and failure, and permit deterministic recovery.
    /// </summary>
    [TestMethod]
    public async Task PackagedNodeBindingFailurePreservesCompanionAndRecovers()
    {
        CancellationToken token = context.CancellationToken;
        string helper = await ReadPackagedBuildToolAsync();
        string root = CreateBindingDirectory();
        // Reproduce a valid output path whose former nested compiler working directory exceeded Windows MAX_PATH.
        string output = Path.Combine(root, new string('p', Math.Max(1, 220 - root.Length - 1)));
        string temporary = Directory.CreateTempSubdirectory("ankus-node-test-").FullName;
        try
        {
            await VerifyNodeBindingRecoveryAsync(helper, output, temporary, token);
        }
        finally
        {
            Directory.Delete(temporary, recursive: true);
        }
    }

    private static async Task VerifyNodeBindingRecoveryAsync(string helper, string output, string temporary, CancellationToken token)
    {
        var environment = new Dictionary<string, string?>(s_environment)
        {
            ["TMPDIR"] = temporary,
            ["TMP"] = temporary,
            ["TEMP"] = temporary,
        };
        string sentinel = Path.Combine(temporary, "unrelated.txt");
        await File.WriteAllTextAsync(sentinel, "preserve unrelated temporary files", token);
        string cache = Path.Combine(temporary, "binding-cache");
        string[] command = [helper, "binding-sources", MajorText(), s_installation.PgConfigPath, output, "", "", "", "", "", "", cache];
        (await ProcessRunner.RunAsync("dotnet", command, environment, token, workingDirectory: s_root)).EnsureSuccess("dotnet", command);
        Assert.IsEmpty(Directory.GetDirectories(temporary, "ankus-node-*"));
        Assert.IsEmpty(Directory.GetDirectories(temporary, "ankus-source-*"));
        string[] names = ["native-binding.g.cs", "native-binding.assembly-name", "native-binding.identity", "Ankus.NativeBindings.csproj",
            "native-records.json", "native-availability.json"];
        var expected = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (string name in names)
        {
            expected.Add(name, await File.ReadAllBytesAsync(Path.Combine(output, name), token));
        }

        ProcessResult rejected = await ProcessRunner.RunAsync("dotnet",
            [.. command[..^2], Path.Combine(output, "missing-libclang"), cache], environment, token, workingDirectory: s_root);
        Assert.AreEqual(1, rejected.ExitCode);
        Assert.Contains("Cannot locate the selected libclang library", rejected.StandardError);
        foreach (string name in names)
        {
            Assert.AreSequenceEqual(expected[name], await File.ReadAllBytesAsync(Path.Combine(output, name), token), name);
        }

        Assert.IsEmpty(Directory.GetDirectories(temporary, "ankus-node-*"));
        Assert.IsEmpty(Directory.GetDirectories(temporary, "ankus-source-*"));
        JsonNode records = JsonNode.Parse(expected["native-records.json"], documentOptions: new()
        {
            MaxDepth = 512
        })!;
        JsonNode range = records["Graph"]!["Declarations"]!.AsArray().Single(static declaration => declaration!["Name"]!.GetValue<string>() == "RangeTblRef")!;
        JsonNode field = range["Fields"]!.AsArray().Single(static member => member!["Name"]!.GetValue<string>() == "rtindex")!;
        Assert.AreEqual(32, field["OffsetBits"]!.GetValue<int>());
        field["OffsetBits"] = 0;
        // Keep the cache manifest internally consistent so only native verification can reject this false layout.
        await ReplaceCachedRecordsAsync(cache, JsonSerializer.SerializeToUtf8Bytes(records, s_bindingJsonOptions), token);
        ProcessResult invalidLayout = await ProcessRunner.RunAsync("dotnet", command, environment, token, workingDirectory: s_root);
        Assert.AreEqual(1, invalidLayout.ExitCode);
        Assert.Contains("offset RangeTblRef.rtindex", invalidLayout.StandardError);
        foreach (string name in names)
        {
            Assert.AreSequenceEqual(expected[name], await File.ReadAllBytesAsync(Path.Combine(output, name), token), name);
        }

        Assert.IsEmpty(Directory.GetDirectories(temporary, "ankus-node-*"));
        Assert.IsEmpty(Directory.GetDirectories(temporary, "ankus-source-*"));
        await ReplaceCachedRecordsAsync(cache, expected["native-records.json"], token);
        ProcessResult recovered = await ProcessRunner.RunAsync("dotnet", command, environment, token, workingDirectory: s_root);
        recovered.EnsureSuccess("dotnet", command);
        Assert.Contains("Native binding sources: reused after native verification.", recovered.StandardOutput);
        foreach (string name in names)
        {
            Assert.AreSequenceEqual(expected[name], await File.ReadAllBytesAsync(Path.Combine(output, name), token), name);
        }

        Assert.IsEmpty(Directory.GetDirectories(temporary, "ankus-node-*"));
        Assert.IsEmpty(Directory.GetDirectories(temporary, "ankus-source-*"));
        Assert.AreEqual("preserve unrelated temporary files", await File.ReadAllTextAsync(sentinel, token));
    }

    /// <summary>
    /// Replaces only this test's cached record observation and its content hash to exercise independent native verification.
    /// </summary>
    /// <param name="cache">The isolated cache owned by this test.</param>
    /// <param name="content">The complete replacement observation.</param>
    /// <param name="token">Cancels fixture file access.</param>
    private static async Task ReplaceCachedRecordsAsync(string cache, byte[] content, CancellationToken token)
    {
        string file = Assert.ContainsSingle(Directory.GetFiles(cache, "native-records.json", SearchOption.AllDirectories));
        await File.WriteAllBytesAsync(file, content, token);
        string manifestPath = Path.Combine(Path.GetDirectoryName(file)!, "manifest.json");
        JsonNode manifest = JsonNode.Parse(await File.ReadAllTextAsync(manifestPath, token))!;
        JsonNode artifact = manifest["Artifacts"]!.AsArray().Single(static entry => entry!["Path"]!.GetValue<string>() == "native-records.json")!;
        artifact["Hash"] = Convert.ToHexString(SHA256.HashData(content));
        await File.WriteAllTextAsync(manifestPath, manifest.ToJsonString(), token);
    }

    private static string CreateBindingDirectory()
        => PhysicalBindingDirectory(new DirectoryInfo(CreateDirectory()));

    private static string PhysicalBindingDirectory(DirectoryInfo directory)
    {
        if (directory.Parent is null)
        {
            return directory.FullName;
        }

        DirectoryInfo resolved = (DirectoryInfo?)directory.ResolveLinkTarget(returnFinalTarget: true) ?? directory;
        return resolved.Parent is DirectoryInfo parent
            ? Path.Combine(PhysicalBindingDirectory(parent), resolved.Name)
            : resolved.FullName;
    }

    private static void LinkBindingIntermediates(string project)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string intermediate = Path.Combine(project, "obj", "Release", "net10.0", RuntimeInformation.RuntimeIdentifier);
        string physical = Path.Combine(intermediate, "physical bindings");
        Directory.CreateDirectory(physical);
        string linked = Path.Combine(intermediate, "ankus-bindings");
        Directory.CreateSymbolicLink(linked, physical);
    }
}
