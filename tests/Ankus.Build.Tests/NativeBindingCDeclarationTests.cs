namespace Ankus.Build.Tests;

/// <summary>
/// Verifies native C type identity and declarator precedence for generated calls and hooks.
/// </summary>
[TestClass]
public sealed class NativeBindingCDeclarationTests
{
    private static readonly NativeBindingCatalog s_catalog = NativeBindingParser.Parse("""
        pub enum NodeTag { T_Invalid = 0, }
        pub struct Node { pub type_: NodeTag, }
        pub struct NativeRecord { pub value: u32, }
        pub union NativeUnion { pub value: u32, }
        pub type NativeWidth = ::core::ffi::c_ulong;
        pub type NativeCallback = ::core::option::Option<unsafe extern "C" fn(value: u32)>;
        pub mod ScanDirection { pub type Type = ::core::ffi::c_int; pub const ForwardScanDirection: Type = 1; }
        """, 18);

    /// <summary>
    /// Exact aliases, signedness, pointer qualifiers and array binding survive projection independently.
    /// </summary>
    /// <param name="representation">The bindgen type.</param>
    /// <param name="expected">The independently specified C declaration.</param>
    [TestMethod]
    [DataRow("NativeWidth", "NativeWidth value")]
    [DataRow("NativeCallback", "NativeCallback value")]
    [DataRow("::core::ffi::c_long", "long value")]
    [DataRow("::core::ffi::c_ulong", "unsigned long value")]
    [DataRow("::core::ffi::c_char", "char value")]
    [DataRow("::core::ffi::c_schar", "signed char value")]
    [DataRow("::core::ffi::c_uchar", "unsigned char value")]
    [DataRow("i64", "int64_t value")]
    [DataRow("u64", "uint64_t value")]
    [DataRow("usize", "uintptr_t value")]
    [DataRow("Oid", "Oid value")]
    [DataRow("Datum", "Datum value")]
    [DataRow("*mut NativeRecord", "struct NativeRecord *value")]
    [DataRow("NativeUnion", "union NativeUnion value")]
    [DataRow("ScanDirection::Type", "ScanDirection value")]
    [DataRow("*const *mut Node", "Node *const *value")]
    [DataRow("*mut *const Node", "const Node **value")]
    [DataRow("*const [u32; 3usize]", "const uint32_t (*value)[3]")]
    [DataRow("[*const Node; 3]", "const Node *value[3]")]
    [DataRow("[[u8; 2]; 3]", "uint8_t value[3][2]")]
    [DataRow("*mut ::core::ffi::c_void", "void *value")]
    [DataRow("/* outer /* inner */ end */ *const // next\n NativeWidth", "const NativeWidth *value")]
    public void ValuesPreserveNativeTypeIdentity(string representation, string expected)
        => Assert.AreEqual(expected, NativeBindingCDeclaration.Value(s_catalog, representation, "value"));

    /// <summary>
    /// Nested callback results and arrays retain their C function-pointer precedence and const levels.
    /// </summary>
    [TestMethod]
    public void CallbacksPreserveNestedResultsAndQualifiers()
    {
        const string callback = "::core::option::Option<unsafe extern \"C-unwind\" fn(value: u32, next: *const NativeWidth) -> *mut Node>";
        Assert.AreEqual("Node *(*visit)(uint32_t ankus_arg0, const NativeWidth *ankus_arg1)",
            NativeBindingCDeclaration.Value(s_catalog, callback, "visit"));
        Assert.AreEqual("void (*visit)(uint32_t ankus_arg0)", NativeBindingCDeclaration.Value(s_catalog,
            "::core::option::Option<unsafe extern \"C\" fn(value: u32,),>", "visit"));
        Assert.AreEqual("Node *(*const *visit)(uint32_t ankus_arg0, const NativeWidth *ankus_arg1)",
            NativeBindingCDeclaration.Value(s_catalog, "*const " + callback, "visit"));
        Assert.AreEqual("Node *(*visit[2])(uint32_t ankus_arg0, const NativeWidth *ankus_arg1)",
            NativeBindingCDeclaration.Value(s_catalog, "[" + callback + "; 2]", "visit"));
        Assert.AreEqual("Node *(*(*resolve)(void))(uint32_t ankus_arg0, const NativeWidth *ankus_arg1)",
            NativeBindingCDeclaration.Value(s_catalog, "::core::option::Option<unsafe extern \"C\" fn() -> " + callback + ">", "resolve"));
    }

