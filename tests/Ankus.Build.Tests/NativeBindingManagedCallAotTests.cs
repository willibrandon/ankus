using System.Xml.Linq;

namespace Ankus.Build.Tests;

public sealed partial class NativeBindingNativeTests
{
    /// <summary>
    /// Native AOT resolves real LibraryImport accessors, trims unused methods and executes the exact selected bodies.
    /// </summary>
    [TestMethod]
    public async Task PublishedManagedCallsUseNativeAccessors()
    {
        const string Headers = """
            typedef struct Payload { unsigned char bytes[8192]; } Payload;
            int observed;
            int calls;
            void consume(Payload value) { ++calls; observed = value.bytes[0] * 3 + value.bytes[8191]; }
            int checksum(void) { return observed + calls * 1000; }
            extern int unavailable(int value);
            """;
        const string Imports = """
            extern void *ankus_native_body_consume(void);
            extern void *ankus_native_body_checksum(void);
            void *first(void) { return ankus_native_body_consume(); }
            void *second(void) { return ankus_native_body_checksum(); }
            """;
        const string Main = """
            public static class Program
            {
                public static void Main()
                {
                    using NativeCallTestBridge.Scope scope = new();
                    Payload payload = default;
                    payload.bytes[0] = 7;
                    payload.bytes[8191] = 201;
                    int rejected = 0;
                    scope.RejectCall = true;
                    try { NativeMethods.consume(payload); }
                    catch (PgException error) when (error.SqlState == "22023" && error.Message == "native call café"
                        && error.Detail == "detail naïve" && error.Hint == "hint déjà") { rejected++; }
                    scope.RejectCall = false;
                    NativeMethods.consume(payload);
                    int result = NativeMethods.checksum();
                    Console.WriteLine($"{rejected},{result},{payload.bytes[0]},{payload.bytes[8191]},{scope.Validations},{scope.Invocations}");
                }
            }
            """;
        string directory = Directory.CreateTempSubdirectory("ankus-managed-call-aot-").FullName;
        try
        {
            string[] names = ["consume", "checksum", "unavailable"];
            NativeHeaderRecords records = await CollectCallRecordsAsync(Headers,
                [.. names.Select(static name => new NativeHeaderRequest(name, name, true))], directory);
            NativeBindingSource binding = NativeBindingRecordCSharp.Generate(records, names);
            byte[] imported = await CompileNativeObjectAsync(Imports);
            string native = NativeBindingCallImports.Generate(records, "#define PG_VERSION_NUM 180006\n" + Headers, imported);
            string file = Path.Combine(directory, "calls.c");
            string nativeObject = Path.Combine(directory, OperatingSystem.IsWindows() ? "calls.obj" : "calls.o");
            await File.WriteAllTextAsync(file, native, context.CancellationToken);
            string compiler = OperatingSystem.IsWindows() ? "cl.exe" : "clang";
            string[] arguments = OperatingSystem.IsWindows()
                ? ["/nologo", "/std:c11", "/W4", "/WX", "/O2", "/c", "/Fo" + nativeObject, file]
                : ["-std=c11", "-Wall", "-Wextra", "-Werror", "-O2", "-fPIC", "-c", file, "-o", nativeObject];
            await RunAsync(compiler, arguments, directory);
            await File.WriteAllTextAsync(Path.Combine(directory, "Bindings.cs"), binding.Source, context.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(directory, "Program.cs"),
                NativeBindingManagedCallHarness.Source.Replace("__EXPECTED_IDENTITY__", binding.AbiIdentity, StringComparison.Ordinal) + "\n" + Main,
                context.CancellationToken);
            var project = new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
                new XElement("PropertyGroup", new XElement("OutputType", "Exe"), new XElement("TargetFramework", "net10.0"),
                    new XElement("PublishAot", "true"), new XElement("AllowUnsafeBlocks", "true"), new XElement("Nullable", "enable"),
                    new XElement("TreatWarningsAsErrors", "true"), new XElement("MSTestAnalysisMode", "All")),
                new XElement("ItemGroup",
                    new XElement("Reference", new XAttribute("Include", "Ankus.Runtime"), new XElement("HintPath", typeof(NativeRawCall).Assembly.Location)),
                    new XElement("Reference", new XAttribute("Include", "System.Formats.Cbor"),
                        new XElement("HintPath", typeof(System.Formats.Cbor.CborReader).Assembly.Location)),
                    new XElement("DirectPInvoke", new XAttribute("Include", "Ankus.NativeBodies")),
                    new XElement("NativeLibrary", new XAttribute("Include", nativeObject))));
            string projectFile = Path.Combine(directory, "Calls.csproj");
            await File.WriteAllTextAsync(projectFile, project.ToString(), context.CancellationToken);
            string output = Path.Combine(directory, "published");
            await RunAsync("dotnet", ["publish", projectFile, "-c", "Release", "-o", output], directory);
            string executable = Path.Combine(output, OperatingSystem.IsWindows() ? "Calls.exe" : "Calls");
            Assert.AreEqual("1,1222,7,201,3,3\n", (await RunAsync(executable, [], directory)).ReplaceLineEndings("\n"));
            string objectName = OperatingSystem.IsWindows() ? "Calls.obj" : "Calls.o";
            string compiled = Assert.ContainsSingle(Directory.EnumerateFiles(Path.Combine(directory, "obj"), objectName, SearchOption.AllDirectories));
            byte[] image = await File.ReadAllBytesAsync(compiled, context.CancellationToken);
            NativeObjectImports actual = NativeObjectSymbols.Read(image, NativeBindingCallImports.Prefix);
            Assert.AreSequenceEqual<string>(["ankus_native_body_checksum", "ankus_native_body_consume"], actual.Symbols);
            Assert.AreEqual(native, NativeBindingCallImports.Generate(records, "#define PG_VERSION_NUM 180006\n" + Headers, image));
        }
        finally { await DeleteDirectoryAsync(directory); }
    }
}
