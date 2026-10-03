using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Ankus.Generators.Tests;

public sealed partial class PgFunctionGeneratorTests
{
    private const string CallbackTypeSource = """
        [Ankus.CompilerServices.NativeFunctionPointer(7)]
        public readonly unsafe struct Hook(void* address) : Ankus.IPgNativeType
        {
            public nint Address => (nint)address;
            static int Ankus.IPgNativeType.PostgresMajor => 18;
            static string Ankus.IPgNativeType.AbiIdentity => new string('A', 64);
            static string Ankus.IPgNativeType.RuntimeIdentifier => System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier;
            static int Ankus.IPgNativeType.NativeSize => System.IntPtr.Size;
            static int Ankus.IPgNativeType.NativeAlignment => System.IntPtr.Size;
            public long Invoke(int first, long second) => throw new System.NotSupportedException();
        }
        """;

    /// <summary>
    /// Static callbacks compile within supported partial containers, including private and keyword-named properties and exact overloads.
    /// </summary>
    /// <param name="container">The complete partial declaration containing the selected property and handler.</param>
    [TestMethod]
    [DataRow("public static partial class Functions")]
    [DataRow("internal partial class Functions")]
    [DataRow("public partial struct Functions")]
    [DataRow("public readonly partial struct Functions")]
    [DataRow("public partial record Functions")]
    [DataRow("public partial record struct Functions")]
    public void NativeCallbackPropertiesCompileWithExactHandlers(string container)
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate(CallbackTypeSource + $$"""
            namespace @event
            {
                public partial class Outer
                {
                    {{container}}
                    {
                        [Ankus.PgNativeCallback(nameof(Handle))]
                        private static partial Hook @delegate { get; }
                        private static long Handle(int first, long second) => second + first;
                        private static int Handle(int first) => first;
                        public static Hook Read() => @delegate;
                    }
                }
            }
            """);
        Assert.IsEmpty(diagnostics);
        INamedTypeSymbol owner = compilation.GetTypeByMetadataName("event.Outer+Functions")!;
        INamedTypeSymbol dispatchType = Assert.ContainsSingle(owner.GetTypeMembers().Where(static type =>
            type.Name.StartsWith("AnkusDispatch_", StringComparison.Ordinal)));
        IMethodSymbol dispatcher = Assert.ContainsSingle(dispatchType.GetMembers().OfType<IMethodSymbol>().Where(static method =>
            method.GetAttributes().Any(static attribute => attribute.AttributeClass?.Name == "UnmanagedCallersOnlyAttribute")));
        AttributeData entry = Assert.ContainsSingle(dispatcher.GetAttributes());
        Assert.DoesNotContain("EntryPoint", entry.NamedArguments.Select(static argument => argument.Key));
        Assert.IsTrue(dispatcher.IsStatic);
        Assert.AreEqual(Accessibility.Private, dispatchType.DeclaredAccessibility);
        Assert.IsEmpty(dispatchType.StaticConstructors);
        IMethodSymbol accessor = Assert.ContainsSingle(owner.GetMembers().OfType<IMethodSymbol>().Where(static method =>
            method.GetAttributes().Any(static attribute => attribute.AttributeClass?.Name == "DllImportAttribute")));
        AttributeData import = Assert.ContainsSingle(accessor.GetAttributes().Where(static attribute => attribute.AttributeClass?.Name == "DllImportAttribute"));
        Assert.AreEqual("Ankus.NativeBodies", import.ConstructorArguments[0].Value);
        Assert.StartsWith("ankus_native_callback_7_", (string)import.NamedArguments.Single(static argument => argument.Key == "EntryPoint").Value.Value!);
        Assert.IsTrue(Assert.IsInstanceOfType<bool>(import.NamedArguments.Single(static argument => argument.Key == "ExactSpelling").Value.Value));
        Assert.AreEqual((int)System.Runtime.InteropServices.CallingConvention.Cdecl,
            import.NamedArguments.Single(static argument => argument.Key == "CallingConvention").Value.Value);
        Assert.AreEqual(SpecialType.System_IntPtr, Assert.ContainsSingle(accessor.Parameters).Type.SpecialType);
        AssertCallbackCompiles(compilation);
    }

    /// <summary>
    /// Every managed transport family compiles, including empty argument lists, void returns, enums and generated native aggregates.
    /// </summary>
    /// <param name="result">The exact callback result type.</param>
    /// <param name="parameters">The exact callback parameters.</param>
    /// <param name="body">The statically selected handler body.</param>
    /// <param name="checkOverflow">Whether consumer arithmetic is checked by default.</param>
    [TestMethod]
    [DataRow("void", "", "return;", false)]
    [DataRow("int", "", "return 731;", false)]
    [DataRow("bool", "bool value", "return !value;", false)]
    [DataRow("nint", "nint value", "return value;", false)]
    [DataRow("nuint", "nuint value", "return value;", false)]
    [DataRow("void*", "void* value", "return value;", false)]
    [DataRow("int*", "int* value", "return value;", false)]
    [DataRow("int**", "int** value", "return value;", false)]
    [DataRow("double", "double value", "return -value;", false)]
    [DataRow("Mode", "Mode value", "return value;", false)]
    [DataRow("Payload", "Payload value", "return value;", false)]
    [DataRow("Empty", "Empty value", "return value;", false)]
    [DataRow("Hook", "Hook value", "return value;", false)]
    [DataRow("void*", "void* value", "return value;", true)]
    [DataRow("int*", "int* value", "return value;", true)]
    [DataRow("int**", "int** value", "return value;", true)]
    public void NativeCallbackPropertiesCompileAndDispatchNativeValueShapes(string result, string parameters, string body, bool checkOverflow)
    {
        string types = CallbackTypeSource.Replace("public long Invoke(int first, long second)",
            $"public {result} Invoke({parameters})", StringComparison.Ordinal);
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate(types + $$"""
            public enum Mode : uint { First = 0xF0000001 }
            public readonly record struct Payload(long First, long Second) : Ankus.IPgNativeType
            {
                static int Ankus.IPgNativeType.PostgresMajor => 18;
                static string Ankus.IPgNativeType.AbiIdentity => new string('A', 64);
                static string Ankus.IPgNativeType.RuntimeIdentifier => System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier;
                static int Ankus.IPgNativeType.NativeSize => 16;
                static int Ankus.IPgNativeType.NativeAlignment => 8;
            }
            public readonly struct Empty : Ankus.IPgNativeType
            {
                static int Ankus.IPgNativeType.PostgresMajor => 18;
                static string Ankus.IPgNativeType.AbiIdentity => new string('A', 64);
                static string Ankus.IPgNativeType.RuntimeIdentifier => System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier;
                static int Ankus.IPgNativeType.NativeSize => 0;
                static int Ankus.IPgNativeType.NativeAlignment => 1;
            }
            public static unsafe partial class Functions
            {
                public static int Effects;
                [Ankus.PgNativeCallback(nameof(Invoke))]
                public static partial Hook Callback { get; }
                private static {{result}} Invoke({{parameters}}) { Effects++; {{body}} }
            }
            """);
        compilation = compilation.WithOptions(((CSharpCompilationOptions)compilation.Options).WithOverflowChecks(checkOverflow));
        Assert.IsEmpty(diagnostics);
        AssertCallbackCompiles(compilation);
        string argument = result switch
        {
            "void" or "int" => string.Empty,
            "bool" => "true",
            "nint" => "unchecked((nint)(long.MinValue + 0x123456789))",
            "nuint" => "unchecked((nuint)(ulong.MaxValue - 0x123456789))",
            "void*" or "int*" or "int**" => "unchecked((" + result + ")(nint)(long.MinValue + 0x123456789))",
            "double" => "System.BitConverter.Int64BitsToDouble(long.MinValue)",
            "Mode" => "Mode.First",
            "Payload" => "new Payload(long.MinValue + 17, long.MaxValue - 29)",
            "Empty" => "default(Empty)",
            _ => "new Hook((void*)0x73117)",
        };
        string size = result is "void" or "Empty" ? "0" : $"sizeof({result})";
        string frame = argument.Length == 0 ? "Ankus.CompilerServices.NativeCallArgument* arguments = null;" :
            $"{result} input = {argument}; Ankus.CompilerServices.NativeCallArgument value = new((nint)(&input), (nuint){size}); Ankus.CompilerServices.NativeCallArgument* arguments = &value;";
        compilation = ImplementCallbackAccessors(compilation).AddSyntaxTrees(CSharpSyntaxTree.ParseText($$"""
            public static unsafe class ValueProbe
            {
                [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
                private static int Resolve(nint api, void* request, nint* result, Ankus.CompilerServices.NativeCallError* error) => 0;

                public static object[] Run()
                {
                    nint* api = stackalloc nint[] { 999, 77, (nint)(delegate* unmanaged[Cdecl]<nint, void*, nint*, Ankus.CompilerServices.NativeCallError*, int>)&Resolve, 0 };
                    nint previous = Ankus.CompilerServices.NativeMemoryContext.Enter((nint)api);
                    try
                    {
                        nint address = Functions.Callback.Address;
                        {{frame}}
                        byte* storage = stackalloc byte[64];
                        new System.Span<byte>(storage, 64).Fill(0xCC);
                        Ankus.CompilerServices.NativeCallError error = default;
                        Ankus.CompilerServices.NativeCallbackContext native = new((nint)(&error), 0, (nint)api, 0, 0);
                        var invoke = (delegate* unmanaged[Cdecl]<Ankus.CompilerServices.NativeCallArgument*, nuint, nint, nuint, Ankus.CompilerServices.NativeCallbackContext*, int>)address;
                        int status = invoke(arguments, {{(argument.Length == 0 ? "0" : "1")}},
                            {{(result == "void" ? "0" : "(nint)(storage + 1)")}}, (nuint){{size}}, &native);
                        return [status, Functions.Effects, new System.ReadOnlySpan<byte>(storage + 1, {{size}}).ToArray(),
                            new System.ReadOnlySpan<byte>(storage + 1 + {{size}}, 63 - {{size}}).ToArray(), storage[0], error];
                    }
                    finally
                    {
                        Ankus.CompilerServices.NativeMemoryContext.Exit(previous);
                    }
                }
            }
            """, cancellationToken: context.CancellationToken));
        using var stream = new MemoryStream();
        Microsoft.CodeAnalysis.Emit.EmitResult emitted = compilation.Emit(stream, cancellationToken: context.CancellationToken);
        Assert.IsTrue(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        stream.Position = 0;
        var load = new AssemblyLoadContext("NativeCallbackValueProbe", isCollectible: true);
        try
        {
            Assembly assembly = load.LoadFromStream(stream);
            MethodInfo run = assembly.GetType("ValueProbe", throwOnError: true)!.GetMethod("Run")!;
            object[] observed = Assert.IsInstanceOfType<object[]>(run.Invoke(null, null));
            try
            {
                Assert.AreEqual(0, Assert.IsInstanceOfType<int>(observed[0]));
                Assert.AreEqual(1, Assert.IsInstanceOfType<int>(observed[1]));
                byte[] expected = result switch
                {
                    "void" or "Empty" => [],
                    "int" => BitConverter.GetBytes(731),
                    "bool" => [0],
                    "nint" or "void*" or "int*" or "int**" => BitConverter.GetBytes(long.MinValue + 0x123456789),
                    "nuint" => BitConverter.GetBytes(ulong.MaxValue - 0x123456789),
                    "double" => new byte[8],
                    "Mode" => BitConverter.GetBytes(0xF0000001U),
                    "Payload" => [.. BitConverter.GetBytes(long.MinValue + 17), .. BitConverter.GetBytes(long.MaxValue - 29)],
                    _ => BitConverter.GetBytes(0x73117L),
                };
                Assert.AreSequenceEqual(expected, Assert.IsInstanceOfType<byte[]>(observed[2]));
                Assert.AreSequenceEqual(Enumerable.Repeat((byte)0xCC, 63 - expected.Length),
                    Assert.IsInstanceOfType<byte[]>(observed[3]));
                Assert.AreEqual((byte)0xCC, Assert.IsInstanceOfType<byte>(observed[4]));
            }
            finally
            {
                typeof(NativeCallError).GetMethod("Release", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(observed[5], null);
            }
        }
        finally
        {
            load.Unload();
        }
    }

    /// <summary>
    /// Unsupported containing declarations cannot accidentally produce inaccessible or open native entry points.
    /// </summary>
    /// <param name="container">The unsupported containing type declaration.</param>
    [TestMethod]
    [DataRow("public class Functions")]
    [DataRow("public partial class Functions<T>")]
    [DataRow("file partial class Functions")]
    [DataRow("public partial interface Functions")]
    public void NativeCallbackDeclarationsRejectUnsupportedContainers(string container)
    {
        (_, ImmutableArray<Diagnostic> diagnostics) = Generate(CallbackTypeSource + $$"""
            {{container}}
            {
                [Ankus.PgNativeCallback(nameof(Handle))]
                public static partial Hook Callback { get; }
                private static long Handle(int first, long second) => second;
            }
            """);
        Diagnostic error = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual("ANKUS021", error.Id);
        Assert.Contains("partial classes or structs", error.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Invalid property and method shapes report one located diagnostic and do not emit a callable dispatcher.
    /// </summary>
    /// <param name="property">The candidate property declaration.</param>
    /// <param name="handler">The candidate handler declaration.</param>
    [TestMethod]
    [DataRow("public partial Hook Callback { get; }", "private static long Handle(int first, long second) => second;")]
    [DataRow("public static Hook Callback => default;", "private static long Handle(int first, long second) => second;")]
    [DataRow("public static partial Hook Callback { get; set; }", "private static long Handle(int first, long second) => second;")]
    [DataRow("public static partial Hook Callback { get; }", "private long Handle(int first, long second) => second;")]
    [DataRow("public static partial Hook Callback { get; }", "private static long Handle<T>(int first, long second) => second;")]
    [DataRow("public static partial Hook Callback { get; }", "private static long Handle(ref int first, long second) => second;")]
    [DataRow("public static partial Hook Callback { get; }", "private static long Handle(in int first, long second) => second;")]
    [DataRow("public static partial Hook Callback { get; }", "private static long Handle(out int first, long second) { first = 0; return second; }")]
    [DataRow("public static partial Hook Callback { get; }", "private static long Handle(long first, long second) => second;")]
    [DataRow("public static partial Hook Callback { get; }", "private static int Handle(int first, long second) => first;")]
    [DataRow("public static partial Hook Callback { get; }", "private static async System.Threading.Tasks.Task<long> Handle(int first, long second) { await System.Threading.Tasks.Task.Yield(); return second; }")]
    [DataRow("public static partial Hook Callback { get; }", "private static partial long Handle(int first, long second);")]
    [DataRow("public static partial Hook Callback { get; }", "[System.Runtime.InteropServices.UnmanagedCallersOnly] private static long Handle(int first, long second) => second;")]
    [DataRow("public static partial Hook Callback { get; }", "private static extern long Handle(int first, long second);")]
    [DataRow("public static partial Hook Callback { get; }", "private static long Handle(int first, long second, __arglist) => second;")]
    [DataRow("public static partial int Callback { get; }", "private static long Handle(int first, long second) => second;")]
    public void NativeCallbackDeclarationsRejectInvalidContracts(string property, string handler)
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate(CallbackTypeSource + $$"""
            public partial class Functions
            {
                [Ankus.PgNativeCallback("Handle")]
                {{property}}
                {{handler}}
            }
            """);
        Diagnostic error = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual("ANKUS021", error.Id);
        Assert.AreEqual(DiagnosticSeverity.Error, error.Severity);
        Assert.IsTrue(error.Location.IsInSource);
        Assert.IsEmpty(compilation.GetTypeByMetadataName("Functions")!.GetTypeMembers()
            .Where(static type => type.Name.StartsWith("AnkusDispatch_", StringComparison.Ordinal)));
        (Compilation corrected, ImmutableArray<Diagnostic> retry) = Generate(CallbackTypeSource + """
            public partial class Functions
            {
                [Ankus.PgNativeCallback(nameof(Handle))]
                public static partial Hook Callback { get; }
                private static long Handle(int first, long second) => second ^ first;
            }
            """);
        Assert.IsEmpty(retry);
        AssertCallbackCompiles(corrected);
    }

    /// <summary>
    /// Native pointer metadata must describe one complete callable contract before a callback can be emitted.
    /// </summary>
    /// <param name="original">The valid native pointer contract fragment.</param>
    /// <param name="replacement">A missing, malformed or ambiguous contract fragment.</param>
    [TestMethod]
    [DataRow("[Ankus.CompilerServices.NativeFunctionPointer(7)]", "")]
    [DataRow("[Ankus.CompilerServices.NativeFunctionPointer(7)]", "[Ankus.CompilerServices.NativeFunctionPointer(-1)]")]
    [DataRow("[Ankus.CompilerServices.NativeFunctionPointer(7)]", "[Ankus.CompilerServices.NativeFunctionPointer(7), Ankus.CompilerServices.NativeFunctionPointer(8)]")]
    [DataRow("Hook(void* address)", "Hook(nint address)")]
    [DataRow("Hook(void* address)", "Hook(int* address)")]
    [DataRow("public long Invoke(int first, long second)", "private long Invoke(int first, long second)")]
    [DataRow("public long Invoke(int first, long second)", "public static long Invoke(int first, long second)")]
    [DataRow("public long Invoke(int first, long second)", "public long Invoke<T>(int first, long second)")]
    [DataRow("public long Invoke(int first, long second)", "public long Invoke(ref int first, long second)")]
    [DataRow("public long Invoke(int first, long second)", "public long Invoke(int first, long second, __arglist)")]
    [DataRow("public long Invoke(int first, long second)", "public long Invoke() => 0; public long Invoke(int first, long second)")]
    [DataRow("public long Invoke(int first, long second)", "public decimal Invoke(int first, long second)")]
    public void NativeCallbackDeclarationsRejectInvalidMetadata(string original, string replacement)
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate(
            CallbackTypeSource.Replace(original, replacement, StringComparison.Ordinal) + """
            public static partial class Functions
            {
                [Ankus.PgNativeCallback(nameof(Handle))]
                public static partial Hook Callback { get; }
                private static long Handle(int first, long second) => second;
            }
            """);
        Diagnostic error = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual("ANKUS021", error.Id);
        Assert.AreEqual(DiagnosticSeverity.Error, error.Severity);
        Assert.Contains("complete fixed Invoke signature", error.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
        Assert.IsEmpty(compilation.GetTypeByMetadataName("Functions")!.GetTypeMembers());
    }

    /// <summary>
    /// Missing and blank static handler names fail at the property declaration without generating an entry point.
    /// </summary>
    /// <param name="name">The attribute argument expression.</param>
    [TestMethod]
    [DataRow("null")]
    [DataRow("\"\"")]
    [DataRow("\" \"")]
    [DataRow("\"Absent\"")]
    public void NativeCallbackDeclarationsRejectMissingHandler(string name)
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate(CallbackTypeSource + $$"""
            public static partial class Functions
            {
                [Ankus.PgNativeCallback({{name}})]
                public static partial Hook Callback { get; }
            }
            """);
        Diagnostic error = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual("ANKUS021", error.Id);
        Assert.IsTrue(error.Location.IsInSource);
        Assert.IsEmpty(compilation.GetTypeByMetadataName("Functions")!.GetTypeMembers());
    }

    /// <summary>
    /// An exact void signature cannot hide asynchronous work outside the generated exception boundary.
    /// </summary>
    /// <param name="partial">Whether the asynchronous body implements a separate partial definition.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void NativeCallbackDeclarationsRejectAsyncVoidHandler(bool partial)
    {
        string types = CallbackTypeSource.Replace("public long Invoke(int first, long second)",
            "public void Invoke()", StringComparison.Ordinal);
        string definition = partial ? "private static partial void Handle();" : string.Empty;
        string modifier = partial ? "partial " : string.Empty;
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate(types + $$"""
            public static partial class Functions
            {
                [Ankus.PgNativeCallback(nameof(Handle))]
                public static partial Hook Callback { get; }
                {{definition}}
                private static async {{modifier}}void Handle() => await System.Threading.Tasks.Task.Yield();
            }
            """);
        Diagnostic error = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual("ANKUS021", error.Id);
        Assert.Contains("synchronous", error.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
        Assert.IsEmpty(compilation.GetTypeByMetadataName("Functions")!.GetTypeMembers());
    }

    /// <summary>
    /// The generated dispatcher preserves exact values, rejects malformed frames before effects, catches handler errors and restores the outer capability.
    /// </summary>
    /// <param name="scenario">A valid call, malformed argument count, malformed argument storage, malformed result storage, or handler failure.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    public void NativeCallbackPropertiesCompileAndDispatch(int scenario)
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate(CallbackTypeSource + """
            public static partial class Functions
            {
                public static int Effects;
                public static object Lease = null!;
                [Ankus.PgNativeCallback(nameof(Handle))]
                public static partial Hook Callback { get; }
                private static long Handle(int first, long second)
                {
                    Effects++;
                    _ = Ankus.PgMemoryContext.Current;
                    Lease = CallbackProbe.CaptureLease();
                    if (first < 0)
                    {
                        throw new Ankus.PgException("22023", "callback café", "owned callback detail", "retry callback");
                    }

                    return second ^ ((long)first << 33);
                }
            }
            """);
        Assert.IsEmpty(diagnostics);
        compilation = ImplementCallbackAccessors(compilation).AddSyntaxTrees(CSharpSyntaxTree.ParseText("""
            public static unsafe class CallbackProbe
            {
                private static nint s_provider;
                public static object CaptureLease() => typeof(Ankus.CompilerServices.NativeMemoryContext)
                    .GetProperty("BorrowScope", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.GetValue(null)!;
                private static void ValidateLease(object lease) => lease.GetType()
                    .GetMethod("Validate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(lease, null);

                [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
                private static int Resolve(nint api, void* request, nint* result, Ankus.CompilerServices.NativeCallError* error)
                {
                    s_provider = ((nint*)api)[0];
                    result[0] = 77;
                    return 0;
                }

                public static object[] Run(int scenario)
                {
                    nint* outer = stackalloc nint[] { 888, 77, (nint)(delegate* unmanaged[Cdecl]<nint, void*, nint*, Ankus.CompilerServices.NativeCallError*, int>)&Resolve, 0 };
                    nint* inner = stackalloc nint[] { 999, 77, (nint)(delegate* unmanaged[Cdecl]<nint, void*, nint*, Ankus.CompilerServices.NativeCallError*, int>)&Resolve, 0 };
                    nint previous = Ankus.CompilerServices.NativeMemoryContext.Enter((nint)outer);
                    try
                    {
                        object outerLease = CaptureLease();
                        nint address = Functions.Callback.Address;
                        int first = scenario == 4 ? -13 : 13;
                        long second = long.MinValue + 0x123456789;
                        Ankus.CompilerServices.NativeCallArgument* arguments = stackalloc Ankus.CompilerServices.NativeCallArgument[]
                        {
                            new((nint)(&first), sizeof(int)), new((nint)(&second), scenario == 2 ? 7U : sizeof(long)),
                        };
                        long result = 17;
                        Ankus.CompilerServices.NativeCallError error = default;
                        Ankus.CompilerServices.NativeCallbackContext native = new((nint)(&error), 0, (nint)inner, 0, 0);
                        var invoke = (delegate* unmanaged[Cdecl]<Ankus.CompilerServices.NativeCallArgument*, nuint, nint, nuint, Ankus.CompilerServices.NativeCallbackContext*, int>)address;
                        int status = invoke(arguments, scenario == 1 ? 1U : 2U, (nint)(&result), scenario == 3 ? 9U : sizeof(long), &native);
                        nint provider = s_provider;
                        _ = Ankus.PgMemoryContext.Current;
                        long[] observed = [status, result, Functions.Effects, provider, s_provider, Functions.Callback.Address == address ? 1 : 0];
                        object firstLease = Functions.Lease;
                        ValidateLease(outerLease);
                        bool restored = System.Object.ReferenceEquals(outerLease, CaptureLease());
                        object failure = error;
                        error = default;
                        first = 13;
                        arguments[1] = new((nint)(&second), sizeof(long));
                        int recovered = invoke(arguments, 2, (nint)(&result), sizeof(long), &native);
                        ValidateLease(outerLease);
                        restored &= System.Object.ReferenceEquals(outerLease, CaptureLease());
                        long[] retry = [recovered, result, Functions.Effects];
                        return [observed, failure, retry, error, outerLease, firstLease, Functions.Lease, restored];
                    }
                    finally
                    {
                        Ankus.CompilerServices.NativeMemoryContext.Exit(previous);
                    }
                }
            }
            """, cancellationToken: context.CancellationToken));
        using var stream = new MemoryStream();
        Microsoft.CodeAnalysis.Emit.EmitResult emitted = compilation.Emit(stream, cancellationToken: context.CancellationToken);
        Assert.IsTrue(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        stream.Position = 0;
        var load = new AssemblyLoadContext("NativeCallbackProbe", isCollectible: true);
        try
        {
            Assembly assembly = load.LoadFromStream(stream);
            MethodInfo run = assembly.GetType("CallbackProbe", throwOnError: true)!.GetMethod("Run")!;
            object[] result = Assert.IsInstanceOfType<object[]>(run.Invoke(null, [scenario]));
            MethodInfo release = typeof(NativeCallError).GetMethod("Release", BindingFlags.Instance | BindingFlags.NonPublic)!;
            try
            {
                const long Expected = (long.MinValue + 0x123456789) ^ (13L << 33);
                int effects = scenario is 0 or 4 ? 1 : 0;
                long[] actual = Assert.IsInstanceOfType<long[]>(result[0]);
                Assert.AreSequenceEqual<long>([scenario == 0 ? 0 : 1, scenario == 0 ? Expected : 17, effects, 999, 888, 1], actual);
                Assert.AreSequenceEqual<long>([0, Expected, effects + 1], Assert.IsInstanceOfType<long[]>(result[2]));
                Assert.IsTrue(Assert.IsInstanceOfType<bool>(result[7]));
                Assert.AreNotSame(result[4], result[6]);
                foreach (object lease in new[] { result[4], result[6] })
                {
                    MethodInfo validate = lease.GetType().GetMethod("Validate", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    TargetInvocationException expired = Assert.ThrowsExactly<TargetInvocationException>(() => validate.Invoke(lease, null));
                    Assert.IsInstanceOfType<ObjectDisposedException>(expired.InnerException);
                }

                if (scenario is 0 or 4)
                {
                    Assert.AreNotSame(result[5], result[6]);
                    MethodInfo validate = result[5].GetType().GetMethod("Validate", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    TargetInvocationException expired = Assert.ThrowsExactly<TargetInvocationException>(() => validate.Invoke(result[5], null));
                    Assert.IsInstanceOfType<ObjectDisposedException>(expired.InnerException);
                }
                else
                {
                    Assert.IsNull(result[5]);
                }

                if (scenario != 0)
                {
                    MethodInfo read = typeof(NativeCallError).GetMethod("ToException", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    PgException error = Assert.IsInstanceOfType<PgException>(read.Invoke(result[1], null));
                    Assert.AreEqual(scenario == 4 ? "22023" : "38000", error.SqlState);
                    Assert.Contains(scenario switch
                    {
                        1 => "argument frame",
                        2 => "argument storage",
                        3 => "result storage",
                        _ => "callback café",
                    }, error.Message);
                    if (scenario == 4)
                    {
                        Assert.AreEqual("owned callback detail", error.Detail);
                        Assert.AreEqual("retry callback", error.Hint);
                    }
                }
            }
            finally
            {
                release.Invoke(result[1], null);
                release.Invoke(result[3], null);
            }
        }
        finally
        {
            load.Unload();
        }
    }

    /// <summary>
    /// A user type initializer fails inside the generated guard rather than before the unmanaged dispatcher's try block.
    /// </summary>
    [TestMethod]
    public void NativeCallbackTypeInitializationCannotEscapeBoundary()
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate(CallbackTypeSource + """
            public static partial class Functions
            {
                static Functions() => throw new System.InvalidOperationException("handler type initialization");
                [Ankus.PgNativeCallback(nameof(Handle))]
                public static partial Hook Callback { get; }
                private static long Handle(int first, long second) => first + second;
            }
            public static unsafe class InitializationProbe
            {
                [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
                private static int Resolve(nint api, void* request, nint* result, Ankus.CompilerServices.NativeCallError* error) => 0;
                public static object[] Run(nint address)
                {
                    nint* api = stackalloc nint[] { 999, 77, (nint)(delegate* unmanaged[Cdecl]<nint, void*, nint*, Ankus.CompilerServices.NativeCallError*, int>)&Resolve, 0 };
                    Ankus.CompilerServices.NativeCallError error = default;
                    Ankus.CompilerServices.NativeCallbackContext native = new((nint)(&error), 0, (nint)api, 0, 0);
                    int first = 13;
                    long second = 29;
                    long result = 71;
                    Ankus.CompilerServices.NativeCallArgument* arguments = stackalloc Ankus.CompilerServices.NativeCallArgument[]
                    {
                        new((nint)(&first), sizeof(int)), new((nint)(&second), sizeof(long)),
                    };
                    var invoke = (delegate* unmanaged[Cdecl]<Ankus.CompilerServices.NativeCallArgument*, nuint, nint, nuint, Ankus.CompilerServices.NativeCallbackContext*, int>)address;
                    int status = invoke(arguments, 2, (nint)(&result), sizeof(long), &native);
                    return [status, result, error];
                }
            }
            """);
        Assert.IsEmpty(diagnostics);
        compilation = ImplementCallbackAccessors(compilation);
        using var stream = new MemoryStream();
        Microsoft.CodeAnalysis.Emit.EmitResult emitted = compilation.Emit(stream, cancellationToken: context.CancellationToken);
        Assert.IsTrue(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        stream.Position = 0;
        var load = new AssemblyLoadContext("NativeCallbackInitializationProbe", isCollectible: true);
        try
        {
            Assembly assembly = load.LoadFromStream(stream);
            Type dispatcher = Assert.ContainsSingle(assembly.GetType("Functions", throwOnError: true)!.GetNestedTypes(BindingFlags.NonPublic));
            MethodInfo entry = dispatcher.GetMethod("Invoke", BindingFlags.Static | BindingFlags.NonPublic)!;
            MethodInfo run = assembly.GetType("InitializationProbe", throwOnError: true)!.GetMethod("Run")!;
            object[] values = Assert.IsInstanceOfType<object[]>(run.Invoke(null, [entry.MethodHandle.GetFunctionPointer()]));
            try
            {
                Assert.AreEqual(1, values[0]);
                Assert.AreEqual(71L, values[1]);
                PgException error = Assert.IsInstanceOfType<PgException>(typeof(NativeCallError)
                    .GetMethod("ToException", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(values[2], null));
                Assert.AreEqual("38000", error.SqlState);
                Assert.Contains("Functions", error.Message);
            }
            finally
            {
                typeof(NativeCallError).GetMethod("Release", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(values[2], null);
            }
        }
        finally
        {
            load.Unload();
        }
    }

    /// <summary>
    /// Supplies the native accessor only for managed dispatch execution; native ABI and linking require separate SDK/AOT witnesses.
    /// </summary>
    private static Compilation ImplementCallbackAccessors(Compilation compilation)
    {
        foreach (SyntaxTree tree in compilation.SyntaxTrees.ToArray())
        {
            SyntaxNode root = tree.GetRoot();
            MethodDeclarationSyntax[] imports = [.. root.DescendantNodes().OfType<MethodDeclarationSyntax>()
                .Where(static method => method.Identifier.ValueText.StartsWith("AnkusCallback_", StringComparison.Ordinal))];
            if (imports.Length == 0)
            {
                continue;
            }

            SyntaxNode rewritten = root.ReplaceNodes(imports, static (_, method) => method.WithAttributeLists(default)
                .WithModifiers(SyntaxFactory.TokenList(method.Modifiers.Where(static token => !token.IsKind(SyntaxKind.ExternKeyword))))
                .WithExpressionBody(SyntaxFactory.ArrowExpressionClause(SyntaxFactory.IdentifierName("target"))));
            compilation = compilation.ReplaceSyntaxTree(tree, tree.WithRootAndOptions(rewritten, tree.Options));
        }

        return compilation;
    }

    private void AssertCallbackCompiles(Compilation compilation)
    {
        using var stream = new MemoryStream();
        Microsoft.CodeAnalysis.Emit.EmitResult emitted = compilation.Emit(stream, cancellationToken: context.CancellationToken);
        Assert.IsTrue(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
    }
}
