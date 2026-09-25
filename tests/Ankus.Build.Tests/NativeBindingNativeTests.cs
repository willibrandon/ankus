using System.Diagnostics;

namespace Ankus.Build.Tests;

/// <summary>
/// Compiles a native probe against independent C declarations, including anonymous embedded values.
/// </summary>
/// <param name="context">The current test's cancellation context.</param>
[TestClass]
public sealed class NativeBindingNativeTests(TestContext context)
{
    /// <summary>
    /// The emitted program measures anonymous unions, array elements and flexible tail padding through actual C paths.
    /// </summary>
    [TestMethod]
    public async Task CompiledProbeObservesAnonymousValuesAndFlexiblePadding()
    {
        const string Declarations = """
            pub enum NodeTag { T_Invalid = 0, T_Leaf = 7, }
            pub struct Node { pub type_: NodeTag, }
            pub struct Leaf {
                pub type_: NodeTag,
                pub meta: Metadata,
                pub values: [u16; 3usize],
                pub tail: __IncompleteArrayField<u8>,
            }
            pub struct Metadata { pub payload: Payload, }
            pub union Payload {
                pub number: u32,
                pub pair: [u16; 2usize],
            }
            """;
        const string Headers = """
            #include <stdint.h>
            #include <stdio.h>
            #define PG_VERSION_NUM 180006
            typedef enum NodeTag { T_Invalid = 0, T_Leaf = 7 } NodeTag;
            typedef struct Node { NodeTag type; } Node;
            typedef struct Leaf {
                NodeTag type;
                struct { union { uint32_t number; uint16_t pair[2]; } payload; } meta;
                uint16_t values[3];
                unsigned char tail[];
            } Leaf;
            """;
        string directory = Path.Combine(Path.GetTempPath(), $"ankus-native-probe-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            NativeBindingCatalog catalog = NativeBindingParser.Parse(Declarations, 18);
            string source = Path.Combine(directory, "probe.c");
            string executable = Path.Combine(directory, OperatingSystem.IsWindows() ? "probe.exe" : "probe");
            await File.WriteAllTextAsync(source, NativeBindingProbe.GenerateSource(catalog, Headers), context.CancellationToken);
            string compiler = OperatingSystem.IsWindows() ? "clang-cl.exe" : "cc";
            string[] arguments = OperatingSystem.IsWindows()
                ? ["/nologo", "/std:c11", "/W4", "/WX", "/Fe" + executable, "/Fo" + Path.ChangeExtension(executable, ".obj"), source]
                : ["-std=c11", "-Wall", "-Wextra", "-Werror", source, "-o", executable];
            await RunAsync(compiler, arguments, directory);
            string observations = await RunAsync(executable, [], directory);
            NativeBindingLayout layout = NativeBindingProbe.Read(catalog, observations);
            Assert.AreEqual(180006, layout.PostgresVersion);
            Assert.AreEqual(IntPtr.Size, layout.PointerSize);
            Assert.AreEqual(OperatingSystem.IsWindows() ? 4 : IntPtr.Size, layout.LongSize);
            Assert.AreEqual(BitConverter.IsLittleEndian, layout.IsLittleEndian);
            Assert.AreSequenceEqual<string>(["Leaf", "Metadata", "Node", "Payload"], layout.Types.Keys);
            Assert.AreEqual(16, layout.Types["Leaf"].Size);
            Assert.AreEqual(4, layout.Types["Leaf"].Alignment);
            Assert.AreEqual(new NativeBindingFieldLayout(4, 4, 4, 4), layout.Types["Leaf"].Fields["meta"]);
            Assert.AreEqual(new NativeBindingFieldLayout(8, 6, 2, 2), layout.Types["Leaf"].Fields["values"]);
            Assert.AreEqual(new NativeBindingFieldLayout(14, 0, 1, 1), layout.Types["Leaf"].Fields["tail"]);
            Assert.AreEqual(new NativeBindingFieldLayout(0, 4, 4, 4), layout.Types["Metadata"].Fields["payload"]);
            Assert.AreEqual(4, layout.Types["Payload"].Size);
            Assert.AreEqual(new NativeBindingFieldLayout(0, 4, 4, 4), layout.Types["Payload"].Fields["number"]);
            Assert.AreEqual(new NativeBindingFieldLayout(0, 4, 2, 2), layout.Types["Payload"].Fields["pair"]);
            Assert.AreEqual(new NativeBindingFieldLayout(0, 4, 4, 4), layout.Types["Node"].Fields["type_"]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private async Task<string> RunAsync(string executable, string[] arguments, string directory)
    {
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = directory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in arguments) { start.ArgumentList.Add(argument); }

        using Process process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {executable}.");
        Task<string> output = process.StandardOutput.ReadToEndAsync(context.CancellationToken);
        Task<string> error = process.StandardError.ReadToEndAsync(context.CancellationToken);
        try
        {
            await process.WaitForExitAsync(context.CancellationToken);
        }
        catch
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); }

            throw;
        }

        string standardOutput = await output;
        string standardError = await error;
        Assert.AreEqual(0, process.ExitCode, $"{executable}: {standardOutput}{standardError}");
        return standardOutput;
    }
}
