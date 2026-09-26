namespace Ankus.Build.Tests;

/// <summary>
/// Checks native enum selection, exact values and target integer representation boundaries.
/// </summary>
[TestClass]
public sealed class NativeBindingEnumTests
{
    private const string Source = """
        pub enum NodeTag { T_Invalid = 0, T_Leaf = 7, }
        pub mod Kind {
            pub type Type = ::core::ffi::c_int;
            pub const KIND_NEGATIVE: Type = -1;
            pub const KIND_LAST: Type = 7;
        }
        pub mod Flags {
            pub type Type = ::core::ffi::c_uint;
            pub const FLAGS_HIGH: Type = 255;
        }
        pub mod Ignored {
            pub type Type = ::core::ffi::c_uint;
            pub const IGNORED: Type = 1;
        }
        pub type KindAlias = Kind::Type;
        pub struct Leaf {
            pub type_: NodeTag,
            pub kind: KindAlias,
            pub flags: [Flags::Type; 2usize],
            pub ignored: *mut Ignored::Type,
        }
        """;

    private static readonly string s_observations = """
        enum|Flags|1|0
        constant|Flags|FLAGS_HIGH|255
        enum|Kind|4|1
        constant|Kind|KIND_NEGATIVE|-1
        constant|Kind|KIND_LAST|7
        """.ReplaceLineEndings("\n");

    /// <summary>
    /// Aliases and fixed arrays retain named enum identity while indirect fields do not expand the value graph.
    /// </summary>
    [TestMethod]
    public void SelectionFollowsAliasesAndArrayValuesOnly()
    {
        NativeBindingCatalog catalog = NativeBindingParser.Parse(Source, 18);
        IReadOnlyDictionary<string, NativeBindingEnum> enums = NativeBindingEnums.Select(catalog);
        Assert.AreSequenceEqual<string>(["Flags", "Kind"], enums.Keys);
        Assert.AreEqual("-1", enums["Kind"].Values["KIND_NEGATIVE"]);
        Assert.AreEqual("255", enums["Flags"].Values["FLAGS_HIGH"]);
    }

