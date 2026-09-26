namespace Ankus.Build.Tests;

/// <summary>
/// Verifies selected-function observation completeness and explicit rejection of incompatible evidence.
/// </summary>
[TestClass]
public sealed class NativeBindingSignatureProbeTests
{
    private static readonly NativeBindingCatalog s_catalog = NativeBindingParser.Parse("pub enum NodeTag { T_Invalid = 0, }", 18);
    private static readonly NativeBindingRawCatalog s_raw = NativeBindingRawParser.Parse("""
        extern "C" {
            pub fn alpha(value: u64) -> u32;
            pub fn done();
            pub fn fail() -> !;
            pub fn report(format: *const ::core::ffi::c_char, ...);
        }
        """, 18);
    private const string Header = "signatures|1|180006|8|1|linux-x64\n";
    private const string Alpha = "value|alpha|0|8|8\nvalue|alpha|result|4|4\n";

    /// <summary>
    /// Results preserve ordered storage, exact target identity, void and variadic distinctions, and deterministic selection.
    /// </summary>
    [TestMethod]
    public void CompleteMeasurementsRetainSignatureContracts()
    {
        NativeBindingSignatures layout = NativeBindingSignatureProbe.Read(s_catalog, s_raw,
            ["report", "fail", "done", "alpha"], Header + "value|report|0|8|8\n" + Alpha);
        Assert.AreEqual(180006, layout.PostgresVersion);
        Assert.AreEqual(8, layout.PointerSize);
        Assert.IsTrue(layout.IsLittleEndian);
        Assert.AreEqual("linux-x64", layout.RuntimeIdentifier);
        Assert.AreSequenceEqual<string>(["alpha", "done", "fail", "report"], layout.Functions.Keys);
        Assert.AreSequenceEqual<NativeBindingSignatureValue>([new(8, 8)], layout.Functions["alpha"].Parameters);
        Assert.AreEqual(new NativeBindingSignatureValue(4, 4), layout.Functions["alpha"].Result);
        Assert.IsFalse(layout.Functions["alpha"].IsVariadic);
        Assert.IsFalse(layout.Functions["alpha"].DoesNotReturn);
        Assert.IsEmpty(layout.Functions["done"].Parameters);
        Assert.IsNull(layout.Functions["done"].Result);
        Assert.IsFalse(layout.Functions["done"].DoesNotReturn);
        Assert.IsTrue(layout.Functions["fail"].DoesNotReturn);
        Assert.IsNull(layout.Functions["fail"].Result);
        Assert.IsTrue(layout.Functions["report"].IsVariadic);
        Assert.IsNull(layout.Functions["report"].Result);
        Assert.AreEqual(NativeBindingSignatureProbe.GenerateSource(s_catalog, s_raw, ["alpha", "done"], "headers"),
            NativeBindingSignatureProbe.GenerateSource(s_catalog, s_raw, ["done", "alpha"], "headers"));
    }

    /// <summary>
    /// Missing, duplicate, extra, malformed, or misaligned values cannot become a measured call contract.
    /// </summary>
    /// <param name="observations">Invalid value records.</param>
    [TestMethod]
    [DataRow("")]
    [DataRow("value|alpha|0|8|8\n")]
    [DataRow("value|alpha|result|4|4\n")]
    [DataRow(Alpha + "value|alpha|0|8|8\n")]
    [DataRow(Alpha + "value|alpha|1|8|8\n")]
    [DataRow(Alpha + "value|alpha|00|8|8\n")]
    [DataRow(Alpha + "value|other|0|8|8\n")]
    [DataRow(Alpha + "value|done|result|4|4\n")]
    [DataRow("value|alpha|0|0|8\nvalue|alpha|result|4|4\n")]
    [DataRow("value|alpha|0|8|0\nvalue|alpha|result|4|4\n")]
    [DataRow("value|alpha|0|8|3\nvalue|alpha|result|4|4\n")]
    [DataRow("value|alpha|0|7|8\nvalue|alpha|result|4|4\n")]
    [DataRow("value|alpha|-1|8|8\nvalue|alpha|result|4|4\n")]
    [DataRow("value|alpha|0|2147483648|8\nvalue|alpha|result|4|4\n")]
    [DataRow(Alpha + "garbage\n")]
    public void InvalidObservationsFailExplicitly(string observations)
        => Assert.ThrowsExactly<FormatException>(() => NativeBindingSignatureProbe.Read(s_catalog, s_raw, ["alpha", "done"], Header + observations));

    /// <summary>
    /// Unsupported versions, byte orders and target combinations are rejected before consuming values.
    /// </summary>
    /// <param name="header">The invalid probe header.</param>
    [TestMethod]
    [DataRow("")]
    [DataRow("signatures|2|180006|8|1|linux-x64")]
    [DataRow("signatures|1|170011|8|1|linux-x64")]
    [DataRow("signatures|1|180006|4|1|linux-x64")]
    [DataRow("signatures|1|180006|8|0|linux-x64")]
    [DataRow("signatures|1|180006|8|2|linux-x64")]
    [DataRow("signatures|1|180006|8|1|unknown-x64")]
    public void InvalidTargetEvidenceIsRejected(string header)
        => Assert.ThrowsExactly<FormatException>(() => NativeBindingSignatureProbe.Read(s_catalog, s_raw, ["alpha"], header + "\n" + Alpha));

    /// <summary>
    /// Empty selection is valid; unknown names, duplicates and mixed catalog majors are explicit errors.
    /// </summary>
    [TestMethod]
    public void FunctionSelectionIsExact()
    {
        Assert.IsEmpty(NativeBindingSignatureProbe.Read(s_catalog, s_raw, [], Header).Functions);
        Assert.ThrowsExactly<FormatException>(() => NativeBindingSignatureProbe.GenerateSource(s_catalog, s_raw, ["unknown"], ""));
        Assert.ThrowsExactly<FormatException>(() => NativeBindingSignatureProbe.GenerateSource(s_catalog, s_raw, ["alpha", "alpha"], ""));
        Assert.ThrowsExactly<FormatException>(() => NativeBindingSignatureProbe.GenerateSource(s_catalog with { PostgresMajor = 17 }, s_raw, [], ""));
        var invalidLinkage = new Dictionary<string, NativeBindingFunction>(s_raw.Functions)
        {
            ["alpha"] = s_raw.Functions["alpha"] with { NativeSymbol = "x; injected" },
        };
        Assert.ThrowsExactly<FormatException>(() => NativeBindingSignatureProbe.GenerateSource(s_catalog,
            s_raw with { Functions = invalidLinkage }, ["alpha"], ""));
    }
}
