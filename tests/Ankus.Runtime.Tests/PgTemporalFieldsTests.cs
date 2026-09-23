namespace Ankus.Runtime.Tests;

/// <summary>
/// Verifies detached calendar decomposition, exact epoch arithmetic, raw normalization, and local-field access guards.
/// </summary>
[TestClass]
public sealed class PgTemporalFieldsTests
{
    /// <summary>
    /// Checks independent Gregorian and epoch vectors across the complete PostgreSQL date range.
    /// </summary>
    [TestMethod]
    [DataRow(-2_451_545, -4714, 11, 24, 0, -2_440_588, -210_866_803_200L)]
    [DataRow(-2_451_544, -4714, 11, 25, 1, -2_440_587, -210_866_716_800L)]
    [DataRow(-730_426, -1, 2, 29, 1_721_119, -719_469, -62_162_121_600L)]
    [DataRow(-730_120, -1, 12, 31, 1_721_425, -719_163, -62_135_683_200L)]
    [DataRow(-730_119, 1, 1, 1, 1_721_426, -719_162, -62_135_596_800L)]
    [DataRow(-146_038, 1600, 2, 29, 2_305_507, -135_081, -11_670_998_400L)]
    [DataRow(-36_466, 1900, 2, 28, 2_415_079, -25_509, -2_203_977_600L)]
    [DataRow(-36_465, 1900, 3, 1, 2_415_080, -25_508, -2_203_891_200L)]
    [DataRow(-10_958, 1969, 12, 31, 2_440_587, -1, -86_400L)]
    [DataRow(-10_957, 1970, 1, 1, 2_440_588, 0, 0L)]
    [DataRow(0, 2000, 1, 1, 2_451_545, 10_957, 946_684_800L)]
    [DataRow(59, 2000, 2, 29, 2_451_604, 11_016, 951_782_400L)]
    [DataRow(60, 2000, 3, 1, 2_451_605, 11_017, 951_868_800L)]
    [DataRow(36_584, 2100, 3, 1, 2_488_129, 47_541, 4_107_542_400L)]
    [DataRow(2_145_031_947, 5_874_897, 12, 30, 2_147_483_492, 2_145_042_904, 185_331_706_905_600L)]
    [DataRow(2_145_031_948, 5_874_897, 12, 31, 2_147_483_493, 2_145_042_905, 185_331_706_992_000L)]
    public void DateFieldsAndEpochsPreserveFullRange(int raw, int year, int month, int day, int julian, int unixDays, long unixSeconds)
    {
        var value = new PgDate(raw);
        Assert.AreEqual((year, month, day), value.GetDateParts());
        Assert.AreEqual(year, value.Year);
        Assert.AreEqual(month, value.Month);
        Assert.AreEqual(day, value.Day);
        Assert.AreEqual(julian, value.ToJulianDays());
        Assert.AreEqual(unixDays, value.ToUnixEpochDays());
        Assert.AreEqual(unixSeconds, value.ToUnixTimeSeconds());
        Assert.IsTrue(value.IsFinite);
        Assert.IsFalse(value.IsPositiveInfinity);
        Assert.IsFalse(value.IsNegativeInfinity);
    }

