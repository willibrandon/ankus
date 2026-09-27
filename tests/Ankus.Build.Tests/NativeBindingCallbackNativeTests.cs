using System.Globalization;

namespace Ankus.Build.Tests;

public sealed partial class NativeBindingNativeTests
{
    /// <summary>
    /// Compiler-specific native callbacks retain wide and over-aligned values, empty types, adjusted arrays, nested targets and native conventions.
    /// </summary>
    [TestMethod]
    public async Task NativeCallbackWrappersPreserveSpecialStorage()
    {
        const string Headers = """
            #include <stdint.h>
            #include <stdbool.h>
            typedef struct __attribute__((aligned(64))) { long double fraction; unsigned __int128 bits; int values[3]; } Payload;
            typedef struct {} Empty;
            typedef struct {} OtherEmpty;
            typedef int (*Arithmetic)(int);
            #if defined(__x86_64__) && !defined(_WIN32)
            #define CALLBACK_ABI __attribute__((ms_abi))
            #elif defined(_WIN32)
            #define CALLBACK_ABI __attribute__((vectorcall))
            #else
            #define CALLBACK_ABI
            #endif
            typedef Payload (CALLBACK_ABI *Transform)(Payload, const int[3], Arithmetic, bool);
            typedef Empty (*Factory)(void);
            typedef OtherEmpty (*EmptyTransform)(const Empty);
            typedef const Payload Immutable;
            typedef Immutable (*ImmutableFactory)(int);
            extern Transform transform;
            extern Factory factory;
            extern EmptyTransform empty_transform;
            extern ImmutableFactory immutable_factory;
            """;
        const string Main = """
            #include <stdio.h>
            #include <stdlib.h>
            #define REQUIRE(value) do { if (!(value)) { fprintf(stderr, "line %d: %s\n", __LINE__, #value); exit(1); } } while (0)
            static int calls;
            void ankus_dispatch_native_callback(AnkusManagedNativeCallback callback, const AnkusNativeCallArgument *arguments,
                size_t count, void *result, size_t size)
            {
                REQUIRE(callback(arguments, count, result, size, &calls) == 0);
            }
            int ankus_managed_callback_11111111111111111111111111111111(const AnkusNativeCallArgument *arguments,
                size_t count, void *result, size_t size, void *context)
            {
                REQUIRE(context == &calls && count == 4 && result != NULL && size == sizeof(Payload));
                REQUIRE(arguments[0].size == sizeof(Payload) && arguments[1].size == sizeof(int *));
                REQUIRE(arguments[2].size == sizeof(Arithmetic) && arguments[3].size == sizeof(bool));
                REQUIRE(_Alignof(Payload) == 64 && (uintptr_t)arguments[0].data % 64 == 0);
                Payload value = *(const Payload *)arguments[0].data;
                const int *values = *(const int * const *)arguments[1].data;
                Arithmetic arithmetic = *(const Arithmetic *)arguments[2].data;
                REQUIRE(*(const bool *)arguments[3].data);
                value.fraction *= 2;
                value.bits ^= (unsigned __int128)1 << 111;
                value.values[2] = arithmetic(values[0] + values[1] + values[2]);
                memcpy(result, &value, sizeof(value));
                ++calls;
                return 0;
            }
            int ankus_managed_callback_22222222222222222222222222222222(const AnkusNativeCallArgument *arguments,
                size_t count, void *result, size_t size, void *context)
            {
                REQUIRE(context == &calls);
                REQUIRE(arguments == NULL);
                REQUIRE(count == 0);
                REQUIRE(result != NULL);
                REQUIRE(size == sizeof(Empty));
                calls *= 3;
                return 0;
            }
            int ankus_managed_callback_33333333333333333333333333333333(const AnkusNativeCallArgument *arguments,
                size_t count, void *result, size_t size, void *context)
            {
                REQUIRE(context == &calls && arguments != NULL && count == 1 && result != NULL && size == sizeof(OtherEmpty));
                REQUIRE(arguments[0].data != NULL && arguments[0].size == sizeof(Empty));
                calls *= 7;
                return 0;
            }
            int ankus_managed_callback_44444444444444444444444444444444(const AnkusNativeCallArgument *arguments,
                size_t count, void *result, size_t size, void *context)
            {
                REQUIRE(context == &calls && count == 1 && size == sizeof(Immutable) && arguments[0].size == sizeof(int));
                Payload value = { 2.5L, (unsigned __int128)1 << 101, { 11, 22, *(const int *)arguments[0].data } };
                memcpy(result, &value, sizeof(value));
                ++calls;
                return 0;
            }
            static int increment(int value) { return value + 25; }
            int main(void)
            {
                Transform transform_target = __TRANSFORM__(ankus_managed_callback_11111111111111111111111111111111);
                Payload value = { 1.25L, ((unsigned __int128)1 << 111) | 17, { -9, 14, 71 } };
                int values[3] = { -7, 11, 13 };
                Payload result = transform_target(value, values, increment, true);
                REQUIRE(result.fraction == 2.5L && result.bits == 17);
                REQUIRE(result.values[0] == -9 && result.values[1] == 14 && result.values[2] == 42);
                REQUIRE(value.fraction == 1.25L && value.bits == (((unsigned __int128)1 << 111) | 17) && value.values[2] == 71);
                Factory make_empty = __FACTORY__(ankus_managed_callback_22222222222222222222222222222222);
                EmptyTransform change_empty = __EMPTY__(ankus_managed_callback_33333333333333333333333333333333);
                OtherEmpty empty = change_empty(make_empty());
                REQUIRE(sizeof(empty) == sizeof(OtherEmpty) && calls == 21);
                ImmutableFactory immutable = __IMMUTABLE__(ankus_managed_callback_44444444444444444444444444444444);
                Immutable preserved = immutable(73);
                REQUIRE(preserved.fraction == 2.5L && preserved.bits == (unsigned __int128)1 << 101);
                REQUIRE(preserved.values[0] == 11 && preserved.values[1] == 22 && preserved.values[2] == 73 && calls == 22);
                puts("wide, aligned, empty, qualified and compiler-specific callback ABI");
                return 0;
            }
            """;
        string directory = Directory.CreateTempSubdirectory("ankus-callback-special-").FullName;
        try
        {
            NativeHeaderRecords records = await CollectCallRecordsAsync(Headers,
                [new("transform", "transform", false), new("factory", "factory", false), new("empty_transform", "empty_transform", false),
                    new("immutable_factory", "immutable_factory", false)], directory);
            Dictionary<string, NativeBindingIndirectCall> calls = NativeBindingIndirectModel.Describe(records.Graph).ToDictionary(static call => call.Name!);
            int expectedConvention = OperatingSystem.IsWindows() ? 12 : records.Graph.Target.RuntimeIdentifier.EndsWith("-x64", StringComparison.Ordinal) ? 10 : 1;
            Assert.AreEqual(expectedConvention, records.Graph.Types[calls["Transform"].FunctionType].Function!.CallingConvention);
            NativeHeaderAlias alias = Assert.IsInstanceOfType<NativeHeaderAlias>(records.Headers.Symbols["transform"].Type);
            NativeHeaderPointer address = Assert.IsInstanceOfType<NativeHeaderPointer>(alias.Underlying);
            NativeHeaderFunction function = Assert.IsInstanceOfType<NativeHeaderFunction>(address.Element);
            Assert.AreEqual(expectedConvention, function.CallingConvention);
            Dictionary<string, NativeHeaderSymbol> symbols = records.Headers.Symbols.ToDictionary();
            symbols["transform"] = symbols["transform"] with
            {
                Type = alias with { Underlying = address with { Element = function with { CallingConvention = expectedConvention == 1 ? 10 : 1 } } },
            };
            FormatException mismatch = Assert.ThrowsExactly<FormatException>(() => NativeBindingSignatureValidation.Validate(records with { Headers = records.Headers with { Symbols = symbols } }));
            Assert.Contains("calling convention", mismatch.Message);
            (string Name, string Marker)[] selected = [("Transform", "__TRANSFORM__"), ("Factory", "__FACTORY__"),
                ("EmptyTransform", "__EMPTY__"), ("ImmutableFactory", "__IMMUTABLE__")];
            var imports = new List<string>();
            string main = Main;
            for (int index = 0; index < selected.Length; index++)
            {
                string name = NativeBindingCallbackImports.Prefix + calls[selected[index].Name].FunctionType.ToString(CultureInfo.InvariantCulture) + "_" + new string((char)('1' + index), 32);
                imports.Add("extern void *" + name + "(void *); void *select_" + index.ToString(CultureInfo.InvariantCulture) + "(void) { return " + name + "((void *)0); }");
                main = main.Replace(selected[index].Marker, name, StringComparison.Ordinal);
            }

            byte[] image = await CompileNativeObjectAsync(string.Join("\n", imports));
            byte[] empty = await CompileNativeObjectAsync("int no_imports(void) { return 0; }");
            string checks = NativeBindingRecordChecks.Generate(records, "#define PG_VERSION_NUM 180006\n" + Headers);
            Assert.AreEqual("", await RunImportedBodiesAsync(checks, NativeBindingRecordChecks.ExecutableEntryPoint, empty, directory, nativeCompiler: false));
            string source = NativeBindingCallImports.Generate(records, "#define PG_VERSION_NUM 180006\n" + Headers, image);
            Assert.AreEqual("wide, aligned, empty, qualified and compiler-specific callback ABI\n",
                await RunImportedBodiesAsync(source, main, image, directory, nativeCompiler: false));
        }
        finally
        {
            await DeleteDirectoryAsync(directory);
        }
    }
}
