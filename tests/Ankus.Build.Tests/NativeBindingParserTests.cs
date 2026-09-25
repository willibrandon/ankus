namespace Ankus.Build.Tests;

/// <summary>
/// Checks native declaration preservation and pgrx's tag-based casting contracts independently of physical layouts.
/// </summary>
[TestClass]
public sealed class NativeBindingParserTests
{
    private const string Graph = """
        pub enum NodeTag {
            T_Invalid = 0,
            T_Leaf = 7,
            T_Sibling = 11,
            T_Alias = 29,
        }
        pub struct Node {
            pub type_: NodeTag,
        }
        pub struct Expr {
            pub type_: NodeTag,
        }
        pub struct Leaf {
            pub xpr: Expr,
            pub payload: ::core::ffi::c_int,
        }
        pub struct Sibling {
            pub type_: NodeTag,
        }
        pub type Alias = Leaf;
        pub union ValUnion {
            pub node: Node,
            pub leaf: Leaf,
        }
        pub struct NotNode {
            pub number: u32,
            pub later: NodeTag,
        }
        pub struct Pointer {
            pub node: *mut Node,
        }
        """;

    /// <summary>
    /// Parent targets accept descendant and typedef tags while unrelated roots, pointers and unions stay distinct.
    /// </summary>
    [TestMethod]
    public void CastGraphPreservesInheritanceAliasesAndUnionDirection()
    {
        NativeBindingCatalog catalog = NativeBindingParser.Parse(Graph, 18);
        Assert.AreEqual(18, catalog.PostgresMajor);
        Assert.AreEqual(29U, catalog.Tags["T_Alias"]);
        Assert.AreSequenceEqual<string>(["T_Alias", "T_Leaf"], catalog.Types["Expr"].CastTags);
        Assert.AreSequenceEqual<string>(["T_Alias", "T_Leaf"], catalog.Types["Leaf"].CastTags);
        Assert.AreSequenceEqual<string>(["T_Sibling"], catalog.Types["Sibling"].CastTags);
        Assert.IsTrue(catalog.Types["Node"].IsNode);
        Assert.IsEmpty(catalog.Types["Node"].CastTags);
        Assert.IsTrue(catalog.Types["ValUnion"].IsNode);
        Assert.IsTrue(catalog.Types["ValUnion"].IsUnion);
        Assert.IsEmpty(catalog.Types["ValUnion"].CastTags);
        Assert.IsFalse(catalog.Types["NotNode"].IsNode);
        Assert.IsFalse(catalog.Types["Pointer"].IsNode);
        Assert.AreEqual("Leaf", catalog.Aliases["Alias"]);
        Assert.AreEqual("type", catalog.Types["Node"].Fields[0].NativeName);
        Assert.AreEqual("::core::ffi::c_int", catalog.Types["Leaf"].Fields[1].Representation);
    }

    /// <summary>
    /// Legacy Value is accepted with all five actual constructor tags only on PostgreSQL 13 and 14.
    /// </summary>
    [TestMethod]
    [DataRow(13)]
    [DataRow(14)]
    [DataRow(15)]
    public void LegacyValueTagsAreVersionSpecific(int major)
    {
        const string source = """
            pub enum NodeTag {
                T_Invalid = 0,
                T_Integer = 1,
                T_Float = 2,
                T_String = 3,
                T_BitString = 4,
                T_Null = 5,
            }
            pub struct Value {
                pub type_: NodeTag,
            }
            """;
        NativeBindingType value = NativeBindingParser.Parse(source, major).Types["Value"];
        Assert.IsTrue(value.IsNode);
        Assert.AreSequenceEqual<string>(major <= 14
            ? ["T_BitString", "T_Float", "T_Integer", "T_Null", "T_String"] : [], value.CastTags);
    }

