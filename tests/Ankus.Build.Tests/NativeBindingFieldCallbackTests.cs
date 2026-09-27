namespace Ankus.Build.Tests;

public sealed partial class NativeBindingNativeTests
{
    /// <summary>
    /// Unrelated native declarations can renumber canonical signatures without changing field callback source names.
    /// </summary>
    [TestMethod]
    public async Task ManagedFieldCallbacksKeepNamesAcrossGraphs()
    {
        const string Headers = """
            typedef struct Noise
            {
                double first;
                long long second;
            } Noise;

            Noise aaa(void)
            {
                Noise value = {1.0, 2};
                return value;
            }

            typedef struct Methods
            {
                int (*apply)(int);
            } Methods;

            int increment(int value)
            {
                return value + 17;
            }

            Methods get(void)
            {
                Methods value = {increment};
                return value;
            }
            """;
        const string Harness = """
            public static class BindingAssertions
            {
                public static long[] Run()
                {
                    using NativeCallTestBridge.Scope scope = new();
                    Methods methods = NativeMethods.get();
                    Methods_applyCallback callback = methods.apply;
                    int result = callback.Invoke(25);
                    methods.apply = callback;
                    return [result, methods.apply.Invoke(-18),
                        ((NativeFunctionPointerAttribute)Attribute.GetCustomAttribute(typeof(Methods_applyCallback), typeof(NativeFunctionPointerAttribute))!).Signature,
                        callback.DangerousGetAddress() == methods.apply.DangerousGetAddress() ? 1 : 0,
                        Unsafe.SizeOf<Methods_applyCallback>(), IntPtr.Size];
                }
            }
            """;
        long[] original = await ExecuteManagedCallsAsync(Headers, ["get"], Harness);
        long[] expanded = await ExecuteManagedCallsAsync(Headers, ["aaa", "get"], Harness);
        Assert.AreSequenceEqual<long>([42, -1], original.Take(2));
        Assert.AreSequenceEqual(original.Take(2), expanded.Take(2));
        Assert.AreNotEqual(original[2], expanded[2], "The extra root must actually change the canonical signature index.");
        Assert.AreSequenceEqual<long>([1, IntPtr.Size, IntPtr.Size], original.Skip(3));
        Assert.AreSequenceEqual(original.Skip(3), expanded.Skip(3));
    }

    /// <summary>
    /// Record fields, unions, callback arrays and typedef-backed fields share exact addresses and native invocation.
    /// </summary>
    [TestMethod]
    public async Task ManagedFieldCallbacksPreserveStorageAndInvocation()
    {
        const string Headers = """
            typedef int (*Arithmetic)(int);
            typedef union Choice
            {
                int (*selected)(int);
                void *address;
            } Choice;

            typedef struct
            {
                int (*apply)(int);
            } Aliased;

            typedef struct Methods
            {
                Arithmetic first;
                int (*second)(int);
                int (*items[2])(int);
                Choice choice;
                Aliased alias;
            } Methods;

            int plus(int value)
            {
                return value + 17;
            }

            int minus(int value)
            {
                return value - 19;
            }

            Methods get(void)
            {
                Methods value = {plus, minus, {plus, minus}, {plus}, {minus}};
                return value;
            }
            """;
        const string Harness = """
            public static class BindingAssertions
            {
                public static long[] Run()
                {
                    using NativeCallTestBridge.Scope scope = new();
                    Methods value = NativeMethods.get();
                    Methods_firstCallback first = value.first;
                    Methods_secondCallback second = value.second;
                    Methods_itemsCallback item = value.items[1];
                    Choice_selectedCallback choice = value.choice.selected;
                    Aliased_applyCallback anonymous = value.alias.apply;
                    value.second = first;
                    value.items[0] = second;
                    value.choice.selected = item;
                    Arithmetic canonical = first;
                    Methods_firstCallback recovered = canonical;
                    Methods_firstCallback bits = new(unchecked((nint)(long.MinValue + 0x123456789)));
                    Arithmetic exact = bits;
                    Methods_firstCallback copied = exact;
                    return [first.Invoke(25), second.Invoke(25), item.Invoke(7), choice.Invoke(7),
                        value.second.Invoke(1), value.items[0].Invoke(1), value.choice.selected.Invoke(1),
                        recovered.DangerousGetAddress() == first.DangerousGetAddress() ? 1 : 0,
                        copied.DangerousGetAddress(), default(Methods_firstCallback).IsNull ? 1 : 0,
                        first.IsNull ? 1 : 0, Unsafe.SizeOf<Methods_firstCallback>(), IntPtr.Size, anonymous.Invoke(7)];
                }
            }
            """;
        Assert.AreSequenceEqual<long>([42, 6, -12, 24, 18, -18, -18, 1, long.MinValue + 0x123456789, 1, 0, IntPtr.Size, IntPtr.Size, -12],
            await ExecuteManagedCallsAsync(Headers, ["get"], Harness));
    }

