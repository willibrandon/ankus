using Microsoft.CodeAnalysis;

namespace Ankus.Generators.Tests;

public sealed partial class NativeUnsafeAccessAnalyzerTests
{
    /// <summary>
    /// Compiler transport contracts that accept arbitrary addresses or handles require acknowledgment at each actual use.
    /// </summary>
    /// <param name="body">The compiler-valid raw transport caller.</param>
    /// <param name="expression">The exact invocation or method group that must require unsafe.</param>
    [TestMethod]
    [DataRow("_ = Ankus.CompilerServices.NativeRawCallback.Read<int>(new(1, 4));", "Ankus.CompilerServices.NativeRawCallback.Read<int>(new(1, 4))")]
    [DataRow("_ = Ankus.CompilerServices.NativeRawCallback.ReadNative<T>(new(1, 4));", "Ankus.CompilerServices.NativeRawCallback.ReadNative<T>(new(1, 4))")]
    [DataRow("Ankus.CompilerServices.NativeRawCallback.Write(1, 4, 42);", "Ankus.CompilerServices.NativeRawCallback.Write(1, 4, 42)")]
    [DataRow("Ankus.CompilerServices.NativeRawCallback.WriteNative<T>(1, 4, default);", "Ankus.CompilerServices.NativeRawCallback.WriteNative<T>(1, 4, default)")]
    [DataRow("_ = Ankus.CompilerServices.NativeBackend.Enter(1);", "Ankus.CompilerServices.NativeBackend.Enter(1)")]
    [DataRow("Ankus.CompilerServices.NativeBackend.Exit(1);", "Ankus.CompilerServices.NativeBackend.Exit(1)")]
    [DataRow("_ = Ankus.CompilerServices.NativeMemoryContext.Enter(1);", "Ankus.CompilerServices.NativeMemoryContext.Enter(1)")]
    [DataRow("Ankus.CompilerServices.NativeMemoryContext.Exit(1);", "Ankus.CompilerServices.NativeMemoryContext.Exit(1)")]
    [DataRow("_ = Ankus.CompilerServices.NativeGuc.Enter(1);", "Ankus.CompilerServices.NativeGuc.Enter(1)")]
    [DataRow("Ankus.CompilerServices.NativeGuc.Exit(1);", "Ankus.CompilerServices.NativeGuc.Exit(1)")]
    [DataRow("_ = Ankus.CompilerServices.NativeLog.Enter(1);", "Ankus.CompilerServices.NativeLog.Enter(1)")]
    [DataRow("Ankus.CompilerServices.NativeLog.Exit(1);", "Ankus.CompilerServices.NativeLog.Exit(1)")]
    [DataRow("_ = Ankus.CompilerServices.NativeAggregate.Enter([], 1, 1);", "Ankus.CompilerServices.NativeAggregate.Enter([], 1, 1)")]
    [DataRow("_ = Ankus.CompilerServices.NativeBackend.CaptureFunction(1);", "Ankus.CompilerServices.NativeBackend.CaptureFunction(1)")]
    [DataRow("_ = Ankus.CompilerServices.NativeSet.MoveNext<int>(1, out _);", "Ankus.CompilerServices.NativeSet.MoveNext<int>(1, out _)")]
    [DataRow("nint handle = 1; Ankus.CompilerServices.NativeSet.Dispose(ref handle);", "Ankus.CompilerServices.NativeSet.Dispose(ref handle)")]
    [DataRow("_ = Ankus.CompilerServices.NativeRelationScope.ForIterator(1);", "Ankus.CompilerServices.NativeRelationScope.ForIterator(1)")]
    [DataRow("System.Func<Ankus.CompilerServices.NativeCallArgument, int> read = Ankus.CompilerServices.NativeRawCallback.Read<int>; _ = read;", "Ankus.CompilerServices.NativeRawCallback.Read<int>")]
    [DataRow("System.Action<nint, nuint, int> write = Ankus.CompilerServices.NativeRawCallback.Write<int>; _ = write;", "Ankus.CompilerServices.NativeRawCallback.Write<int>")]
    [DataRow("System.Func<nint, bool, nint> enter = Ankus.CompilerServices.NativeBackend.Enter; _ = enter;", "Ankus.CompilerServices.NativeBackend.Enter")]
    [DataRow("System.Func<nint, nint> enter = Ankus.CompilerServices.NativeLog.Enter; _ = enter;", "Ankus.CompilerServices.NativeLog.Enter")]
    public async Task CompilerTransportRawApisRequireExplicitContext(string body, string expression)
    {
        const string Prefix = "public static void Run<T>() where T : unmanaged, Ankus.IPgNativeType { ";
        Diagnostic error = Assert.ContainsSingle(await Analyze(Prefix + body + " }"));
        Assert.AreEqual("ANKUS129", error.Id);
        Assert.AreEqual(DiagnosticSeverity.Error, error.Severity);
        Assert.AreEqual(expression, error.Location.SourceTree!.GetText(context.CancellationToken).ToString(error.Location.SourceSpan));
        Assert.IsEmpty(await Analyze(Prefix + "unsafe { " + body + " } }"));
    }
}
