using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace Ankus.Build;

/// <summary>
/// Compiles verified companions once per content contract and copies leased artifacts to each consumer.
/// </summary>
internal static class NativeBindingCompilationCommand
{
    private static readonly string[] s_artifactExtensions = [".dll", ".xml", ".pdb"];

    private const string CompilerInputs = """
        <Target Name="_RecordAnkusCompilerInputs" BeforeTargets="CoreCompile">
          <Error Condition="'$(NETCoreSdkVersion)' != '$(AnkusSelectedSdkVersion)'" Text="The binding compiler resolved a different .NET SDK." />
          <ItemGroup>
            <_AnkusAnalyzerDirectory Include="@(Analyzer->'%(RootDir)%(Directory)')" />
            <_AnkusCompilerInput Include="@(ReferencePath);@(Analyzer);@(Compile);@(EditorConfigFiles)" />
            <_AnkusCompilerInput Include="%(_AnkusAnalyzerDirectory.Identity)**/*" />
          </ItemGroup>
          <GetFileHash Files="@(_AnkusCompilerInput)" Algorithm="SHA256">
            <Output TaskParameter="Items" ItemName="_AnkusHashedCompilerInput" />
          </GetFileHash>
          <WriteLinesToFile File="compiler-inputs.txt" Lines="@(_AnkusHashedCompilerInput->'%(FileHash) %(FullPath)')" Overwrite="true" />
        </Target>
        """;