    /// <summary>
    /// Selected-header widths and signedness are retained even when they differ from reference bindgen storage.
    /// </summary>
    /// <param name="size">The selected compiler's enum width.</param>
    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(4)]
    [DataRow(8)]
    public void ObservationsRetainSelectedCompilerStorage(int size)
    {
        NativeBindingCatalog catalog = NativeBindingParser.Parse(Source, 18);
        string output = s_observations.Replace("enum|Kind|4|1", $"enum|Kind|{size}|1", StringComparison.Ordinal);
        IReadOnlyDictionary<string, NativeBindingEnumLayout> layouts = NativeBindingEnums.Read(catalog, output.Split('\n'));
        Assert.HasCount(2, layouts);
        Assert.AreEqual(new NativeBindingEnumLayout(size, true), layouts["Kind"]);
        Assert.AreEqual(new NativeBindingEnumLayout(1, false), layouts["Flags"]);
    }

    /// <summary>
    /// Signed native storage retains the same high bits as the pinned unsigned constant without truncation.
    /// </summary>
    [TestMethod]
    public void SignedStoragePreservesUnsignedHighBits()
    {
        NativeBindingCatalog catalog = NativeBindingParser.Parse(Source, 18);
        string output = s_observations.Replace("enum|Flags|1|0", "enum|Flags|1|1", StringComparison.Ordinal)
            .Replace("constant|Flags|FLAGS_HIGH|255", "constant|Flags|FLAGS_HIGH|-1", StringComparison.Ordinal);
        IReadOnlyDictionary<string, NativeBindingEnumLayout> layouts = NativeBindingEnums.Read(catalog, output.Split('\n'));
        Assert.AreEqual(new NativeBindingEnumLayout(1, true), layouts["Flags"]);
        Assert.AreEqual(new System.Numerics.BigInteger(-1), NativeBindingEnums.NativeValue("255", layouts["Flags"]));
    }

    /// <summary>
    /// Missing, malformed, changed or unrepresentable enum observations cannot generate a binding contract.
    /// </summary>
    /// <param name="original">The valid record text.</param>
    /// <param name="replacement">The invalid replacement.</param>
    [TestMethod]
    [DataRow("enum|Kind|4|1", "")]
    [DataRow("enum|Kind|4|1", "enum|Kind|4|1\nenum|Kind|4|1")]
    [DataRow("enum|Kind|4|1", "enum|Unknown|4|1")]
    [DataRow("enum|Kind|4|1", "enum|Kind|0|1")]
    [DataRow("enum|Kind|4|1", "enum|Kind|3|1")]
    [DataRow("enum|Kind|4|1", "enum|Kind|16|1")]
    [DataRow("enum|Kind|4|1", "enum|Kind|4|2")]
    [DataRow("enum|Kind|4|1", "enum|Kind|4|1|extra")]
    [DataRow("enum|Kind|4|1", "enum|Kind|4|0")]
    [DataRow("enum|Flags|1|0", "enum|Flags|1|1")]
    [DataRow("constant|Kind|KIND_LAST|7", "")]
    [DataRow("constant|Kind|KIND_LAST|7", "constant|Kind|KIND_LAST|7\nconstant|Kind|KIND_LAST|7")]
    [DataRow("constant|Kind|KIND_LAST|7", "constant|Kind|UNKNOWN|7")]
    [DataRow("constant|Kind|KIND_LAST|7", "constant|Unknown|KIND_LAST|7")]
    [DataRow("constant|Kind|KIND_LAST|7", "constant|Kind|KIND_LAST|8")]
    [DataRow("constant|Kind|KIND_LAST|7", "constant|Kind|KIND_LAST|bad")]
    [DataRow("constant|Kind|KIND_LAST|7", "constant|Kind|KIND_LAST|7|extra")]
    public void InvalidObservationsAreRejected(string original, string replacement)
    {
        NativeBindingCatalog catalog = NativeBindingParser.Parse(Source, 18);
        string output = s_observations.Replace(original, replacement, StringComparison.Ordinal);
        Assert.ThrowsExactly<FormatException>(() => NativeBindingEnums.Read(catalog, output.Split('\n')));
    }

    /// <summary>
    /// The signed and unsigned 64-bit edges remain exact and their immediately adjacent invalid values are rejected.
    /// </summary>
    /// <param name="isSigned">Whether native storage is signed.</param>
    /// <param name="value">The exact catalog and observed integer.</param>
    /// <param name="valid">Whether that value fits the selected storage.</param>
    [TestMethod]
    [DataRow(true, "-9223372036854775808", true)]
    [DataRow(true, "9223372036854775807", true)]
    [DataRow(true, "-9223372036854775809", false)]
    [DataRow(true, "9223372036854775808", false)]
    [DataRow(false, "0", true)]
    [DataRow(false, "18446744073709551615", true)]
    [DataRow(false, "-1", false)]
    [DataRow(false, "18446744073709551616", false)]
    public void WideIntegerBoundariesAreExact(bool isSigned, string value, bool valid)
    {
        string declarations = Source.Replace("255", value, StringComparison.Ordinal);
        string output = s_observations.Replace("enum|Flags|1|0", $"enum|Flags|8|{(isSigned ? 1 : 0)}", StringComparison.Ordinal)
            .Replace("constant|Flags|FLAGS_HIGH|255", $"constant|Flags|FLAGS_HIGH|{value}", StringComparison.Ordinal);
        NativeBindingCatalog catalog = NativeBindingParser.Parse(declarations, 18);
        if (valid)
        {
            IReadOnlyDictionary<string, NativeBindingEnumLayout> layouts = NativeBindingEnums.Read(catalog, output.Split('\n'));
            Assert.AreEqual(new NativeBindingEnumLayout(8, isSigned), layouts["Flags"]);
        }
        else
        {
            Assert.ThrowsExactly<FormatException>(() => NativeBindingEnums.Read(catalog, output.Split('\n')));
        }
    }
}
