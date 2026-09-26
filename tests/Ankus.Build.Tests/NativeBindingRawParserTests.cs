using System.Text.Json;

namespace Ankus.Build.Tests;

/// <summary>
/// Verifies raw declaration identity and complete signatures without treating reference values as a target ABI.
/// </summary>
[TestClass]
public sealed class NativeBindingRawParserTests
{
    /// <summary>
    /// Foreign parameters retain order, nested commas, array extents, variadics, return types and explicit linkage.
    /// </summary>
    [TestMethod]
    public void ForeignFunctionsPreserveSignaturesAndLinkage()
    {
        const string source = """
            unsafe extern "C-unwind" {
                #[link_name = "newNode__pgrx_cshim"]
                pub fn newNode(size: usize, tag: NodeTag) -> *mut Node;
                pub fn callback(
                    values: [u32; 3usize],
                    handler: ::core::option::Option<unsafe extern "C" fn(a: u32, b: [u8; 2usize]) -> *const Node>,
                ) -> !;
                pub fn report(format: *const ::core::ffi::c_char, ...);
                pub fn ready() -> bool;
                pub fn resolver() -> ::core::option::Option<unsafe extern "C" fn(values: [u8; 2usize]) -> *mut Node>;
            }
            extern "C" {
                pub fn finish();
            }
            """;
        NativeBindingRawCatalog catalog = NativeBindingRawParser.Parse(source, 18);
        Assert.AreEqual(18, catalog.PostgresMajor);
        Assert.IsNull(catalog.SourceRevision);
        Assert.HasCount(6, catalog.Functions);
        NativeBindingFunction allocation = catalog.Functions["newNode"];
        Assert.AreEqual("newNode__pgrx_cshim", allocation.NativeSymbol);
        Assert.AreEqual("C-unwind", allocation.Abi);
        Assert.AreEqual("*mut Node", allocation.ReturnType);
        Assert.IsFalse(allocation.IsVariadic);
        Assert.AreSequenceEqual<NativeBindingParameter>([new("size", "usize"), new("tag", "NodeTag")], allocation.Parameters);
        Assert.AreSequenceEqual<string>(["#[link_name = \"newNode__pgrx_cshim\"]"], allocation.Attributes);
        NativeBindingFunction callback = catalog.Functions["callback"];
        Assert.AreEqual("callback", callback.NativeSymbol);
        Assert.AreEqual("!", callback.ReturnType);
        Assert.AreSequenceEqual<NativeBindingParameter>([
            new("values", "[u32; 3usize]"),
            new("handler", "::core::option::Option<unsafe extern \"C\" fn(a: u32, b: [u8; 2usize]) -> *const Node>")], callback.Parameters);
        NativeBindingFunction report = catalog.Functions["report"];
        Assert.IsTrue(report.IsVariadic);
        Assert.AreEqual("()", report.ReturnType);
        Assert.AreSequenceEqual<NativeBindingParameter>([new("format", "*const ::core::ffi::c_char")], report.Parameters);
        Assert.IsEmpty(catalog.Functions["ready"].Parameters);
        Assert.AreEqual("bool", catalog.Functions["ready"].ReturnType);
        Assert.AreEqual("::core::option::Option<unsafe extern \"C\" fn(values: [u8; 2usize]) -> *mut Node>", catalog.Functions["resolver"].ReturnType);
        Assert.AreEqual("C", catalog.Functions["finish"].Abi);
        Assert.AreEqual("()", catalog.Functions["finish"].ReturnType);
        Assert.IsEmpty(catalog.Functions["finish"].Attributes);
    }

    /// <summary>
    /// Globals distinguish writable hooks from immutable data and retain explicit linkage independently.
    /// </summary>
    [TestMethod]
    public void ForeignGlobalsRetainMutabilityAndCallbacks()
    {
        const string source = """
            unsafe extern "C-unwind" {
                pub static mut ExecutorRun_hook: ExecutorRun_hook_type;
                #[link_name = "native_limits"]
                pub static limits: /* native widths */ [::core::ffi::c_ulong; 2usize];
                pub static mut visitor: ::core::option::Option<unsafe extern "C" fn(a: *mut Node, b: u32)>;
            }
            extern "C" { pub static version: u32; }
            """;
        NativeBindingRawCatalog catalog = NativeBindingRawParser.Parse(source, 17);
        Assert.HasCount(4, catalog.Globals);
        NativeBindingGlobal hook = catalog.Globals["ExecutorRun_hook"];
        Assert.IsTrue(hook.IsMutable);
        Assert.AreEqual("ExecutorRun_hook_type", hook.Representation);
        Assert.AreEqual("ExecutorRun_hook", hook.NativeSymbol);
        Assert.AreEqual("C-unwind", hook.Abi);
        NativeBindingGlobal limits = catalog.Globals["limits"];
        Assert.IsFalse(limits.IsMutable);
        Assert.AreEqual("/* native widths */ [::core::ffi::c_ulong; 2usize]", limits.Representation);
        Assert.AreEqual("native_limits", limits.NativeSymbol);
        Assert.AreSequenceEqual<string>(["#[link_name = \"native_limits\"]"], limits.Attributes);
        Assert.AreEqual("::core::option::Option<unsafe extern \"C\" fn(a: *mut Node, b: u32)>", catalog.Globals["visitor"].Representation);
        Assert.AreEqual("C", catalog.Globals["version"].Abi);
        Assert.IsEmpty(catalog.Functions);
    }

