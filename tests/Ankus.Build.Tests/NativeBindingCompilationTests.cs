using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Xml.Linq;

namespace Ankus.Build.Tests;

/// <summary>
/// Exercises real isolated compiler processes, shared artifacts and consumer recovery.
/// </summary>
/// <param name="context">The test's cooperative cancellation context.</param>
[TestClass]
public sealed class NativeBindingCompilationTests(TestContext context)
{
    private static readonly string[] s_offlinePackages = ["microsoft.net.illink.tasks", "microsoft.netcore.app.ref", "microsoft.aspnetcore.app.ref"];

    private const string AssemblyName = "Ankus.Postgres.CacheProbe";
    private const string Source = """
        /// <summary>
        /// An executable witness for the compiled input bytes.
        /// </summary>
        public static class Probe
        {
            /// <summary>
            /// Returns the selected source value.
            /// </summary>
            public static int Read() => 41;
        }
        """;

    /// <summary>
    /// Concurrent processes compile once, survive the producer's cleanup and preserve unchanged local timestamps.
    /// </summary>
    [TestMethod]
    public async Task CompiledCompanionsShareAcrossProcessesAndConsumerCleanup()
    {
        string root = Directory.CreateTempSubdirectory("ankus-compiled-sharing-").FullName;
        try
        {
            string[] settings = await SettingsAsync(root);
            string first = await SourceAsync(root, "first consumer");
            string second = await SourceAsync(root, "second consumer");
            string[] firstArguments = Arguments(root, first, settings);
            string[] secondArguments = Arguments(root, second, settings);
            string tool = typeof(NativeBindingCompilationCommand).Assembly.Location;
            string program = "dotnet";
            string[] prefix = [];
            if (!OperatingSystem.IsWindows())
            {
                string physical = Directory.CreateDirectory(Path.Combine(root, "temporary")).FullName;
                string alias = Path.Combine(root, "linked temporary");
                Directory.CreateSymbolicLink(alias, physical);
                program = "/usr/bin/env";
                prefix = ["TMPDIR=" + alias, "dotnet"];
            }

            string[] results = await Task.WhenAll(
                NativeBindingLayoutCommand.RunProcessAsync(program, [.. prefix, tool, "binding-compile", .. firstArguments], root, context.CancellationToken),
                NativeBindingLayoutCommand.RunProcessAsync(program, [.. prefix, tool, "binding-compile", .. secondArguments], root, context.CancellationToken));
            Assert.ContainsSingle(results.Where(static result => result.Contains("Managed binding compilation: built ", StringComparison.Ordinal)));
            Assert.ContainsSingle(results.Where(static result => result.Contains("Managed binding compilation: reused ", StringComparison.Ordinal)));
            string artifact = Artifact(second);
            Assert.AreEqual(41, Execute(artifact));
            Assert.AreEqual(await NativeBindingCache.HashAsync(Artifact(first), context.CancellationToken),
                await NativeBindingCache.HashAsync(artifact, context.CancellationToken));
            Directory.Delete(first, recursive: true);
            DateTime timestamp = DateTime.UtcNow.AddDays(-1);
            File.SetLastWriteTimeUtc(artifact, timestamp);
            timestamp = File.GetLastWriteTimeUtc(artifact);
            string reused = await NativeBindingLayoutCommand.RunProcessAsync(program, [.. prefix, tool, "binding-compile", .. secondArguments], root, context.CancellationToken);
            Assert.Contains("Managed binding compilation: reused ", reused);
            Assert.AreEqual(timestamp, File.GetLastWriteTimeUtc(artifact));
            Assert.AreEqual(41, Execute(artifact));
            Assert.HasCount(1, Directory.GetDirectories(Path.Combine(root, "cache")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Changed source and runtime bytes rebuild; compilation failure preserves the consumer's last usable result.
    /// </summary>
    [TestMethod]
    public async Task CompiledCompanionsInvalidateContentAndRecoverFromCompilerFailure()
    {
        string root = Directory.CreateTempSubdirectory("ankus-compiled-recovery-").FullName;
        try
        {
            string[] settings = await SettingsAsync(root);
            string source = await SourceAsync(root, "consumer");
            string[] arguments = Arguments(root, source, settings);
            await NativeBindingCompilationCommand.RunAsync(arguments, context.CancellationToken);
            string artifact = Artifact(source);
            Assert.AreEqual(41, Execute(artifact));
            string original = await NativeBindingCache.HashAsync(artifact, context.CancellationToken);
            string input = Path.Combine(source, "native-binding.g.cs");
            await File.WriteAllTextAsync(input, "This source does not compile.", context.CancellationToken);
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => NativeBindingCompilationCommand.RunAsync(arguments, context.CancellationToken));
            Assert.AreEqual(original, await NativeBindingCache.HashAsync(artifact, context.CancellationToken));
            Assert.AreEqual(41, Execute(artifact));
            Assert.DoesNotContain(static path => Path.GetFileName(path).Contains(".stage-", StringComparison.Ordinal), Directory.GetDirectories(Path.Combine(root, "cache")));
            await File.WriteAllTextAsync(input, Source.Replace("41", "43", StringComparison.Ordinal), context.CancellationToken);
            await NativeBindingCompilationCommand.RunAsync(arguments, context.CancellationToken);
            Assert.AreEqual(43, Execute(artifact));
            Assert.AreNotEqual(original, await NativeBindingCache.HashAsync(artifact, context.CancellationToken));
            int builds = Directory.GetFiles(source, "binding-compile-*.binlog").Length;
            DateTime timestamp = File.GetLastWriteTimeUtc(arguments[1]);
            await using (FileStream runtime = new(arguments[1], FileMode.Append, FileAccess.Write))
            {
                await runtime.WriteAsync(new byte[] { 0 }, context.CancellationToken);
            }

            File.SetLastWriteTimeUtc(arguments[1], timestamp);
            await NativeBindingCompilationCommand.RunAsync(arguments, context.CancellationToken);
            Assert.HasCount(builds + 1, Directory.GetFiles(source, "binding-compile-*.binlog"));
            Assert.AreEqual(43, Execute(artifact));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// A fresh package cache uses the consumer's explicit offline configuration and enforces its source mapping.
    /// </summary>
    /// <param name="useSourceOverride">Whether the consumer limits the configured sources through an MSBuild override.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CompiledCompanionsHonorOfflineFeedConfigurationAndSourceMapping(bool useSourceOverride)
    {
        string root = Directory.CreateTempSubdirectory("ankus-compiled-offline-").FullName;
        try
        {
            string[] settings = await SettingsAsync(root);
            NativeBindingRestoreSettings initial = await NativeBindingRestoreSettings.ReadAsync(settings[2], context.CancellationToken);
            string configurationDirectory = Directory.CreateDirectory(Path.Combine(root, "feed settings")).FullName;
            string feed = Directory.CreateDirectory(Path.Combine(configurationDirectory, "feed")).FullName;
            string[] packages = [.. s_offlinePackages
                .Select(package => Path.Combine(initial.Packages, package)).Where(Directory.Exists)
                .SelectMany(static directory => Directory.GetFiles(directory, "*.nupkg", SearchOption.AllDirectories))];
            Assert.IsNotEmpty(packages);
            foreach (string package in packages)
            {
                File.Copy(package, Path.Combine(feed, Path.GetFileName(package)));
            }

            var configuration = XDocument.Parse("""
                <configuration>
                  <packageSources><clear /><add key="offline" value="feed" /></packageSources>
                  <packageSourceMapping>
                    <clear />
                    <packageSource key="offline">
                      <package pattern="Microsoft.*" />
                      <package pattern="Microsoft.NETCore.App.Ref" />
                      <package pattern="Microsoft.AspNetCore.App.Ref" />
                    </packageSource>
                  </packageSourceMapping>
                  <config><add key="globalPackagesFolder" value="packages" /></config>
                </configuration>
                """);
            string configurationPath = Path.Combine(configurationDirectory, "explicit.config");
            string[] restoreArguments = ["-property:RestoreConfigFile=" + configurationPath];
            if (useSourceOverride)
            {
                configuration.Root!.Element("packageSources")!.Add(new XElement("add", new XAttribute("key", "unavailable"),
                    new XAttribute("value", "missing-feed")));
                restoreArguments = [.. restoreArguments, "-property:RestoreSources=" + feed];
            }

            await File.WriteAllTextAsync(configurationPath, configuration.ToString(), context.CancellationToken);
            settings = await SettingsAsync(root, restoreArguments);
            NativeBindingRestoreSettings resolved = await NativeBindingRestoreSettings.ReadAsync(settings[2], context.CancellationToken);
            Assert.Contains(feed, resolved.Sources);
            Assert.DoesNotContain(static source => !Path.IsPathFullyQualified(source), resolved.Sources);
            Assert.DoesNotContain(Path.Combine(configurationDirectory, "missing-feed"), resolved.Sources);
            Assert.AreSequenceEqual([configurationPath], resolved.Configurations);
            string source = await SourceAsync(root, "consumer");
            string[] arguments = Arguments(root, source, settings);
            await NativeBindingCompilationCommand.RunAsync(arguments, context.CancellationToken);
            Assert.AreEqual(41, Execute(Artifact(source)));
            Assert.IsNotEmpty(Directory.GetFiles(resolved.Packages, "*.nupkg", SearchOption.AllDirectories));

            // A warm compiled artifact must not bypass a changed restore policy with a new package cache.
            configuration.Root!.Element("packageSourceMapping")!.Element("packageSource")!.Element("package")!.SetAttributeValue("pattern", "Unrelated.*");
            configuration.Root.Element("config")!.Element("add")!.SetAttributeValue("value", "denied-packages");
            await File.WriteAllTextAsync(configurationPath, configuration.ToString(), context.CancellationToken);
            settings = await SettingsAsync(root, restoreArguments);
            string tool = typeof(NativeBindingCompilationCommand).Assembly.Location;
            InvalidOperationException rejected = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                NativeBindingLayoutCommand.RunProcessAsync("dotnet", [tool, "binding-compile", .. Arguments(root, source, settings)], root, context.CancellationToken));
            Assert.Contains("NU1100", rejected.Message);
            Assert.Contains("PackageSourceMapping", rejected.Message);
            Assert.AreEqual(41, Execute(Artifact(source)));

            configuration.Root.Element("packageSourceMapping")!.Element("packageSource")!.Element("package")!.SetAttributeValue("pattern", "Microsoft.*");
            await File.WriteAllTextAsync(configurationPath, configuration.ToString(), context.CancellationToken);
            await NativeBindingCompilationCommand.RunAsync(Arguments(root, source, settings), context.CancellationToken);
            Assert.AreEqual(41, Execute(Artifact(source)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private async Task<string[]> SettingsAsync(string root, string[]? restoreArguments = null)
    {
        await TestDotnetSdk.ConfigureAsync(root, context.CancellationToken);
        string project = Path.Combine(root, "Settings.csproj");
        await File.WriteAllTextAsync(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>", context.CancellationToken);
        string properties = await NativeBindingLayoutCommand.RunProcessAsync("dotnet", ["msbuild", project, "-nologo",
            "-restore", "-target:ResolveReferences", "-verbosity:quiet", "-getProperty:NETCoreSdkVersion,MSBuildToolsPath,ProjectAssetsFile", .. restoreArguments ?? []], root, context.CancellationToken);
        using JsonDocument document = JsonDocument.Parse(properties);
        JsonElement values = document.RootElement.GetProperty("Properties");
        Assert.AreEqual(TestDotnetSdk.Version, values.GetProperty("NETCoreSdkVersion").GetString());
        File.Copy(typeof(PgDatum).Assembly.Location, Path.Combine(root, "Ankus.Runtime.dll"), overwrite: true);
        return [values.GetProperty("NETCoreSdkVersion").GetString()!, values.GetProperty("MSBuildToolsPath").GetString()!, values.GetProperty("ProjectAssetsFile").GetString()!];
    }

    private async Task<string> SourceAsync(string root, string name)
    {
        string directory = Path.Combine(root, name);
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "native-binding.assembly-name"), AssemblyName, context.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(directory, "native-binding.g.cs"), Source, context.CancellationToken);
        return directory;
    }

    private static string[] Arguments(string root, string source, string[] settings)
        => [source, Path.Combine(root, "Ankus.Runtime.dll"), "net10.0", "Release", .. settings, Path.Combine(root, "cache")];

    private static string Artifact(string source) => Path.Combine(source, "compiled", AssemblyName + ".dll");

    private static int Execute(string artifact)
    {
        var loader = new AssemblyLoadContext("binding-compile-test", isCollectible: true);
        try
        {
            using FileStream stream = File.OpenRead(artifact);
            Assembly assembly = loader.LoadFromStream(stream);
            return (int)assembly.GetType("Probe", throwOnError: true)!.GetMethod("Read")!.Invoke(null, null)!;
        }
        finally
        {
            loader.Unload();
        }
    }
}
