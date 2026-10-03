namespace Ankus.Build.Tests;

public sealed partial class NativeBindingNativeTests
{
    /// <summary>
    /// Variadic, unprototyped and incomplete-result pointers retain exact address transport without exposing a guessed invocation API.
    /// </summary>
    [TestMethod]
    public async Task ManagedIndirectPointersRetainUnsupportedSignatures()
    {
        const string Headers = """
            struct Missing;
            typedef int (*Variadic)(int, ...);
            typedef int (*Unprototyped)();
            typedef struct Missing (*Incomplete)(void);
            typedef struct Carrier { Variadic variable; Unprototyped legacy; Incomplete opaque; } Carrier;
            Carrier state = {0};
            """;
        const string Harness = """
            public static class BindingAssertions
            {
                public static unsafe long[] Run()
                {
                    using NativeCallTestBridge.Scope scope = new();
                    Carrier value = NativeGlobals.state;
                    int originallyNull = value.variable.IsNull && value.legacy.IsNull && value.opaque.IsNull ? 1 : 0;
                    value.variable = new Variadic((void*)123);
                    value.legacy = new Unprototyped((void*)456);
                    value.opaque = new Incomplete((void*)789);
                    NativeGlobals.state = value;
                    Carrier recovered = NativeGlobals.state;
                    return [originallyNull, (nint)recovered.variable.DangerousGetAddress(), (nint)recovered.legacy.DangerousGetAddress(), (nint)recovered.opaque.DangerousGetAddress(),
                        typeof(Variadic).GetMethod("Invoke") is null ? 1 : 0, typeof(Unprototyped).GetMethod("Invoke") is null ? 1 : 0,
                        typeof(Incomplete).GetMethod("Invoke") is null ? 1 : 0, scope.Validations, scope.Invocations];
                }
            }
            """;
        Assert.AreSequenceEqual<long>([1, 123, 456, 789, 1, 1, 1, 3, 3],
            await ExecuteManagedCallsAsync(Headers, [], Harness, globalNames: ["state"]));
    }

    /// <summary>
    /// Native typedef names cannot collide with pointer operations, stored state or C# contextual native integer types.
    /// </summary>
    /// <param name="name">The native typedef identifier that requires a distinct managed type name.</param>
    [TestMethod]
    [DataRow("Invoke")]
    [DataRow("IsNull")]
    [DataRow("DangerousGetAddress")]
    [DataRow("GetNativeBody")]
    [DataRow("_address")]
    [DataRow("nint")]
    [DataRow("nuint")]
    public async Task ManagedIndirectCallsPreserveReservedNames(string name)
    {
        string headers = "typedef int (*" + name + ")(int); int triple(int value) { return value * 3; } " + name + " get(void) { return triple; }";
        string harness = """
            public static class BindingAssertions
            {
                public static long[] Run()
                {
                    using NativeCallTestBridge.Scope scope = new();
                    int result = NativeMethods.get().Invoke(9);
                    return [result, typeof(NativeMethods).GetMethod("get")!.ReturnType.Name == "Native___NAME__" ? 1 : 0,
                        scope.Validations, scope.Invocations];
                }
            }
            """;
        Assert.AreSequenceEqual<long>([27, 1, 2, 2], await ExecuteManagedCallsAsync(headers, ["get"], harness.Replace("__NAME__", name, StringComparison.Ordinal)));
    }