    /// <summary>
    /// Platform-dependent values remain unevaluated expressions, including strings and nested delimiters.
    /// </summary>
    [TestMethod]
    public void ReferenceConstantsPreserveExpressions()
    {
        const string source = """
            pub const SIZEOF_LONG: /* reference */ u32 = 8;
            pub const MASK: u64 = (1 << 63) | (8 >> 1);
            pub const LABEL : & :: core :: ffi :: CStr = c"quoted \"; { /* still text */ }" ;
            pub const VALUES: [u8; 2usize] = [1, 2];
            pub const BLOCK: u32 = { let value = 1; value + 2 };
            """;
        NativeBindingRawCatalog catalog = NativeBindingRawParser.Parse(source, 18);
        Assert.HasCount(5, catalog.ReferenceConstants);
        Assert.AreEqual(new NativeBindingConstant("/* reference */ u32", "8"), catalog.ReferenceConstants["SIZEOF_LONG"]);
        Assert.AreEqual(new NativeBindingConstant("u64", "(1 << 63) | (8 >> 1)"), catalog.ReferenceConstants["MASK"]);
        Assert.AreEqual(new NativeBindingConstant("& :: core :: ffi :: CStr", "c\"quoted \\\"; { /* still text */ }\""), catalog.ReferenceConstants["LABEL"]);
        Assert.AreEqual(new NativeBindingConstant("[u8; 2usize]", "[1, 2]"), catalog.ReferenceConstants["VALUES"]);
        Assert.AreEqual(new NativeBindingConstant("u32", "{ let value = 1; value + 2 }"), catalog.ReferenceConstants["BLOCK"]);
        Assert.IsEmpty(catalog.Globals);
    }

    /// <summary>
    /// Helper methods, associated constants, enum members and misleading trivia cannot become raw exports.
    /// </summary>
    [TestMethod]
    public void RawInventoryExcludesHelpersAndTrivia()
    {
        const string source = """
            impl Helper {
            pub const FLAG: u32 = 1;
                pub const fn new() -> Self { Self {} }
                pub fn read(&self) -> u32 { 1 }
            }
            pub mod State {
            pub const FLAG: u32 = 2;
                extern "C" { pub fn nested(); }
            }
            pub type Callback = ::core::option::Option<
                unsafe extern "C-unwind" fn(value: u32)
            >;
            /* outer /* nested */
            unsafe extern "C" { pub fn fake(); }
            */
            #[doc = "}
            unsafe extern \"C\" { pub fn fake(); }
            "]
            unsafe extern "C-unwind" {
                // #[link_name = "wrong"]
                #[doc = "Ignore } and ; here"]
                pub fn real(value: /* retained */ u32);
            }
            """;
        NativeBindingRawCatalog catalog = NativeBindingRawParser.Parse(source, 18);
        Assert.AreSequenceEqual<string>(["real"], catalog.Functions.Keys);
        Assert.AreEqual("real", catalog.Functions["real"].NativeSymbol);
        Assert.AreSequenceEqual<NativeBindingParameter>([new("value", "/* retained */ u32")], catalog.Functions["real"].Parameters);
        Assert.AreSequenceEqual<string>(["#[doc = \"Ignore } and ; here\"]"], catalog.Functions["real"].Attributes);
        Assert.IsEmpty(catalog.Globals);
        Assert.IsEmpty(catalog.ReferenceConstants);
    }

