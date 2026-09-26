namespace Ankus.Build.Tests;

/// <summary>
/// Checks concrete formatting bounds independently of inheritance-based cast acceptance.
/// </summary>
[TestClass]
public sealed class NativeBindingNodeLayoutTests
{
    /// <summary>
    /// Concrete structures, typedefs and list families use measured layouts; abstract roots do not replace them.
    /// </summary>
    [TestMethod]
    public void ConcreteNodeLayoutsPreserveAliasesAndListFamilies()
    {
        NativeBindingCatalog catalog = NativeBindingParser.Parse("""
            pub enum NodeTag { T_Invalid = 0, T_Leaf = 7, T_Alias = 8, T_List = 9, T_IntList = 10, T_OidList = 11, T_XidList = 12, T_Unknown = 99, }
            pub struct Node { pub type_: NodeTag, }
            pub struct Leaf { pub type_: NodeTag, pub value: i64, }
            pub type Alias = Leaf;
            pub struct List { pub type_: NodeTag, pub values: *mut Node, }
            """, 18);
        NativeBindingLayout layout = Layout(new Dictionary<string, NativeBindingTypeLayout>(StringComparer.Ordinal)
        {
            ["Node"] = new(4, 4, new Dictionary<string, NativeBindingFieldLayout>()),
            ["Leaf"] = new(24, 8, new Dictionary<string, NativeBindingFieldLayout>()),
            ["List"] = new(32, 8, new Dictionary<string, NativeBindingFieldLayout>()),
        });
        Assert.AreEqual("7:24:8;8:24:8;9:32:8;10:32:8;11:32:8;12:32:8", NativeBindingNodeLayouts.Encode(catalog, layout));
        Assert.AreEqual("7:40:8;8:40:8;9:32:8;10:32:8;11:32:8;12:32:8", NativeBindingNodeLayouts.Encode(catalog,
            layout with { Types = new Dictionary<string, NativeBindingTypeLayout>(layout.Types) { ["Leaf"] = new(40, 8, new Dictionary<string, NativeBindingFieldLayout>()) } }));
    }

    /// <summary>
    /// Legacy scalar tags share Value, while later versions retain independent measured scalar structures.
    /// </summary>
    /// <param name="major">The selected declaration major.</param>
    [TestMethod]
    [DataRow(13)]
    [DataRow(14)]
    [DataRow(15)]
    [DataRow(18)]
    [DataRow(19)]
    public void ConcreteValueLayoutsRespectSelectedMajor(int major)
    {
        NativeBindingCatalog catalog = NativeBindingParser.Parse("""
            pub enum NodeTag { T_Invalid = 0, T_Integer = 1, T_Float = 2, T_String = 3, T_BitString = 4, T_Null = 5, }
            pub struct Node { pub type_: NodeTag, }
            pub struct Value { pub type_: NodeTag, pub payload: i64, }
            pub struct Integer { pub type_: NodeTag, pub ival: i32, }
            pub struct String { pub type_: NodeTag, pub sval: *mut i8, }
            """, major);
        NativeBindingLayout layout = Layout(new Dictionary<string, NativeBindingTypeLayout>(StringComparer.Ordinal)
        {
            ["Value"] = new(24, 8, new Dictionary<string, NativeBindingFieldLayout>()),
            ["Integer"] = new(8, 4, new Dictionary<string, NativeBindingFieldLayout>()),
            ["String"] = new(16, 8, new Dictionary<string, NativeBindingFieldLayout>()),
        });
        Assert.AreEqual(major <= 14 ? "1:24:8;2:24:8;3:24:8;4:24:8;5:24:8" : "1:8:4;3:16:8",
            NativeBindingNodeLayouts.Encode(catalog, layout));
    }

    private static NativeBindingLayout Layout(IReadOnlyDictionary<string, NativeBindingTypeLayout> types)
        => new(180006, 8, 8, true, true, "linux-x64", types, new Dictionary<string, NativeBindingEnumLayout>());
}
