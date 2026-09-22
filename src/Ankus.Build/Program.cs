using System.Diagnostics;
using System.Globalization;
using System.Text;
using Ankus.Build;
using Ankus.PgConfig;

try
{
    if (args.Length != 10)
    {
        throw new ArgumentException(
            "Expected assembly, artifact directory, PostgreSQL major, linker, toolchain libraries, " +
            "extension name, version, library, runtime identifier, and optional pg_config path.");
    }

    string assembly = Path.GetFullPath(args[0]);
    string output = Path.GetFullPath(args[1]);
    int major = int.Parse(args[2], CultureInfo.InvariantCulture);
    PostgresInstallation installation = string.IsNullOrEmpty(args[9])
        ? await PostgresInstallation.DiscoverAsync(major)
        : await PostgresInstallation.CreateAsync(args[9]);
    if (installation.Version.Major != major)
    {
        throw new InvalidOperationException($"Expected PostgreSQL {major}, but '{installation.PgConfigPath}' is {installation.Label}.");
    }

    ExtensionManifest manifest = ExtensionManifest.Read(assembly);
    IReadOnlyDictionary<string, string> package = ExtensionPackage.Create(args[5], args[6], args[7], manifest.Sql, manifest.Relocatable);
    Directory.CreateDirectory(output);
    new PublishedExtension(major, args[8], args[7], args[5] + ".control",
        args[5] + "--" + args[6] + ".sql").Write(output);
    string extensionDirectory = Path.Combine(output, "extension");
    Directory.CreateDirectory(extensionDirectory);
    foreach ((string name, string content) in package)
    {
        WriteIfDifferent(Path.Combine(extensionDirectory, name), content);
    }

    string source = Path.Combine(output, "bridge.c");
    string nativeObject = Path.Combine(output, OperatingSystem.IsWindows() ? "bridge.obj" : "bridge.o");
    WriteIfDifferent(source, manifest.NativeSource);
    WriteIfDifferent(Path.Combine(output, "schema.sql"), manifest.Sql);
    WriteIfDifferent(Path.Combine(output, "exports.txt"), manifest.Exports);

    var libraries = new List<string> { nativeObject };
    var compilerArguments = new List<string>();
    string compiler;
    if (OperatingSystem.IsWindows())
    {
        compiler = Path.Combine(Path.GetDirectoryName(args[3]) ?? string.Empty, "cl.exe");
        compilerArguments.AddRange(["/nologo", "/c", "/O2", "/MD", $"/Fo{nativeObject}"]);
        compilerArguments.Add($"/I{installation.ServerIncludeDirectory}");
        compilerArguments.Add($"/I{Path.Combine(installation.ServerIncludeDirectory, "port", "win32")}");
        compilerArguments.Add($"/I{Path.Combine(installation.ServerIncludeDirectory, "port", "win32_msvc")}");
        foreach (string directory in WindowsToolchain.GetIncludeDirectories(args[4]))
        {
            compilerArguments.Add($"/I{directory}");
        }

        libraries.Add(Path.Combine(installation.LibraryDirectory, "postgres.lib"));
    }
    else
    {
        compiler = args[3];
        compilerArguments.AddRange(["-c", "-O2", "-fPIC", "-Wall", "-Wextra", "-Werror"]);
        compilerArguments.AddRange(["-isystem", installation.ServerIncludeDirectory, "-o", nativeObject]);
    }

    compilerArguments.Add(source);
    WriteIfDifferent(Path.Combine(output, "libraries.txt"), string.Join(Environment.NewLine, libraries));
    // Compile against the installed headers each publish; no stale ABI objects survive an installation change.
    await RunCompilerAsync(compiler, compilerArguments);
    Console.WriteLine($"Ankus: generated PostgreSQL {major} native boundaries and SQL.");
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine($"Ankus build failed: {error}");
    return 1;
}

static async Task RunCompilerAsync(string compiler, IEnumerable<string> arguments)
{
    using var process = new Process { StartInfo = new ProcessStartInfo(compiler) { UseShellExecute = false } };
    foreach (string argument in arguments)
    {
        process.StartInfo.ArgumentList.Add(argument);
    }

    process.Start();
    await process.WaitForExitAsync();
    if (process.ExitCode != 0)
    {
        throw new InvalidOperationException($"Native compiler exited with code {process.ExitCode}.");
    }
}

static void WriteIfDifferent(string path, string content)
{
    if (!File.Exists(path) || File.ReadAllText(path) != content)
    {
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}
