using System.Globalization;

namespace Ankus.Build.Tests;

public sealed partial class NativeBindingNativeTests
{
    /// <summary>
    /// Actual imports select independent static targets with exact aggregate, integer and void ABI and no unused-handler dependency.
    /// </summary>
    [TestMethod]
    public async Task CallbackImportsSelectIndependentTargets()
    {
        const string Headers = """
            #include <stdint.h>
            typedef uint64_t (*Arithmetic)(uint64_t, int);
            typedef struct { uint64_t bits; double fraction; int values[3]; } Payload;
            typedef Payload (*Transform)(Payload);
            typedef void (*Action)(void);
            typedef int (*Unused)(double);
            extern Arithmetic arithmetic;
            extern Transform transform;
            extern Action action;
            extern Unused unavailable;
            """;
        const string Main = """
            #include <stdio.h>
            #include <stdlib.h>
            #define REQUIRE(value) do { if (!(value)) { fprintf(stderr, "line %d\n", __LINE__); exit(1); } } while (0)
            static int calls, dispatches;
            void ankus_dispatch_native_callback(AnkusManagedNativeCallback callback, const AnkusNativeCallArgument *arguments,
                size_t count, void *result, size_t size)
            {
                ++dispatches;
                REQUIRE(callback(arguments, count, result, size, &dispatches) == 0);
            }
            int ankus_managed_callback_11111111111111111111111111111111(const AnkusNativeCallArgument *arguments,
                size_t count, void *result, size_t size, void *context)
            {
                REQUIRE(context == &dispatches && count == 2 && arguments != NULL && result != NULL && size == sizeof(uint64_t));
                REQUIRE(arguments[0].size == sizeof(uint64_t) && arguments[1].size == sizeof(int));
                uint64_t value = *(const uint64_t *)arguments[0].data + (uint64_t)*(const int *)arguments[1].data;
                memcpy(result, &value, sizeof(value));
                ++calls;
                return 0;
            }
            int ankus_managed_callback_22222222222222222222222222222222(const AnkusNativeCallArgument *arguments,
                size_t count, void *result, size_t size, void *context)
            {
                REQUIRE(context == &dispatches && count == 2 && size == sizeof(uint64_t));
                REQUIRE(arguments[0].size == sizeof(uint64_t) && arguments[1].size == sizeof(int));
                uint64_t value = *(const uint64_t *)arguments[0].data ^ ((uint64_t)*(const int *)arguments[1].data << 8);
                memcpy(result, &value, sizeof(value));
                ++calls;
                return 0;
            }
            int ankus_managed_callback_33333333333333333333333333333333(const AnkusNativeCallArgument *arguments,
                size_t count, void *result, size_t size, void *context)
            {
                REQUIRE(context == &dispatches && count == 1 && size == sizeof(Payload) && arguments[0].size == sizeof(Payload));
                Payload value = *(const Payload *)arguments[0].data;
                value.bits ^= UINT64_MAX;
                value.fraction *= 3;
                value.values[2] += 7;
                memcpy(result, &value, sizeof(value));
                ++calls;
                return 0;
            }
            int ankus_managed_callback_44444444444444444444444444444444(const AnkusNativeCallArgument *arguments,
                size_t count, void *result, size_t size, void *context)
            {
                REQUIRE(context == &dispatches && arguments == NULL && count == 0 && result == NULL && size == 0);
                ++calls;
                return 0;
            }
            int main(void)
            {
                REQUIRE(__FIRST__(NULL) == NULL);
                Arithmetic first = __FIRST__(ankus_managed_callback_11111111111111111111111111111111);
                Arithmetic second = __SECOND__(ankus_managed_callback_22222222222222222222222222222222);
                Transform aggregate = __AGGREGATE__(ankus_managed_callback_33333333333333333333333333333333);
                Action empty = __EMPTY__(ankus_managed_callback_44444444444444444444444444444444);
                REQUIRE(first != NULL && second != NULL && aggregate != NULL && empty != NULL);
                REQUIRE(first != second && first == __FIRST__(ankus_managed_callback_11111111111111111111111111111111) && calls == 0 && dispatches == 0);
                REQUIRE(__FIRST__(NULL) == NULL && __FIRST__(ankus_managed_callback_22222222222222222222222222222222) == NULL);
                REQUIRE(first(UINT64_C(0xFEDCBA9876543210), 7) == UINT64_C(0xFEDCBA9876543217));
                REQUIRE(second(UINT64_C(0xFEDCBA9876543210), 7) == UINT64_C(0xFEDCBA9876543510));
                REQUIRE(first(20, 22) == 42 && calls == 3 && dispatches == 3);
                Payload value = { UINT64_C(0xFEDCBA9876543210), 1.25, { -9, 14, 71 } };
                Payload changed = aggregate(value);
                REQUIRE(changed.bits == UINT64_C(0x0123456789ABCDEF) && changed.fraction == 3.75);
                REQUIRE(changed.values[0] == -9 && changed.values[1] == 14 && changed.values[2] == 78);
                REQUIRE(value.bits == UINT64_C(0xFEDCBA9876543210) && value.fraction == 1.25 && value.values[2] == 71);
                empty();
                REQUIRE(calls == 5 && dispatches == 5);
                puts("independent static callbacks preserve native values and identity");
                return 0;
            }
            """;
        string directory = Directory.CreateTempSubdirectory("ankus-callback-import-").FullName;
        try
        {
            NativeHeaderRecords records = await CollectCallRecordsAsync(Headers,
                [new("arithmetic", "arithmetic", false), new("transform", "transform", false), new("action", "action", false),
                    new("unavailable", "unavailable", false)], directory);
            Dictionary<string, NativeBindingIndirectCall> signatures = NativeBindingIndirectModel.Describe(records.Graph).ToDictionary(static call => call.Name!);
            NativeBindingCallbackImport[] callbacks =
            [
                new(signatures["Arithmetic"].FunctionType, new('1', 32)), new(signatures["Arithmetic"].FunctionType, new('2', 32)),
                new(signatures["Transform"].FunctionType, new('3', 32)), new(signatures["Action"].FunctionType, new('4', 32)),
            ];
            string[] accessors = [.. callbacks.Select(static callback => NativeBindingCallbackImports.Prefix +
                callback.Signature.ToString(CultureInfo.InvariantCulture) + "_" + callback.Identity)];
            byte[] image = await CompileNativeObjectAsync(string.Join("\n", accessors.Select((name, index) =>
                "extern void *" + name + "(void *); void *select_" + index.ToString(CultureInfo.InvariantCulture) + "(void) { return " + name + "((void *)0); }")));
            IReadOnlyList<NativeBindingCallbackImport> selected = NativeBindingCallbackImports.Select(image);
            Assert.AreSequenceEqual(callbacks.OrderBy(static callback => callback.Signature).ThenBy(static callback => callback.Identity, StringComparer.Ordinal), selected);
            Assert.ThrowsExactly<NotSupportedException>(() => ((IList<NativeBindingCallbackImport>)selected).Clear());
            string source = NativeBindingCallImports.Generate(records, "#define PG_VERSION_NUM 180006\n" + Headers, image);
            string main = Main.Replace("__FIRST__", accessors[0], StringComparison.Ordinal).Replace("__SECOND__", accessors[1], StringComparison.Ordinal)
                .Replace("__AGGREGATE__", accessors[2], StringComparison.Ordinal).Replace("__EMPTY__", accessors[3], StringComparison.Ordinal);
            Assert.AreEqual("independent static callbacks preserve native values and identity\n", await RunImportedBodiesAsync(source, main, image, directory));
            byte[] defined = await CompileNativeObjectAsync("void *" + accessors[0] + "(void *target) { (void)target; return (void *)0; }");
            Assert.IsEmpty(NativeBindingCallbackImports.Select(defined));
            string none = NativeBindingCallImports.Generate(records, "#define PG_VERSION_NUM 180006\n" + Headers, defined);
            Assert.AreEqual("", await RunImportedBodiesAsync(none, "int main(void) { return 0; }", defined, directory));
        }
        finally
        {
            await DeleteDirectoryAsync(directory);
        }
    }