    /// <summary>
    /// Empty argument lists, void results and aggregate argument/results retain their exact native signatures.
    /// </summary>
    [TestMethod]
    public async Task ManagedFieldCallbacksPreserveEmptyAndAggregateSignatures()
    {
        const string Headers = """
            typedef struct Pair
            {
                long long first;
                long long second;
            } Pair;

            typedef struct Methods
            {
                void (*reset)(void);
                long long (*read)(void);
                Pair (*change)(Pair);
            } Methods;

            long long ankus_field_alias_counter = 91;
            void ankus_field_alias_reset(void)
            {
                ankus_field_alias_counter = 0;
            }

            long long ankus_field_alias_read(void)
            {
                return ankus_field_alias_counter;
            }

            Pair ankus_field_alias_change(Pair value)
            {
                value.first += 17;
                value.second -= 19;
                ankus_field_alias_counter++;
                return value;
            }

            Methods get(void)
            {
                Methods value = {ankus_field_alias_reset, ankus_field_alias_read, ankus_field_alias_change};
                return value;
            }
            """;
        const string Harness = """
            public static class BindingAssertions
            {
                public static long[] Run()
                {
                    using NativeCallTestBridge.Scope scope = new();
                    Methods methods = NativeMethods.get();
                    Methods_resetCallback reset = methods.reset;
                    Methods_readCallback read = methods.read;
                    Methods_changeCallback change = methods.change;
                    long initial = read.Invoke();
                    reset.Invoke();
                    long cleared = read.Invoke();
                    Pair input = new() { first = long.MinValue + 31, second = long.MaxValue - 47 };
                    Pair result = change.Invoke(input);
                    return [initial, cleared, result.first, result.second, input.first, input.second, read.Invoke()];
                }
            }
            """;
        Assert.AreSequenceEqual<long>([91, 0, long.MinValue + 48, long.MaxValue - 66, long.MinValue + 31, long.MaxValue - 47, 1],
            await ExecuteManagedCallsAsync(Headers, ["get"], Harness));
    }

    /// <summary>
    /// Field callbacks keep unsupported native prototypes address-only without guessing an invocation signature.
    /// </summary>
    [TestMethod]
    public async Task ManagedFieldCallbacksKeepUnsupportedSignaturesAddressOnly()
    {
        const string Headers = """
            struct Missing;
            typedef struct Methods
            {
                int (*variadic)(int, ...);
                int (*unprototyped)();
                struct Missing (*incomplete)(void);
            } Methods;

            Methods state = {0};
            """;
        const string Harness = """
            public static class BindingAssertions
            {
                public static long[] Run()
                {
                    using NativeCallTestBridge.Scope scope = new();
                    Methods value = NativeGlobals.state;
                    int empty = value.variadic.IsNull && value.unprototyped.IsNull && value.incomplete.IsNull ? 1 : 0;
                    value.variadic = new Methods_variadicCallback(123);
                    value.unprototyped = new Methods_unprototypedCallback(456);
                    value.incomplete = new Methods_incompleteCallback(789);
                    NativeGlobals.state = value;
                    Methods recovered = NativeGlobals.state;
                    Methods_variadicCallback variadic = recovered.variadic;
                    Methods_unprototypedCallback legacy = recovered.unprototyped;
                    Methods_incompleteCallback incomplete = recovered.incomplete;
                    return [empty, variadic.DangerousGetAddress(), legacy.DangerousGetAddress(), incomplete.DangerousGetAddress(),
                        typeof(Methods_variadicCallback).GetMethod("Invoke") is null ? 1 : 0,
                        typeof(Methods_unprototypedCallback).GetMethod("Invoke") is null ? 1 : 0,
                        typeof(Methods_incompleteCallback).GetMethod("Invoke") is null ? 1 : 0];
                }
            }
            """;
        Assert.AreSequenceEqual<long>([1, 123, 456, 789, 1, 1, 1],
            await ExecuteManagedCallsAsync(Headers, [], Harness, globalNames: ["state"]));
    }