    /// <summary>
    /// Null and unavailable scopes reject before lookup, nested binding changes revalidate, and owned native diagnostics permit a corrected retry.
    /// </summary>
    [TestMethod]
    public async Task ManagedIndirectCallsValidateEveryInvocationAndRecover()
    {
        const string Headers = """
            typedef int (*Operation)(int);
            int calls;
            int increment(int value) { return value + ++calls; }
            Operation get(void) { return increment; }
            """;
        const string Harness = """
            public static class BindingAssertions
            {
                public static unsafe long[] Run()
                {
                    Operation operation;
                    using (NativeCallTestBridge.Scope factory = new())
                    {
                        operation = NativeMethods.get();
                    }

                    int absent = 0;
                    foreach (Operation value in new[] { operation, default(Operation) })
                    {
                        try
                        {
                            value.Invoke(7);
                        }
                        catch (InvalidOperationException error) when (error.Message.Contains("active backend callback", StringComparison.Ordinal))
                        {
                            absent++;
                        }
                    }

                    using NativeCallTestBridge.Scope outer = new();
                    int nullTarget = 0;
                    try
                    {
                        default(Operation).Invoke(7);
                    }
                    catch (InvalidOperationException error) when (error.Message.Contains("null native function pointer", StringComparison.Ordinal))
                    {
                        nullTarget++;
                    }

                    int enteredBefore = NativeCallTestBridge.Accessors - 1 + outer.Invocations;
                    int first = operation.Invoke(4);
                    int nested = 0;
                    int nestedValidations;
                    int nestedInvocations;
                    using (NativeCallTestBridge.Scope inner = new())
                    {
                        inner.Identity = "different";
                        try
                        {
                            operation.Invoke(100);
                        }
                        catch (PgException error) when (error.SqlState == "0A000")
                        {
                            nested++;
                        }

                        nestedValidations = inner.Validations;
                        nestedInvocations = inner.Invocations;
                    }

                    PgException? failure = null;
                    outer.RejectCall = true;
                    try
                    {
                        operation.Invoke(1000);
                    }
                    catch (PgException error)
                    {
                        failure = error;
                    }

                    outer.RejectCall = false;
                    Operation restored = new(operation.DangerousGetAddress());
                    int recovered = restored.Invoke(7);
                    return [absent, nullTarget, enteredBefore, first, recovered, nested, nestedValidations, nestedInvocations,
                        failure?.SqlState == "22023" ? 1 : 0, failure?.Message == "native call café" ? 1 : 0,
                        failure?.Detail == "detail naïve" ? 1 : 0, failure?.Hint == "hint déjà" ? 1 : 0,
                        outer.Validations, outer.Invocations, NativeCallTestBridge.Accessors - 1];
                }
            }
            """;
        Assert.AreSequenceEqual<long>([2, 1, 0, 5, 9, 1, 1, 0, 1, 1, 1, 1, 4, 3, 3],
            await ExecuteManagedCallsAsync(Headers, ["get"], Harness));
    }

    /// <summary>
    /// Large aligned indirect arguments and results release every allocation after native errors, null targets and allocation failures.
    /// </summary>
    [TestMethod]
    public async Task ManagedIndirectCallsReleaseLargeFramesAndRecover()
    {
        const string Headers = """
            #if defined(_MSC_VER) && !defined(__clang__)
            typedef struct __declspec(align(64)) Payload { unsigned char bytes[8192]; } Payload;
            #else
            typedef struct __attribute__((aligned(64))) Payload { unsigned char bytes[8192]; } Payload;
            #endif
            typedef Payload (*Transform)(Payload);
            int calls;
            Payload change(Payload value) { value.bytes[0] += 7; value.bytes[1] = (unsigned char)++calls; value.bytes[8191] ^= 0xff; return value; }
            Transform get(void) { return change; }
            """;
        const string Harness = """
            public static class BindingAssertions
            {
                public static long[] Run()
                {
                    using NativeCallTestBridge.Scope scope = new();
                    Transform target = NativeMethods.get();
                    Payload value = default;
                    value.bytes[0] = 17;
                    value.bytes[8191] = 201;
                    int nullTarget = 0;
                    try
                    {
                        default(Transform).Invoke(value);
                    }
                    catch (InvalidOperationException error) when (error.Message.Contains("null native function pointer", StringComparison.Ordinal))
                    {
                        nullTarget++;
                    }

                    int allocatedForNull = NativeCallTestBridge.Allocator.Allocations;
                    scope.RejectCall = true;
                    PgException? failure = null;
                    try
                    {
                        target.Invoke(value);
                    }
                    catch (PgException error)
                    {
                        failure = error;
                    }

                    int liveAfterError = NativeCallTestBridge.Allocator.Live;
                    scope.RejectCall = false;
                    Payload first = target.Invoke(value);
                    int beforeFailure = NativeCallTestBridge.Accessors + scope.Invocations;
                    NativeCallTestBridge.Allocator.Reject = true;
                    int rejected = 0;
                    try
                    {
                        target.Invoke(value);
                    }
                    catch (OutOfMemoryException)
                    {
                        rejected++;
                    }

                    int enteredOnFailure = NativeCallTestBridge.Accessors + scope.Invocations - beforeFailure;
                    NativeCallTestBridge.Allocator.Reject = false;
                    Payload recovered = target.Invoke(value);
                    return [nullTarget, allocatedForNull, failure?.SqlState == "22023" ? 1 : 0, liveAfterError,
                        first.bytes[0], first.bytes[1], first.bytes[8191], recovered.bytes[1], value.bytes[0], value.bytes[8191],
                        rejected, enteredOnFailure, NativeCallTestBridge.Allocator.Allocations, NativeCallTestBridge.Allocator.Releases,
                        NativeCallTestBridge.Allocator.Live, scope.Validations, scope.Invocations];
                }
            }
            """;
        Assert.AreSequenceEqual<long>([1, 0, 1, 0, 24, 1, 54, 2, 17, 201, 1, 0, 3, 3, 0, 6, 4],
            await ExecuteManagedCallsAsync(Headers, ["get"], Harness));
    }

