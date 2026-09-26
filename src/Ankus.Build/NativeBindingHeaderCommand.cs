using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using Ankus.PgConfig;

namespace Ankus.Build;

/// <summary>
/// Collects semantic native types from the selected PostgreSQL headers with a Clang frontend.
/// </summary>
internal static class NativeBindingHeaderCommand
{
    private static readonly JsonSerializerOptions s_jsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// Writes a target-specific native type contract after complete frontend and identity validation.
    /// </summary>
    internal static async Task<NativeHeaderCatalog> RunAsync(string[] arguments, CancellationToken cancellationToken = default)
    {
        if (arguments.Length is < 4 or > 8)
        {
            throw new ArgumentException("Expected binding-header-types <symbol-list> <major> <pg_config> <output-directory> [clang] [windows-library-directories] [runtime-identifier] [target-triple].", nameof(arguments));
        }

        int major = int.Parse(arguments[1], NumberStyles.None, CultureInfo.InvariantCulture);
        NativeBindingRawCatalog raw = NativeBindingResources.ReadRawCatalog(major);
        string[] names = [.. (await File.ReadAllLinesAsync(arguments[0], cancellationToken)).Where(static line => !string.IsNullOrWhiteSpace(line)).Select(static line => line.Trim())];
        NativeHeaderRequest[] requests = [.. names.Select(name => Request(raw, name))];
        string source = NativeBindingHeaderParser.GenerateSource(
            NativeBindingHeaderTarget.GenerateSource(NativeBindingResources.ReadHeaders(major), major), requests);
        PostgresInstallation installation = arguments[2].Length == 0 ? await PostgresInstallation.DiscoverAsync(major, cancellationToken)
            : await PostgresInstallation.CreateAsync(arguments[2], cancellationToken);
        if (installation.Version.Major != major) { throw new InvalidOperationException("The selected installation has the wrong PostgreSQL major."); }

        string output = Path.GetFullPath(arguments[3]);
        Directory.CreateDirectory(output);
        string file = Path.Combine(output, "native-header-types.c");
        string ast = Path.Combine(output, "native-header-types.ast.json");
        await File.WriteAllTextAsync(file, source, cancellationToken);
        await InspectAsync(installation, arguments, file, ast, output, cancellationToken);
        await using FileStream stream = File.OpenRead(ast);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, new JsonDocumentOptions { MaxDepth = 512 }, cancellationToken);
        IReadOnlyDictionary<string, NativeHeaderSymbol> symbols = NativeBindingHeaderParser.Read(document.RootElement, requests);
        NativeHeaderTarget target = NativeBindingHeaderTarget.Read(document.RootElement, major);
        string runtime = arguments.Length >= 7 && arguments[6].Length != 0 ? arguments[6] : RuntimeInformation.RuntimeIdentifier;
        if (target.RuntimeIdentifier != runtime) { throw new InvalidOperationException("Native header types do not match the requested runtime ABI."); }

