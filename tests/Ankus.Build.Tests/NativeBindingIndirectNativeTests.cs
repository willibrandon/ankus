namespace Ankus.Build.Tests;

public sealed partial class NativeBindingNativeTests
{
    /// <summary>
    /// Invocation compilation derives named and embedded anonymous types from real headers without collection-only global declarations.
    /// </summary>
    [TestMethod]
    public async Task IndirectBodiesUseHeaderTypesWithoutProbeGlobals()
    {
        const string Headers = """
            typedef struct Holder { struct { int bits; } payload; } Holder;
            typedef __typeof__(((Holder *)0)->payload) (*Transform)(__typeof__(((Holder *)0)->payload));
            int calls;
            __typeof__(((Holder *)0)->payload) change(__typeof__(((Holder *)0)->payload) value)
            {
                ++calls;
                value.bits += 7;
                return value;
            }
            """;
        const string Probes = """
            extern Holder ankus_probe_holder;
            extern __typeof__(((Holder *)0)->payload) ankus_probe_payload;
            extern Transform ankus_probe_target;
            """;
        const string Main = """
            #include <stdio.h>
            int main(void)
            {
                Transform target = change;
                __typeof__(((Holder *)0)->payload) value = {5}, result = {0};
                AnkusNativeCallArgument arguments[] = {{ &target, sizeof(target) }, { &value, sizeof(value) }};
                if (__BODY__(arguments, 2, &result, sizeof(result)) != ANKUS_CALL_OK) return 1;
                if (result.bits != 12 || value.bits != 5 || calls != 1) return 2;
                puts("anonymous aggregate identity without probe globals");
                return 0;
            }
            """;
        string directory = Directory.CreateTempSubdirectory("ankus-indirect-probe-roots-").FullName;
        try
        {
            NativeHeaderRecords records = await CollectCallRecordsAsync(Headers + "\n" + Probes,
                [new("ankus_probe_holder", "ankus_probe_holder", false), new("ankus_probe_payload", "ankus_probe_payload", false),
                    new("ankus_probe_target", "ankus_probe_target", false)], directory);
            int signature = NativeBindingIndirectModel.Describe(records.Graph).Single().FunctionType;
            string source = NativeBindingCallSource.Generate(records, "#define PG_VERSION_NUM 180006\n" + Headers, []) +
                NativeBindingIndirectSource.Bodies(records, [signature]);
            byte[] image = await CompileNativeObjectAsync("int indirect_consumer(void) { return 0; }");
            Assert.AreEqual("anonymous aggregate identity without probe globals\n",
                await RunImportedBodiesAsync(source, Main.Replace("__BODY__", NativeBindingIndirectSource.BodyName(signature), StringComparison.Ordinal), image, directory));
        }
        finally
        {
            await DeleteDirectoryAsync(directory);
        }
    }