    /// <summary>
    /// Resolves the consumer's exact SDK and runtime reference, then reuses or builds content-verified artifacts.
    /// </summary>
    internal static async Task RunAsync(string[] arguments, CancellationToken cancellationToken = default)
    {
        if (arguments.Length != 8)
        {
            throw new ArgumentException("Expected binding-compile <source-directory> <runtime-assembly> <target-framework> <configuration> <sdk-version> <sdk-directory> <consumer-assets> <cache-directory>.", nameof(arguments));
        }

        string source = Path.GetFullPath(arguments[0]);
        string runtime = Path.GetFullPath(arguments[1]);
        string sdk = Path.GetFullPath(arguments[5]);
        NativeBindingRestoreSettings restore = await NativeBindingRestoreSettings.ReadAsync(arguments[6], cancellationToken);
        string packages = restore.Packages;
        string cache = string.IsNullOrEmpty(arguments[7])
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ankus", "bindings")
            : Path.GetFullPath(arguments[7]);
        string host = Path.GetFullPath(Path.Combine(sdk, "..", "..", OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"));
        string assembly = (await File.ReadAllTextAsync(Path.Combine(source, "native-binding.assembly-name"), cancellationToken)).Trim();
        if (assembly.Length == 0 || assembly.Any(static value => !char.IsAsciiLetterOrDigit(value) && value != '.'))
        {
            throw new FormatException("The binding assembly name is invalid.");
        }

        // Inventory every SDK/runtime file as well as hashing its existing content. New imports
        // or analyzer dependencies cannot hide behind a manifest containing only older filenames.
        string[] installation = [.. Directory.GetFiles(sdk, "*", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*", SearchOption.AllDirectories))
            .Append(host).Append(typeof(NativeBindingCompilationCommand).Assembly.Location).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        string[] explicitFiles = [Path.Combine(source, "native-binding.g.cs"), runtime];
        var start = new ProcessStartInfo(host) { UseShellExecute = false };
        // Generated companions have their own build contract. Do not inherit arbitrary
        // environment properties that can redirect SDK imports or compiler tasks.
        string[] environmentNames = ["PATH", "HOME", "USERPROFILE", "APPDATA", "LOCALAPPDATA", "TEMP", "TMP", "TMPDIR",
            "SystemRoot", "SystemDrive", "COMSPEC", "ProgramFiles", "ProgramFiles(x86)", "ProgramW6432",
            "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "NO_PROXY", "http_proxy", "https_proxy", "all_proxy", "no_proxy",
            "SSL_CERT_FILE", "SSL_CERT_DIR", "LANG", "LC_ALL", "DOTNET_ROOT", "DOTNET_ROOT_X64", "DOTNET_ROOT_ARM64"];
        KeyValuePair<string, string?>[] environment = [.. start.Environment.Where(pair => environmentNames.Contains(pair.Key, StringComparer.OrdinalIgnoreCase))];
        start.Environment.Clear();
        foreach (KeyValuePair<string, string?> pair in environment)
        {
            start.Environment.Add(pair);
        }

        start.Environment["MSBuildSDKsPath"] = Path.Combine(sdk, "Sdks");
        start.Environment["DOTNET_MSBUILD_SDK_RESOLVER_CLI_DIR"] = Path.GetDirectoryName(host);
        // Only the hash of inherited settings participates in identity; values are not persisted.
        string settings = JsonSerializer.Serialize(start.Environment.OrderBy(static pair => pair.Key, StringComparer.Ordinal));
        string work = NativeBuildDirectory.PhysicalPath(Directory.CreateTempSubdirectory("ankus-binding-compile-"));
        try
        {
            NativeBindingCacheFile[] toolchain = await NativeBindingCache.SnapshotAsync(installation.Concat(restore.Configurations), cancellationToken);
            var project = XDocument.Parse(NativeBindingSourceCommand.CreateProject(work));
            XElement root = project.Root!;
            root.AddFirst(new XElement("PropertyGroup",
                Property("AnkusBindingTargetFramework", arguments[2]),
                Property("AnkusBindingAssemblyName", assembly),
                Property("AnkusRuntimeAssembly", runtime),
                Property("Configuration", arguments[3]),
                Property("UseSharedCompilation", "false"),
                Property("AnkusSelectedSdkVersion", arguments[4]),
                Property("MSBuildUserExtensionsPath", Path.Combine(work, "user-extensions")),
                Property("RestorePackagesPath", packages)));
            root.Add(XElement.Parse(CompilerInputs));
            root.Add(restore.CreateTarget());
            await File.WriteAllTextAsync(Path.Combine(work, "Ankus.NativeBindings.csproj"), project.ToString(), cancellationToken);
            File.Copy(explicitFiles[0], Path.Combine(work, "native-binding.g.cs"));
            await File.WriteAllTextAsync(Path.Combine(work, "global.json"), JsonSerializer.Serialize(new
            {
                sdk = new
                {
                    version = arguments[4],
                    rollForward = "disable",
                    allowPrerelease = true
                },
            }), cancellationToken);
            start.WorkingDirectory = work;
            foreach (string argument in new[] { "exec", "--fx-version", Environment.Version.ToString(), Path.Combine(sdk, "MSBuild.dll"),
                    "Ankus.NativeBindings.csproj", "-nologo", "-verbosity:minimal", "-nodeReuse:false" })
            {
                start.ArgumentList.Add(argument);
            }

            // NuGet and credential-provider plugins need the consumer's environment, including
            // custom configuration substitutions. Only compilation uses the closed environment.
            var restoreStart = new ProcessStartInfo(host) { UseShellExecute = false, WorkingDirectory = work };
            restoreStart.Environment["MSBuildSDKsPath"] = Path.Combine(sdk, "Sdks");
            restoreStart.Environment["DOTNET_MSBUILD_SDK_RESOLVER_CLI_DIR"] = Path.GetDirectoryName(host);
            foreach (string argument in start.ArgumentList)
            {
                restoreStart.ArgumentList.Add(argument);
            }

            restoreStart.ArgumentList.Add("-target:Restore");
            await CompileAsync(restoreStart, cancellationToken);
            restore.Verify(await NativeBindingRestoreSettings.ReadAsync(Path.Combine(work, "obj", "project.assets.json"), cancellationToken));
            start.ArgumentList.Add("-target:Compile");
            start.ArgumentList.Add("-property:SkipCompilerExecution=true");
            await CompileAsync(start, cancellationToken);
            start.ArgumentList.Remove("-property:SkipCompilerExecution=true");
            start.ArgumentList.Remove("-target:Compile");
            start.ArgumentList.Add("-target:Build");
            NativeBindingCacheFile[] compiler = await ReadInputsAsync(work, cancellationToken);
            NativeBindingCacheFile[] dependencies = [.. toolchain, .. compiler, .. await ReadPackagesAsync(work, cancellationToken)];
            var identity = new List<NativeBindingCacheFile>();
            foreach (NativeBindingCacheFile input in dependencies)
            {
                if (!input.Path.StartsWith(work + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                {
                    identity.Add(input);
                    continue;
                }

                string hash = input.Hash;
                // The SDK records ProjectDir in its generated analyzer configuration. Its
                // logical source location is fixed by PathMap, not the owned temporary name.
                if (Path.GetExtension(input.Path) == ".editorconfig")
                {
                    string configuration = (await File.ReadAllTextAsync(input.Path, cancellationToken)).Replace(work, "/_/Ankus.Postgres", StringComparison.Ordinal);
                    hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(configuration)));
                }

                identity.Add(new("/_/Ankus.Postgres/" + Path.GetRelativePath(work, input.Path).Replace('\\', '/'), hash));
            }

            string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
            {
                Inputs = identity,
                Assembly = assembly,
                Framework = arguments[2],
                Configuration = arguments[3],
                SdkVersion = arguments[4],
                Packages = packages,
                Restore = restore,
                Environment = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(settings))),
            }))));

