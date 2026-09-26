namespace Ankus.Build.Tests;

/// <summary>
/// Verifies header command validation, bounded compiler output and cancellation before process creation.
/// </summary>
/// <param name="context">The test cancellation context.</param>
[TestClass]
public sealed class NativeBindingHeaderCommandTests(TestContext context)
{
    /// <summary>
    /// Invalid command shapes fail before input or toolchain access.
    /// </summary>
    /// <param name="count">The unsupported argument count.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(3)]
    [DataRow(9)]
    public async Task InvalidHeaderCommandAritiesFailExplicitly(int count)
    {
        ArgumentException failure = await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            NativeBindingHeaderCommand.RunAsync([.. Enumerable.Repeat("", count)], context.CancellationToken));
        Assert.AreEqual("arguments", failure.ParamName);
    }

    /// <summary>
    /// Bad selections cannot start discovery or replace a previously validated contract.
    /// </summary>
    /// <param name="names">The invalid requested symbol list.</param>
    [TestMethod]
    [DataRow("missing_native_symbol\n")]
    [DataRow("ExecutorRun\nExecutorRun\n")]
    [DataRow("ExecutorRun;\n")]
    public async Task InvalidHeaderSelectionsPreserveOutput(string names)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"ankus-header-command-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string list = Path.Combine(directory, "symbols.txt");
            string contract = Path.Combine(directory, "native-header-types.json");
            await File.WriteAllTextAsync(list, names, context.CancellationToken);
            await File.WriteAllTextAsync(contract, "existing-contract", context.CancellationToken);
            await Assert.ThrowsExactlyAsync<FormatException>(() => NativeBindingHeaderCommand.RunAsync(
                [list, "18", "missing-pg-config", directory], context.CancellationToken));
            Assert.AreEqual("existing-contract", await File.ReadAllTextAsync(contract, context.CancellationToken));
            Assert.AreSequenceEqual<string>(["native-header-types.json", "symbols.txt"], Directory.GetFiles(directory).Select(Path.GetFileName).Order(StringComparer.Ordinal)!);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// The byte bound preserves every accepted byte across empty, interior and buffer-boundary inputs.
    /// </summary>
    /// <param name="count">The exact accepted input extent.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(65535)]
    [DataRow(65536)]
    [DataRow(65537)]
    public async Task BoundedCompilerOutputPreservesExactBytes(int count)
    {
        byte[] bytes = [.. Enumerable.Range(0, count).Select(static value => unchecked((byte)(value * 31 + 7)))];
        using var source = new MemoryStream(bytes);
        using var destination = new MemoryStream();
        await NativeBindingHeaderCommand.CopyAsync(source, destination, count, context.CancellationToken);
        Assert.AreSequenceEqual(bytes, destination.ToArray());
    }

    /// <summary>
    /// An overrun is an explicit failure and cannot write beyond the allowed output extent.
    /// </summary>
    /// <param name="limit">The maximum accepted extent.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(65536)]
    public async Task CompilerOutputOverrunsFailExplicitly(int limit)
    {
        byte[] bytes = [.. Enumerable.Range(0, limit + 1).Select(static value => unchecked((byte)(value * 17)))];
        using var source = new MemoryStream(bytes);
        using var destination = new MemoryStream();
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => NativeBindingHeaderCommand.CopyAsync(source, destination, limit, context.CancellationToken));
        Assert.IsLessThanOrEqualTo(limit, destination.Length);
        Assert.AreSequenceEqual(bytes.Take((int)destination.Length), destination.ToArray());
    }

    /// <summary>
    /// Invalid bounds and cancellation fail without starting a compiler or mutating existing artifacts.
    /// </summary>
    [TestMethod]
    public async Task InvalidBoundsAndPreCancelledCompilationPreserveState()
    {
        using var source = new MemoryStream([1]);
        using var destination = new MemoryStream();
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => NativeBindingHeaderCommand.CopyAsync(source, destination, -1, context.CancellationToken));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => NativeBindingHeaderCommand.CopyAsync(source, destination, 1, cancellation.Token));
        Assert.AreEqual(0L, destination.Length);
        string file = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(file, "unchanged", context.CancellationToken);
            await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => NativeBindingHeaderCommand.CompileAsync(
                "missing-compiler", [], file, Path.GetDirectoryName(file)!, cancellation.Token));
            Assert.AreEqual("unchanged", await File.ReadAllTextAsync(file, context.CancellationToken));
        }
        finally
        {
            File.Delete(file);
        }
    }
}