    /// <summary>
    /// Checks exact day floor and time remainder before the epoch, across the era boundary, and at the finite endpoints.
    /// </summary>
    [TestMethod]
    [DataRow(-211_813_488_000_000_000L, -4714, 11, 24, 0, 0, 0, 0)]
    [DataRow(-211_813_487_999_999_999L, -4714, 11, 24, 0, 0, 0, 1)]
    [DataRow(-63_082_281_600_000_001L, -1, 12, 31, 23, 59, 59, 999_999)]
    [DataRow(-63_082_281_600_000_000L, 1, 1, 1, 0, 0, 0, 0)]
    [DataRow(-86_400_000_001L, 1999, 12, 30, 23, 59, 59, 999_999)]
    [DataRow(-86_400_000_000L, 1999, 12, 31, 0, 0, 0, 0)]
    [DataRow(-86_399_999_999L, 1999, 12, 31, 0, 0, 0, 1)]
    [DataRow(-1_000_001L, 1999, 12, 31, 23, 59, 58, 999_999)]
    [DataRow(-1L, 1999, 12, 31, 23, 59, 59, 999_999)]
    [DataRow(0L, 2000, 1, 1, 0, 0, 0, 0)]
    [DataRow(1L, 2000, 1, 1, 0, 0, 0, 1)]
    [DataRow(45_296_123_456L, 2000, 1, 1, 12, 34, 56, 123_456)]
    [DataRow(86_399_999_999L, 2000, 1, 1, 23, 59, 59, 999_999)]
    [DataRow(86_400_000_000L, 2000, 1, 2, 0, 0, 0, 0)]
    [DataRow(9_223_371_331_199_999_998L, 294276, 12, 31, 23, 59, 59, 999_998)]
    [DataRow(9_223_371_331_199_999_999L, 294276, 12, 31, 23, 59, 59, 999_999)]
    public void TimestampFieldsUseFloorDivision(long raw, int year, int month, int day, int hour, int minute, int second, int microseconds)
    {
        var value = new PgTimestamp(raw);
        Assert.AreEqual((year, month, day), value.GetDateParts());
        Assert.AreEqual((hour, minute, second, microseconds), value.GetTimeParts());
        Assert.AreEqual(year, value.Year);
        Assert.AreEqual(month, value.Month);
        Assert.AreEqual(day, value.Day);
        Assert.AreEqual(hour, value.Hour);
        Assert.AreEqual(minute, value.Minute);
        Assert.AreEqual(second, value.Second);
        Assert.AreEqual(microseconds, value.MicrosecondsWithinSecond);
        Assert.AreEqual(second + microseconds / 1_000_000d, value.FractionalSecond, 0.000000000001);
        Assert.IsTrue(value.IsFinite);
        Assert.IsFalse(value.IsPositiveInfinity);
        Assert.IsFalse(value.IsNegativeInfinity);
    }

    /// <summary>
    /// Checks hour, minute, second, and microsecond rollover independently, including the distinct end-of-day value.
    /// </summary>
    [TestMethod]
    [DataRow(0L, 0, 0, 0, 0, 0d)]
    [DataRow(1L, 0, 0, 0, 1, 0.000001d)]
    [DataRow(999_999L, 0, 0, 0, 999_999, 0.999999d)]
    [DataRow(1_000_000L, 0, 0, 1, 0, 1d)]
    [DataRow(59_999_999L, 0, 0, 59, 999_999, 59.999999d)]
    [DataRow(60_000_000L, 0, 1, 0, 0, 0d)]
    [DataRow(3_599_999_999L, 0, 59, 59, 999_999, 59.999999d)]
    [DataRow(3_600_000_000L, 1, 0, 0, 0, 0d)]
    [DataRow(45_296_123_456L, 12, 34, 56, 123_456, 56.123456d)]
    [DataRow(86_399_999_999L, 23, 59, 59, 999_999, 59.999999d)]
    [DataRow(86_400_000_000L, 24, 0, 0, 0, 0d)]
    public void TimeFieldsPreserveEndOfDayAndFractions(long raw, int hour, int minute, int second, int microseconds, double fraction)
    {
        var value = new PgTime(raw);
        var zoned = new PgTimeTz(value, -19_845);
        Assert.AreEqual((hour, minute, second, microseconds), value.GetTimeParts());
        Assert.AreEqual(hour, value.Hour);
        Assert.AreEqual(minute, value.Minute);
        Assert.AreEqual(second, value.Second);
        Assert.AreEqual(microseconds, value.MicrosecondsWithinSecond);
        Assert.AreEqual(fraction, value.FractionalSecond, 0.000000000001);
        Assert.AreEqual((hour, minute, second, microseconds), zoned.GetTimeParts());
        Assert.AreEqual(hour, zoned.Hour);
        Assert.AreEqual(minute, zoned.Minute);
        Assert.AreEqual(second, zoned.Second);
        Assert.AreEqual(microseconds, zoned.MicrosecondsWithinSecond);
        Assert.AreEqual(fraction, zoned.FractionalSecond, 0.000000000001);
    }

