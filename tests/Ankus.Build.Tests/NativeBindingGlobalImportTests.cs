namespace Ankus.Build.Tests;

public sealed partial class NativeBindingNativeTests
{
    /// <summary>
    /// Actual object imports retain only required operations and pure accessors leave native storage untouched.
    /// </summary>
    [TestMethod]
    public async Task NativeGlobalImportsSelectReferencedOperations()
    {
        const string Headers = "int current_value = -17; const int fixed_value = 73; extern int unavailable;";
        const string Consumer = """
            extern void *ankus_native_global_body_read_current_value(void);
            extern void *ankus_native_global_body_write_current_value(void);
            extern void *ankus_native_global_body_address_current_value(void);
            extern void *ankus_native_global_body_read_fixed_value(void);
            void *read_body(void) { return ankus_native_global_body_read_current_value(); }
            void *write_body(void) { return ankus_native_global_body_write_current_value(); }
            void *address_body(void) { return ankus_native_global_body_address_current_value(); }
            void *constant_body(void) { return ankus_native_global_body_read_fixed_value(); }
            """;
        const string Main = """
            #include <stdio.h>
            int main(void)
            {
                AnkusNativeCallBody read = ankus_native_global_body_read_current_value();
                AnkusNativeCallBody write = ankus_native_global_body_write_current_value();
                AnkusNativeCallBody address = ankus_native_global_body_address_current_value();
                if (read == NULL || write == NULL || address == NULL || current_value != -17) return 1;
                int value = 0;
                if (read(NULL, 0, &value, sizeof(value)) != 0 || value != -17) return 2;
                value = 127;
                AnkusNativeCallArgument argument = { &value, sizeof(value) };
                if (write(&argument, 1, NULL, 0) != 0 || current_value != 127) return 3;
                uintptr_t original = 0;
                if (address(NULL, 0, &original, sizeof(original)) != 0 || original != (uintptr_t)&current_value) return 4;
                current_value = -41;
                if (read(NULL, 0, &value, sizeof(value)) != 0 || value != -41) return 5;
                if (ankus_native_global_body_read_fixed_value()(NULL, 0, &value, sizeof(value)) != 0 || value != 73) return 6;
                puts("selected global operations preserve native identity and values");
                return 0;
            }
            """;
        string directory = Directory.CreateTempSubdirectory("ankus-import-globals-").FullName;
        try
        {
            NativeHeaderRecords records = await CollectCallRecordsAsync(Headers,
                [new("current_value", "current_value", false), new("fixed_value", "fixed_value", false), new("unavailable", "unavailable", false)], directory);
            byte[] image = await CompileNativeObjectAsync(Consumer);
            IReadOnlyList<NativeBindingGlobalAccess> selected = NativeBindingGlobalImports.Select(image);
            Assert.AreSequenceEqual<NativeBindingGlobalAccess>([new("current_value", NativeBindingGlobalOperation.Read),
                new("current_value", NativeBindingGlobalOperation.Write), new("current_value", NativeBindingGlobalOperation.Address),
                new("fixed_value", NativeBindingGlobalOperation.Read)], selected);
            string source = NativeBindingCallImports.Generate(records, "#define PG_VERSION_NUM 180006\n" + Headers, image);
            Assert.AreEqual("selected global operations preserve native identity and values\n", await RunImportedBodiesAsync(source, Main, image, directory));
            Assert.IsEmpty(NativeObjectSymbols.Read(image, NativeBindingCallImports.Prefix).Symbols);
        }
        finally
        {
            await DeleteDirectoryAsync(directory);
        }
    }

    /// <summary>
    /// Unsupported accesses and incompatible object targets fail before compilation and a corrected import remains usable.
    /// </summary>
    [TestMethod]
    public async Task NativeGlobalImportsRejectInvalidOperationsAndTargets()
    {
        const string Headers = "int current; const int fixed_value = 73; extern int incomplete[]; int function(void) { return 0; }";
        string directory = Directory.CreateTempSubdirectory("ankus-global-import-contract-").FullName;
        try
        {
            NativeHeaderRecords records = await CollectCallRecordsAsync(Headers,
                [new("current", "current", false), new("fixed_value", "fixed_value", false), new("incomplete", "incomplete", false), new("function", "function", true)], directory);
            byte[] valid = await CompileNativeObjectAsync(Import("read_current"));
            string expected = NativeBindingCallImports.Generate(records, Headers, valid);
            foreach (string suffix in new[] { "read_missing", "read_function", "write_fixed_value", "read_incomplete", "write_incomplete", "invalid_current", "read", "read_" })
            {
                byte[] image = await CompileNativeObjectAsync(Import(suffix));
                Assert.ThrowsExactly<FormatException>(() => NativeBindingCallImports.Generate(records, Headers, image), suffix);
            }

            NativeHeaderTarget target = records.Headers.Target;
            NativeHeaderTarget reversed = target with
            {
                IsLittleEndian = !target.IsLittleEndian
            };
            NativeHeaderRecords incompatible = records with
            {
                Headers = records.Headers with { Target = reversed },
                Graph = records.Graph with { Target = reversed }
            };
            Assert.ThrowsExactly<FormatException>(() => NativeBindingCallImports.Generate(incompatible, Headers, valid));
            string processor = target.RuntimeIdentifier.EndsWith("-x64", StringComparison.Ordinal) ? "x86_64" : "aarch64";
            byte[] foreign = await CompileNativeObjectAsync(Import("read_current"), processor + (OperatingSystem.IsWindows() ? "-unknown-linux-gnu" : "-pc-windows-msvc"));
            Assert.ThrowsExactly<FormatException>(() => NativeBindingCallImports.Generate(records, Headers, foreign));
            Assert.ThrowsExactly<FormatException>(() => NativeBindingGlobalSource.Bodies(records,
                [new("current", NativeBindingGlobalOperation.Read), new("current", NativeBindingGlobalOperation.Read)]));
            Assert.ThrowsExactly<FormatException>(() => NativeBindingGlobalSource.Bodies(records, [new("current", (NativeBindingGlobalOperation)999)]));
            Assert.AreEqual(expected, NativeBindingCallImports.Generate(records, Headers, valid));
        }
        finally
        {
            await DeleteDirectoryAsync(directory);
        }

        static string Import(string suffix) => $"extern void *ankus_native_global_body_{suffix}(void); void *entry(void) {{ return ankus_native_global_body_{suffix}(); }}";
    }
}
