#:property TargetFramework=net10.0
#:property PackAsTool=false
#:property PublishAot=false
#:project ../src/Ankus.PgConfig/Ankus.PgConfig.csproj

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security;
using System.Text.RegularExpressions;
using Ankus.PgConfig;

const string RuntimeRepository = "willibrandon/runtime";
const string RuntimeBase = "v10.0.12";
const string RuntimeCommit = "a96595dcd43cf770466315673afd6cf04669d4c8";
const string RuntimeVersion = "10.0.12-ankus.2";
const string RuntimeCompilerVersion = "10.0.12";

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
            InstallPostgreSql(repositoryRoot, "18");
            ConfigureHeaderFrontend(repositoryRoot);
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

        case "runtime-build":
            RequireArguments(args, 4);
            ValidateRuntimeIdentity(repositoryRoot);
            InstallRuntimePrerequisites();
            BuildRuntime(repositoryRoot, args[1], args[2]);
            StageRuntime(repositoryRoot, args[1], args[2], args[3]);
            break;

        case "runtime-pack":
            RequireArguments(args, 2);
            ValidateRuntimeIdentity(repositoryRoot);
            VerifyStagedRuntime(repositoryRoot, args[1]);
            PackRuntime(
                repositoryRoot,
                args[1],
                GetStagedRuntimePath(repositoryRoot, args[1]),
                GetStagedRuntimeSourcePath(repositoryRoot, args[1]));
            break;

        case "runtime-test":
        case "runtime-test-build":
            RequireArguments(args, 3);
            ValidateRuntimeIdentity(repositoryRoot);
            VerifyStagedRuntime(repositoryRoot, args[1]);
            InstallPostgreSql(repositoryRoot, args[2]);
            ConfigureHeaderFrontend(repositoryRoot);
            ConfigureBindingCache(repositoryRoot);
            BuildTests(repositoryRoot);
            if (args[0] == "runtime-test")
            {
                RunRuntimeTests(repositoryRoot);
            }

            break;

        case "runtime-test-run":
            RequireArguments(args, 1);
            ValidateRuntimeIdentity(repositoryRoot);
            RunRuntimeTests(repositoryRoot);
            break;

        case "header-frontend-check":
            RequireArguments(args, 2);
            VerifyHeaderFrontend(args[1]);
            break;

        case "postgresql-check":
            RequireArguments(args, 3);
            VerifyPostgreSqlVersion(args[1]);
            VerifyPostgreSqlHeaders(args[2], args[1]);
            break;

        case "windows-toolchain":
            RequireArguments(args, 1);
            ConfigureWindowsToolchain();
            break;

        case "unit-test":
            RequireArguments(args, 1);
            RunUnitTests(repositoryRoot);
            break;

        case "prepare-reports":
            RequireArguments(args, 1);
            PrepareReports(repositoryRoot);
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
            PackRuntime(
                repositoryRoot,
                args[3],
                GetBuiltRuntimePath(repositoryRoot, args[1], args[2]),
                Path.Combine(repositoryRoot, "runtime"));
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
    List<string> arguments =
    [
        "-s",
        "clr.nativeaotruntime+clr.nativeaotlibs",
        "-c",
        "Release",
        "-arch",
        architecture,
        "/p:ManagePackageVersionsCentrally=false",
    ];

    if (OperatingSystem.IsWindows())
    {
        Run(Path.Combine(runtimeRoot, "build.cmd"), arguments, runtimeRoot, true);
        return;
    }

    if (IsMacOsCrossBuild(architecture))
    {
        arguments.Add("-cross");
    }

    Run(Path.Combine(runtimeRoot, "build.sh"), arguments, runtimeRoot);
    if (!IsMacOsCrossBuild(architecture))
    {
        VerifyNativeHostShutdown(repositoryRoot, platform, architecture);
    }
}

