namespace Ankus.Build.Tests;

public sealed partial class NativeBindingNativeTests
{
    /// <summary>
    /// An unrelated earlier typedef changes the canonical type without changing a global hook's consumer.
    /// </summary>
    [TestMethod]
    public async Task ManagedGlobalCallbacksKeepNamesAcrossAliases()
    {
        const string Headers = """
            typedef void (*Zeta)(void);
            int calls;
            void target(void)
            {
                calls++;
            }

            Zeta current = target;
            """;
        const string Harness = """
            public static class BindingAssertions
            {
                public static unsafe long[] Run()
                {
                    using NativeCallTestBridge.Scope scope = new();
                    NativeGlobals_currentCallback callback = NativeGlobals.current;
                    callback.Invoke();
                    NativeGlobals.current = default(NativeGlobals_currentCallback);
                    bool cleared = NativeGlobals.current.IsNull;
                    NativeGlobals.current = callback;
                    NativeGlobals.current.Invoke();
                    string canonical = typeof(NativeGlobals).GetProperty("current")!.PropertyType.Name;
                    return [NativeGlobals.calls, cleared ? 1 : 0,
                        NativeGlobals.current.DangerousGetAddress() == callback.DangerousGetAddress() ? 1 : 0,
                        canonical == "Zeta" ? 1 : 0, canonical == "Alpha" ? 1 : 0];
                }
            }
            """;
        Assert.AreSequenceEqual<long>([2, 1, 1, 1, 0],
            await ExecuteManagedCallsAsync(Headers, [], Harness, globalNames: ["current", "calls"]));
        Assert.AreSequenceEqual<long>([2, 1, 1, 0, 1],
            await ExecuteManagedCallsAsync(Headers + "\ntypedef void (*Alpha)(void);\nAlpha unrelated = target;", [], Harness,
                globalNames: ["current", "calls", "unrelated"]));
    }

    /// <summary>
    /// Global callback names preserve addresses, native writes, array elements and object qualifiers.
    /// </summary>
    [TestMethod]
    public async Task ManagedGlobalCallbacksPreserveStorageAndInvocation()
    {
        const string Headers = """
            typedef int (*Arithmetic)(int);
            int plus(int value)
            {
                return value + 17;
            }

            int minus(int value)
            {
                return value - 19;
            }

            Arithmetic current = plus;
            Arithmetic items[2] = {plus, minus};
            Arithmetic const constant = plus;
            Arithmetic volatile observed = minus;
            int inspect(int value)
            {
                return current(value) + items[0](value) + items[1](value) + observed(value);
            }
            """;
        const string Harness = """
            public static class BindingAssertions
            {
                public static unsafe long[] Run()
                {
                    using NativeCallTestBridge.Scope scope = new();
                    NativeGlobals_currentCallback first = NativeGlobals.current;
                    var items = NativeGlobals.items;
                    NativeGlobals_itemsCallback second = items[1];
                    NativeGlobals_constantCallback constant = NativeGlobals.constant;
                    NativeGlobals_observedCallback observed = NativeGlobals.observed;
                    NativeGlobals.current = second;
                    items[0] = second;
                    items[1] = first;
                    NativeGlobals.items = items;
                    NativeGlobals.observed = first;
                    Arithmetic canonical = first;
                    NativeGlobals_currentCallback recovered = canonical;
                    NativeGlobals_currentCallback bits = new((void*)unchecked((nint)(long.MinValue + 0x123456789)));
                    Arithmetic exact = bits;
                    NativeGlobals_currentCallback copied = exact;
                    return [first.Invoke(25), second.Invoke(25), constant.Invoke(7), observed.Invoke(7),
                        NativeMethods.inspect(20), NativeGlobals.observed.Invoke(1),
                        recovered.DangerousGetAddress() == first.DangerousGetAddress() ? 1 : 0,
                        (nint)copied.DangerousGetAddress(), default(NativeGlobals_currentCallback).IsNull ? 1 : 0,
                        first.IsNull ? 1 : 0, Unsafe.SizeOf<NativeGlobals_currentCallback>(),
                        typeof(NativeGlobals).GetProperty("constant")!.SetMethod is null ? 1 : 0];
                }
            }
            """;
        Assert.AreSequenceEqual<long>([42, 6, 24, -12, 76, 18, 1, long.MinValue + 0x123456789, 1, 0, IntPtr.Size, 1],
            await ExecuteManagedCallsAsync(Headers, ["inspect"], Harness, globalNames: ["current", "items", "constant", "observed"]));
    }