    /// <summary>
    /// Zero-byte native results retain distinct logical types while qualified parameters share the same value identity.
    /// </summary>
    [TestMethod]
    public async Task ManagedIndirectCallsPreserveEmptyValuesAndNames()
    {
        const string Headers = """
            typedef struct {} First;
            typedef struct {} Second;
            typedef First (*Factory)(void);
            typedef Second (*Invoke)(const First);
            int calls;
            First first(void) { First result; calls = 3; return result; }
            Second change(const First value) { Second result; (void)value; calls *= 7; return result; }
            Factory get_factory(void) { return first; }
            Invoke get_change(void) { return change; }
            int finish(Second value) { (void)value; return calls; }
            """;
        const string Harness = """
            public static class BindingAssertions
            {
                public static long[] Run()
                {
                    using NativeCallTestBridge.Scope scope = new();
                    Native_Invoke change = NativeMethods.get_change();
                    Factory factory = NativeMethods.get_factory();
                    int result = NativeMethods.finish(change.Invoke(factory.Invoke()));
                    System.Type first = typeof(Factory).GetMethod("Invoke")!.ReturnType;
                    System.Reflection.MethodInfo transform = typeof(Native_Invoke).GetMethod("Invoke")!;
                    return [result, first == transform.GetParameters()[0].ParameterType ? 1 : 0,
                        first != transform.ReturnType ? 1 : 0, scope.Validations, scope.Invocations,
                        NativeCallTestBridge.Allocator.Allocations];
                }
            }
            """;
        Assert.AreSequenceEqual<long>([21, 1, 1, 5, 5, 0],
            await ExecuteManagedCallsAsync(Headers, ["get_factory", "get_change", "finish"], Harness, nativeCompiler: false));
    }

    /// <summary>
    /// Function pointers retain identity through results, parameters, qualified globals, record fields and arrays while invoking actual native targets.
    /// </summary>
    [TestMethod]
    public async Task ManagedIndirectCallsPreserveValuesAndIdentity()
    {
        const string Headers = """
            typedef int (*Arithmetic)(int);
            typedef Arithmetic ArithmeticAlias;
            typedef struct Registry { Arithmetic left; ArithmeticAlias entries[2]; } Registry;
            int first(int value) { return value + 7; }
            int second(int value) { return value * 3; }
            Arithmetic current = first;
            const ArithmeticAlias original = first;
            Arithmetic choose(int use_second) { return use_second ? second : first; }
            Registry bundle(void) { Registry value = { first, { first, second } }; return value; }
            int consume(ArithmeticAlias target, int value) { return target(value); }
            """;
        const string Harness = """
            public static class BindingAssertions
            {
                public static unsafe long[] Run()
                {
                    using NativeCallTestBridge.Scope scope = new();
                    Arithmetic first = NativeMethods.choose(0);
                    Arithmetic second = NativeMethods.choose(1);
                    int firstResult = first.Invoke(5);
                    int secondResult = second.Invoke(5);
                    Arithmetic saved = NativeGlobals.current;
                    Arithmetic original = NativeGlobals.original;
                    NativeGlobals.current = second;
                    int currentResult = NativeGlobals.current.Invoke(7);
                    Registry registry = NativeMethods.bundle();
                    int fieldResult = registry.left.Invoke(9);
                    int arrayResult = registry.entries[1].Invoke(11);
                    int argumentResult = NativeMethods.consume(first, 4);
                    return [firstResult, secondResult, currentResult, fieldResult, arrayResult, argumentResult,
                        saved.DangerousGetAddress() == first.DangerousGetAddress() ? 1 : 0,
                        original.DangerousGetAddress() == first.DangerousGetAddress() ? 1 : 0,
                        registry.entries[0].DangerousGetAddress() == first.DangerousGetAddress() ? 1 : 0,
                        first.IsNull ? 1 : 0, default(Arithmetic).IsNull ? 1 : 0,
                        System.Runtime.CompilerServices.Unsafe.SizeOf<Arithmetic>() == System.IntPtr.Size ? 1 : 0,
                        Metadata<Arithmetic>(),
                        scope.Validations, scope.Invocations, NativeCallTestBridge.Accessors];
                }

                private static int Metadata<T>() where T : unmanaged, IPgNativeType
                    => T.NativeSize == System.IntPtr.Size && T.NativeAlignment == System.IntPtr.Size && T.PostgresMajor == 18 &&
                        T.AbiIdentity == NativeBinding.Identity && T.RuntimeIdentifier == System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier ? 1 : 0;
            }
            """;
        Assert.AreSequenceEqual<long>([12, 15, 21, 16, 33, 11, 1, 1, 1, 0, 1, 1, 1, 13, 13, 13],
            await ExecuteManagedCallsAsync(Headers, ["choose", "bundle", "consume"], Harness, globalNames: ["current", "original"]));
    }
}