    /// <summary>
    /// Declaration order cannot affect catalog serialization or the ordinal ordering of exported identifiers.
    /// </summary>
    [TestMethod]
    public void RawCatalogOrderingIsStable()
    {
        const string first = """
            pub const Z: u32 = 1;
            pub const a: u32 = 2;
            extern "C" { pub fn Zed(); pub static Zoo: u32; }
            extern "C" { pub fn apple(); pub static aardvark: u32; }
            """;
        const string second = """
            extern "C" { pub static aardvark: u32; pub fn apple(); }
            extern "C" { pub static Zoo: u32; pub fn Zed(); }
            pub const a: u32 = 2;
            pub const Z: u32 = 1;
            """;
        NativeBindingRawCatalog catalog = NativeBindingRawParser.Parse(first, 18);
        Assert.AreSequenceEqual<string>(["Zed", "apple"], catalog.Functions.Keys);
        Assert.AreSequenceEqual<string>(["Zoo", "aardvark"], catalog.Globals.Keys);
        Assert.AreSequenceEqual<string>(["Z", "a"], catalog.ReferenceConstants.Keys);
        Assert.AreEqual(JsonSerializer.Serialize(catalog), JsonSerializer.Serialize(NativeBindingRawParser.Parse(second, 18)));
    }

    /// <summary>
    /// Invalid foreign declarations fail as a whole instead of yielding a truncated or ambiguous signature.
    /// </summary>
    /// <param name="source">One malformed or duplicate declaration.</param>
    [TestMethod]
    [DataRow("extern \"C\" { pub fn f(); pub fn f(); }")]
    [DataRow("extern \"C\" { pub static f: u32; pub static f: u32; }")]
    [DataRow("extern \"C\" { pub static f: u32; pub fn f(); }")]
    [DataRow("extern \"C\" { pub fn f(a: u32, a: u64); }")]
    [DataRow("extern \"C\" { pub fn f(a:); }")]
    [DataRow("extern \"C\" { pub fn f(u32); }")]
    [DataRow("extern \"C\" { pub fn f(a: u32,, b: u32); }")]
    [DataRow("extern \"C\" { pub fn f(a: u32, ..., b: u32); }")]
    [DataRow("extern \"C\" { pub fn f() ->; }")]
    [DataRow("extern \"C\" { pub fn f() bool; }")]
    [DataRow("extern \"C\" { pub fn f(a: [u8; 3); }")]
    [DataRow("extern \"C\" { pub fn f() }")]
    [DataRow("extern \"C\" { pub static missing:; }")]
    [DataRow("extern \"C\" { pub type Opaque; }")]
    [DataRow("extern \"C\" { #[link_name = \"first\"] #[link_name = \"second\"] pub fn f(); }")]
    [DataRow("extern \"C\" { #[link_name = 1] pub fn f(); }")]
    [DataRow("extern \"C\" { #[link_name = \"\"] pub fn f(); }")]
    [DataRow("extern \"C\" { #[link_name = \"unused\"] }")]
    [DataRow("extern \"C\" { pub fn f();")]
    [DataRow("extern \"Rust\" { pub fn f(); }")]
    [DataRow("pub const F: u32 = 1;\npub const F: u32 = 2;")]
    [DataRow("pub const F: = 1;")]
    [DataRow("pub const F: u32 =;")]
    [DataRow("pub const F: u32 = [1, 2;")]
    [DataRow("pub const F: u32 = 1;\nextern \"C\" { pub fn F(); }")]
    [DataRow("extern \"C\" { pub static F: u32; }\npub const F: u32 = 1;")]
    [DataRow("extern \"C\" { pub fn f(); }\nextern \"C-unwind\" { pub fn f(); }")]
    [DataRow("extern \"C\" { pub fn f() -> [u8; 2]; pub static f: u32; }")]
    [DataRow("extern \"C\" { pub fn f(a: u32, ..., ...); }")]
    [DataRow("extern \"C\" { #[link_name = \"bad\\nname\"] pub fn f(); }")]
    [DataRow("extern \"C\" { #[link_name = \"bad] pub fn f(); }")]
    public void MalformedRawDeclarationsFailExplicitly(string source)
        => Assert.ThrowsExactly<FormatException>(() => NativeBindingRawParser.Parse(source, 18));

    /// <summary>
    /// Empty inventories stay empty, while unsupported majors and missing input are rejected.
    /// </summary>
    [TestMethod]
    public void EmptyInventoriesAndArgumentBoundariesAreExplicit()
    {
        NativeBindingRawCatalog empty = NativeBindingRawParser.Parse("extern \"C\" { }", 13);
        Assert.IsEmpty(empty.Functions);
        Assert.IsEmpty(empty.Globals);
        Assert.IsEmpty(empty.ReferenceConstants);
        Assert.ThrowsExactly<ArgumentNullException>(() => NativeBindingRawParser.Parse(null!, 18));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => NativeBindingRawParser.Parse(string.Empty, 12));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => NativeBindingRawParser.Parse(string.Empty, 20));
    }
}
