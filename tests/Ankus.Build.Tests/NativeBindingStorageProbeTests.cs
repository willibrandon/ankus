namespace Ankus.Build.Tests;

/// <summary>
/// Verifies complete selected-header storage observations and rejection of inconsistent native facts.
/// </summary>
[TestClass]
public sealed class NativeBindingStorageProbeTests
{
    private static readonly NativeHeaderTarget s_target = new(180006, "linux-x64", 8, true, 21, NativeNumericModelFixture.Binary80);
    private static readonly NativeHeaderScalar s_integer = new("int");
    private const string Header = "storage|2|180006|8|1|linux-x64|21|" + NativeNumericModelFixture.EncodedBinary80 + "\n";
    private const string Argument = "value|call|0|4|4|-|1\n";

    /// <summary>
    /// Functions, globals, callback addresses and array shapes retain their distinct native storage contracts.
    /// </summary>
    [TestMethod]
    public void CompleteHeaderStorageRetainsNativeShapes()
    {
        var adjusted = new NativeHeaderAdjusted(new NativeHeaderArray(s_integer, 3), new NativeHeaderPointer(s_integer));
        var callback = new NativeHeaderAlias("Callback", new NativeHeaderPointer(
            new NativeHeaderFunction(new NativeHeaderScalar("void"), [s_integer], false, true, false)));
        var symbols = new Dictionary<string, NativeHeaderSymbol>
        {
            ["call"] = Symbol("call", new NativeHeaderFunction(new NativeHeaderScalar("_Bool"),
                [new NativeHeaderAlias("NativeWidth", new NativeHeaderScalar("unsigned long long")), adjusted], false, true, false)),
            ["done"] = Symbol("done", new NativeHeaderFunction(new NativeHeaderScalar("void"), [], false, true, false)),
            ["report"] = Symbol("report", new NativeHeaderFunction(new NativeHeaderScalar("void"), [callback], true, true, false)),
            ["unknown"] = Symbol("unknown", new NativeHeaderFunction(s_integer, [], false, false, false)),
            ["hook"] = Symbol("hook", callback, false),
            ["rows"] = Symbol("rows", new NativeHeaderArray(s_integer, 3), false),
            ["tail"] = Symbol("tail", new NativeHeaderArray(s_integer, null), false),
            ["zero"] = Symbol("zero", new NativeHeaderArray(s_integer, 0), false),
            ["aligned"] = Symbol("aligned", new NativeHeaderAlias("Aligned", s_integer), false),
        };
        var catalog = new NativeHeaderCatalog(s_target, symbols);
        NativeHeaderStorage storage = NativeBindingStorageProbe.Read(catalog, Header + """
            value|zero|global|0|4|4|-
            value|tail|global|-|4|4|-
            value|rows|global|12|16|4|-
            value|aligned|global|4|16|-|1
            value|hook|global|8|8|-|-
            value|unknown|result|4|4|-|1
            value|report|0|8|8|-|-
            value|call|result|1|1|-|0
            value|call|1|8|8|-|-
            value|call|0|8|8|-|0
            """);
        Assert.AreEqual(s_target, storage.Headers.Target);
        Assert.IsTrue(Assert.IsInstanceOfType<NativeHeaderFunction>(storage.Headers.Symbols["report"].Type).IsVariadic);
        Assert.IsFalse(Assert.IsInstanceOfType<NativeHeaderFunction>(storage.Headers.Symbols["unknown"].Type).HasPrototype);
        Assert.IsTrue(Assert.IsInstanceOfType<NativeHeaderFunction>(storage.Headers.Symbols["done"].Type).HasPrototype);
        Assert.AreSequenceEqual(symbols.Keys.Order(StringComparer.Ordinal), storage.Symbols.Keys);
        Assert.AreSequenceEqual<NativeHeaderValueStorage>([new(8, 8, null, false), new(8, 8, null, null)], storage.Symbols["call"].Parameters);
        Assert.AreEqual(new NativeHeaderValueStorage(1, 1, null, false), storage.Symbols["call"].Result);
        Assert.IsNull(storage.Symbols["call"].Global);
        Assert.IsEmpty(storage.Symbols["done"].Parameters);
        Assert.IsNull(storage.Symbols["done"].Result);
        Assert.IsNull(storage.Symbols["done"].Global);
        Assert.AreEqual(new NativeHeaderValueStorage(8, 8, null, null), Assert.ContainsSingle(storage.Symbols["report"].Parameters));
        Assert.AreEqual(new NativeHeaderValueStorage(4, 4, null, true), storage.Symbols["unknown"].Result);
        Assert.AreEqual(new NativeHeaderValueStorage(8, 8, null, null), storage.Symbols["hook"].Global);
        Assert.IsEmpty(storage.Symbols["hook"].Parameters);
        Assert.IsNull(storage.Symbols["hook"].Result);
        Assert.AreEqual(new NativeHeaderValueStorage(12, 16, 4, null), storage.Symbols["rows"].Global);
        Assert.AreEqual(new NativeHeaderValueStorage(null, 4, 4, null), storage.Symbols["tail"].Global);
        Assert.AreEqual(new NativeHeaderValueStorage(0, 4, 4, null), storage.Symbols["zero"].Global);
        Assert.AreEqual(new NativeHeaderValueStorage(4, 16, null, true), storage.Symbols["aligned"].Global);
        var reversed = new NativeHeaderCatalog(s_target, symbols.Reverse().ToDictionary());
        Assert.AreEqual(NativeBindingStorageProbe.GenerateSource(catalog, ""), NativeBindingStorageProbe.GenerateSource(reversed, ""));
    }

