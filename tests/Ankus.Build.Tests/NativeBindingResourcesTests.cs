namespace Ankus.Build.Tests;

/// <summary>
/// Checks that packaged PostgreSQL declarations form complete selectable native value graphs.
/// </summary>
[TestClass]
public sealed class NativeBindingResourcesTests
{
    /// <summary>
    /// Each supported catalog resolves embedded dependencies and retains native fields and tag identity.
    /// </summary>
    /// <param name="major">The supported PostgreSQL major.</param>
    [TestMethod]
    [DataRow(13)]
    [DataRow(14)]
    [DataRow(15)]
    [DataRow(16)]
    [DataRow(17)]
    [DataRow(18)]
    [DataRow(19)]
    public void PackagedDeclarationsResolveNativeDependencies(int major)
    {
        NativeBindingCatalog catalog = NativeBindingResources.ReadCatalog(major);
        Assert.AreEqual(major, catalog.PostgresMajor);
        Assert.AreEqual(0U, catalog.Tags["T_Invalid"]);
        IReadOnlyList<NativeBindingSelectionEntry> entries = NativeBindingSelection.Create(catalog);
        NativeBindingType constant = entries.Single(static entry => entry.Type.Name == "Const").Type;
        Assert.AreEqual("Datum", constant.Fields.Single(static field => field.Name == "constvalue").Representation);
        Assert.AreEqual("Oid", constant.Fields.Single(static field => field.Name == "consttype").Representation);
        Assert.AreSequenceEqual<string>(["T_Const"], constant.CastTags);
        NativeBindingType cell = entries.Single(static entry => entry.Type.Name == "ListCell").Type;
        Assert.IsTrue(cell.IsUnion);
        Assert.AreEqual("::core::ffi::c_int", cell.Fields.Single(static field => field.Name == "int_value").Representation);
        Assert.AreEqual("Oid", cell.Fields.Single(static field => field.Name == "oid_value").Representation);
        Assert.Contains("#include \"postgres.h\"", NativeBindingResources.ReadHeaders(major));
    }

    /// <summary>
    /// Unsupported majors cannot silently load another version's declarations or headers.
    /// </summary>
    /// <param name="major">An unsupported version boundary.</param>
    [TestMethod]
    [DataRow(12)]
    [DataRow(20)]
    public void UnsupportedMajorsAreRejected(int major)
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => NativeBindingResources.ReadCatalog(major));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => NativeBindingResources.ReadHeaders(major));
    }
}