    /// <summary>
    /// Real C targets preserve aggregate and integer ABI while every malformed envelope rejects before side effects or result writes.
    /// </summary>
    [TestMethod]
    public async Task IndirectBodiesPreserveValuesAndRejectFrames()
    {
        const string Headers = """
            #include <stdint.h>
            typedef struct { uint64_t bits; double fraction; int values[3]; } Payload;
            typedef Payload (*Transform)(Payload, int);
            typedef uint64_t (*Arithmetic)(uint64_t, int);
            typedef void (*Action)(void);
            int calls;
            Payload transform(Payload value, int factor) { ++calls; value.bits ^= UINT64_MAX; value.fraction *= factor; value.values[2] += factor; return value; }
            uint64_t first(uint64_t value, int extra) { ++calls; return value + (uint64_t)extra; }
            uint64_t second(uint64_t value, int extra) { ++calls; return value ^ ((uint64_t)extra << 8); }
            void action(void) { ++calls; }
            Transform transform_address = transform;
            Arithmetic arithmetic_address = first;
            Action action_address = action;
            """;
        const string Main = """
            #include <stdio.h>
            #define REQUIRE(expression) do { if (!(expression)) { fprintf(stderr, "line %d: %s\n", __LINE__, #expression); return 1; } } while (0)
            int main(void)
            {
                Arithmetic target = first;
                uint64_t input = UINT64_C(0xFEDCBA9876543210), result = UINT64_C(0x123456789ABCDEF0);
                int extra = 7;
                AnkusNativeCallArgument arguments[] = {{ &target, sizeof(target) }, { &input, sizeof(input) }, { &extra, sizeof(extra) }};
                REQUIRE(__BODY_Arithmetic__(arguments, 2, &result, sizeof(result)) == ANKUS_CALL_COUNT);
                REQUIRE(__BODY_Arithmetic__(arguments, 4, &result, sizeof(result)) == ANKUS_CALL_COUNT);
                REQUIRE(__BODY_Arithmetic__(NULL, 3, &result, sizeof(result)) == ANKUS_CALL_ARGUMENTS);
                REQUIRE(__BODY_Arithmetic__(arguments, 3, NULL, sizeof(result)) == ANKUS_CALL_RESULT);
                REQUIRE(__BODY_Arithmetic__(arguments, 3, &result, sizeof(result) - 1) == ANKUS_CALL_RESULT);
                REQUIRE(__BODY_Arithmetic__(arguments, 3, &result, sizeof(result) + 1) == ANKUS_CALL_RESULT);
                union { uint64_t aligned; unsigned char bytes[32]; } misaligned = {0};
                for (size_t index = 0; index < 3; ++index)
                {
                    AnkusNativeCallArgument saved = arguments[index];
                    arguments[index].data = NULL;
                    REQUIRE(__BODY_Arithmetic__(arguments, 3, &result, sizeof(result)) == ANKUS_CALL_STORAGE);
                    arguments[index] = saved;
                    arguments[index].size--;
                    REQUIRE(__BODY_Arithmetic__(arguments, 3, &result, sizeof(result)) == ANKUS_CALL_STORAGE);
                    arguments[index].size += 2;
                    REQUIRE(__BODY_Arithmetic__(arguments, 3, &result, sizeof(result)) == ANKUS_CALL_STORAGE);
                    arguments[index] = saved;
                    arguments[index].data = &misaligned.bytes[1];
                    REQUIRE(__BODY_Arithmetic__(arguments, 3, &result, sizeof(result)) == ANKUS_CALL_ALIGNMENT);
                    arguments[index] = saved;
                    REQUIRE(calls == 0 && result == UINT64_C(0x123456789ABCDEF0));
                }

                target = NULL;
                REQUIRE(__BODY_Arithmetic__(arguments, 3, &result, sizeof(result)) == ANKUS_CALL_TARGET);
                REQUIRE(calls == 0 && result == UINT64_C(0x123456789ABCDEF0));
                target = first;
                REQUIRE(__BODY_Arithmetic__(arguments, 3, &result, sizeof(result)) == ANKUS_CALL_OK);
                REQUIRE(result == UINT64_C(0xFEDCBA9876543217) && calls == 1);
                target = second;
                REQUIRE(__BODY_Arithmetic__(arguments, 3, &result, sizeof(result)) == ANKUS_CALL_OK);
                REQUIRE(result == UINT64_C(0xFEDCBA9876543510) && calls == 2);
                REQUIRE(input == UINT64_C(0xFEDCBA9876543210) && extra == 7);
                Payload value = { UINT64_C(0xFEDCBA9876543210), 1.25, { -9, 14, 71 } }, transformed = {0};
                Transform aggregate = transform;
                int factor = 3;
                AnkusNativeCallArgument aggregate_arguments[] = {{ &aggregate, sizeof(aggregate) }, { &value, sizeof(value) }, { &factor, sizeof(factor) }};
                REQUIRE(__BODY_Transform__(aggregate_arguments, 3, &transformed, sizeof(transformed)) == ANKUS_CALL_OK);
                REQUIRE(transformed.bits == UINT64_C(0x0123456789ABCDEF) && transformed.fraction == 3.75);
                REQUIRE(transformed.values[0] == -9 && transformed.values[1] == 14 && transformed.values[2] == 74);
                REQUIRE(value.bits == UINT64_C(0xFEDCBA9876543210) && value.fraction == 1.25 && value.values[2] == 71 && calls == 3);
                Action empty = action;
                AnkusNativeCallArgument empty_argument = { &empty, sizeof(empty) };
                REQUIRE(__BODY_Action__(&empty_argument, 1, &result, 0) == ANKUS_CALL_RESULT);
                REQUIRE(__BODY_Action__(&empty_argument, 1, NULL, 1) == ANKUS_CALL_RESULT);
                REQUIRE(calls == 3);
                REQUIRE(__BODY_Action__(&empty_argument, 1, NULL, 0) == ANKUS_CALL_OK && calls == 4);
                puts("indirect ABI, target selection and failed-frame preservation");
                return 0;
            }
            """;
        string directory = Directory.CreateTempSubdirectory("ankus-indirect-native-").FullName;
        try
        {
            NativeHeaderRecords records = await CollectCallRecordsAsync(Headers,
                [new("transform_address", "transform_address", false), new("arithmetic_address", "arithmetic_address", false),
                    new("action_address", "action_address", false)], directory);
            IReadOnlyList<NativeBindingIndirectCall> calls = NativeBindingIndirectModel.Describe(records.Graph);
            string source = NativeBindingCallSource.Generate(records, "#define PG_VERSION_NUM 180006\n" + Headers, []) +
                NativeBindingIndirectSource.Bodies(records, [.. calls.Select(static call => call.FunctionType)]);
            string main = Main;
            foreach (NativeBindingIndirectCall call in calls)
            {
                main = main.Replace("__BODY_" + call.Name + "__", NativeBindingIndirectSource.BodyName(call.FunctionType), StringComparison.Ordinal);
            }

            byte[] image = await CompileNativeObjectAsync("int indirect_consumer(void) { return 0; }");
            Assert.AreEqual("indirect ABI, target selection and failed-frame preservation\n", await RunImportedBodiesAsync(source, main, image, directory));
        }
        finally
        {
            await DeleteDirectoryAsync(directory);
        }
    }
}