            bool compiled = false;
            await using NativeBindingCacheLease lease = await NativeBindingCache.GetAsync(cache, key, async (stage, token) =>
            {
                compiled = true;
                start.ArgumentList.Add("-bl:" + Path.Combine(source, "binding-compile-" + Guid.NewGuid().ToString("N") + ".binlog"));
                await CompileAsync(start, token);
                NativeBindingCacheFile[] actual = await ReadInputsAsync(work, token);
                if (!compiler.SequenceEqual(actual))
                {
                    throw new IOException("The binding compiler inputs changed after preparation.");
                }

                string output = Path.Combine(work, "bin", arguments[3], arguments[2]);
                foreach (string extension in s_artifactExtensions)
                {
                    string name = assembly + extension;
                    File.Copy(Path.Combine(output, name), Path.Combine(stage, name));
                }

                return [.. dependencies.Where(input => !input.Path.StartsWith(work + Path.DirectorySeparatorChar, StringComparison.Ordinal))];
            }, cancellationToken);

            string destination = Path.Combine(source, "compiled");
            Directory.CreateDirectory(destination);
            foreach (string extension in s_artifactExtensions)
            {
                string name = assembly + extension;
                string target = Path.Combine(destination, name);
                string artifact = Path.Combine(lease.Directory, name);
                if (!File.Exists(target) || await NativeBindingCache.HashAsync(target, cancellationToken) != await NativeBindingCache.HashAsync(artifact, cancellationToken))
                {
                    File.Copy(artifact, target, overwrite: true);
                }
            }

            string reference = Path.Combine(source, "native-binding.assembly-path");
            string content = Path.Combine(destination, assembly + ".dll") + "\n";
            if (!File.Exists(reference) || await File.ReadAllTextAsync(reference, cancellationToken) != content)
            {
                await File.WriteAllTextAsync(reference, content, cancellationToken);
            }

            Console.WriteLine($"Managed binding compilation: {(compiled ? "built" : "reused")} {assembly}");
        }
        finally
        {
            await NativeBuildDirectory.DeleteAsync(work);
        }
    }

    private static XElement Property(string name, string value) => new(name, NativeBindingSourceCommand.EscapeProperty(value));

    private static async Task<NativeBindingCacheFile[]> ReadInputsAsync(string work, CancellationToken cancellationToken)
    {
        string[] lines = await File.ReadAllLinesAsync(Path.Combine(work, "compiler-inputs.txt"), cancellationToken);
        var compiler = new List<NativeBindingCacheFile>();
        foreach (string line in lines)
        {
            if (line.Length < 66 || line[64] != ' ' || line[..64].Any(static value => !char.IsAsciiHexDigit(value)) || !Path.IsPathFullyQualified(line[65..]))
            {
                throw new FormatException("The binding compiler input snapshot is invalid.");
            }

            compiler.Add(new(line[65..], line[..64]));
        }

        return [.. compiler.Distinct().OrderBy(static input => input.Path, StringComparer.Ordinal)];
    }

    private static async Task<NativeBindingCacheFile[]> ReadPackagesAsync(string work, CancellationToken cancellationToken)
    {
        await using FileStream stream = File.OpenRead(Path.Combine(work, "obj", "project.assets.json"));
        using JsonDocument assets = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        string[] folders = [.. assets.RootElement.GetProperty("packageFolders").EnumerateObject().Select(static folder => folder.Name)];
        var files = new List<string>();
        foreach (JsonProperty library in assets.RootElement.GetProperty("libraries").EnumerateObject())
        {
            if (library.Value.GetProperty("type").GetString() != "package")
            {
                continue;
            }

            string path = library.Value.GetProperty("path").GetString()!;
            string directory = folders.Select(folder => Path.Combine(folder, path)).First(Directory.Exists);
            files.AddRange(Directory.GetFiles(directory, "*", SearchOption.AllDirectories));
        }

        return await NativeBindingCache.SnapshotAsync(files.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal), cancellationToken);
    }

    private static async Task CompileAsync(ProcessStartInfo start, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start the binding compiler.");
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"The binding compiler exited with {process.ExitCode}.");
        }
    }
}