        string checks = Path.Combine(output, "native-header-checks.c");
        await File.WriteAllTextAsync(checks, NativeBindingHeaderParser.GenerateChecks(NativeBindingResources.ReadHeaders(major), symbols), cancellationToken);
        await InspectAsync(installation, arguments, checks, Path.Combine(output, "native-header-checks.txt"), output, cancellationToken, dumpAst: false);
        var catalog = new NativeHeaderCatalog(target, symbols);
        await File.WriteAllTextAsync(Path.Combine(output, "native-header-types.json"), JsonSerializer.Serialize(catalog, s_jsonOptions) + "\n", cancellationToken);
        Console.WriteLine($"PG{major}: collected {symbols.Count} native header symbol types with Clang {target.ClangMajor} for {target.RuntimeIdentifier}.");
        return catalog;
    }

    /// <summary>
    /// Inspects declarations and constants using the selected header command's exact frontend arguments.
    /// </summary>
    internal static Task InspectAsync(PostgresInstallation installation, string[] arguments, string source, string observations,
        string output, CancellationToken cancellationToken, bool dumpAst = true, string? serializedAst = null, bool inspectBodies = false)
    {
        if (dumpAst && serializedAst is not null) { throw new ArgumentException("Select one native AST output format.", nameof(serializedAst)); }

        string compiler = arguments.Length >= 5 && arguments[4].Length != 0 ? arguments[4] : OperatingSystem.IsWindows() ? "clang-cl.exe" : "clang";
        var options = new List<string>();
        if (OperatingSystem.IsWindows())
        {
            options.AddRange(["/nologo", "/std:c11", "/WX", "/Zs", "/I" + installation.ServerIncludeDirectory, "/I" + installation.IncludeDirectory,
                "/I" + Path.Combine(installation.ServerIncludeDirectory, "port", "win32"), "/I" + Path.Combine(installation.ServerIncludeDirectory, "port", "win32_msvc")]);
            string libraries = arguments.Length >= 6 ? arguments[5] : "";
            options.AddRange(WindowsToolchain.GetIncludeDirectories(libraries).Select(static path => "/I" + path));
        }
        else
        {
            options.AddRange(installation.PreprocessorArguments);
            options.AddRange(["-std=c11", "-Wall", "-Wextra", "-Werror", "-fsyntax-only", "-isystem", installation.ServerIncludeDirectory,
                "-isystem", installation.IncludeDirectory]);
        }

        if (arguments.Length == 8 && arguments[7].Length != 0) { options.Add("--target=" + arguments[7]); }

        if (dumpAst) { options.AddRange(["-Xclang", "-ast-dump=json"]); }

        if (serializedAst is not null) { options.AddRange(["-Xclang", "-emit-pch", "-Xclang", "-o", "-Xclang", serializedAst]); }

        return CompileAsync(compiler, [.. options, source], observations, output, cancellationToken, inspectBodies);
    }

    /// <summary>
    /// Captures a frontend's structured output with a finite memory-independent byte limit.
    /// </summary>
    internal static async Task CompileAsync(string compiler, IReadOnlyList<string> arguments, string ast, string directory,
        CancellationToken cancellationToken, bool inspectBodies = false)
    {
        var start = new ProcessStartInfo(compiler)
        {
            WorkingDirectory = directory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        // Metadata collection only needs declarations. Generated call validation must also check bodies.
        if (!inspectBodies)
        {
            start.ArgumentList.Add("-Xclang");
            start.ArgumentList.Add("-skip-function-bodies");
        }

        foreach (string argument in arguments) { start.ArgumentList.Add(argument); }

        cancellationToken.ThrowIfCancellationRequested();
        await using FileStream output = File.Create(ast);
        using Process process = Process.Start(start) ?? throw new InvalidOperationException($"Cannot start {compiler}.");
        Task copy = CopyAsync(process.StandardOutput.BaseStream, output, 512 * 1024 * 1024, cancellationToken);
        Task<string> errors = process.StandardError.ReadToEndAsync(cancellationToken);
        Task exit = process.WaitForExitAsync(cancellationToken);
        try
        {
            await Task.WhenAny(copy, exit);
            if (copy.IsCompleted) { await copy; }

            await exit;
            await copy;
            string error = await errors;
            if (process.ExitCode != 0)
            {
                if (inspectBodies)
                {
                    output.Position = 0;
                    byte[] diagnostic = new byte[(int)Math.Min(output.Length, 16 * 1024)];
                    await output.ReadExactlyAsync(diagnostic, cancellationToken);
                    error = System.Text.Encoding.UTF8.GetString(diagnostic) + error;
                }

                throw new InvalidOperationException($"{compiler} exited with {process.ExitCode}: {error}");
            }
        }
        catch
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); }

            await process.WaitForExitAsync(CancellationToken.None);
            // Observe stream cancellation/failure before disposing their owned buffers, preserving the original error.
            await Task.WhenAll(copy, errors, exit).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            throw;
        }
    }

    /// <summary>
    /// Copies compiler output without accepting an unbounded AST allocation.
    /// </summary>
    internal static async Task CopyAsync(Stream source, Stream destination, int maximumBytes, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumBytes);
        byte[] buffer = new byte[64 * 1024];
        long total = 0;
        int count;
        while ((count = await source.ReadAsync(buffer, cancellationToken)) != 0)
        {
            total += count;
            if (total > maximumBytes) { throw new InvalidDataException("Native compiler AST exceeds the supported byte limit."); }

            await destination.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
        }
    }

    private static NativeHeaderRequest Request(NativeBindingRawCatalog raw, string name)
    {
        if (raw.Functions.TryGetValue(name, out NativeBindingFunction? function))
        {
            return new(name, function.NativeSymbol == name + "__pgrx_cshim" ? name : function.NativeSymbol, true);
        }

        if (raw.Globals.TryGetValue(name, out NativeBindingGlobal? global)) { return new(name, global.NativeSymbol, false); }

        throw new FormatException($"Unknown native function or global '{name}'.");
    }
}

/// <summary>
/// Couples native type facts with the exact selected-header compiler target.
/// </summary>
/// <param name="Target">The observed header and compiler identity.</param>
/// <param name="Symbols">Every requested function and global, in ordinal order.</param>
internal sealed record NativeHeaderCatalog(NativeHeaderTarget Target, IReadOnlyDictionary<string, NativeHeaderSymbol> Symbols);