    /// <summary>
    /// Checks signed offset components, second-resolution offsets, and UTC wrap in both directions.
    /// </summary>
    [TestMethod]
    [DataRow(0L, 0, 0, 0, 0L)]
    [DataRow(86_400_000_000L, 0, 0, 0, 0L)]
    [DataRow(0L, 19_845, 5, 30, 66_555_000_000L)]
    [DataRow(0L, -19_845, -5, -30, 19_845_000_000L)]
    [DataRow(0L, 1, 0, 0, 86_399_000_000L)]
    [DataRow(86_399_999_999L, -1, 0, 0, 999_999L)]
    [DataRow(0L, 59, 0, 0, 86_341_000_000L)]
    [DataRow(0L, -60, 0, -1, 60_000_000L)]
    [DataRow(0L, 60, 0, 1, 86_340_000_000L)]
    [DataRow(0L, -3_600, -1, 0, 3_600_000_000L)]
    [DataRow(86_400_000_000L, 57_599, 15, 59, 28_801_000_000L)]
    [DataRow(86_400_000_000L, -57_599, -15, -59, 57_599_000_000L)]
    public void OffsetFieldsAndUtcPreserveSeconds(long raw, int offset, int hours, int minutes, long utc)
    {
        var value = new PgTimeTz(new PgTime(raw), offset);
        Assert.AreEqual(hours, value.OffsetHours);
        Assert.AreEqual(minutes, value.OffsetMinutes);
        Assert.AreEqual(offset, value.OffsetSeconds);
        Assert.AreEqual(utc, value.ToUtc().Microseconds);
        Assert.AreEqual(raw, value.Time.Microseconds);
    }

