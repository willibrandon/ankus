namespace Ankus.Build.Tests;

public sealed partial class NativeBindingNativeTests
{
    /// <summary>
    /// Compiled C and C# agree on signed enums, high unsigned values and borrowed pointer bits.
    /// </summary>
    [TestMethod]
    public async Task CompiledEnumAndPointerFieldsPreserveNativeValues()
    {
        const string Declarations = """
            pub enum NodeTag { T_Invalid = 0, T_Leaf = 7, }
            pub mod State {
                pub type Type = ::core::ffi::c_int;
                pub const STATE_NEGATIVE: Type = -3;
                pub const STATE_READY: Type = 7;
            }
            pub mod Flags {
                pub type Type = ::core::ffi::c_uint;
                pub const FLAGS_HIGH: Type = 2147483648;
            }
            pub struct Leaf {
                pub type_: NodeTag,
                pub state: State::Type,
                pub flags: Flags::Type,
                pub borrowed: *mut ::core::ffi::c_void,
            }
            """;
        const string Headers = """
            #include <stdint.h>
            #include <stdio.h>
            #define PG_VERSION_NUM 180006
            typedef enum NodeTag { T_Invalid = 0, T_Leaf = 7 } NodeTag;
            typedef enum State { STATE_NEGATIVE = -3, STATE_READY = 7 } State;
            typedef enum Flags { FLAGS_HIGH = 2147483648U } Flags;
            typedef struct Leaf {
                NodeTag type;
                State state;
                Flags flags;
                void* borrowed;
            } Leaf;
            """;
        string directory = Path.Combine(Path.GetTempPath(), $"ankus-native-enums-{Guid.NewGuid():N}");
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
            Assert.AreEqual(new NativeBindingEnumLayout(4, true), layout.Enums["State"]);
            Assert.AreEqual(new NativeBindingEnumLayout(4, OperatingSystem.IsWindows()), layout.Enums["Flags"]);
            NativeBindingSource binding = NativeBindingCSharp.Generate(catalog, layout);
            const string Harness = """
                using Ankus.Postgres;
                public static class BindingAssertions
                {
                    public static unsafe long[] Run()
                    {
                        Leaf leaf = default;
                        leaf.state = State.STATE_NEGATIVE;
                        leaf.flags = Flags.FLAGS_HIGH;
                        leaf.borrowed = unchecked((nint)0xFEDCBA9876543210UL);
                        return [sizeof(Leaf), (byte*)&leaf.state - (byte*)&leaf,
                            (byte*)&leaf.flags - (byte*)&leaf, (byte*)&leaf.borrowed - (byte*)&leaf,
                            (int)leaf.state, unchecked((uint)leaf.flags), leaf.borrowed,
                            typeof(State).GetEnumUnderlyingType() == typeof(int) ? 1 : 0,
                            typeof(Flags).GetEnumUnderlyingType() == typeof(uint) ? 1 : 0];
                    }
                }
                """;
            long[] observed = GeneratedBindingCompilation.Run(binding, Harness, context.CancellationToken);
            Assert.AreSequenceEqual<long>(
                [IntPtr.Size == 8 ? 24 : 16, 4, 8, IntPtr.Size == 8 ? 16 : 12,
                    -3, 2147483648, IntPtr.Size == 8 ? unchecked((long)0xFEDCBA9876543210UL) : 0x76543210,
                    1, OperatingSystem.IsWindows() ? 0 : 1], observed);
            Assert.AreEqual(binding, NativeBindingCSharp.Generate(catalog, layout));
            NativeBindingLayout changed = layout with
            {
                Enums = new Dictionary<string, NativeBindingEnumLayout>(layout.Enums, StringComparer.Ordinal)
                {
                    ["Flags"] = new(4, !layout.Enums["Flags"].IsSigned),
                },
            };
            NativeBindingSource alternative = NativeBindingCSharp.Generate(catalog, changed);
            Assert.AreNotEqual(binding.AbiIdentity, alternative.AbiIdentity);
            Assert.AreNotEqual(binding.AssemblyName, alternative.AssemblyName);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