    /// <summary>
    /// Function declarations support ordinary, void, never-returning, variadic and callback-returning results.
    /// </summary>
    [TestMethod]
    public void FunctionPrototypesRetainCompleteSignatures()
    {
        NativeBindingRawCatalog raw = NativeBindingRawParser.Parse("""
            extern "C" {
                pub fn read(count: NativeWidth, direction: ScanDirection::Type) -> *const Node;
                pub fn done();
                pub fn fail() -> !;
                pub fn report(format: *const ::core::ffi::c_char, ...);
                pub fn resolver() -> ::core::option::Option<unsafe extern "C" fn(value: u32)>;
            }
            """, 18);
        Assert.AreEqual("const Node *(*read)(NativeWidth ankus_arg0, ScanDirection ankus_arg1)",
            NativeBindingCDeclaration.FunctionPointer(s_catalog, raw.Functions["read"], "read"));
        Assert.AreEqual("void (*done)(void)", NativeBindingCDeclaration.FunctionPointer(s_catalog, raw.Functions["done"], "done"));
        Assert.AreEqual("void (*fail)(void)", NativeBindingCDeclaration.FunctionPointer(s_catalog, raw.Functions["fail"], "fail"));
        Assert.AreEqual("void (*report)(const char *ankus_arg0, ...)", NativeBindingCDeclaration.FunctionPointer(s_catalog, raw.Functions["report"], "report"));
        Assert.AreEqual("void (*(*resolve)(void))(uint32_t ankus_arg0)", NativeBindingCDeclaration.FunctionPointer(s_catalog, raw.Functions["resolver"], "resolve"));
    }

    /// <summary>
    /// Every retained function and global projects without silently replacing an unsupported type by an address.
    /// </summary>
    /// <param name="major">The pinned PostgreSQL major.</param>
    [TestMethod]
    [DataRow(13)]
    [DataRow(14)]
    [DataRow(15)]
    [DataRow(16)]
    [DataRow(17)]
    [DataRow(18)]
    [DataRow(19)]
    public void AllVersionedForeignDeclarationsProject(int major)
    {
        NativeBindingCatalog catalog = NativeBindingResources.ReadCatalog(major);
        NativeBindingRawCatalog raw = NativeBindingResources.ReadRawCatalog(major);
        foreach ((string name, NativeBindingFunction function) in raw.Functions)
        {
            string declaration = NativeBindingCDeclaration.FunctionPointer(catalog, function, "ankus_" + name);
            Assert.Contains("ankus_" + name, declaration);
        }

        foreach ((string name, NativeBindingGlobal global) in raw.Globals)
        {
            string declaration = NativeBindingCDeclaration.Global(catalog, global.Representation, "ankus_" + name);
            Assert.Contains("ankus_" + name, declaration);
        }

        Assert.AreEqual(major >= 18
            ? "void (*execute)(struct QueryDesc *ankus_arg0, ScanDirection ankus_arg1, uint64 ankus_arg2)"
            : "void (*execute)(struct QueryDesc *ankus_arg0, ScanDirection ankus_arg1, uint64 ankus_arg2, bool ankus_arg3)",
            NativeBindingCDeclaration.FunctionPointer(catalog, raw.Functions["ExecutorRun"], "execute"));
        Assert.AreEqual("union ListCell *(*head)(const List *ankus_arg0)",
            NativeBindingCDeclaration.FunctionPointer(catalog, raw.Functions["list_head"], "head"));
    }

