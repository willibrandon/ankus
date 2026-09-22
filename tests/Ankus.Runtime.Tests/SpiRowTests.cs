namespace Ankus.Runtime.Tests;

/// <summary>
/// Verifies owned row edits, exact-name lookup, type replacement, and exception atomicity without a PostgreSQL binding.
/// </summary>
[TestClass]
public sealed class SpiRowTests
{
    /// <summary>
    /// Verifies replacement types remain local to one row while result metadata and other rows keep their original values.
    /// </summary>
    [TestMethod]
    public void ReplacementUpdatesOnlyTheSelectedRowAndCellType()
    {
        SpiColumn[] columns = [new("value", 23), new("text", 25)];
        var first = new SpiRow([1, "original"], columns);
        var second = new SpiRow([2, "other"], columns);
        first.Set("value", "changed");
        first.Set(1, Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"));

        Assert.AreEqual(2, first.Count);
        Assert.AreEqual("changed", first.Get<string>(0));
        Assert.AreEqual(25U, first.GetTypeOid("value"));
        Assert.AreEqual(Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"), first.Get<Guid>("text"));
        Assert.AreEqual(2950U, first.GetTypeOid(1));
        Assert.AreEqual(2, second.Get<int>(0));
        Assert.AreEqual("other", second["text"]);
        Assert.AreEqual(23U, second.GetTypeOid(0));
        Assert.AreEqual(23U, columns[0].TypeOid);
        Assert.AreEqual(25U, columns[1].TypeOid);
        Assert.ThrowsExactly<InvalidCastException>(() => first.Get<int>(0));
    }

    /// <summary>
    /// Verifies replacing a domain value with typed NULL keeps the replacement's declared type and null semantics.
    /// </summary>
    [TestMethod]
    public void TypedNullAndJsonNullKeepDistinctTypesAndValues()
    {
        var row = new SpiRow([42], [new SpiColumn("value", 123456)]);
        Assert.AreEqual(123456U, row.GetTypeOid(0));
        row.Set<Guid?>(0, null);
        Assert.IsNull(row[0]);
        Assert.IsNull(row.Get<Guid?>(0));
        Assert.AreEqual(2950U, row.GetTypeOid(0));
        Assert.ThrowsExactly<InvalidOperationException>(() => row.Get<Guid>(0));
        row.Set(0, default(PgJsonb));
        Assert.AreEqual("null", row.Get<PgJsonb>(0).Text);
        Assert.AreEqual(3802U, row.GetTypeOid(0));
        row.Set<PgJson?>(0, null);
        Assert.IsNull(row.Get<PgJson?>(0));
        Assert.AreEqual(114U, row.GetTypeOid(0));
    }

    /// <summary>
    /// Verifies exact-name lookup, first-duplicate behavior, and ordinal writes to a later duplicate.
    /// </summary>
    [TestMethod]
    public void NameLookupIsOrdinalAndChoosesFirstDuplicate()
    {
        var row = new SpiRow([1, 2, 3], [new("value", 23), new("value", 23), new("Value", 23)]);
        Assert.AreEqual(0, row.GetOrdinal("value"));
        Assert.AreEqual(2, row.GetOrdinal("Value"));
        row.Set("value", 10);
        row.Set(1, 20);
        Assert.AreEqual(10, row["value"]);
        Assert.AreEqual(20, row.Get<int>(1));
        Assert.AreEqual(3, row.Get<int>("Value"));
        Assert.ThrowsExactly<ArgumentException>(() => row.GetOrdinal("VALUE"));
    }

    /// <summary>
    /// Verifies rejected edits do not replace data or its type, whether before or after the first successful edit.
    /// </summary>
    /// <param name="edited">Whether the row already owns replacement type metadata.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void InvalidEditsLeaveTheRowUnchanged(bool edited)
    {
        var row = new SpiRow([42], [new SpiColumn("value", 23)]);
        if (edited)
        {
            row.Set(0, 42);
        }

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => row.Set(-1, 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => row.Set(1, 1));
        Assert.ThrowsExactly<ArgumentException>(() => row.Set("missing", 1));
        Assert.ThrowsExactly<ArgumentNullException>(() => row.Set(null!, 1));
        Assert.ThrowsExactly<NotSupportedException>(() => row.Set(0, DateTime.MinValue));
        Assert.AreEqual(42, row.Get<int>(0));
        Assert.AreEqual(23U, row.GetTypeOid(0));
    }

    /// <summary>
    /// Verifies an empty row has no writable cells or valid column names.
    /// </summary>
    [TestMethod]
    public void EmptyRowRejectsAllEdits()
    {
        var row = new SpiRow([], []);
        Assert.AreEqual(0, row.Count);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => row.Set(0, 42));
        Assert.ThrowsExactly<ArgumentException>(() => row.GetOrdinal("value"));
    }
}
