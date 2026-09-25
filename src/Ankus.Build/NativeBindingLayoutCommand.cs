using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Ankus.PgConfig;

namespace Ankus.Build;

/// <summary>
/// Measures native value layouts using the selected installation's headers and the host C toolchain.
/// </summary>
internal static class NativeBindingLayoutCommand
{
    private static readonly JsonSerializerOptions s_jsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// Compiles, executes and validates one complete native layout probe.
    /// </summary>
    /// <param name="arguments">The major, pg_config path, output directory, optional compiler and Windows library paths.</param>
    /// <param name="cancellationToken">Cancels installation discovery and child processes.</param>
    internal static async Task RunAsync(string[] arguments, CancellationToken cancellationToken = default)
    {
        if (arguments.Length is < 3 or > 5)
        {
            throw new ArgumentException("Expected binding-layouts <major> <pg_config> <output-directory> [compiler] [windows-library-directories].", nameof(arguments));
        }

        int major = int.Parse(arguments[0], NumberStyles.None, CultureInfo.InvariantCulture);
        NativeBindingCatalog catalog = NativeBindingResources.ReadCatalog(major);
        PostgresInstallation installation = string.IsNullOrEmpty(arguments[1])
            ? await PostgresInstallation.DiscoverAsync(major, cancellationToken: cancellationToken)
            : await PostgresInstallation.CreateAsync(arguments[1], cancellationToken);
        if (installation.Version.Major != major)
        {
            throw new InvalidOperationException($"Expected PostgreSQL {major}, but the selected installation is {installation.Label}.");
        }

        string output = Path.GetFullPath(arguments[2]);
        Directory.CreateDirectory(output);
        string source = Path.Combine(output, "native-layout.c");
        string executable = Path.Combine(output, OperatingSystem.IsWindows() ? "native-layout.exe" : "native-layout");
        await File.WriteAllTextAsync(source, NativeBindingProbe.GenerateSource(catalog, NativeBindingResources.ReadHeaders(major)), cancellationToken);
        string compiler = arguments.Length >= 4 ? arguments[3] : OperatingSystem.IsWindows() ? "cl.exe" : "cc";
        var options = new List<string>();
        if (OperatingSystem.IsWindows())
        {
            string libraries = arguments.Length == 5 ? arguments[4] : string.Empty;
            options.AddRange(["/nologo", "/O2", "/MD", "/WX", "/Fo" + Path.ChangeExtension(executable, ".obj"), "/Fe" + executable]);
            options.AddRange(["/I" + installation.ServerIncludeDirectory, "/I" + installation.IncludeDirectory,
                "/I" + Path.Combine(installation.ServerIncludeDirectory, "port", "win32"),
                "/I" + Path.Combine(installation.ServerIncludeDirectory, "port", "win32_msvc")]);
            options.AddRange(WindowsToolchain.GetIncludeDirectories(libraries).Select(static path => "/I" + path));
            options.Add(source);
            if (libraries.Length != 0)
            {
                options.Add("/link");
                options.AddRange(libraries.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(static path => "/LIBPATH:" + path));
            }
        }
        else
        {
            options.AddRange(installation.PreprocessorArguments);
            options.AddRange(["-O2", "-Wall", "-Wextra", "-Werror", "-isystem", installation.ServerIncludeDirectory,
                "-isystem", installation.IncludeDirectory, source, "-o", executable]);
        }

        await RunAsync(compiler, options, output, cancellationToken);
        string observations = await RunAsync(executable, [], output, cancellationToken);
        NativeBindingLayout layout = NativeBindingProbe.Read(catalog, observations);
        await File.WriteAllTextAsync(Path.Combine(output, "native-layout.txt"), observations, cancellationToken);
        string json = JsonSerializer.Serialize(layout, s_jsonOptions) + "\n";
        await File.WriteAllTextAsync(Path.Combine(output, "native-layout.json"), json, cancellationToken);
        Console.WriteLine($"PG{major}: measured {layout.Types.Count} native values and {layout.Types.Values.Sum(static type => type.Fields.Count)} fields.");
    }

    private static async Task<string> RunAsync(string program, IEnumerable<string> arguments, string directory, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(program)
        {
            WorkingDirectory = directory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in arguments) { start.ArgumentList.Add(argument); }

        using Process process = Process.Start(start) ?? throw new InvalidOperationException($"Cannot start {program}.");
        Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> errors = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); }

            throw;
        }

        string standardOutput = await output;
        string standardError = await errors;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"{program} exited with {process.ExitCode}: {standardOutput}{standardError}");
        }

        return standardOutput;
    }
}
