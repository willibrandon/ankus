namespace Ankus.Build.Tests;

/// <summary>
/// Checks the refreshed upstream release and concrete declarations from newly included backend headers.
/// </summary>
[TestClass]
public sealed class NativeBindingUpstreamTests
{
    /// <summary>
    /// Every major comes from one upstream release and retains the actual reference server version.
    /// </summary>
    /// <param name="major">The PostgreSQL major.</param>
    /// <param name="version">The version recorded by the upstream reference build.</param>
    [TestMethod]
    [DataRow(13, "13.23")]
    [DataRow(14, "14.24")]
    [DataRow(15, "15.19")]
    [DataRow(16, "16.15")]
    [DataRow(17, "17.11")]
    [DataRow(18, "18.6")]
    [DataRow(19, "19beta4")]
    public void CatalogsShareCurrentUpstreamRevisionAndServerRelease(int major, string version)
    {
        NativeBindingRawCatalog catalog = NativeBindingResources.ReadRawCatalog(major);
        Assert.AreEqual("fc91c63ebad11784647b50ee7e265c1fd9c9924f", catalog.SourceRevision);
        Assert.AreEqual(new NativeBindingConstant("&::core::ffi::CStr", "c\"" + version + "\""),
            catalog.ReferenceConstants["PG_VERSION"]);
    }

    /// <summary>
    /// Custom scan initialization retains its complete native signature and its declaring header on every major.
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
    public void ExpandedInventoryRetainsCustomScanInitialization(int major)
    {
        Assert.Contains("#include \"executor/nodeCustom.h\"", NativeBindingResources.ReadHeaders(major));
        NativeBindingFunction function = NativeBindingResources.ReadRawCatalog(major).Functions["ExecInitCustomScan"];
        Assert.AreEqual("ExecInitCustomScan", function.NativeSymbol);
        Assert.AreEqual("*mut CustomScanState", function.ReturnType);
        Assert.AreSequenceEqual<NativeBindingParameter>(
            [new("cscan", "*mut CustomScan"), new("estate", "*mut EState"), new("eflags", "::core::ffi::c_int")], function.Parameters);
        Assert.IsFalse(function.IsVariadic);
    }

    /// <summary>
    /// Page checksums retain writable page storage, the block number and the exact unsigned result width.
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
    public void ExpandedInventoryRetainsPageChecksumContract(int major)
    {
        Assert.Contains("#include \"storage/checksum_impl.h\"", NativeBindingResources.ReadHeaders(major));
        NativeBindingFunction function = NativeBindingResources.ReadRawCatalog(major).Functions["pg_checksum_page"];
        Assert.AreEqual("pg_checksum_page", function.NativeSymbol);
        Assert.AreEqual("uint16", function.ReturnType);
        Assert.AreSequenceEqual<NativeBindingParameter>(
            [new("page", "*mut ::core::ffi::c_char"), new("blkno", "BlockNumber")], function.Parameters);
        Assert.IsFalse(function.IsVariadic);
    }

    /// <summary>
    /// The expanded inventory retains the complete barrier layout and prepared-transaction global on every major.
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
    public void ExpandedInventoryRetainsBarrierLayoutAndPreparedTransactionGlobal(int major)
    {
        Assert.Contains("#include \"storage/barrier.h\"", NativeBindingResources.ReadHeaders(major));
        NativeBindingType barrier = NativeBindingResources.ReadCatalog(major).Types["Barrier"];
        Assert.IsFalse(barrier.IsUnion);
        Assert.AreSequenceEqual<NativeBindingField>(
        [
            new("mutex", "mutex", "slock_t"),
            new("phase", "phase", "::core::ffi::c_int"),
            new("participants", "participants", "::core::ffi::c_int"),
            new("arrived", "arrived", "::core::ffi::c_int"),
            new("elected", "elected", "::core::ffi::c_int"),
            new("static_party", "static_party", "bool"),
            new("condition_variable", "condition_variable", "ConditionVariable")
        ], barrier.Fields);

        NativeBindingGlobal global = NativeBindingResources.ReadRawCatalog(major).Globals["max_prepared_xacts"];
        Assert.AreEqual("max_prepared_xacts", global.NativeSymbol);
        Assert.AreEqual("::core::ffi::c_int", global.Representation);
        Assert.IsTrue(global.IsMutable);
    }

    /// <summary>
    /// PostgreSQL 18 and later retain every callback in the newly public COPY FROM format contract.
    /// </summary>
    /// <param name="major">The PostgreSQL major.</param>
    [TestMethod]
    [DataRow(18)]
    [DataRow(19)]
    public void ExpandedInventoryRetainsCopyFromCallbacks(int major)
    {
        Assert.Contains("#include \"commands/copyapi.h\"", NativeBindingResources.ReadHeaders(major));
        NativeBindingType routine = NativeBindingResources.ReadCatalog(major).Types["CopyFromRoutine"];
        Assert.IsFalse(routine.IsUnion);
        Assert.AreSequenceEqual(["CopyFromInFunc", "CopyFromStart", "CopyFromOneRow", "CopyFromEnd"],
            routine.Fields.Select(static field => field.Name));
        Assert.Contains("fn(\n            cstate: CopyFromState,\n            atttypid: Oid,", routine.Fields[0].Representation);
        Assert.Contains("fn(cstate: CopyFromState, tupDesc: TupleDesc)", routine.Fields[1].Representation);
        Assert.Contains("values: *mut Datum,\n            nulls: *mut bool,\n        ) -> bool", routine.Fields[2].Representation);
        Assert.Contains("fn(cstate: CopyFromState)", routine.Fields[3].Representation);
    }
}