    /// <summary>
    /// Every requested value must have one valid observation with the correct signedness and shape.
    /// </summary>
    /// <param name="observations">The invalid native value records.</param>
    [TestMethod]
    [DataRow("")]
    [DataRow(Argument + Argument)]
    [DataRow(Argument + "value|other|0|4|4|-|1\n")]
    [DataRow(Argument + "value|call|result|4|4|-|1\n")]
    [DataRow("value|call|00|4|4|-|1\n")]
    [DataRow("value|call|-1|4|4|-|1\n")]
    [DataRow("value|call|0|-|4|-|1\n")]
    [DataRow("value|call|0|0|4|-|1\n")]
    [DataRow("value|call|0|4|0|-|1\n")]
    [DataRow("value|call|0|4|3|-|1\n")]
    [DataRow("value|call|0|4|-|-|1\n")]
    [DataRow("value|call|0|4|4|4|1\n")]
    [DataRow("value|call|0|4|4|-|-\n")]
    [DataRow("value|call|0|4|4|-|0\n")]
    [DataRow("value|call|0|4|4|-|2\n")]
    [DataRow("value|call|0|-1|4|-|1\n")]
    [DataRow("value|call|0|+4|4|-|1\n")]
    [DataRow("value|call|0|18446744073709551616|4|-|1\n")]
    [DataRow("value|call|0|4|4|-|1|extra\n")]
    [DataRow("invalid\n")]
    public void InvalidStorageObservationsFailExplicitly(string observations)
        => Assert.ThrowsExactly<FormatException>(() => NativeBindingStorageProbe.Read(FunctionCatalog(s_integer), Header + observations));

    /// <summary>
    /// A changed minor version, compiler, runtime, pointer width or byte order cannot reuse collected types.
    /// </summary>
    /// <param name="header">The incompatible or malformed native header.</param>
    [TestMethod]
    [DataRow("")]
    [DataRow("storage|2|180006|8|1|linux-x64|21")]
    [DataRow("storage|2|180005|8|1|linux-x64|21|" + NativeNumericModelFixture.EncodedBinary80)]
    [DataRow("storage|2|170011|8|1|linux-x64|21|" + NativeNumericModelFixture.EncodedBinary80)]
    [DataRow("storage|2|180006|4|1|linux-x64|21|" + NativeNumericModelFixture.EncodedBinary80)]
    [DataRow("storage|2|180006|8|0|linux-x64|21|" + NativeNumericModelFixture.EncodedBinary80)]
    [DataRow("storage|2|180006|8|2|linux-x64|21|" + NativeNumericModelFixture.EncodedBinary80)]
    [DataRow("storage|2|180006|8|1|osx-arm64|21|" + NativeNumericModelFixture.EncodedBinary80)]
    [DataRow("storage|2|180006|8|1|linux-x64|19|" + NativeNumericModelFixture.EncodedBinary80)]
    [DataRow("storage|2|180006|8|1|linux-x64|0|" + NativeNumericModelFixture.EncodedBinary80)]
    [DataRow("storage|2|180006|8|1|linux-x64|2147483648|" + NativeNumericModelFixture.EncodedBinary80)]
    [DataRow("storage|2|180006|8|1|linux-x64|21|" + NativeNumericModelFixture.EncodedBinary80 + "|extra")]
    [DataRow("storage|1|180006|8|1|linux-x64|21")]
    public void StorageTargetMustMatchCollectedHeaders(string header)
        => Assert.ThrowsExactly<FormatException>(() => NativeBindingStorageProbe.Read(FunctionCatalog(s_integer), header + "\n" + Argument));