// Prove that a newly built fork runtime preserves bounded native host shutdown and managed thread cleanup.
static void VerifyNativeHostShutdown(string repositoryRoot, string platform, string architecture)
{
    string runtimeIdentifier = $"{platform}-{architecture}";
    string probeRoot = Path.Combine(repositoryRoot, "runtime", "eng", "ankus", "fork-probes", "host-shutdown");
    string output = Path.Combine(repositoryRoot, "artifacts", "runtime-checks", runtimeIdentifier);
    Run(GetDotNetHost(),
    [
        "publish", Path.Combine(probeRoot, "NativeHostShutdownProbe.csproj"),
        "--configuration", "Release", "--runtime", runtimeIdentifier, "--output", output,
        $"-p:IlcSdkPath={GetBuiltRuntimePath(repositoryRoot, platform, architecture)}{Path.DirectorySeparatorChar}",
    ]);

    string host = Path.Combine(output, "host");
    List<string> compilerArguments =
    [
        "-std=gnu17", "-O2", "-Wall", "-Wextra", "-Werror", Path.Combine(probeRoot, "host.c"),
        "-pthread", "-o", host,
    ];
    if (OperatingSystem.IsLinux())
    {
        compilerArguments.Add("-ldl");
    }

    Run("clang", compilerArguments);
    string library = "NativeHostShutdownProbe" + (OperatingSystem.IsMacOS() ? ".dylib" : ".so");
    Run(host, [Path.Combine(output, library)]);
}

