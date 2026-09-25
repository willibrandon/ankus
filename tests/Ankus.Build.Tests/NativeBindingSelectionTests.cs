namespace Ankus.Build.Tests;

/// <summary>
/// Checks complete embedded-value selection independently of native field sizes.
/// </summary>
[TestClass]
public sealed class NativeBindingSelectionTests
{
    private const string Source = """
        pub enum NodeTag { T_Invalid = 0, T_Leaf = 7, }
        pub struct Node {
            pub type_: NodeTag,
        }
        pub struct Leaf {
            pub type_: NodeTag,
            pub meta: Alias,
            pub items: [Pair; 2usize],
            pub indirect: *mut Unrelated,
            pub callback: Callback,
            pub tail: __IncompleteArrayField<Pair>,
        }
        pub type Alias = Embedded;
        pub type Callback = ::core::option::Option<unsafe extern "C" fn(*mut Unrelated)>;
        pub struct Embedded {
            pub payload: Anonymous,
        }
        pub union Anonymous {
            pub number: u64,
            pub pair: Pair,
        }
        pub struct Pair {
            pub left: u16,
            pub right: u16,
        }
        pub struct Unrelated {
            pub other: u8,
        }
        """;

    /// <summary>
    /// Embedded aliases, anonymous unions and arrays retain actual C access paths; indirect values do not expand the graph.
    /// </summary>
    [TestMethod]
    public void EmbeddedValuesUseNativePathsWithoutFollowingPointers()
    {
        NativeBindingCatalog catalog = NativeBindingParser.Parse(Source, 18);
        IReadOnlyList<NativeBindingSelectionEntry> entries = NativeBindingSelection.Create(catalog);
        Assert.AreSequenceEqual<string>(["Anonymous", "Embedded", "Leaf", "Node", "Pair"], entries.Select(static entry => entry.Type.Name));
        NativeBindingSelectionEntry anonymous = entries.Single(static entry => entry.Type.Name == "Anonymous");
        Assert.AreEqual("Leaf", anonymous.Root);
        Assert.AreEqual("meta.payload", anonymous.Path);
        Assert.AreEqual("((Leaf*)0)->meta.payload", anonymous.Expression);
        Assert.IsTrue(anonymous.Type.IsUnion);
        NativeBindingSelectionEntry pair = entries.Single(static entry => entry.Type.Name == "Pair");
        Assert.AreEqual("items[0]", pair.Path);
        NativeBindingSelectionEntry node = entries.Single(static entry => entry.Type.Name == "Node");
        Assert.AreEqual("*((Node*)0)", node.Expression);
    }

    /// <summary>
    /// A flexible tail still requires its complete element declaration even without an ordinary array field.
    /// </summary>
    [TestMethod]
    public void FlexibleArrayElementsAreIncluded()
    {
        string source = Source.Replace("    pub items: [Pair; 2usize],", string.Empty, StringComparison.Ordinal);
        NativeBindingSelectionEntry pair = NativeBindingSelection.Create(NativeBindingParser.Parse(source, 18))
            .Single(static entry => entry.Type.Name == "Pair");
        Assert.AreEqual("tail[0]", pair.Path);
        Assert.AreEqual("((Leaf*)0)->tail[0]", pair.Expression);
    }

    /// <summary>
    /// Invalid typedef and value representations are rejected before a compiler can observe a partial graph.
    /// </summary>
    /// <param name="alias">The invalid declaration replacing the embedded alias target.</param>
    [TestMethod]
    [DataRow("Missing")]
    [DataRow("Alias")]
    [DataRow("[Alias; 1usize]")]
    [DataRow("__IncompleteArrayField<Alias>")]
    [DataRow("::core::option::Option<Embedded>")]
    public void InvalidEmbeddedRepresentationIsRejected(string alias)
    {
        string source = Source.Replace("pub type Alias = Embedded;", $"pub type Alias = {alias};", StringComparison.Ordinal);
        NativeBindingCatalog catalog = NativeBindingParser.Parse(source, 18);
        Assert.ThrowsExactly<FormatException>(() => NativeBindingSelection.Create(catalog));
    }

    /// <summary>
    /// Array element parsing distinguishes nested values, flexible tails and pointer-only representations.
    /// </summary>
    /// <param name="representation">The normalized bindgen representation.</param>
    /// <param name="element">The expected element representation, or null for nonarrays.</param>
    [TestMethod]
    [DataRow("[[u16; 3usize]; 2usize]", "[u16; 3usize]")]
    [DataRow("__IncompleteArrayField<Pair>", "Pair")]
    [DataRow("[u8; 0usize]", "u8")]
    [DataRow("*mut Pair", null)]
    [DataRow("Pair", null)]
    public void ArrayElementsRetainTheirFullRepresentation(string representation, string? element)
        => Assert.AreEqual(element, NativeBindingSelection.ArrayElement(representation));
}