    /// <summary>
    /// Checks both adjacent finite boundaries and both infinity sentinels without changing checked constructor behavior.
    /// </summary>
    [TestMethod]
    [DataRow(int.MinValue, int.MinValue)]
    [DataRow(int.MinValue + 1, int.MinValue)]
    [DataRow(-2_451_546, int.MinValue)]
    [DataRow(-2_451_545, -2_451_545)]
    [DataRow(-2_451_544, -2_451_544)]
    [DataRow(0, 0)]
    [DataRow(2_145_031_947, 2_145_031_947)]
    [DataRow(2_145_031_948, 2_145_031_948)]
    [DataRow(2_145_031_949, int.MaxValue)]
    [DataRow(int.MaxValue - 1, int.MaxValue)]
    [DataRow(int.MaxValue, int.MaxValue)]
    public void RawDateSaturationPreservesBoundaries(int raw, int expected)
    {
        PgDate value = PgDate.FromRawSaturating(raw);
        Assert.AreEqual(expected, value.DaysSinceEpoch);
        Assert.AreEqual(expected == int.MinValue, value.IsNegativeInfinity);
        Assert.AreEqual(expected == int.MaxValue, value.IsPositiveInfinity);
        Assert.AreEqual(expected is not (int.MinValue or int.MaxValue), value.IsFinite);
        if (raw != expected)
        {
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new PgDate(raw));
        }
    }

    /// <summary>
    /// Checks matching timestamp and instant saturation while preserving exact finite microseconds and UTC conversion.
    /// </summary>
    [TestMethod]
    [DataRow(long.MinValue, long.MinValue)]
    [DataRow(long.MinValue + 1, long.MinValue)]
    [DataRow(-211_813_488_000_000_001L, long.MinValue)]
    [DataRow(-211_813_488_000_000_000L, -211_813_488_000_000_000L)]
    [DataRow(-211_813_487_999_999_999L, -211_813_487_999_999_999L)]
    [DataRow(-1L, -1L)]
    [DataRow(0L, 0L)]
    [DataRow(9_223_371_331_199_999_998L, 9_223_371_331_199_999_998L)]
    [DataRow(9_223_371_331_199_999_999L, 9_223_371_331_199_999_999L)]
    [DataRow(9_223_371_331_200_000_000L, long.MaxValue)]
    [DataRow(long.MaxValue - 1, long.MaxValue)]
    [DataRow(long.MaxValue, long.MaxValue)]
    public void RawTimestampSaturationPreservesBoundaries(long raw, long expected)
    {
        PgTimestamp value = PgTimestamp.FromRawSaturating(raw);
        PgTimestampTz instant = PgTimestampTz.FromRawSaturating(raw);
        Assert.AreEqual(expected, value.MicrosecondsSinceEpoch);
        Assert.AreEqual(expected, instant.MicrosecondsSinceEpoch);
        Assert.AreEqual(expected == long.MinValue, value.IsNegativeInfinity);
        Assert.AreEqual(expected == long.MaxValue, value.IsPositiveInfinity);
        Assert.AreEqual(expected is not (long.MinValue or long.MaxValue), value.IsFinite);
        Assert.AreEqual(expected == long.MinValue, instant.IsNegativeInfinity);
        Assert.AreEqual(expected == long.MaxValue, instant.IsPositiveInfinity);
        Assert.AreEqual(expected is not (long.MinValue or long.MaxValue), instant.IsFinite);
        Assert.AreEqual(expected, instant.ToUtc().MicrosecondsSinceEpoch);
        if (raw != expected)
        {
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new PgTimestamp(raw));
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new PgTimestampTz(raw));
        }
    }

    /// <summary>
    /// Checks independent Euclidean remainder vectors, including signed extrema and each side of midnight.
    /// </summary>
    [TestMethod]
    [DataRow(long.MinValue, 71_945_224_192L)]
    [DataRow(-86_400_000_001L, 86_399_999_999L)]
    [DataRow(-86_400_000_000L, 0L)]
    [DataRow(-86_399_999_999L, 1L)]
    [DataRow(-1L, 86_399_999_999L)]
    [DataRow(0L, 0L)]
    [DataRow(1L, 1L)]
    [DataRow(86_399_999_999L, 86_399_999_999L)]
    [DataRow(86_400_000_000L, 0L)]
    [DataRow(86_400_000_001L, 1L)]
    [DataRow(long.MaxValue, 14_454_775_807L)]
    public void RawTimeWrappingUsesEuclideanRemainders(long raw, long expected)
    {
        Assert.AreEqual(expected, PgTime.FromMicrosecondsWrapping(raw).Microseconds);
        PgTimeTz zoned = PgTimeTz.FromRawWrapping(raw, 0);
        Assert.AreEqual(expected, zoned.Time.Microseconds);
        Assert.AreEqual(0, zoned.OffsetSeconds);
    }

    /// <summary>
    /// Checks raw seconds-west wrapping preserves pgrx's nonnegative remainder before converting to the public east sign.
    /// </summary>
    [TestMethod]
    [DataRow(int.MinValue, -17_152)]
    [DataRow(-57_601, -57_599)]
    [DataRow(-57_600, 0)]
    [DataRow(-57_599, -1)]
    [DataRow(-1, -57_599)]
    [DataRow(0, 0)]
    [DataRow(1, -1)]
    [DataRow(57_599, -57_599)]
    [DataRow(57_600, 0)]
    [DataRow(57_601, -1)]
    [DataRow(int.MaxValue, -40_447)]
    public void RawOffsetWrappingUsesPostgresWestConvention(int west, int east)
    {
        PgTimeTz value = PgTimeTz.FromRawWrapping(-1, west);
        Assert.AreEqual(86_399_999_999L, value.Time.Microseconds);
        Assert.AreEqual(east, value.OffsetSeconds);
        Assert.AreEqual(east * TimeSpan.TicksPerSecond, value.Offset.Ticks);
    }

    /// <summary>
    /// Checks every finite date-field and epoch API rejects both infinities before any backend access.
    /// </summary>
    [TestMethod]
    [DataRow(int.MinValue)]
    [DataRow(int.MaxValue)]
    public void InfiniteDateFieldsAndEpochsAreRejected(int raw)
    {
        var value = new PgDate(raw);
        Assert.ThrowsExactly<InvalidOperationException>(() => value.GetDateParts());
        Assert.ThrowsExactly<InvalidOperationException>(() => value.Year);
        Assert.ThrowsExactly<InvalidOperationException>(() => value.Month);
        Assert.ThrowsExactly<InvalidOperationException>(() => value.Day);
        Assert.ThrowsExactly<InvalidOperationException>(() => value.ToJulianDays());
        Assert.ThrowsExactly<InvalidOperationException>(() => value.ToUnixEpochDays());
        Assert.ThrowsExactly<InvalidOperationException>(() => value.ToUnixTimeSeconds());
    }

    /// <summary>
    /// Checks every finite wall-clock timestamp API rejects both infinities.
    /// </summary>
    [TestMethod]
    [DataRow(long.MinValue)]
    [DataRow(long.MaxValue)]
    public void InfiniteTimestampFieldsAreRejected(long raw)
    {
        var value = new PgTimestamp(raw);
        Assert.ThrowsExactly<InvalidOperationException>(() => value.GetDateParts());
        Assert.ThrowsExactly<InvalidOperationException>(() => value.GetTimeParts());
        Assert.ThrowsExactly<InvalidOperationException>(() => value.Year);
        Assert.ThrowsExactly<InvalidOperationException>(() => value.Month);
        Assert.ThrowsExactly<InvalidOperationException>(() => value.Day);
        Assert.ThrowsExactly<InvalidOperationException>(() => value.Hour);
        Assert.ThrowsExactly<InvalidOperationException>(() => value.Minute);
        Assert.ThrowsExactly<InvalidOperationException>(() => value.Second);
        Assert.ThrowsExactly<InvalidOperationException>(() => value.MicrosecondsWithinSecond);
        Assert.ThrowsExactly<InvalidOperationException>(() => value.FractionalSecond);
    }

    /// <summary>
    /// Distinguishes finite local fields requiring a backend from infinities rejected before backend access.
    /// </summary>
    [TestMethod]
    [DataRow(long.MinValue, true)]
    [DataRow(long.MaxValue, true)]
    [DataRow(0L, false)]
    [DataRow(-211_813_488_000_000_000L, false)]
    [DataRow(9_223_371_331_199_999_999L, false)]
    public void ZonedTimestampFieldsRequireFiniteBackendContext(long raw, bool infinite)
    {
        var value = new PgTimestampTz(raw);
        string expected = infinite
            ? "Infinite temporal values do not have finite calendar fields or epoch offsets."
            : "PostgreSQL APIs can only be used on the active PostgreSQL backend thread.";
        Assert.AreEqual(expected, Assert.ThrowsExactly<InvalidOperationException>(() => value.GetDateParts()).Message);
        Assert.AreEqual(expected, Assert.ThrowsExactly<InvalidOperationException>(() => value.GetTimeParts()).Message);
        Assert.AreEqual(expected, Assert.ThrowsExactly<InvalidOperationException>(() => value.Year).Message);
        Assert.AreEqual(expected, Assert.ThrowsExactly<InvalidOperationException>(() => value.Month).Message);
        Assert.AreEqual(expected, Assert.ThrowsExactly<InvalidOperationException>(() => value.Day).Message);
        Assert.AreEqual(expected, Assert.ThrowsExactly<InvalidOperationException>(() => value.Hour).Message);
        Assert.AreEqual(expected, Assert.ThrowsExactly<InvalidOperationException>(() => value.Minute).Message);
        Assert.AreEqual(expected, Assert.ThrowsExactly<InvalidOperationException>(() => value.Second).Message);
        Assert.AreEqual(expected, Assert.ThrowsExactly<InvalidOperationException>(() => value.MicrosecondsWithinSecond).Message);
        Assert.AreEqual(expected, Assert.ThrowsExactly<InvalidOperationException>(() => value.FractionalSecond).Message);
        Assert.AreEqual(raw, value.ToUtc().MicrosecondsSinceEpoch);
    }

    /// <summary>
    /// Keeps record diagnostic formatting detached and finite-field-free, including nested time records and all infinities.
    /// </summary>
    [TestMethod]
    public void DiagnosticStringsDoNotRequireBackendOrFiniteValues()
    {
        Assert.AreEqual("PgDate { DaysSinceEpoch = 0, IsFinite = True }", default(PgDate).ToString());
        Assert.AreEqual("PgDate { DaysSinceEpoch = -2147483648, IsFinite = False }", PgDate.NegativeInfinity.ToString());
        Assert.AreEqual("PgDate { DaysSinceEpoch = 2147483647, IsFinite = False }", PgDate.PositiveInfinity.ToString());
        Assert.AreEqual("PgTime { Microseconds = 0 }", default(PgTime).ToString());
        Assert.AreEqual("PgTime { Microseconds = 86400000000 }", PgTime.EndOfDay.ToString());
        Assert.AreEqual("PgTimeTz { Time = PgTime { Microseconds = 0 }, OffsetSeconds = 0, Offset = 00:00:00 }", default(PgTimeTz).ToString());
        Assert.AreEqual("PgTimestamp { MicrosecondsSinceEpoch = 0, IsFinite = True }", default(PgTimestamp).ToString());
        Assert.AreEqual("PgTimestamp { MicrosecondsSinceEpoch = -9223372036854775808, IsFinite = False }", PgTimestamp.NegativeInfinity.ToString());
        Assert.AreEqual("PgTimestamp { MicrosecondsSinceEpoch = 9223372036854775807, IsFinite = False }", PgTimestamp.PositiveInfinity.ToString());
        Assert.AreEqual("PgTimestampTz { MicrosecondsSinceEpoch = 0, IsFinite = True }", default(PgTimestampTz).ToString());
        Assert.AreEqual("PgTimestampTz { MicrosecondsSinceEpoch = -9223372036854775808, IsFinite = False }", PgTimestampTz.NegativeInfinity.ToString());
        Assert.AreEqual("PgTimestampTz { MicrosecondsSinceEpoch = 9223372036854775807, IsFinite = False }", PgTimestampTz.PositiveInfinity.ToString());
    }
}
