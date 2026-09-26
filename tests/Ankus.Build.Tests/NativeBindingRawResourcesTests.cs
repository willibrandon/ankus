namespace Ankus.Build.Tests;

/// <summary>
/// Checks independently inventoried pinned declarations and concrete PostgreSQL-major signature changes.
/// </summary>
[TestClass]
public sealed class NativeBindingRawResourcesTests
{
    /// <summary>
    /// Every major retains its complete foreign inventory, original shim linkage and reference-only values.
    /// </summary>
    /// <param name="major">The PostgreSQL major.</param>
    /// <param name="functions">Independently counted foreign functions in the pinned input.</param>
    /// <param name="globals">Independently counted foreign globals.</param>
    /// <param name="constants">Independently counted top-level reference constants.</param>
    /// <param name="shims">Independently counted renamed pgrx C shims.</param>
    /// <param name="variadics">Independently counted variadic signatures.</param>
    [TestMethod]
    [DataRow(13, 7349, 475, 4968, 250, 14)]
    [DataRow(14, 7701, 508, 5592, 268, 15)]
    [DataRow(15, 7890, 515, 5683, 279, 15)]
    [DataRow(16, 8193, 529, 5765, 472, 15)]
    [DataRow(17, 8339, 546, 5882, 499, 15)]
    [DataRow(18, 8645, 579, 6112, 571, 16)]
    [DataRow(19, 8799, 591, 6159, 632, 16)]
    public void PackagedRawDeclarationsRetainVersionedContracts(int major, int functions, int globals, int constants, int shims, int variadics)
    {
        NativeBindingRawCatalog catalog = NativeBindingResources.ReadRawCatalog(major);
        Assert.AreEqual(major, catalog.PostgresMajor);
        Assert.AreEqual("70383e884582d1bcc7cd681d10886b995a2830cb", catalog.SourceRevision);
        Assert.HasCount(functions, catalog.Functions);
        Assert.HasCount(globals, catalog.Globals);
        Assert.HasCount(constants, catalog.ReferenceConstants);
        Assert.AreEqual(shims, catalog.Functions.Count(static entry => entry.Value.NativeSymbol != entry.Key));
        Assert.AreEqual(variadics, catalog.Functions.Values.Count(static function => function.IsVariadic));
        Assert.IsTrue(catalog.Functions.Values.All(static function => function.Abi == "C-unwind"));
        NativeBindingFunction executor = catalog.Functions["ExecutorRun"];
        NativeBindingParameter[] expected = [
            new("queryDesc", "*mut QueryDesc"), new("direction", "ScanDirection::Type"), new("count", "uint64")];
        if (major <= 17) { expected = [.. expected, new("execute_once", "bool")]; }

        Assert.AreSequenceEqual(expected, executor.Parameters);
        Assert.AreEqual("ExecutorRun", executor.NativeSymbol);
        Assert.AreEqual("()", executor.ReturnType);
        Assert.IsFalse(executor.IsVariadic);
        Assert.AreEqual("list_head__pgrx_cshim", catalog.Functions["list_head"].NativeSymbol);
        Assert.AreEqual("*mut ListCell", catalog.Functions["list_head"].ReturnType);
        NativeBindingFunction formatted = catalog.Functions["appendStringInfo"];
        Assert.IsTrue(formatted.IsVariadic);
        Assert.AreSequenceEqual<NativeBindingParameter>([new("str_", "StringInfo"), new("fmt", "*const ::core::ffi::c_char")], formatted.Parameters);
        NativeBindingGlobal hook = catalog.Globals["ExecutorRun_hook"];
        Assert.AreEqual("ExecutorRun_hook", hook.NativeSymbol);
        Assert.AreEqual("ExecutorRun_hook_type", hook.Representation);
        Assert.IsTrue(hook.IsMutable);
        Assert.AreEqual(new NativeBindingConstant("u32", "8"), catalog.ReferenceConstants["SIZEOF_LONG"]);
        Assert.AreEqual(new NativeBindingConstant("u32", major.ToString(System.Globalization.CultureInfo.InvariantCulture)), catalog.ReferenceConstants["PG_MAJORVERSION_NUM"]);
        Assert.IsFalse(catalog.Functions.ContainsKey("get_bit"));
        Assert.IsFalse(catalog.Functions.ContainsKey("new"));
    }

    /// <summary>
    /// Raw globals resolve to the matching major's complete callback type, including the const change in PostgreSQL 19.
    /// </summary>
    /// <param name="major">The PostgreSQL major.</param>
    [TestMethod]
    [DataRow(13)]
    [DataRow(14)]
    [DataRow(15)]
    [DataRow(16)]
    [DataRow(17)]
    [DataRow(18)]
    [DataRow(19)]
    public void RawHooksResolveTheSelectedMajorsCallbackContract(int major)
    {
        NativeBindingRawCatalog raw = NativeBindingResources.ReadRawCatalog(major);
        NativeBindingCatalog types = NativeBindingResources.ReadCatalog(major);
        NativeBindingGlobal hook = raw.Globals["post_parse_analyze_hook"];
        Assert.AreEqual("post_parse_analyze_hook_type", hook.Representation);
        string representation = types.Aliases[hook.Representation];
        string normalized = string.Concat(representation.Where(static character => !char.IsWhiteSpace(character)));
        string expected = major switch
        {
            13 => "::core::option::Option<unsafeextern\"C-unwind\"fn(pstate:*mutParseState,query:*mutQuery)>",
            19 => "::core::option::Option<unsafeextern\"C-unwind\"fn(pstate:*mutParseState,query:*mutQuery,jstate:*constJumbleState,),>",
            _ => "::core::option::Option<unsafeextern\"C-unwind\"fn(pstate:*mutParseState,query:*mutQuery,jstate:*mutJumbleState,),>",
        };
        Assert.AreEqual(expected, normalized);
        Assert.IsTrue(hook.IsMutable);
    }

    /// <summary>
    /// Unsupported majors cannot silently substitute another embedded raw catalog.
    /// </summary>
    /// <param name="major">An unsupported major boundary.</param>
    [TestMethod]
    [DataRow(12)]
    [DataRow(20)]
    public void UnsupportedRawCatalogMajorsAreRejected(int major)
        => Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => NativeBindingResources.ReadRawCatalog(major));
}