    /// <summary>
    /// Nested callbacks, arrays and typedefs retain complete representations instead of stopping at inner delimiters.
    /// </summary>
    [TestMethod]
    public void NestedDeclarationsAndEnumConstantsRemainComplete()
    {
        const string source = """
            pub enum NodeTag { T_Invalid = 0, T_Leaf = 1, }
            pub struct Leaf {
                pub type_: NodeTag,
                pub override_: bool,
                pub callback: ::core::option::Option<
                    unsafe extern "C-unwind" fn(first: *mut Leaf, values: [u32; 3usize]) -> *mut Leaf,
                >,
                pub items: __IncompleteArrayField<Leaf>,
            }
            pub type Callback = ::core::option::Option<unsafe extern "C" fn([u32; 2usize], *mut Leaf)>;
            pub mod State {
                pub type Type = ::core::ffi::c_uint;
                pub const READY: Type = 0;
                pub const INVALID: Type = 4294967295;
            }
            """;
        NativeBindingCatalog catalog = NativeBindingParser.Parse(source, 19);
        IReadOnlyList<NativeBindingField> fields = catalog.Types["Leaf"].Fields;
        Assert.HasCount(4, fields);
        Assert.AreEqual("override", fields[1].NativeName);
        Assert.AreEqual("""
            ::core::option::Option<
                unsafe extern "C-unwind" fn(first: *mut Leaf, values: [u32; 3usize]) -> *mut Leaf,
            >
            """.Replace("\n", "\n    ", StringComparison.Ordinal), fields[2].Representation);
        Assert.AreEqual("__IncompleteArrayField<Leaf>", fields[3].Representation);
        Assert.AreEqual("::core::option::Option<unsafe extern \"C\" fn([u32; 2usize], *mut Leaf)>", catalog.Aliases["Callback"]);
        Assert.AreEqual("::core::ffi::c_uint", catalog.Enums["State"].Storage);
        Assert.AreEqual("4294967295", catalog.Enums["State"].Values["INVALID"]);
        Assert.AreEqual("0", catalog.Enums["State"].Values["READY"]);
    }

    /// <summary>
    /// Comment and documentation text cannot inject types or delimiters into the declaration graph.
    /// </summary>
    [TestMethod]
    public void TriviaCannotCreateDeclarations()
    {
        string source = """
            /* outer /* nested } */
            pub struct Fake { pub type_: NodeTag, }
            */
            #[doc = "}
            pub struct AlsoFake { pub type_: NodeTag, }
            "]
            // pub enum NodeTag { T_Invalid = 0, T_Invalid = 7 }
            """ + "\n" + Graph.Replace("pub type_: NodeTag,", "pub type_: /* retained */ NodeTag,", StringComparison.Ordinal);
        NativeBindingCatalog catalog = NativeBindingParser.Parse(source, 18);
        Assert.HasCount(7, catalog.Types);
        Assert.IsFalse(catalog.Types.ContainsKey("Fake"));
        Assert.IsFalse(catalog.Types.ContainsKey("AlsoFake"));
        Assert.IsTrue(catalog.Types["Leaf"].IsNode);
        Assert.AreSequenceEqual<string>(["T_Alias", "T_Leaf"], catalog.Types["Expr"].CastTags);
    }

    /// <summary>
    /// Malformed or ambiguous metadata is rejected before it can describe an unsafe layout or cast.
    /// </summary>
    [TestMethod]
    [DataRow("pub enum NodeTag { T_Invalid = 1, }")]
    [DataRow("pub enum NodeTag { T_Invalid = 0, T_Invalid = 1, }")]
    [DataRow("pub enum NodeTag { T_Invalid = 0, T_Other = 0, }")]
    [DataRow("pub enum NodeTag { T_Invalid = 0, T_Other = 4294967296, }")]
    [DataRow("pub enum NodeTag { T_Invalid = 0,")]
    [DataRow("pub enum NodeTag { T_Invalid = 0, }\n/* unterminated")]
    [DataRow("pub enum NodeTag { T_Invalid = 0, }\n#[doc = \"unterminated")]
    [DataRow("pub enum NodeTag { T_Invalid = 0, }\npub struct Node {\n    pub type_: NodeTag\n}")]
    [DataRow("pub enum NodeTag { T_Invalid = 0, }\npub struct Node {\n    pub type_: NodeTag,\n    pub type_: NodeTag,\n}")]
    public void InvalidMetadataFailsExplicitly(string source)
        => Assert.ThrowsExactly<FormatException>(() => NativeBindingParser.Parse(source, 18));

    /// <summary>
    /// Unsupported versions and absent input are rejected independently of declaration validity.
    /// </summary>
    [TestMethod]
    public void ArgumentsAreValidated()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => NativeBindingParser.Parse(null!, 18));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => NativeBindingParser.Parse(Graph, 12));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => NativeBindingParser.Parse(Graph, 20));
        Assert.ThrowsExactly<FormatException>(() => NativeBindingParser.Parse(string.Empty, 18));
    }
}