    /// <summary>
    /// A layout-only binding preserves callback addresses without offering calls that have no native header contract.
    /// </summary>
    [TestMethod]
    public async Task ManagedFieldCallbacksWithoutHeadersRetainOnlyAddresses()
    {
        string directory = Directory.CreateTempSubdirectory("ankus-field-callback-layout-").FullName;
        try
        {
            NativeHeaderRecords records = await CollectCallRecordsAsync(
                """
                typedef struct Methods
                {
                    int (*apply)(int);
                } Methods;

                extern Methods state;
                """,
                [new("state", "state", false)], directory);
            const string Harness = """
                public static class BindingAssertions
                {
                    public static long[] Run()
                    {
                        Ankus.Postgres.Methods_applyCallback callback = new(-731);
                        Ankus.Postgres.Methods value = new() { apply = callback };
                        Ankus.Postgres.Methods_applyCallback recovered = value.apply;
                        return [recovered.DangerousGetAddress(), recovered.IsNull ? 1 : 0,
                            typeof(Ankus.Postgres.Methods_applyCallback).GetMethod("Invoke") is null ? 1 : 0,
                            default(Ankus.Postgres.Methods_applyCallback).IsNull ? 1 : 0];
                    }
                }
                """;
            Assert.AreSequenceEqual<long>([-731, 0, 1, 1],
                GeneratedBindingCompilation.Run(NativeBindingRecordCSharp.Generate(records.Graph), Harness, context.CancellationToken));
        }
        finally
        {
            await DeleteDirectoryAsync(directory);
        }
    }

    /// <summary>
    /// Field names remain valid C# identifiers and avoid collisions with existing native declarations.
    /// </summary>
    [TestMethod]
    public async Task ManagedFieldCallbacksAvoidNameCollisions()
    {
        const string Headers = """
            typedef struct Methods_eventCallback
            {
                int value;
            } Methods_eventCallback;

            typedef struct Methods
            {
                int (*event)(int);
                Methods_eventCallback witness;
            } Methods;

            int twice(int value)
            {
                return value * 2;
            }

            Methods get(void)
            {
                Methods value = {twice, {731}};
                return value;
            }
            """;
        const string Harness = """
            public static class BindingAssertions
            {
                public static long[] Run()
                {
                    using NativeCallTestBridge.Scope scope = new();
                    Methods value = NativeMethods.get();
                    Methods_eventCallback_1 callback = value.@event;
                    value.@event = callback;
                    return [callback.Invoke(21), value.@event.Invoke(-19), value.witness.value];
                }
            }
            """;
        Assert.AreSequenceEqual<long>([42, -38, 731], await ExecuteManagedCallsAsync(Headers, ["get"], Harness));
    }

    /// <summary>
    /// Field aliases retain backend admission, null rejection, native diagnostics and a healthy retry.
    /// </summary>
    [TestMethod]
    public async Task ManagedFieldCallbacksValidateInvocationAndRecover()
    {
        const string Headers = """
            typedef struct Methods
            {
                int (*apply)(int);
            } Methods;

            int calls;
            int increment(int value)
            {
                return value + ++calls;
            }

            Methods get(void)
            {
                Methods value = {increment};
                return value;
            }
            """;
        const string Harness = """
            public static class BindingAssertions
            {
                public static long[] Run()
                {
                    Methods_applyCallback callback;
                    using (NativeCallTestBridge.Scope factory = new())
                    {
                        callback = NativeMethods.get().apply;
                    }

                    int absent = 0;
                    try
                    {
                        callback.Invoke(100);
                    }
                    catch (InvalidOperationException error) when (error.Message.Contains("active backend callback", StringComparison.Ordinal))
                    {
                        absent++;
                    }

                    using NativeCallTestBridge.Scope scope = new();
                    int empty = 0;
                    try
                    {
                        default(Methods_applyCallback).Invoke(100);
                    }
                    catch (InvalidOperationException error) when (error.Message.Contains("null native function pointer", StringComparison.Ordinal))
                    {
                        empty++;
                    }

                    int before = scope.Invocations;
                    scope.RejectCall = true;
                    PgException? failure = null;
                    try
                    {
                        callback.Invoke(100);
                    }
                    catch (PgException error)
                    {
                        failure = error;
                    }

                    scope.RejectCall = false;
                    return [absent, empty, before, callback.Invoke(41),
                        failure?.SqlState == "22023" ? 1 : 0, failure?.Message == "native call café" ? 1 : 0,
                        failure?.Detail == "detail naïve" ? 1 : 0, failure?.Hint == "hint déjà" ? 1 : 0,
                        scope.Validations, scope.Invocations];
                }
            }
            """;
        Assert.AreSequenceEqual<long>([1, 1, 0, 42, 1, 1, 1, 1, 3, 2],
            await ExecuteManagedCallsAsync(Headers, ["get"], Harness));
    }
}