    /// <summary>
    /// Unsupported native prototypes retain exact address storage without an inferred invocation signature.
    /// </summary>
    [TestMethod]
    public async Task ManagedGlobalCallbacksKeepUnsupportedSignaturesAddressOnly()
    {
        const string Headers = """
            struct Missing;
            int (*variadic)(int, ...);
            int (*unprototyped)();
            struct Missing (*incomplete)(void);
            """;
        const string Harness = """
            public static class BindingAssertions
            {
                public static unsafe long[] Run()
                {
                    using NativeCallTestBridge.Scope scope = new();
                    int empty = NativeGlobals.variadic.IsNull && NativeGlobals.unprototyped.IsNull && NativeGlobals.incomplete.IsNull ? 1 : 0;
                    NativeGlobals.variadic = new NativeGlobals_variadicCallback((void*)123);
                    NativeGlobals.unprototyped = new NativeGlobals_unprototypedCallback((void*)456);
                    NativeGlobals.incomplete = new NativeGlobals_incompleteCallback((void*)789);
                    NativeGlobals_variadicCallback variadic = NativeGlobals.variadic;
                    NativeGlobals_unprototypedCallback legacy = NativeGlobals.unprototyped;
                    NativeGlobals_incompleteCallback incomplete = NativeGlobals.incomplete;
                    return [empty, (nint)variadic.DangerousGetAddress(), (nint)legacy.DangerousGetAddress(), (nint)incomplete.DangerousGetAddress(),
                        typeof(NativeGlobals_variadicCallback).GetMethod("Invoke") is null ? 1 : 0,
                        typeof(NativeGlobals_unprototypedCallback).GetMethod("Invoke") is null ? 1 : 0,
                        typeof(NativeGlobals_incompleteCallback).GetMethod("Invoke") is null ? 1 : 0];
                }
            }
            """;
        Assert.AreSequenceEqual<long>([1, 123, 456, 789, 1, 1, 1],
            await ExecuteManagedCallsAsync(Headers, [], Harness, globalNames: ["variadic", "unprototyped", "incomplete"]));
    }

    /// <summary>
    /// Keyword globals and colliding record declarations keep distinct usable names, without exposing unselected globals.
    /// </summary>
    [TestMethod]
    public async Task ManagedGlobalCallbacksAvoidNameCollisions()
    {
        const string Headers = """
            typedef struct NativeGlobals_eventCallback
            {
                int value;
            } NativeGlobals_eventCallback;

            int twice(int value)
            {
                return value * 2;
            }

            NativeGlobals_eventCallback witness = {731};
            int (*event)(int) = twice;
            int (*ignored)(int) = twice;
            """;
        const string Harness = """
            public static class BindingAssertions
            {
                public static long[] Run()
                {
                    using NativeCallTestBridge.Scope scope = new();
                    NativeGlobals_eventCallback_1 callback = NativeGlobals.@event;
                    NativeGlobals.@event = callback;
                    return [callback.Invoke(21), NativeGlobals.@event.Invoke(-19), NativeGlobals.witness.value,
                        typeof(NativeGlobals).Assembly.GetType("Ankus.Postgres.NativeGlobals_ignoredCallback") is null ? 1 : 0];
                }
            }
            """;
        Assert.AreSequenceEqual<long>([42, -38, 731, 1],
            await ExecuteManagedCallsAsync(Headers, [], Harness, globalNames: ["event", "witness"]));
    }

    /// <summary>
    /// Global callback aliases preserve backend admission, null rejection, native diagnostics and a healthy retry.
    /// </summary>
    [TestMethod]
    public async Task ManagedGlobalCallbacksValidateInvocationAndRecover()
    {
        const string Headers = """
            int calls;
            int increment(int value)
            {
                return value + ++calls;
            }

            int (*current)(int) = increment;
            """;
        const string Harness = """
            public static class BindingAssertions
            {
                public static long[] Run()
                {
                    NativeGlobals_currentCallback callback;
                    using (NativeCallTestBridge.Scope factory = new())
                    {
                        callback = NativeGlobals.current;
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
                        default(NativeGlobals_currentCallback).Invoke(100);
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
            await ExecuteManagedCallsAsync(Headers, [], Harness, globalNames: ["current"]));
    }
}