    /// <summary>
    /// Malformed or unsupported types fail instead of producing a permissive C declaration.
    /// </summary>
    /// <param name="representation">The invalid expression.</param>
    [TestMethod]
    [DataRow("")]
    [DataRow("Unknown")]
    [DataRow("u32 trailing")]
    [DataRow("()")]
    [DataRow("!")]
    [DataRow("[u8; 0]")]
    [DataRow("[u8; -1]")]
    [DataRow("[u8; 2147483648]")]
    [DataRow("[(); 2]")]
    [DataRow("*mutable u32")]
    [DataRow("*mut")]
    [DataRow("*mut ()")]
    [DataRow("*const !")]
    [DataRow("ScanDirection::Wrong")]
    [DataRow("/* unterminated")]
    [DataRow("::core::option::Option<u32>")]
    [DataRow("::core::option::Option<unsafe extern \"Rust\" fn()>")]
    [DataRow("::core::option::Option<unsafe extern \"C\" fn(...)>")]
    [DataRow("::core::option::Option<unsafe extern \"C\" fn(a: ())>")]
    [DataRow("::core::option::Option<unsafe extern \"C\" fn(a: u32, ..., b: u32)>")]
    [DataRow("::core::option::Option<unsafe extern \"C\" fn() -> [u8; 2]>")]
    [DataRow("::core::option::Option<unsafe extern \"C\" fn()")]
    public void InvalidTypesAreRejected(string representation)
        => Assert.ThrowsExactly<FormatException>(() => NativeBindingCDeclaration.Value(s_catalog, representation, "value"));

    /// <summary>
    /// Only a foreign global's outermost zero extent denotes an incomplete C array.
    /// </summary>
    [TestMethod]
    public void GlobalArraysRetainUnknownOuterExtents()
    {
        Assert.AreEqual("uint8_t globals[]", NativeBindingCDeclaration.Global(s_catalog, "[u8; 0usize]", "globals"));
        Assert.AreEqual("uint8_t globals[][2]", NativeBindingCDeclaration.Global(s_catalog, "[[u8; 2]; 0]", "globals"));
        Assert.AreEqual("uint8_t globals[2]", NativeBindingCDeclaration.Global(s_catalog, "[u8; 2]", "globals"));
        Assert.ThrowsExactly<FormatException>(() => NativeBindingCDeclaration.Global(s_catalog, "[[u8; 0]; 2]", "globals"));
        Assert.ThrowsExactly<FormatException>(() => NativeBindingCDeclaration.Global(s_catalog, "()", "globals"));
    }

    /// <summary>
    /// Generated identifiers and excessive nesting cannot inject C or exhaust the recursive reader.
    /// </summary>
    [TestMethod]
    public void InputBoundariesFailExplicitly()
    {
        Assert.ThrowsExactly<FormatException>(() => NativeBindingCDeclaration.Value(s_catalog, "u32", "x; int other"));
        Assert.ThrowsExactly<FormatException>(() => NativeBindingCDeclaration.Value(s_catalog, "u32", ""));
        Assert.ThrowsExactly<ArgumentNullException>(() => NativeBindingCDeclaration.Value(s_catalog, null!, "value"));
        Assert.ThrowsExactly<ArgumentNullException>(() => NativeBindingCDeclaration.Value(s_catalog, "u32", null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => NativeBindingCDeclaration.Value(null!, "u32", "value"));
        string deep = string.Concat(Enumerable.Repeat("*mut ", 128)) + "u32";
        Assert.ThrowsExactly<FormatException>(() => NativeBindingCDeclaration.Value(s_catalog, deep, "value"));
        Assert.AreEqual("uint32_t " + new string('*', 127) + "value",
            NativeBindingCDeclaration.Value(s_catalog, deep[5..], "value"));
        var function = new NativeBindingFunction("f", "C", [], "()", false, []);
        Assert.ThrowsExactly<FormatException>(() => NativeBindingCDeclaration.FunctionPointer(s_catalog, function with { Abi = "stdcall" }, "value"));
        Assert.ThrowsExactly<FormatException>(() => NativeBindingCDeclaration.FunctionPointer(s_catalog, function with { IsVariadic = true }, "value"));
        Assert.ThrowsExactly<FormatException>(() => NativeBindingCDeclaration.FunctionPointer(s_catalog, function with { ReturnType = "[u8; 2]" }, "value"));
    }
}
