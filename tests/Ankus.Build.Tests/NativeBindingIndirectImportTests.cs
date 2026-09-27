using System.Globalization;

namespace Ankus.Build.Tests;

public sealed partial class NativeBindingNativeTests
{
    /// <summary>
    /// Actual object imports select only used indirect signatures and their pure accessors preserve live target selection.
    /// </summary>
    [TestMethod]
    public async Task IndirectImportsSelectReferencedSignatures()
    {
        const string Headers = """
            typedef int (*Arithmetic)(int);
            typedef void (*Unused)(double);
            int calls;
            int triple(int value) { ++calls; return value * 3; }
            Arithmetic available = triple;
            extern Unused unavailable;
            """;
        const string Main = """
            #include <stdio.h>
            int main(void)
            {
                AnkusNativeCallBody body = __ACCESSOR__();
                if (body == NULL || calls != 0) return 1;
                Arithmetic target = available;
                int value = 14, result = 0;
                AnkusNativeCallArgument arguments[] = {{ &target, sizeof(target) }, { &value, sizeof(value) }};
                if (body(arguments, 2, &result, sizeof(result)) != ANKUS_CALL_OK || result != 42 || calls != 1) return 2;
                value = -11;
                if (body(arguments, 2, &result, sizeof(result)) != ANKUS_CALL_OK || result != -33 || calls != 2) return 3;
                puts("selected indirect accessor preserves target and values");
                return 0;
            }
            """;
        string directory = Directory.CreateTempSubdirectory("ankus-indirect-import-").FullName;
        try
        {
            NativeHeaderRecords records = await CollectCallRecordsAsync(Headers,
                [new("available", "available", false), new("unavailable", "unavailable", false)], directory);
            int signature = NativeBindingIndirectModel.Describe(records.Graph).Single(static call => call.Name == "Arithmetic").FunctionType;
            string accessor = NativeBindingIndirectImports.Prefix + signature.ToString(CultureInfo.InvariantCulture);
            byte[] image = await CompileNativeObjectAsync("extern void *" + accessor + "(void); void *selected(void) { return " + accessor + "(); }");
            Assert.AreSequenceEqual<int>([signature], NativeBindingIndirectImports.Select(image));
            string source = NativeBindingCallImports.Generate(records, "#define PG_VERSION_NUM 180006\n" + Headers, image);
            Assert.AreEqual("selected indirect accessor preserves target and values\n",
                await RunImportedBodiesAsync(source, Main.Replace("__ACCESSOR__", accessor, StringComparison.Ordinal), image, directory));
            byte[] empty = await CompileNativeObjectAsync("void *" + accessor + "(void) { return (void *)0; }");
            Assert.IsEmpty(NativeBindingIndirectImports.Select(empty));
            string none = NativeBindingCallImports.Generate(records, "#define PG_VERSION_NUM 180006\n" + Headers, empty);
            Assert.AreEqual("", await RunImportedBodiesAsync(none, "int main(void) { return calls; }", empty, directory));
        }
        finally
        {
            await DeleteDirectoryAsync(directory);
        }
    }

    /// <summary>
    /// Malformed names, unsupported signatures, invalid unselected roots and foreign objects fail before native publication.
    /// </summary>
    [TestMethod]
    public async Task IndirectImportsRejectInvalidSignaturesAndTargets()
    {
        string directory = Directory.CreateTempSubdirectory("ankus-indirect-import-errors-").FullName;
        try
        {
            NativeHeaderRecords records = await CollectCallRecordsAsync(IndirectModelHeaders,
                [new("registry", "registry", false), new("current", "current", false), new("ordinary", "ordinary", true)], directory);
            IReadOnlyList<NativeBindingIndirectCall> calls = NativeBindingIndirectModel.Describe(records.Graph);
            int valid = calls.First(static call => call.CanInvoke).FunctionType;
            byte[] selected = await CompileNativeObjectAsync(Import(valid.ToString(CultureInfo.InvariantCulture)));
            string expected = NativeBindingCallImports.Generate(records, IndirectModelHeaders, selected);
            string[] invalid = ["", "unknown", "_1", "00" + valid.ToString(CultureInfo.InvariantCulture), "2147483648",
                records.Graph.Types.Count.ToString(CultureInfo.InvariantCulture), records.Graph.Roots["current"].ToString(CultureInfo.InvariantCulture),
                .. calls.Where(static call => !call.CanInvoke).Select(static call => call.FunctionType.ToString(CultureInfo.InvariantCulture))];
            foreach (string suffix in invalid)
            {
                byte[] image = await CompileNativeObjectAsync(Import(suffix));
                Assert.ThrowsExactly<FormatException>(() => NativeBindingCallImports.Generate(records, IndirectModelHeaders, image), suffix);
            }

            NativeHeaderTarget target = records.Headers.Target;
            string foreignFormat = target.RuntimeIdentifier.StartsWith("win-", StringComparison.Ordinal) ? "x86_64-unknown-linux-gnu" : "x86_64-pc-windows-msvc";
            byte[] foreign = await CompileNativeObjectAsync(Import(valid.ToString(CultureInfo.InvariantCulture)), foreignFormat);
            Assert.ThrowsExactly<FormatException>(() => NativeBindingCallImports.Generate(records, IndirectModelHeaders, foreign));
            Dictionary<string, int> roots = records.Graph.Roots.ToDictionary();
            roots["ordinary"] = records.Graph.Types.Count;
            NativeHeaderRecords broken = records with { Graph = records.Graph with { Roots = roots } };
            Assert.ThrowsExactly<FormatException>(() => NativeBindingCallImports.Generate(broken, IndirectModelHeaders, selected));
            Assert.AreEqual(expected, NativeBindingCallImports.Generate(records, IndirectModelHeaders, selected));
        }
        finally
        {
            await DeleteDirectoryAsync(directory);
        }

        static string Import(string suffix)
            => "extern void *" + NativeBindingIndirectImports.Prefix + suffix + "(void); void *select_body(void) { return " +
                NativeBindingIndirectImports.Prefix + suffix + "(); }";
    }
}
