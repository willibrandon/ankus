#:property TargetFramework=net10.0
#:property PackAsTool=false
#:property PublishAot=false

using System.Diagnostics;
using System.Text.RegularExpressions;

const string RuntimeRepository = "willibrandon/runtime";
const string RuntimeBase = "v10.0.11";
const string RuntimeCommit = "232bc9d1f06c300554a0646fa742d60cca49bcdd";
const string RuntimeVersion = "10.0.11-ankus.1";
const string RuntimeCompilerVersion = "10.0.11";

string repositoryRoot = FindRepositoryRoot();
Directory.SetCurrentDirectory(repositoryRoot);

try
{
    if (args.Length == 0)
    {
        throw new InvalidOperationException("Specify an automation command.");
    }

    switch (args[0])
    {
        case "metadata":
            ValidateRuntimeIdentity(repositoryRoot);
            WriteRuntimeOutputs();
            break;

        case "release-metadata":
            ValidateRuntimeIdentity(repositoryRoot);
            WriteOutput("version", GetReleaseVersion());
            WriteRuntimeOutputs();
            break;

        case "quality":
            ValidateRuntimeIdentity(repositoryRoot);
            Run(GetDotNetHost(), ["restore", "Ankus.slnx", "-m:1", "-p:PublishAot=false"]);
            Run(GetDotNetHost(), ["build", "Ankus.slnx", "--configuration", "Release", "--no-incremental", "--no-restore", "-m:1", "-p:PublishAot=false"]);
            Run(GetDotNetHost(), ["run", "--project", "src/Ankus.DocGenerator", "--configuration", "Release", "--no-build", "--", "--check"]);
            Run("corepack", ["enable"]);
            Dictionary<string, string?> continuousIntegration = new()
            {
                ["CI"] = "true",
            };
            Run("pnpm", ["install", "--frozen-lockfile"], Path.Combine(repositoryRoot, "docs"), environment: continuousIntegration);
            Run("pnpm", ["exec", "astro", "build"], Path.Combine(repositoryRoot, "docs"), environment: continuousIntegration);
            Run("pnpm", ["check"], Path.Combine(repositoryRoot, "docs"), environment: continuousIntegration);
            break;

        case "runtime-ci":
            RequireArguments(args, 5);
            ValidateRuntimeIdentity(repositoryRoot);
            InstallRuntimePrerequisites();
            BuildRuntime(repositoryRoot, args[1], args[2]);
            StageRuntime(repositoryRoot, args[1], args[2], args[3]);
            InstallPostgreSql(repositoryRoot, args[4]);
            RunRuntimeTests(repositoryRoot);
            PackRuntime(repositoryRoot, args[3], GetStagedRuntimePath(repositoryRoot, args[3]));
            break;

        case "release-managed":
            RequireArguments(args, 2);
            PackManaged(repositoryRoot, args[1]);
            break;

        case "release-runtime":
            RequireArguments(args, 4);
            ValidateRuntimeIdentity(repositoryRoot);
            InstallRuntimePrerequisites();
            BuildRuntime(repositoryRoot, args[1], args[2]);
            PackRuntime(repositoryRoot, args[3], GetBuiltRuntimePath(repositoryRoot, args[1], args[2]));
            break;

        case "publish":
            RequireArguments(args, 2);
            PublishPackages(repositoryRoot, args[1]);
            break;

        default:
            throw new InvalidOperationException($"Unknown automation command '{args[0]}'.");
    }
}
catch (Exception exception) when (exception is ArgumentException or IOException or InvalidOperationException)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}

return 0;

static string FindRepositoryRoot()
{
    DirectoryInfo? directory = new(Directory.GetCurrentDirectory());

    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "Ankus.slnx")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    throw new DirectoryNotFoundException("Could not locate the Ankus repository root.");
}

static void RequireArguments(string[] commandArguments, int count)
{
    if (commandArguments.Length != count)
    {
        throw new InvalidOperationException($"Command '{commandArguments[0]}' expects {count - 1} arguments.");
    }
}

