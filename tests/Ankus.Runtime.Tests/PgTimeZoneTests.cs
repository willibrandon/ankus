namespace Ankus.Runtime.Tests;

/// <summary>
/// Checks validation and backend access for PostgreSQL timezone and wall-clock helpers.
/// </summary>
[TestClass]
public sealed class PgTimeZoneTests
{
    /// <summary>
    /// Rejects null zone names before attempting backend access on either lookup or construction.
    /// </summary>
    [TestMethod]
    public void NullZoneNamesAreRejectedBeforeBackendAccess()
    {
        Assert.AreEqual("value", Assert.ThrowsExactly<ArgumentNullException>(() => PgTimeZone.GetOffset(null!)).ParamName);
        Assert.AreEqual("value", Assert.ThrowsExactly<ArgumentNullException>(() => PgTimeZone.GetOffset(null!, default)).ParamName);
        Assert.AreEqual("value", Assert.ThrowsExactly<ArgumentNullException>(() => PgTimeTz.Create(12, 34, 56, null!)).ParamName);
    }

    /// <summary>
    /// Rejects zero characters instead of permitting native string truncation.
    /// </summary>
    [TestMethod]
    public void ZeroCharactersCannotTruncateZoneNames()
    {
        const string zone = "UTC\0Unknown/Zone";
        Assert.AreEqual("value", Assert.ThrowsExactly<ArgumentException>(() => PgTimeZone.GetOffset(zone)).ParamName);
        Assert.AreEqual("value", Assert.ThrowsExactly<ArgumentException>(() => PgTimeZone.GetOffset(zone, default)).ParamName);
        Assert.AreEqual("value", Assert.ThrowsExactly<ArgumentException>(() => PgTimeTz.Create(12, 34, 56, zone)).ParamName);
    }

    /// <summary>
    /// Rejects either infinite instant before native lookup while accepting finite encodings for backend dispatch.
    /// </summary>
    /// <param name="raw">An infinite timestamp encoding.</param>
    [TestMethod]
    [DataRow(long.MinValue)]
    [DataRow(long.MaxValue)]
    public void InfiniteInstantsHaveNoTimezoneOffset(long raw)
    {
        ArgumentOutOfRangeException error = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => PgTimeZone.GetOffset("UTC", new PgTimestampTz(raw)));
        Assert.AreEqual("instant", error.ParamName);
    }

    /// <summary>
    /// Preserves the backend-thread requirement for valid inputs, without substituting system timezone data.
    /// </summary>
    [TestMethod]
    public void ZoneAndLiveClockOperationsRequireBackendAccess()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => PgTimeZone.GetOffset("UTC"));
        Assert.ThrowsExactly<InvalidOperationException>(() => PgTimeZone.GetOffset("UTC", default));
        Assert.ThrowsExactly<InvalidOperationException>(() => PgTimeTz.Create(12, 34, 56.123456, "UTC"));
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = PgTimestampTz.TimeOfDay);
        Assert.ThrowsExactly<InvalidOperationException>(() => default(PgTimestamp).AtTimeZone(default(PgInterval)));
        Assert.ThrowsExactly<InvalidOperationException>(() => default(PgTimestampTz).AtTimeZone(default(PgInterval)));
        Assert.ThrowsExactly<InvalidOperationException>(() => default(PgTimeTz).AtTimeZone(default(PgInterval)));
    }
}
