using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ankus.Runtime.Tests;

/// <summary>
/// Checks detached interval operations and the scalar converters' backend-access contract.
/// </summary>
[TestClass]
public sealed class PgTemporalConvenienceTests
{
    /// <summary>
    /// Checks exact construction and comparison scale without backend access or Int64 overflow.
    /// </summary>
    [TestMethod]
    public void IntervalComponentConveniencesPreserveExactStorage()
    {
        Assert.AreEqual(new PgInterval(0, 0, long.MaxValue), PgInterval.FromMicroseconds(long.MaxValue));
        Assert.AreEqual(new PgInterval(0, 0, long.MinValue), PgInterval.FromMicroseconds(long.MinValue));
        Assert.AreEqual(new PgInterval(int.MinValue, 0, 0), PgInterval.FromMonths(int.MinValue));
        Assert.AreEqual(new PgInterval(0, int.MaxValue, 0), PgInterval.FromDays(int.MaxValue));
        Assert.AreEqual(new PgInterval(1, 2, 3), new PgInterval(-1, 2, -3).Abs());
        Assert.AreEqual(default(PgInterval), default(PgInterval).Abs());
        Assert.AreEqual(PgInterval.PositiveInfinity, PgInterval.NegativeInfinity.Abs());
        Assert.ThrowsExactly<OverflowException>(() => new PgInterval(int.MinValue, 0, 0).Abs());
        Assert.ThrowsExactly<OverflowException>(() => new PgInterval(0, int.MinValue, 0).Abs());
        Assert.ThrowsExactly<OverflowException>(() => new PgInterval(0, 0, long.MinValue).Abs());
        Assert.AreEqual(0, new PgInterval(1, -30, 0).Sign);
        Assert.AreEqual(1, new PgInterval(-1, 31, 0).Sign);
        Assert.AreEqual(-1, new PgInterval(1, -31, 0).Sign);
        Assert.AreEqual(0, default(PgInterval).Sign);
        Assert.AreEqual(1, PgInterval.PositiveInfinity.Sign);
        Assert.AreEqual(-1, PgInterval.NegativeInfinity.Sign);
        Assert.AreEqual((Int128)2_592_000_000_000, PgInterval.FromMonths(1).ToComparisonMicroseconds());
        Assert.IsGreaterThan((Int128)long.MaxValue, new PgInterval(int.MaxValue, int.MaxValue, long.MaxValue).ToComparisonMicroseconds());
        Assert.ThrowsExactly<InvalidOperationException>(() => PgInterval.PositiveInfinity.ToComparisonMicroseconds());
    }

    /// <summary>
    /// Invalid precision fails before backend access, whereas valid precision still requires the backend.
    /// </summary>
    /// <param name="precision">An out-of-range precision.</param>
    [TestMethod]
    [DataRow(-1)]
    [DataRow(7)]
    public void TemporalPrecisionRejectsInvalidDigits(int precision)
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => default(PgTime).Round(precision));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => default(PgTimeTz).Round(precision));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => default(PgTimestamp).Round(precision));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => default(PgTimestampTz).Round(precision));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PgTime.GetLocalTime(precision));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PgTimeTz.GetCurrentTime(precision));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PgTimestamp.GetLocalTimestamp(precision));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PgTimestampTz.GetCurrentTimestamp(precision));
        Assert.ThrowsExactly<InvalidOperationException>(() => default(PgTimestamp).Round(6));
    }

    /// <summary>
    /// Checks numeric JSON writes remain available outside PostgreSQL and temporal operations preserve access errors.
    /// </summary>
    [TestMethod]
    public void ScalarConvertersPreserveBackendAccessErrors()
    {
        Assert.AreEqual("\"123.4500\"", JsonSerializer.Serialize(PgNumeric.FromDecimal(123.4500m), DetachedScalarContext.Default.PgNumeric));
        Assert.AreEqual("\"NaN\"", JsonSerializer.Serialize(PgNumeric.NaN, DetachedScalarContext.Default.PgNumeric));
        Assert.AreEqual("\"Infinity\"", JsonSerializer.Serialize(PgNumeric.PositiveInfinity, DetachedScalarContext.Default.PgNumeric));
        Assert.AreEqual("\"-Infinity\"", JsonSerializer.Serialize(PgNumeric.NegativeInfinity, DetachedScalarContext.Default.PgNumeric));
        Assert.ThrowsExactly<InvalidOperationException>(() => JsonSerializer.Deserialize("123.45", DetachedScalarContext.Default.PgNumeric));
        Assert.ThrowsExactly<InvalidOperationException>(() => JsonSerializer.Deserialize("\"2000-01-01\"", DetachedScalarContext.Default.PgDate));
        Assert.ThrowsExactly<InvalidOperationException>(() => JsonSerializer.Serialize(default(PgDate), DetachedScalarContext.Default.PgDate));
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize("null", DetachedScalarContext.Default.PgNumeric));
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize("[]", DetachedScalarContext.Default.PgDate));
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize("\"1\\u0000\"", DetachedScalarContext.Default.PgNumeric));
    }
}

[JsonSerializable(typeof(PgNumeric))]
[JsonSerializable(typeof(PgDate))]
internal sealed partial class DetachedScalarContext : JsonSerializerContext;