static string GetDotNetHost()
{
    string? hostPath = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");

    if (!string.IsNullOrWhiteSpace(hostPath))
    {
        return hostPath;
    }

    string? root = Environment.GetEnvironmentVariable("DOTNET_ROOT");

    if (!string.IsNullOrWhiteSpace(root))
    {
        return Path.Combine(root, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
    }

    return OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
}

static void ValidateRuntimeIdentity(string repositoryRoot)
{
    if (!AutomationPatterns.RuntimeCommit().IsMatch(RuntimeCommit))
    {
        throw new InvalidOperationException("The runtime commit must be a full lowercase Git commit ID.");
    }

    if (!AutomationPatterns.RuntimeVersion().IsMatch(RuntimeVersion))
    {
        throw new InvalidOperationException("The runtime package version is invalid.");
    }

    string[] runtimeFiles =
    [
        "src/Ankus.NativeAot.Runtime/Ankus.NativeAot.Runtime.csproj",
        "src/Ankus.NativeAot.Runtime/build/Ankus.NativeAot.Runtime.linux-x64.props",
        "src/Ankus.NativeAot.Runtime/build/Ankus.NativeAot.Runtime.osx-arm64.props",
        "src/Ankus.NativeAot.Runtime/build/Ankus.NativeAot.Runtime.osx-x64.props",
        "src/Ankus.NativeAot.Runtime/build/Ankus.NativeAot.Runtime.win-x64.props",
        "src/Ankus.Sdk/targets/Ankus.NativeAot.targets",
    ];

    foreach (string relativePath in runtimeFiles)
    {
        RequireText(repositoryRoot, relativePath, $">{RuntimeVersion}<");
    }

    string[] compilerFiles =
    [
        "src/Ankus.NativeAot.Runtime/build/Ankus.NativeAot.Runtime.linux-x64.props",
        "src/Ankus.NativeAot.Runtime/build/Ankus.NativeAot.Runtime.osx-arm64.props",
        "src/Ankus.NativeAot.Runtime/build/Ankus.NativeAot.Runtime.osx-x64.props",
        "src/Ankus.NativeAot.Runtime/build/Ankus.NativeAot.Runtime.win-x64.props",
        "src/Ankus.Sdk/targets/Ankus.NativeAot.targets",
    ];

    foreach (string relativePath in compilerFiles)
    {
        RequireText(repositoryRoot, relativePath, $">{RuntimeCompilerVersion}<");
    }
}

static void RequireText(string repositoryRoot, string relativePath, string expected)
{
    string path = Path.Combine(repositoryRoot, relativePath);
    string contents = File.ReadAllText(path);

    if (!contents.Contains(expected, StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"{relativePath} does not contain the pinned value {expected}.");
    }
}

static string GetReleaseVersion()
{
    string tag = Environment.GetEnvironmentVariable("RELEASE_TAG")
        ?? throw new InvalidOperationException("RELEASE_TAG is required.");

    if (!AutomationPatterns.ReleaseTag().IsMatch(tag))
    {
        throw new InvalidOperationException("Release tags must use vMAJOR.MINOR.PATCH or a valid prerelease suffix.");
    }

    return tag[1..];
}

static void WriteRuntimeOutputs()
{
    WriteOutput("repository", RuntimeRepository);
    WriteOutput("base", RuntimeBase);
    WriteOutput("commit", RuntimeCommit);
    WriteOutput("runtime_version", RuntimeVersion);
    WriteOutput("compiler_version", RuntimeCompilerVersion);
}

static void WriteOutput(string name, string value)
{
    string outputPath = Environment.GetEnvironmentVariable("GITHUB_OUTPUT")
        ?? throw new InvalidOperationException("GITHUB_OUTPUT is required.");
    File.AppendAllText(outputPath, $"{name}={value}{Environment.NewLine}");
}

static void BuildRuntime(string repositoryRoot, string platform, string architecture)
{
    VerifyPlatform(platform, architecture);
    string runtimeRoot = Path.Combine(repositoryRoot, "runtime");

    if (OperatingSystem.IsWindows())
    {
        Run(Path.Combine(runtimeRoot, "build.cmd"),
        [
            "-s",
            "clr.nativeaotruntime+clr.nativeaotlibs",
            "-c",
            "Release",
            "-arch",
            architecture,
            "/p:ManagePackageVersionsCentrally=false",
        ], runtimeRoot, true);
        return;
    }

    Run(Path.Combine(runtimeRoot, "build.sh"),
    [
        "-s",
        "clr.nativeaotruntime+clr.nativeaotlibs",
        "-c",
        "Release",
        "-arch",
        architecture,
        "/p:ManagePackageVersionsCentrally=false",
    ], runtimeRoot);
}

static void VerifyPlatform(string platform, string architecture)
{
    bool matches = platform switch
    {
        "linux" => OperatingSystem.IsLinux() && architecture == "x64",
        "osx" => OperatingSystem.IsMacOS() && architecture is "x64" or "arm64",
        "windows" => OperatingSystem.IsWindows() && architecture == "x64",
        _ => false,
    };

    if (!matches)
    {
        throw new PlatformNotSupportedException($"The {platform}-{architecture} runtime cannot be built on this runner.");
    }
}

static void InstallRuntimePrerequisites()
{
    if (!OperatingSystem.IsLinux())
    {
        return;
    }

    Run("sudo", ["apt-get", "update"]);
    Run("sudo",
    [
        "apt-get",
        "install",
        "--yes",
        "build-essential",
        "clang",
        "cmake",
        "cpio",
        "curl",
        "git",
        "libicu-dev",
        "libkrb5-dev",
        "liblttng-ust-dev",
        "libssl-dev",
        "lld",
        "lldb",
        "llvm",
        "ninja-build",
        "pigz",
        "python-is-python3",
    ]);
}

static string GetBuiltRuntimePath(string repositoryRoot, string platform, string architecture)
{
    return Path.Combine(repositoryRoot, "runtime", "artifacts", "bin", "coreclr", $"{platform}.{architecture}.Release", "aotsdk");
}

static string GetStagedRuntimePath(string repositoryRoot, string runtimeIdentifier)
{
    return Path.Combine(repositoryRoot, "artifacts", "nativeaot", runtimeIdentifier, "aotsdk");
}

static void StageRuntime(string repositoryRoot, string platform, string architecture, string runtimeIdentifier)
{
    string source = GetBuiltRuntimePath(repositoryRoot, platform, architecture);
    string destination = GetStagedRuntimePath(repositoryRoot, runtimeIdentifier);

    if (!Directory.Exists(source))
    {
        throw new DirectoryNotFoundException($"Runtime output was not found at {source}.");
    }

    CopyDirectory(source, destination);
}

static void CopyDirectory(string source, string destination)
{
    Directory.CreateDirectory(destination);

    foreach (string sourceDirectory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
    {
        string relativePath = Path.GetRelativePath(source, sourceDirectory);
        Directory.CreateDirectory(Path.Combine(destination, relativePath));
    }

    foreach (string sourceFile in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
    {
        string relativePath = Path.GetRelativePath(source, sourceFile);
        string destinationFile = Path.Combine(destination, relativePath);
        File.Copy(sourceFile, destinationFile, true);

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(destinationFile, File.GetUnixFileMode(sourceFile));
        }
    }
}

static void InstallPostgreSql(string repositoryRoot, string version)
{
    VerifyPostgreSqlVersion(version);

    if (OperatingSystem.IsLinux())
    {
        InstallPostgreSqlLinux(repositoryRoot, version);
        WriteEnvironment("ANKUS_TEST_PG_CONFIG", $"/usr/lib/postgresql/{version}/bin/pg_config");
        return;
    }

    if (OperatingSystem.IsMacOS())
    {
        string formula = $"postgresql@{version}";
        Run("brew", ["install", formula], environment: new Dictionary<string, string?>
        {
            ["HOMEBREW_NO_AUTO_UPDATE"] = "1",
        });
        string prefix = Capture("brew", ["--prefix", formula]);
        WriteEnvironment("ANKUS_TEST_PG_CONFIG", Path.Combine(prefix, "bin", "pg_config"));
        return;
    }

    if (OperatingSystem.IsWindows())
    {
        string root = Environment.GetEnvironmentVariable("PGROOT")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PostgreSQL", version);
        string pgConfig = Path.Combine(root, "bin", "pg_config.exe");

        if (!File.Exists(pgConfig))
        {
            throw new FileNotFoundException($"The Windows runner does not contain PostgreSQL {version}.", pgConfig);
        }

        string actualVersion = Capture(pgConfig, ["--version"]);

        if (!actualVersion.StartsWith($"PostgreSQL {version}.", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Expected PostgreSQL {version}, but the runner provides {actualVersion}.");
        }

        Console.WriteLine($"Using preinstalled {actualVersion}.");
        WriteEnvironment("ANKUS_TEST_PG_CONFIG", pgConfig);
        return;
    }

    throw new PlatformNotSupportedException("PostgreSQL installation is not defined for this runner.");
}

static void VerifyPostgreSqlVersion(string version)
{
    if (version is not ("15" or "16" or "17" or "18"))
    {
        throw new ArgumentOutOfRangeException(nameof(version), version, "PostgreSQL 15 through 18 are supported.");
    }
}

static void InstallPostgreSqlLinux(string repositoryRoot, string version)
{
    Run("sudo", ["apt-get", "update"]);
    Run("sudo", ["apt-get", "install", "--yes", "ca-certificates", "gnupg"]);

    Dictionary<string, string> operatingSystem = File.ReadAllLines("/etc/os-release")
        .Select(static line => line.Split('=', 2))
        .Where(static parts => parts.Length == 2)
        .ToDictionary(static parts => parts[0], static parts => parts[1].Trim('"'), StringComparer.Ordinal);
    string codeName = operatingSystem.GetValueOrDefault("VERSION_CODENAME")
        ?? throw new InvalidOperationException("VERSION_CODENAME is missing from /etc/os-release.");
    string temporaryDirectory = Path.Combine(repositoryRoot, "artifacts", "ci");
    Directory.CreateDirectory(temporaryDirectory);
    string keyPath = Path.Combine(temporaryDirectory, "postgresql.asc");
    string sourcePath = Path.Combine(temporaryDirectory, "pgdg.list");

    using (HttpClient client = new())
    {
        byte[] key = client.GetByteArrayAsync("https://www.postgresql.org/media/keys/ACCC4CF8.asc").GetAwaiter().GetResult();
        File.WriteAllBytes(keyPath, key);
    }

    File.WriteAllText(sourcePath, $"deb [signed-by=/usr/share/keyrings/postgresql.gpg] https://apt.postgresql.org/pub/repos/apt {codeName}-pgdg main{Environment.NewLine}");
    Run("sudo", ["gpg", "--dearmor", "--yes", "--output", "/usr/share/keyrings/postgresql.gpg", keyPath]);
    Run("sudo", ["install", "-m", "644", sourcePath, "/etc/apt/sources.list.d/pgdg.list"]);
    Run("sudo", ["apt-get", "update"]);
    Run("sudo", ["apt-get", "install", "--yes", $"postgresql-{version}", $"postgresql-server-dev-{version}"]);
}

static void WriteEnvironment(string name, string value)
{
    Environment.SetEnvironmentVariable(name, value);
    string environmentPath = Environment.GetEnvironmentVariable("GITHUB_ENV")
        ?? throw new InvalidOperationException("GITHUB_ENV is required.");
    File.AppendAllText(environmentPath, $"{name}={value}{Environment.NewLine}");
}

static void RunRuntimeTests(string repositoryRoot)
{
    Run(GetDotNetHost(), ["build", "Ankus.slnx", "-m:1"], repositoryRoot);
    string[] testModules =
    [
        "tests/Ankus.Examples.Hello.Tests/bin/Debug/net10.0/Ankus.Examples.Hello.Tests.dll",
        "tests/Ankus.Generators.Tests/bin/Debug/net10.0/Ankus.Generators.Tests.dll",
        "tests/Ankus.PgConfig.Tests/bin/Debug/net10.0/Ankus.PgConfig.Tests.dll",
        "tests/Ankus.IntegrationTests/bin/Debug/net10.0/Ankus.IntegrationTests.dll",
        "tests/Ankus.Runtime.Tests/bin/Debug/net10.0/Ankus.Runtime.Tests.dll",
    ];

    foreach (string testModule in testModules)
    {
        string path = Path.Combine(repositoryRoot, testModule.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("A required test module was not built.", path);
        }

        Run(GetDotNetHost(),
        [
            "test",
            "--test-modules",
            path,
            "--root-directory",
            repositoryRoot,
            "--minimum-expected-tests",
            "1",
        ], repositoryRoot);
    }
}

static void PackManaged(string repositoryRoot, string packageVersion)
{
    Run(GetDotNetHost(), ["restore", "Ankus.slnx", "-m:1", "-p:PublishAot=false"], repositoryRoot);
    string[] projects =
    [
        "src/Ankus.Generators/Ankus.Generators.csproj",
        "src/Ankus.PgConfig/Ankus.PgConfig.csproj",
        "src/Ankus.Runtime/Ankus.Runtime.csproj",
        "src/Ankus.Sdk/Ankus.Sdk.csproj",
        "src/Ankus.Testing/Ankus.Testing.csproj",
        "src/Ankus.Tool/Ankus.Tool.csproj",
    ];

    foreach (string project in projects)
    {
        Run(GetDotNetHost(),
        [
            "pack",
            project,
            "--configuration",
            "Release",
            "--no-restore",
            "--output",
            "artifacts/packages",
            "-m:1",
            $"-p:PackageVersion={packageVersion}",
        ], repositoryRoot);
    }
}

static void PackRuntime(string repositoryRoot, string runtimeIdentifier, string runtimeSdkPath)
{
    string sourcePath = Path.Combine(repositoryRoot, "runtime");
    string normalizedSdkPath = Path.EndsInDirectorySeparator(runtimeSdkPath)
        ? runtimeSdkPath
        : runtimeSdkPath + Path.DirectorySeparatorChar;
    Run(GetDotNetHost(),
    [
        "pack",
        "src/Ankus.NativeAot.Runtime/Ankus.NativeAot.Runtime.csproj",
        "--configuration",
        "Release",
        "--output",
        "artifacts/packages",
        "-m:1",
        $"-p:PackageVersion={RuntimeVersion}",
        $"-p:AnkusRuntimeIdentifier={runtimeIdentifier}",
        $"-p:AnkusRuntimeSdkPath={normalizedSdkPath}",
        $"-p:AnkusRuntimeSourcePath={sourcePath}",
    ], repositoryRoot);
}

static void PublishPackages(string repositoryRoot, string packageVersion)
{
    string packageDirectory = Path.Combine(repositoryRoot, "artifacts", "packages");
    string[] packages =
    [
        $"Ankus.NativeAot.Runtime.linux-x64.{RuntimeVersion}.nupkg",
        $"Ankus.NativeAot.Runtime.osx-arm64.{RuntimeVersion}.nupkg",
        $"Ankus.NativeAot.Runtime.osx-x64.{RuntimeVersion}.nupkg",
        $"Ankus.NativeAot.Runtime.win-x64.{RuntimeVersion}.nupkg",
        $"Ankus.Generators.{packageVersion}.nupkg",
        $"Ankus.PgConfig.{packageVersion}.nupkg",
        $"Ankus.Runtime.{packageVersion}.nupkg",
        $"Ankus.Testing.{packageVersion}.nupkg",
        $"Ankus.Tool.{packageVersion}.nupkg",
        $"Ankus.Sdk.{packageVersion}.nupkg",
    ];
    string[] actual =
    [
        .. Directory.GetFiles(packageDirectory, "*.nupkg", SearchOption.TopDirectoryOnly)
            .Select(static path => Path.GetFileName(path)
                ?? throw new InvalidOperationException($"Package path has no file name: {path}"))
            .Order(StringComparer.Ordinal),
    ];
    string[] expected = [.. packages.Order(StringComparer.Ordinal)];

    if (!actual.SequenceEqual(expected, StringComparer.Ordinal))
    {
        throw new InvalidOperationException($"Package set does not match. Expected: {string.Join(", ", expected)}. Actual: {string.Join(", ", actual)}.");
    }

    string apiKey = Environment.GetEnvironmentVariable("NUGET_API_KEY")
        ?? throw new InvalidOperationException("NUGET_API_KEY is required.");

    foreach (string package in packages)
    {
        Run(GetDotNetHost(),
        [
            "nuget",
            "push",
            Path.Combine(packageDirectory, package),
            "--api-key",
            apiKey,
            "--source",
            "https://api.nuget.org/v3/index.json",
            "--skip-duplicate",
        ], repositoryRoot, sensitiveValue: apiKey);
    }
}

static void Run(
    string fileName,
    IReadOnlyList<string> arguments,
    string? workingDirectory = null,
    bool useShellExecute = false,
    IReadOnlyDictionary<string, string?>? environment = null,
    string? sensitiveValue = null)
{
    using Process process = new();
    process.StartInfo.FileName = fileName;
    process.StartInfo.WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory();
    process.StartInfo.UseShellExecute = useShellExecute;

    if (!useShellExecute)
    {
        if (Path.GetFileNameWithoutExtension(fileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            process.StartInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        }

        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.OutputDataReceived += static (_, eventArguments) =>
        {
            if (eventArguments.Data is not null)
            {
                Console.Out.WriteLine(eventArguments.Data);
            }
        };
        process.ErrorDataReceived += static (_, eventArguments) =>
        {
            if (eventArguments.Data is not null)
            {
                Console.Error.WriteLine(eventArguments.Data);
            }
        };
    }

    foreach (string argument in arguments)
    {
        process.StartInfo.ArgumentList.Add(argument);
    }

    if (environment is not null)
    {
        foreach ((string name, string? value) in environment)
        {
            process.StartInfo.Environment[name] = value;
        }
    }

    Console.WriteLine($"> {fileName} {string.Join(' ', arguments.Select(argument => QuoteArgument(argument == sensitiveValue ? "***" : argument)))}");

    if (!process.Start())
    {
        throw new InvalidOperationException($"Command could not start: {fileName}");
    }

    if (!useShellExecute)
    {
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
    }

    process.WaitForExit();

    if (process.ExitCode != 0)
    {
        throw new InvalidOperationException($"Command failed with exit code {process.ExitCode}: {fileName}");
    }
}

static string Capture(string fileName, IReadOnlyList<string> arguments)
{
    using Process process = new();
    process.StartInfo.FileName = fileName;
    process.StartInfo.UseShellExecute = false;
    process.StartInfo.RedirectStandardOutput = true;
    process.StartInfo.RedirectStandardError = true;

    foreach (string argument in arguments)
    {
        process.StartInfo.ArgumentList.Add(argument);
    }

    process.Start();
    Task<string> output = process.StandardOutput.ReadToEndAsync();
    Task<string> error = process.StandardError.ReadToEndAsync();
    process.WaitForExit();
    Task.WaitAll(output, error);

    if (process.ExitCode != 0)
    {
        throw new InvalidOperationException($"Command failed: {fileName}{Environment.NewLine}{error.Result}");
    }

    return output.Result.Trim();
}

static string QuoteArgument(string argument)
{
    return argument.Any(char.IsWhiteSpace) ? $"\"{argument}\"" : argument;
}

/// <summary>
/// Provides the generated regular expressions used by repository automation.
/// </summary>
internal static partial class AutomationPatterns
{
    /// <summary>
    /// Matches a full lowercase Git commit ID.
    /// </summary>
    [GeneratedRegex("^[0-9a-f]{40}$", RegexOptions.CultureInvariant)]
    internal static partial Regex RuntimeCommit();

    /// <summary>
    /// Matches an Ankus runtime package version.
    /// </summary>
    [GeneratedRegex("^[0-9]+\\.[0-9]+\\.[0-9]+-[0-9A-Za-z.-]+$", RegexOptions.CultureInvariant)]
    internal static partial Regex RuntimeVersion();

    /// <summary>
    /// Matches an Ankus release tag.
    /// </summary>
    [GeneratedRegex("^v[0-9]+\\.[0-9]+\\.[0-9]+(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?$", RegexOptions.CultureInvariant)]
    internal static partial Regex ReleaseTag();
}