    /// <summary>
    /// A storage observation cannot be reused with a different interpretation of identically sized native values.
    /// </summary>
    /// <param name="index">The independent numeric identity field to change.</param>
    /// <param name="value">Its alternate valid value.</param>
    [TestMethod]
    [DataRow(0, "0")]
    [DataRow(1, "2")]
    [DataRow(2, "0")]
    [DataRow(3, "10")]
    [DataRow(4, "23")]
    [DataRow(5, "-126")]
    [DataRow(6, "127")]
    [DataRow(7, "52")]
    [DataRow(8, "-1022")]
    [DataRow(9, "1023")]
    [DataRow(10, "113")]
    [DataRow(11, "-16382")]
    [DataRow(12, "16385")]
    public void HeaderStorageRejectsNumericMismatch(int index, string value)
    {
        string[] fields = NativeNumericModelFixture.EncodedBinary80.Split(',');
        fields[index] = value;
        string changed = "storage|2|180006|8|1|linux-x64|21|" + string.Join(',', fields) + "\n" + Argument;
        NativeHeaderCatalog catalog = FunctionCatalog(s_integer);
        Assert.ThrowsExactly<FormatException>(() => NativeBindingStorageProbe.Read(catalog, changed));
        Assert.AreEqual(new NativeHeaderValueStorage(4, 4, null, true),
            Assert.ContainsSingle(NativeBindingStorageProbe.Read(catalog, Header + Argument).Symbols["call"].Parameters));
    }

    /// <summary>
    /// Incomplete and fixed arrays require correct extents and strides without inferring a missing length.
    /// </summary>
    /// <param name="incomplete">Whether the outer native extent is unknown.</param>
    /// <param name="observation">The invalid array measurement.</param>
    [TestMethod]
    [DataRow(false, "-|4|4|-")]
    [DataRow(false, "12|4|-|-")]
    [DataRow(false, "12|4|0|-")]
    [DataRow(false, "12|4|8|-")]
    [DataRow(false, "12|4|4|0")]
    [DataRow(false, "0|4|4|-")]
    [DataRow(false, "12|4|18446744073709551615|-")]
    [DataRow(true, "12|4|4|-")]
    [DataRow(true, "-|4|-|-")]
    [DataRow(true, "-|4|0|-")]
    public void ArrayStorageMustRetainExactExtent(bool incomplete, string observation)
    {
        NativeHeaderCatalog catalog = GlobalCatalog(new NativeHeaderArray(s_integer, incomplete ? null : 3));
        Assert.ThrowsExactly<FormatException>(() => NativeBindingStorageProbe.Read(catalog, Header + "value|state|global|" + observation));
    }

    /// <summary>
    /// Native integer kinds preserve known signs while plain char and enums retain the compiler's choice.
    /// </summary>
    /// <param name="kind">The semantic native type.</param>
    /// <param name="sign">The independently observed signedness field.</param>
    /// <param name="accepted">Whether that field is compatible with the type.</param>
    [TestMethod]
    [DataRow("unsigned int", "0", true)]
    [DataRow("unsigned int", "1", false)]
    [DataRow("_Bool", "0", true)]
    [DataRow("_Bool", "1", false)]
    [DataRow("char", "0", false)]
    [DataRow("char", "1", true)]
    [DataRow("enum", "0", true)]
    [DataRow("enum", "1", true)]
    [DataRow("double", "-", true)]
    [DataRow("double", "1", false)]
    public void SignednessMatchesNativeValueKind(string kind, string sign, bool accepted)
    {
        NativeHeaderType type = kind == "enum" ? new NativeHeaderEnum("Choice") : new NativeHeaderScalar(kind);
        NativeHeaderCatalog catalog = GlobalCatalog(type);
        ulong size = kind is "char" or "_Bool" ? 1UL : kind == "double" ? 8UL : 4UL;
        string extent = size.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string output = Header + "value|state|global|" + extent + "|" + extent + "|-|" + sign;
        if (accepted)
        {
            bool? expected = sign == "-" ? null : sign == "1";
            Assert.AreEqual(new NativeHeaderValueStorage(size, size, null, expected), NativeBindingStorageProbe.Read(catalog, output).Symbols["state"].Global);
        }
        else
        {
            Assert.ThrowsExactly<FormatException>(() => NativeBindingStorageProbe.Read(catalog, output));
        }
    }

