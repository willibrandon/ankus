using System.Diagnostics;
using System.Globalization;
using System.Text;
using Ankus.PgConfig;

namespace Ankus.Build;

/// <summary>
/// Implements the framework-owned native build step invoked by the Ankus MSBuild integration.
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] arguments)
    {
        try
        {
            if (arguments.Length != 5)
            {
                throw new ArgumentException("Expected assembly, artifact directory, PostgreSQL major, linker, and toolchain libraries.");
            }

            string assembly = Path.GetFullPath(arguments[0]);
            string output = Path.GetFullPath(arguments[1]);
            int major = int.Parse(arguments[2], CultureInfo.InvariantCulture);
            PostgresInstallation installation = await PostgresInstallation.DiscoverAsync(major);
            ExtensionManifest manifest = ExtensionManifest.Read(assembly);
            Directory.CreateDirectory(output);
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
                compiler = Path.Combine(Path.GetDirectoryName(arguments[3]) ?? string.Empty, "cl.exe");
                compilerArguments.AddRange(["/nologo", "/c", "/O2", "/MD", $"/Fo{nativeObject}"]);
                compilerArguments.Add($"/I{installation.ServerIncludeDirectory}");
                compilerArguments.Add($"/I{Path.Combine(installation.ServerIncludeDirectory, "port", "win32")}");
                compilerArguments.Add($"/I{Path.Combine(installation.ServerIncludeDirectory, "port", "win32_msvc")}");
                foreach (string directory in WindowsToolchain.GetIncludeDirectories(arguments[4]))
                {
                    compilerArguments.Add($"/I{directory}");
                }

                libraries.Add(Path.Combine(installation.LibraryDirectory, "postgres.lib"));
            }
            else
            {
                compiler = arguments[3];
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
    }

    private static async Task RunCompilerAsync(string compiler, IEnumerable<string> arguments)
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

    private static void WriteIfDifferent(string path, string content)
    {
        if (!File.Exists(path) || File.ReadAllText(path) != content)
        {
            File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
    }
}
