namespace Ankus.Build.Tests;

/// <summary>
/// Validates ABI observation records against independent layouts and malformed boundary inputs.
/// </summary>
[TestClass]
public sealed class NativeBindingProbeTests
{
    private const string Source = """
        pub enum NodeTag { T_Invalid = 0, T_Leaf = 7, }
        pub struct Node {
            pub type_: NodeTag,
        }
        pub struct Leaf {
            pub type_: NodeTag,
            pub child: Child,
            pub values: [u16; 3usize],
            pub tail: __IncompleteArrayField<u8>,
        }
        pub struct Child {
            pub value: u32,
        }
        """;

    private const string Observations = """
        header|1|180006|8|8|1|1
        tag|T_Invalid|0
        tag|T_Leaf|7
        type|Child|4|4
        field|Child|value|0|4|4|4
        type|Leaf|16|4
        field|Leaf|type_|0|4|4|4
        field|Leaf|child|4|4|4|4
        field|Leaf|values|8|6|2|2
        field|Leaf|tail|14|0|1|1
        type|Node|4|4
        field|Node|type_|0|4|4|4
        """;

    /// <summary>
    /// Native primitive models and padded flexible tails are retained independently of the build host.
    /// </summary>
    /// <param name="pointerSize">The observed pointer width.</param>
    /// <param name="nativeLong">The observed C long width.</param>
    /// <param name="charIsSigned">The observed plain-char signedness.</param>
    /// <param name="little">The observed byte order.</param>
    [TestMethod]
    [DataRow(4, 4, false, false)]
    [DataRow(8, 4, true, true)]
    [DataRow(8, 8, false, true)]
    public void LayoutPreservesTargetPrimitivesEmbeddedValuesAndFlexiblePadding(int pointerSize, int nativeLong, bool charIsSigned, bool little)
    {
        string output = Observations.Replace("header|1|180006|8|8|1|1",
            $"header|1|180006|{pointerSize}|{nativeLong}|{(charIsSigned ? 1 : 0)}|{(little ? 1 : 0)}", StringComparison.Ordinal);
        NativeBindingLayout layout = NativeBindingProbe.Read(NativeBindingParser.Parse(Source, 18), output);
        Assert.AreEqual(180006, layout.PostgresVersion);
        Assert.AreEqual(pointerSize, layout.PointerSize);
        Assert.AreEqual(nativeLong, layout.LongSize);
        Assert.AreEqual(charIsSigned, layout.CharIsSigned);
        Assert.AreEqual(little, layout.IsLittleEndian);
        Assert.AreSequenceEqual<string>(["Child", "Leaf", "Node"], layout.Types.Keys);
        Assert.AreEqual(16, layout.Types["Leaf"].Size);
        Assert.AreEqual(4, layout.Types["Leaf"].Alignment);
        Assert.HasCount(4, layout.Types["Leaf"].Fields);
        Assert.AreEqual(new NativeBindingFieldLayout(4, 4, 4, 4), layout.Types["Leaf"].Fields["child"]);
        Assert.AreEqual(new NativeBindingFieldLayout(8, 6, 2, 2), layout.Types["Leaf"].Fields["values"]);
        Assert.AreEqual(new NativeBindingFieldLayout(14, 0, 1, 1), layout.Types["Leaf"].Fields["tail"]);
        Assert.AreEqual(4, layout.Types["Child"].Size);
        Assert.AreEqual(new NativeBindingFieldLayout(0, 4, 4, 4), layout.Types["Node"].Fields["type_"]);
    }

    /// <summary>
    /// A malformed observation cannot become a trusted native layout through missing, duplicate or invalid records.
    /// </summary>
    /// <param name="original">The valid record text to replace.</param>
    /// <param name="replacement">The malformed replacement.</param>
    [TestMethod]
    [DataRow("header|1|", "header|2|")]
    [DataRow("180006", "170011")]
    [DataRow("|8|8|1|1", "|2|8|1|1")]
    [DataRow("|8|8|1|1", "|8|2|1|1")]
    [DataRow("|8|8|1|1", "|8|8|2|1")]
    [DataRow("|8|8|1|1", "|8|8|1|2")]
    [DataRow("tag|T_Leaf|7", "tag|T_Leaf|8")]
    [DataRow("tag|T_Leaf|7", "tag|T_Absent|7")]
    [DataRow("tag|T_Leaf|7", "tag|T_Leaf|4294967296")]
    [DataRow("tag|T_Leaf|7", "")]
    [DataRow("tag|T_Leaf|7", "tag|T_Leaf|7\ntag|T_Leaf|7")]
    [DataRow("type|Leaf|16|4", "type|Leaf|0|4")]
    [DataRow("type|Leaf|16|4", "type|Leaf|16|0")]
    [DataRow("type|Leaf|16|4", "type|Leaf|16|3")]
    [DataRow("type|Leaf|16|4", "type|Leaf|15|4")]
    [DataRow("type|Leaf|16|4", "type|Leaf|2147483648|4")]
    [DataRow("type|Leaf|16|4", "type|Leaf|16|4\ntype|Leaf|16|4")]
    [DataRow("type|Leaf|16|4", "type|Unknown|16|4")]
    [DataRow("type|Leaf|16|4", "")]
    [DataRow("field|Leaf|child|4|4|4|4", "field|Leaf|child|4|4|4|4\nfield|Leaf|child|4|4|4|4")]
    [DataRow("field|Leaf|child|4|4|4|4", "field|Leaf|unknown|4|4|4|4")]
    [DataRow("field|Leaf|child|4|4|4|4", "field|Leaf|child|17|4|4|4")]
    [DataRow("field|Leaf|child|4|4|4|4", "field|Leaf|child|14|4|4|4")]
    [DataRow("field|Leaf|child|4|4|4|4", "field|Leaf|child|4|0|4|4")]
    [DataRow("field|Leaf|child|4|4|4|4", "field|Leaf|child|4|4|3|4")]
    [DataRow("field|Leaf|child|4|4|4|4", "field|Leaf|child|4|4|4|0")]
    [DataRow("field|Leaf|child|4|4|4|4", "field|Leaf|child|4|4|4|2")]
    [DataRow("field|Leaf|child|4|4|4|4", "field|Leaf|child|-1|4|4|4")]
    [DataRow("field|Leaf|child|4|4|4|4", "")]
    [DataRow("field|Leaf|values|8|6|2|2", "field|Leaf|values|8|5|2|2")]
    [DataRow("field|Leaf|tail|14|0|1|1", "field|Leaf|tail|14|1|1|1")]
    [DataRow("tag|T_Leaf|7", "unknown|T_Leaf|7")]
    [DataRow("tag|T_Leaf|7", "tag|T_Leaf|7|extra")]
    public void InvalidObservationsAreRejected(string original, string replacement)
    {
        NativeBindingCatalog catalog = NativeBindingParser.Parse(Source, 18);
        string output = Observations.Replace(original, replacement, StringComparison.Ordinal);
        Assert.ThrowsExactly<FormatException>(() => NativeBindingProbe.Read(catalog, output));
    }

    /// <summary>
    /// Absent output cannot be confused with a successful empty layout.
    /// </summary>
    [TestMethod]
    public void EmptyObservationsAreRejected()
        => Assert.ThrowsExactly<FormatException>(() => NativeBindingProbe.Read(NativeBindingParser.Parse(Source, 18), string.Empty));
}