    /// <summary>
    /// Malformed imports, incompatible signatures and invalid unselected roots fail before generating native wrappers and permit corrected retries.
    /// </summary>
    [TestMethod]
    public async Task CallbackImportsRejectIncompatibleContracts()
    {
        string directory = Directory.CreateTempSubdirectory("ankus-callback-import-errors-").FullName;
        try
        {
            NativeHeaderRecords records = await CollectCallRecordsAsync(IndirectModelHeaders,
                [new("registry", "registry", false), new("current", "current", false), new("ordinary", "ordinary", true)], directory);
            IReadOnlyList<NativeBindingIndirectCall> calls = NativeBindingIndirectModel.Describe(records.Graph);
            int signature = calls.First(static call => call.CanInvoke).FunctionType;
            string number = signature.ToString(CultureInfo.InvariantCulture);
            string identity = new('a', 32);
            var callback = new NativeBindingCallbackImport(signature, identity);
            byte[] valid = await CompileNativeObjectAsync(Import(number + "_" + identity));
            string expected = NativeBindingCallImports.Generate(records, IndirectModelHeaders, valid);
            string[] invalid = ["", number, "0" + number + "_" + identity, "2147483648_" + identity, number + "_", number + "_" + new string('a', 31),
                number + "_" + new string('a', 33), number + "_" + new string('A', 32), number + "_" + new string('z', 32),
                records.Graph.Types.Count.ToString(CultureInfo.InvariantCulture) + "_" + identity,
                records.Graph.Roots["current"].ToString(CultureInfo.InvariantCulture) + "_" + identity,
                .. calls.Where(static call => !call.CanInvoke).Select(call => call.FunctionType.ToString(CultureInfo.InvariantCulture) + "_" + identity)];
            foreach (string suffix in invalid)
            {
                byte[] image = await CompileNativeObjectAsync(Import(suffix));
                Assert.ThrowsExactly<FormatException>(() => NativeBindingCallImports.Generate(records, IndirectModelHeaders, image), suffix);
            }

            Assert.ThrowsExactly<FormatException>(() => NativeBindingCallbackSource.Generate(records, [callback, callback]));
            Assert.ThrowsExactly<FormatException>(() => NativeBindingCallbackSource.Generate(records,
                [callback, new(calls.Last(static call => call.CanInvoke).FunctionType, identity)]));
            Assert.ThrowsExactly<FormatException>(() => NativeBindingCallbackSource.Generate(records, [new(signature, "invalid")]));
            Assert.ThrowsExactly<ArgumentNullException>(() => NativeBindingCallbackSource.Generate(null!, [callback]));
            Assert.ThrowsExactly<ArgumentNullException>(() => NativeBindingCallbackSource.Generate(records, null!));
            Assert.AreEqual("", NativeBindingCallbackSource.Generate(records, []));
            Dictionary<string, int> roots = records.Graph.Roots.ToDictionary();
            roots["ordinary"] = records.Graph.Types.Count;
            NativeHeaderRecords broken = records with { Graph = records.Graph with { Roots = roots } };
            Assert.ThrowsExactly<FormatException>(() => NativeBindingCallImports.Generate(broken, IndirectModelHeaders, valid));
            Assert.ThrowsExactly<FormatException>(() => NativeBindingCallbackSource.Generate(broken, []));
            string foreignTarget = OperatingSystem.IsWindows() ? "x86_64-unknown-linux-gnu" : "x86_64-pc-windows-msvc";
            byte[] foreign = await CompileNativeObjectAsync(Import(number + "_" + identity), foreignTarget);
            Assert.ThrowsExactly<FormatException>(() => NativeBindingCallImports.Generate(records, IndirectModelHeaders, foreign));
            Assert.AreEqual(expected, NativeBindingCallImports.Generate(records, IndirectModelHeaders, valid));
        }
        finally
        {
            await DeleteDirectoryAsync(directory);
        }

        static string Import(string suffix)
            => "extern void *" + NativeBindingCallbackImports.Prefix + suffix + "(void *); void *select_callback(void) { return " +
                NativeBindingCallbackImports.Prefix + suffix + "((void *)0); }";
    }
}