static bool IsMacOsCrossBuild(string architecture)
    => OperatingSystem.IsMacOS() && (architecture, RuntimeInformation.ProcessArchitecture) is
        ("x64", Architecture.Arm64) or ("arm64", Architecture.X64);

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

    if (Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT") == "self-hosted")
    {
        Run("clang", ["--version"]);
        Run("cmake", ["--version"]);
        Run("ninja", ["--version"]);
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

static string GetStagedRuntimeSourcePath(string repositoryRoot, string runtimeIdentifier)
{
    return Path.Combine(repositoryRoot, "artifacts", "nativeaot", runtimeIdentifier, "source");
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

    string runtimeSource = Path.Combine(repositoryRoot, "runtime");
    string stagedSource = GetStagedRuntimeSourcePath(repositoryRoot, runtimeIdentifier);
    Directory.CreateDirectory(stagedSource);
    string[] metadataFiles = ["LICENSE.TXT", "THIRD-PARTY-NOTICES.TXT"];

    foreach (string fileName in metadataFiles)
    {
        string sourceFile = Path.Combine(runtimeSource, fileName);

        if (!File.Exists(sourceFile))
        {
            throw new FileNotFoundException("Required runtime package metadata was not found.", sourceFile);
        }

        File.Copy(sourceFile, Path.Combine(stagedSource, fileName), true);
    }
}

static void VerifyStagedRuntime(string repositoryRoot, string runtimeIdentifier)
{
    string path = GetStagedRuntimePath(repositoryRoot, runtimeIdentifier);
    string sourcePath = GetStagedRuntimeSourcePath(repositoryRoot, runtimeIdentifier);

    if (!File.Exists(Path.Combine(path, "System.Private.CoreLib.dll"))
        || !File.Exists(Path.Combine(sourcePath, "LICENSE.TXT"))
        || !File.Exists(Path.Combine(sourcePath, "THIRD-PARTY-NOTICES.TXT")))
    {
        throw new DirectoryNotFoundException($"The staged {runtimeIdentifier} runtime is incomplete.");
    }
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
        SelectPostgreSql(version, $"/usr/lib/postgresql/{version}/bin/pg_config");
        return;
    }

    if (OperatingSystem.IsMacOS())
    {
        string formula = $"postgresql@{version}";
        if (Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT") != "self-hosted")
        {
            Run("brew", ["install", formula], environment: new Dictionary<string, string?>
            {
                ["HOMEBREW_NO_AUTO_UPDATE"] = "1",
            });
        }

        string prefix = Capture("brew", ["--prefix", formula]);
        string pgConfig = Path.Combine(prefix, "bin", "pg_config");
        VerifyPostgreSqlHeaders(pgConfig, version);
        SelectPostgreSql(version, pgConfig);
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

        PostgresVersion actualVersion = VerifyPostgreSqlHeaders(pgConfig, version);
        int minimumMinor = version switch
        {
            "16" => 15,
            "17" => 11,
            "18" => 6,
            _ => 0,
        };
        if (actualVersion.Stage == PostgresReleaseStage.Stable && actualVersion.Minor < minimumMinor)
        {
            throw new InvalidOperationException($"The complete test suite requires PostgreSQL {version}.{minimumMinor} or later in major {version}; the runner provides {actualVersion}. Update the runner installation or select it through PGROOT.");
        }

        Console.WriteLine($"Using preinstalled {actualVersion}.");
        SelectPostgreSql(version, pgConfig);
        return;
    }

    throw new PlatformNotSupportedException("PostgreSQL installation is not defined for this runner.");
}

static void SelectPostgreSql(string version, string pgConfig)
{
    WriteEnvironment("ANKUS_TEST_PG_CONFIG", pgConfig);
    WriteEnvironment("AnkusPostgresMajor", version);
    WriteEnvironment("AnkusPgConfigPath", pgConfig);
}

static void VerifyPostgreSqlVersion(string version)
{
    if (version is not ("13" or "14" or "15" or "16" or "17" or "18" or "19"))
    {
        throw new ArgumentOutOfRangeException(nameof(version), version, "PostgreSQL 13 through 19 are supported.");
    }
}

static void ConfigureWindowsToolchain()
{
    if (!OperatingSystem.IsWindows())
    {
        throw new PlatformNotSupportedException("The Windows C++ toolchain requires Windows.");
    }

    string locator = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        "Microsoft Visual Studio", "Installer", "vswhere.exe");
    string installation = Capture(locator,
    [
        "-latest", "-products", "*", "-version", "[17.9,)",
        "-requires", "Microsoft.VisualStudio.Component.VC.Tools.x86.x64", "-property", "installationPath",
    ]);
    string setup = Path.Combine(installation, "VC", "Auxiliary", "Build", "vcvarsall.bat");
    if (string.IsNullOrWhiteSpace(installation) || !File.Exists(setup))
    {
        throw new InvalidOperationException("Visual Studio 2022 17.9 or later with the C++ x64 tools is required.");
    }

    // cmd.exe needs its own quoting rules for a batch path containing spaces.
    var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"))
    {
        Arguments = $"/d /s /c \"\"{setup}\" x64 >nul && set\"",
    };
    string configured = CaptureProcess(start);
    foreach (string line in configured.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
    {
        int separator = line.IndexOf('=');
        if (separator <= 0)
        {
            continue;
        }

        string name = line[..separator];
        string value = line[(separator + 1)..];
        if (Environment.GetEnvironmentVariable(name) != value)
        {
            WriteEnvironment(name, value);
        }
    }

    Console.WriteLine($"Selected MSVC {Environment.GetEnvironmentVariable("VCToolsVersion")} for x64.");
}

static void ConfigureHeaderFrontend(string repositoryRoot)
{
    string directory;
    if ((OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) &&
        Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT") == "self-hosted")
    {
        string resourceDirectory = Capture("clang", ["-print-resource-dir"]);
        directory = Path.GetFullPath(Path.Combine(resourceDirectory, "..", "..", "..", "bin"));
    }
    else if (OperatingSystem.IsLinux())
    {
        Dictionary<string, string> operatingSystem = File.ReadAllLines("/etc/os-release")
            .Select(static line => line.Split('=', 2))
            .Where(static parts => parts.Length == 2)
            .ToDictionary(static parts => parts[0], static parts => parts[1].Trim('"'), StringComparer.Ordinal);
        string codeName = operatingSystem.GetValueOrDefault("VERSION_CODENAME")
            ?? throw new InvalidOperationException("VERSION_CODENAME is missing from /etc/os-release.");
        string temporaryDirectory = Path.Combine(repositoryRoot, "artifacts", "ci");
        Directory.CreateDirectory(temporaryDirectory);
        string keyPath = Path.Combine(temporaryDirectory, "llvm.asc");
        string sourcePath = Path.Combine(temporaryDirectory, "llvm.list");
        using (HttpClient client = new())
        {
            File.WriteAllBytes(keyPath, client.GetByteArrayAsync("https://apt.llvm.org/llvm-snapshot.gpg.key").GetAwaiter().GetResult());
        }

        File.WriteAllText(sourcePath,
            $"deb [signed-by=/usr/share/keyrings/llvm.gpg] https://apt.llvm.org/{codeName}/ llvm-toolchain-{codeName}-20 main{Environment.NewLine}");
        Run("sudo", ["gpg", "--dearmor", "--yes", "--output", "/usr/share/keyrings/llvm.gpg", keyPath]);
        Run("sudo", ["install", "-m", "644", sourcePath, "/etc/apt/sources.list.d/llvm.list"]);
        Run("sudo", ["apt-get", "update"]);
        Run("sudo", ["apt-get", "install", "--yes", "--no-install-recommends", "clang-20", "libclang-20-dev"]);
        directory = "/usr/lib/llvm-20/bin";
    }
    else if (OperatingSystem.IsMacOS())
    {
        Run("brew", ["install", "llvm@20"], environment: new Dictionary<string, string?>
        {
            ["HOMEBREW_NO_AUTO_UPDATE"] = "1",
        });
        directory = Path.Combine(Capture("brew", ["--prefix", "llvm@20"]), "bin");
    }
    else if (OperatingSystem.IsWindows())
    {
        directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "LLVM", "bin");
    }
    else
    {
        throw new PlatformNotSupportedException("Header frontend installation is not defined for this runner.");
    }

    string compiler = Path.Combine(directory, OperatingSystem.IsWindows() ? "clang-cl.exe" : "clang");
    VerifyHeaderFrontend(compiler);
    Environment.SetEnvironmentVariable("PATH", directory + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"));
    string pathFile = Environment.GetEnvironmentVariable("GITHUB_PATH")
        ?? throw new InvalidOperationException("GITHUB_PATH is required.");
    File.AppendAllText(pathFile, directory + Environment.NewLine);
}

static void VerifyHeaderFrontend(string compiler)
{
    Run(compiler, ["--version"]);
    string help = Capture(compiler, ["-cc1", "--help"]);
    if (!help.Split('\n').Any(static line => line.TrimStart().StartsWith("-skip-function-bodies ", StringComparison.Ordinal)))
    {
        throw new InvalidOperationException($"The header collector requires Clang 20 or later with -skip-function-bodies support. Select a supported LLVM toolchain instead of '{compiler}'.");
    }
}

static void ConfigureBindingCache(string repositoryRoot)
{
    string? directory = Environment.GetEnvironmentVariable("AnkusBindingCacheDirectory");
    if (string.IsNullOrWhiteSpace(directory))
    {
        directory = Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT") == "self-hosted"
            ? Path.Combine(Environment.GetEnvironmentVariable("RUNNER_TOOL_CACHE")
                ?? throw new InvalidOperationException("RUNNER_TOOL_CACHE is required for the dedicated runner."), "ankus-binding-cache")
            : Path.Combine(repositoryRoot, "artifacts", "binding-cache");
    }

    Directory.CreateDirectory(directory);
    WriteEnvironment("AnkusBindingCacheDirectory", Path.GetFullPath(directory));
}

static void InstallPostgreSqlLinux(string repositoryRoot, string version)
{
    if (Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT") == "self-hosted")
    {
        string pgConfig = $"/usr/lib/postgresql/{version}/bin/pg_config";
        VerifyPostgreSqlHeaders(pgConfig, version);
        Run("valgrind", ["--version"]);
        Run("valgrind", ["--error-exitcode=1", "/bin/true"]);
        return;
    }

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

    string components = version == "19" ? "main 19" : "main";
    File.WriteAllText(sourcePath, $"deb [signed-by=/usr/share/keyrings/postgresql.gpg] https://apt.postgresql.org/pub/repos/apt {codeName}-pgdg {components}{Environment.NewLine}");
    Run("sudo", ["gpg", "--dearmor", "--yes", "--output", "/usr/share/keyrings/postgresql.gpg", keyPath]);
    Run("sudo", ["install", "-m", "644", sourcePath, "/etc/apt/sources.list.d/pgdg.list"]);
    Run("sudo", ["apt-get", "update"]);
    Run("sudo", ["apt-get", "install", "--yes", $"postgresql-{version}", $"postgresql-server-dev-{version}", "valgrind", "libc6-dbg"]);
}

static PostgresVersion VerifyPostgreSqlHeaders(string pgConfig, string version)
{
    PostgresInstallation installation = PostgresInstallation.CreateAsync(pgConfig).GetAwaiter().GetResult();
    if (installation.Label != "pg" + version)
    {
        throw new InvalidOperationException($"Expected PostgreSQL {version}, but the installation provides {installation.Version}.");
    }

    Console.WriteLine($"Using PostgreSQL {installation.Version}.");
    string header = Path.Combine(installation.ServerIncludeDirectory, "postgres.h");
    if (!File.Exists(header))
    {
        throw new FileNotFoundException("The runner requires PostgreSQL server headers.", header);
    }

    return installation.Version;
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
    string integrationTestModule = "tests/Ankus.IntegrationTests/bin/Release/net10.0/Ankus.IntegrationTests.dll";
    Task integrationTests = Task.Run(() => RunTestModule(repositoryRoot, integrationTestModule));
    Task unitTests = Task.Run(() => RunUnitTestModules(repositoryRoot));
    Task.WhenAll(integrationTests, unitTests).GetAwaiter().GetResult();
}

static void RunUnitTests(string repositoryRoot)
{
    BuildTests(repositoryRoot);
    RunUnitTestModules(repositoryRoot);
}

// Keep private machine identifiers out of uploaded files as well as masked job logs.
static void PrepareReports(string repositoryRoot)
{
    string identifiers = Environment.GetEnvironmentVariable("ANKUS_RUNNER_PRIVATE_IDENTIFIERS") ?? string.Empty;
    if (Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT") == "self-hosted" &&
        string.IsNullOrWhiteSpace(identifiers))
    {
        throw new InvalidOperationException("Configure the runner privacy secret before uploading dedicated-runner reports.");
    }

    string[] replacements = [.. identifiers.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .SelectMany(static value => new[] { value, SecurityElement.Escape(value) ?? value })
        .Distinct(StringComparer.Ordinal)
        .OrderByDescending(static value => value.Length)];
    string destination = Path.Combine(repositoryRoot, "artifacts", "ci-reports");
    if (Directory.Exists(destination))
    {
        Directory.Delete(destination, true);
    }

    if (Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT") == "github-hosted")
    {
        SaveBuildTimings(repositoryRoot);
    }

    foreach ((string directory, string pattern) in new[] { ("test-results", "*.trx"), ("test-logs", "*.log") })
    {
        string source = Path.Combine(repositoryRoot, "artifacts", directory);
        if (!Directory.Exists(source))
        {
            continue;
        }

        string target = Path.Combine(destination, directory);
        Directory.CreateDirectory(target);
        foreach (string file in Directory.EnumerateFiles(source, pattern))
        {
            string contents = File.ReadAllText(file);
            foreach (string value in replacements)
            {
                contents = contents.Replace(value, "[private]", StringComparison.Ordinal);
            }

            File.WriteAllText(Path.Combine(target, Path.GetFileName(file)), contents);
        }
    }
}

// Replay existing logs without rebuilding. Persist only target/task names and
// durations; binary logs and their environment/property payloads stay local.
static void SaveBuildTimings(string repositoryRoot)
{
    string logs = Path.Combine(repositoryRoot, "artifacts", "test-logs");
    Directory.CreateDirectory(logs);
    string[] roots = [logs,
        .. Directory.EnumerateDirectories(Path.GetTempPath(), "ankus package tests *")];
    List<string> report = [];
    EnumerationOptions options = new() { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
    string[] files = [.. Directory.EnumerateFiles(Path.Combine(repositoryRoot, "artifacts"), "*.binlog")
        .Concat(roots.SelectMany(root => Directory.EnumerateFiles(root, "*.binlog", options)))
        .Order(StringComparer.Ordinal)];
    foreach (string file in files)
    {
        report.Add("Build: " + Path.GetFileName(file));
        string output;
        try
        {
            output = Capture(GetDotNetHost(), ["msbuild", file, "-nologo", "-verbosity:quiet", "-clp:PerformanceSummary"]);
        }
        catch (InvalidOperationException)
        {
            report.Add("Timing replay unavailable; the build log may be incomplete after cancellation.");
            continue;
        }

        bool include = false;
        int rows = 0;
        foreach (string line in output.Split('\n'))
        {
            string text = line.Trim();
            if (text is "Target Performance Summary:" or "Task Performance Summary:")
            {
                include = true;
                report.Add(text);
            }
            else if (include && AutomationPatterns.BuildTiming().IsMatch(text))
            {
                report.Add(text);
                rows++;
            }
        }

        if (rows == 0)
        {
            report.Add("No completed target/task timings were recorded; the build log may be incomplete.");
        }
    }

    if (files.Length > 0)
    {
        report.AddRange(ReadBindingTimings(repositoryRoot, files).Split('\n'));
    }

    File.WriteAllLines(Path.Combine(logs, "build-timings.log"), report);
}

// Only fixed phase metadata reaches job logs or upload reports, never command
// arguments or binary-log properties. Replay failure does not change test outcomes.
static string ReadBindingTimings(string repositoryRoot, IReadOnlyList<string> files)
{
    try
    {
        return Capture(GetDotNetHost(), ["run", "--file",
            Path.Combine(repositoryRoot, "eng", "Ankus.BuildTimings.cs"), "--", .. files]);
    }
    catch (InvalidOperationException)
    {
        return "Binding task timing reader unavailable; existing target/task summaries remain authoritative.";
    }
}

static void BuildTests(string repositoryRoot)
{
    string logs = Path.Combine(repositoryRoot, "artifacts", "test-logs");
    Directory.CreateDirectory(logs);
    string binlog = Path.Combine(logs, "build-tests-" + Guid.NewGuid().ToString("N") + ".binlog");
    Run(GetDotNetHost(), ["build", "Ankus.slnx", "--configuration", "Release", "-m",
        "-clp:PerformanceSummary", "-bl:" + binlog], repositoryRoot);
    if (Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT") == "github-hosted")
    {
        Console.WriteLine(ReadBindingTimings(repositoryRoot, [binlog]));
    }
}

static void RunUnitTestModules(string repositoryRoot)
{
    string[] testModules =
    [
        "tests/Ankus.Build.Tests/bin/Release/net10.0/Ankus.Build.Tests.dll",
        "tests/Ankus.Examples.Hello.Tests/bin/Release/net10.0/Ankus.Examples.Hello.Tests.dll",
        "tests/Ankus.Generators.Tests/bin/Release/net10.0/Ankus.Generators.Tests.dll",
        "tests/Ankus.PgConfig.Tests/bin/Release/net10.0/Ankus.PgConfig.Tests.dll",
        "tests/Ankus.Runtime.Tests/bin/Release/net10.0/Ankus.Runtime.Tests.dll",
    ];

    foreach (string testModule in testModules)
    {
        RunTestModule(repositoryRoot, testModule);
    }
}

static void RunTestModule(string repositoryRoot, string testModule)
{
    string path = Path.Combine(repositoryRoot, testModule.Replace('/', Path.DirectorySeparatorChar));

    if (!File.Exists(path))
    {
        throw new FileNotFoundException("A required test module was not built.", path);
    }

    List<string> arguments =
    [
        "test",
        "--test-modules",
        testModule,
        "--root-directory",
        repositoryRoot,
        "--minimum-expected-tests",
        "1",
        "--report-trx",
        "--results-directory",
        Path.Combine(repositoryRoot, "artifacts", "test-results"),
    ];
    if (Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT") == "github-hosted")
    {
        // Completed case names and durations survive a timeout that prevents the final TRX from being written.
        arguments.AddRange(["--output", "Detailed", "--show-stdout", "Failed", "--show-stderr", "Failed"]);
    }

    Run(GetDotNetHost(), arguments, repositoryRoot);
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
        "src/Ankus.Templates/Ankus.Templates.csproj",
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

static void PackRuntime(
    string repositoryRoot,
    string runtimeIdentifier,
    string runtimeSdkPath,
    string runtimeSourcePath)
{
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
        $"-p:AnkusRuntimeSourcePath={runtimeSourcePath}",
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
        $"Ankus.Templates.{packageVersion}.nupkg",
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
            process.StartInfo.Environment.Remove("Platform");
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
    var start = new ProcessStartInfo(fileName);
    foreach (string argument in arguments)
    {
        start.ArgumentList.Add(argument);
    }

    return CaptureProcess(start);
}

static string CaptureProcess(ProcessStartInfo start)
{
    start.UseShellExecute = false;
    start.RedirectStandardOutput = true;
    start.RedirectStandardError = true;
    using Process process = new() { StartInfo = start };
    process.Start();
    Task<string> output = process.StandardOutput.ReadToEndAsync();
    Task<string> error = process.StandardError.ReadToEndAsync();
    process.WaitForExit();
    Task.WaitAll(output, error);

    if (process.ExitCode != 0)
    {
        throw new InvalidOperationException($"Command failed: {start.FileName}{Environment.NewLine}{error.Result}");
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
    /// Matches a performance row with an MSBuild target/task name and no command or property payload.
    /// </summary>
    [GeneratedRegex(@"^\d+ ms\s+[A-Za-z_][A-Za-z0-9_.]*\s+\d+ calls$", RegexOptions.CultureInvariant)]
    internal static partial Regex BuildTiming();

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