    /// <summary>
    /// Unsigned extents retain their complete numeric range and pointer widths remain target-specific.
    /// </summary>
    [TestMethod]
    public void StorageBoundsAndPointerWidthAreExact()
    {
        NativeHeaderCatalog catalog = GlobalCatalog(new NativeHeaderRecord("Large", false));
        NativeHeaderValueStorage? storage = NativeBindingStorageProbe.Read(catalog,
            Header + "value|state|global|18446744073709551615|1|-|-").Symbols["state"].Global;
        Assert.AreEqual(new NativeHeaderValueStorage(ulong.MaxValue, 1, null, null), storage);
        Assert.AreEqual(new NativeHeaderValueStorage(0, 1, null, null), NativeBindingStorageProbe.Read(catalog,
            Header + "value|state|global|0|1|-|-").Symbols["state"].Global);
        catalog = GlobalCatalog(new NativeHeaderPointer(s_integer));
        Assert.ThrowsExactly<FormatException>(() => NativeBindingStorageProbe.Read(catalog, Header + "value|state|global|4|4|-|-"));
        Assert.ThrowsExactly<FormatException>(() => NativeBindingStorageProbe.Read(catalog, Header + "value|state|global|8|8|-|0"));
        Assert.AreEqual(new NativeHeaderValueStorage(8, 16, null, null), NativeBindingStorageProbe.Read(catalog,
            Header + "value|state|global|8|16|-|-").Symbols["state"].Global);
    }

    /// <summary>
    /// Forward-declared records and enums have unknown object storage rather than fabricated zero-sized layouts.
    /// </summary>
    /// <param name="enumeration">Whether the opaque tag is an enum.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void OpaqueTagsRetainUnknownStorage(bool enumeration)
    {
        NativeHeaderType type = enumeration ? new NativeHeaderEnum("Hidden", false) : new NativeHeaderRecord("Hidden", false, false);
        NativeHeaderCatalog catalog = GlobalCatalog(type);
        Assert.AreEqual(new NativeHeaderValueStorage(null, null, null, null), NativeBindingStorageProbe.Read(catalog,
            Header + "value|state|global|-|-|-|-").Symbols["state"].Global);
        Assert.ThrowsExactly<FormatException>(() => NativeBindingStorageProbe.Read(catalog, Header + "value|state|global|0|1|-|-"));
        Assert.ThrowsExactly<FormatException>(() => NativeBindingStorageProbe.Read(catalog, Header + "value|state|global|-|1|-|-"));
        Assert.ThrowsExactly<FormatException>(() => NativeBindingStorageProbe.Read(catalog, Header + "value|state|global|-|-|-|0"));
        NativeHeaderCatalog address = GlobalCatalog(new NativeHeaderPointer(type));
        Assert.AreEqual(new NativeHeaderValueStorage(8, 8, null, null), NativeBindingStorageProbe.Read(address,
            Header + "value|state|global|8|8|-|-").Symbols["state"].Global);
    }

    /// <summary>
    /// Empty selections are valid, while invalid catalog targets and non-object storage fail before emission.
    /// </summary>
    [TestMethod]
    public void InvalidStorageCatalogsFailBeforeEmission()
    {
        var empty = new NativeHeaderCatalog(s_target, new Dictionary<string, NativeHeaderSymbol>());
        Assert.IsEmpty(NativeBindingStorageProbe.Read(empty, Header).Symbols);
        Assert.ThrowsExactly<FormatException>(() => NativeBindingStorageProbe.GenerateSource(
            empty with { Target = s_target with { PointerSize = 4 } }, ""));
        Assert.ThrowsExactly<FormatException>(() => NativeBindingStorageProbe.GenerateSource(
            empty with { Target = s_target with { ClangMajor = 0 } }, ""));
        Assert.ThrowsExactly<FormatException>(() => NativeBindingStorageProbe.GenerateSource(GlobalCatalog(new NativeHeaderScalar("void")), ""));
        Assert.ThrowsExactly<FormatException>(() => NativeBindingStorageProbe.GenerateSource(FunctionCatalog(new NativeHeaderScalar("void")), ""));
        NativeHeaderCatalog badFunction = GlobalCatalog(s_integer);
        badFunction = badFunction with { Symbols = new Dictionary<string, NativeHeaderSymbol> { ["state"] = badFunction.Symbols["state"] with { IsFunction = true } } };
        Assert.ThrowsExactly<FormatException>(() => NativeBindingStorageProbe.GenerateSource(badFunction, ""));
        var badName = new NativeHeaderCatalog(s_target, new Dictionary<string, NativeHeaderSymbol> { ["bad;"] = Symbol("bad", s_integer, false) });
        Assert.ThrowsExactly<FormatException>(() => NativeBindingStorageProbe.GenerateSource(badName, ""));
    }

    private static NativeHeaderCatalog FunctionCatalog(NativeHeaderType parameter)
        => new(s_target, new Dictionary<string, NativeHeaderSymbol>
        {
            ["call"] = Symbol("call", new NativeHeaderFunction(new NativeHeaderScalar("void"), [parameter], false, true, false)),
        });

    private static NativeHeaderCatalog GlobalCatalog(NativeHeaderType type)
        => new(s_target, new Dictionary<string, NativeHeaderSymbol> { ["state"] = Symbol("state", type, false) });

    private static NativeHeaderSymbol Symbol(string name, NativeHeaderType type, bool function = true)
        => new(name, name, function, type, [], false, false, "extern", []);
}
