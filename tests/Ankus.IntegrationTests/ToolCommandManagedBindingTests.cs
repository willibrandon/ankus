using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using Ankus.Testing;
using Npgsql;

namespace Ankus.IntegrationTests;

public sealed partial class ToolCommandTests
{
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
        project.Save(consumerProject);
        await File.WriteAllTextAsync(Path.Combine(provider, "BindingProvider.cs"), """
            using Ankus.Postgres;
            public static class BindingProvider
            {
                public static RangeTblRef Create(int value) => new() { type = NodeTag.T_RangeTblRef, rtindex = value };
                public static void Increment(ref RangeTblRef value) => value.rtindex++;
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
        (await RunDotnetAsync(["build", consumerProject, .. options, "--no-restore"], token))
            .EnsureSuccess("dotnet", ["build"]);
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

    private static string CreateBindingDirectory()
        => PhysicalBindingDirectory(new DirectoryInfo(CreateDirectory()));

    private static string PhysicalBindingDirectory(DirectoryInfo directory)
    {
        if (directory.Parent is null) { return directory.FullName; }

        DirectoryInfo resolved = (DirectoryInfo?)directory.ResolveLinkTarget(returnFinalTarget: true) ?? directory;
        return resolved.Parent is DirectoryInfo parent
            ? Path.Combine(PhysicalBindingDirectory(parent), resolved.Name)
            : resolved.FullName;
    }

    private static void LinkBindingIntermediates(string project)
    {
        if (OperatingSystem.IsWindows()) { return; }

        string intermediate = Path.Combine(project, "obj", "Release", "net10.0", RuntimeInformation.RuntimeIdentifier);
        string physical = Path.Combine(intermediate, "physical bindings");
        Directory.CreateDirectory(physical);
        string linked = Path.Combine(intermediate, "ankus-bindings");
        Directory.CreateSymbolicLink(linked, physical);
    }
}
