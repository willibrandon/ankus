using System.Text.Json;

namespace Ankus.Runtime.Tests;

/// <summary>
/// Verifies the session-independent ISO 8601 interval text and its exact JSON round trip without backend access.
/// </summary>
[TestClass]
public sealed class PgIntervalIsoTests
{
    /// <summary>
    /// Formats PostgreSQL's iso_8601 IntervalStyle text, keeping each component and its sign exact.
    /// </summary>
    /// <param name="months">The month component.</param>
    /// <param name="days">The day component.</param>
    /// <param name="microseconds">The microsecond component.</param>
    /// <param name="expected">PostgreSQL's iso_8601 output for the same components.</param>
    [TestMethod]
    [DataRow(0, 0, 0L, "PT0S")]
    [DataRow(0, -1, -7_200_000_000L, "P-1DT-2H")]
    [DataRow(0, -1, 7_200_000_000L, "P-1DT2H")]
    [DataRow(1, -2, 3L, "P1M-2DT0.000003S")]
    [DataRow(14, 3, 3_723_500_000L, "P1Y2M3DT1H2M3.5S")]
    [DataRow(14, -3, -7_200_500_000L, "P1Y2M-3DT-2H-0.5S")]
    [DataRow(-13, 0, -500_000L, "P-1Y-1MT-0.5S")]
    [DataRow(12, 0, 0L, "P1Y")]
    [DataRow(-1, 31, 0L, "P-1M31D")]
    [DataRow(5, 0, 100_000L, "P5MT0.1S")]
    [DataRow(0, 0, 60_000_000L, "PT1M")]
    [DataRow(0, 0, 1_000_000L, "PT1S")]
    [DataRow(0, 0, -1L, "PT-0.000001S")]
    [DataRow(0, 0, 1_234_560L, "PT1.23456S")]
    [DataRow(0, 0, 86_400_000_000L, "PT24H")]
    [DataRow(0, 0, -3_599_999_999L, "PT-59M-59.999999S")]
    [DataRow(int.MinValue, int.MinValue, long.MinValue + 1, "P-178956970Y-8M-2147483648DT-2562047788H-54.775807S")]
    [DataRow(int.MaxValue, int.MaxValue, long.MaxValue - 1, "P178956970Y7M2147483647DT2562047788H54.775806S")]
    [DataRow(int.MinValue, 0, long.MinValue, "P-178956970Y-8MT-2562047788H-54.775808S")]
    [DataRow(int.MaxValue, 0, long.MaxValue, "P178956970Y7MT2562047788H54.775807S")]
    public void IsoTextMatchesPostgresAndRoundTripsExactly(int months, int days, long microseconds, string expected)
    {
        var value = new PgInterval(months, days, microseconds);
        Assert.AreEqual(expected, value.ToIsoString());
        Assert.IsTrue(PgInterval.TryParseIsoString(expected, out PgInterval parsed));
        AssertSameComponents(value, parsed);
        string json = JsonSerializer.Serialize(value, DetachedScalarContext.Default.PgInterval);
        Assert.AreEqual("\"" + expected + "\"", json);
        AssertSameComponents(value, JsonSerializer.Deserialize(json, DetachedScalarContext.Default.PgInterval));
    }

    /// <summary>
    /// Writes a reported sql_standard ambiguity and every equivalent representation without changing stored components.
    /// </summary>
    [TestMethod]
    public void EquivalentIntervalsKeepDistinctIsoComponents()
    {
        PgInterval[] values = [PgInterval.FromMonths(1), PgInterval.FromDays(30), PgInterval.FromMicroseconds(2_592_000_000_000),
            new(1, -1, 86_400_000_000), new(0, -1, -7_200_000_000), new(0, -1, 7_200_000_000)];
        string[] expected = ["P1M", "P30D", "PT720H", "P1M-1DT24H", "P-1DT-2H", "P-1DT2H"];
        for (int index = 0; index < values.Length; index++)
        {
            string json = JsonSerializer.Serialize(values[index], DetachedScalarContext.Default.PgInterval);
            Assert.AreEqual("\"" + expected[index] + "\"", json);
            AssertSameComponents(values[index], JsonSerializer.Deserialize(json, DetachedScalarContext.Default.PgInterval));
        }

        Assert.AreEqual(values[0], values[1]);
        Assert.AreNotEqual(values[4], values[5]);
    }

    /// <summary>
    /// Formats both infinities like PostgreSQL 17 while leaving their parsing to the server's version-specific rules.
    /// </summary>
    [TestMethod]
    public void InfinitiesUsePostgresTextAndBackendInput()
    {
        Assert.AreEqual("infinity", PgInterval.PositiveInfinity.ToIsoString());
        Assert.AreEqual("-infinity", PgInterval.NegativeInfinity.ToIsoString());
        Assert.AreEqual("\"infinity\"", JsonSerializer.Serialize(PgInterval.PositiveInfinity, DetachedScalarContext.Default.PgInterval));
        Assert.AreEqual("\"-infinity\"", JsonSerializer.Serialize(PgInterval.NegativeInfinity, DetachedScalarContext.Default.PgInterval));
        Assert.IsFalse(PgInterval.TryParseIsoString("infinity", out _));
        Assert.IsFalse(PgInterval.TryParseIsoString("-infinity", out _));
        Assert.ThrowsExactly<InvalidOperationException>(() => JsonSerializer.Deserialize("\"infinity\"", DetachedScalarContext.Default.PgInterval));
    }

    /// <summary>
    /// Leaves other PostgreSQL interval syntax, noncanonical ISO spellings and infinity sentinels to the backend parser.
    /// </summary>
    /// <param name="text">Text that is not exactly the managed ISO form.</param>
    [TestMethod]
    [DataRow("1 day")]
    [DataRow("-1 days +02:00:00")]
    [DataRow("@ 1 day ago")]
    [DataRow("P")]
    [DataRow("PT")]
    [DataRow("P0D")]
    [DataRow("P0Y1D")]
    [DataRow("P12M")]
    [DataRow("P1D1D")]
    [DataRow("P1DT")]
    [DataRow("PT0.5000S")]
    [DataRow("PT1.S")]
    [DataRow("PT.5S")]
    [DataRow("PT0.0000001S")]
    [DataRow("PT60S")]
    [DataRow("PT1H-1M")]
    [DataRow("PT-0S")]
    [DataRow("P-0D")]
    [DataRow("P+1D")]
    [DataRow("P1W")]
    [DataRow("P1.5D")]
    [DataRow("PT1.5H")]
    [DataRow("p1d")]
    [DataRow("P1d")]
    [DataRow(" P1D")]
    [DataRow("P1D ")]
    [DataRow("P1DT1H1M1S1S")]
    [DataRow("PT1S1M")]
    [DataRow("P1TD")]
    [DataRow("P0001-02-03")]
    [DataRow("P1Y2M3DT4H5M6.5")]
    [DataRow("P1e2D")]
    [DataRow("P2147483648D")]
    [DataRow("P-2147483649D")]
    [DataRow("P178956970Y8M")]
    [DataRow("PT2562047788H54.775808S")]
    [DataRow("PT99999999999999999999H")]
    [DataRow("P178956970Y7M2147483647DT2562047788H54.775807S")]
    [DataRow("P-178956970Y-8M-2147483648DT-2562047788H-54.775808S")]
    public void NoncanonicalTextUsesTheBackendParser(string text)
    {
        Assert.IsFalse(PgInterval.TryParseIsoString(text, out PgInterval value));
        Assert.AreEqual(default, value);
        Assert.ThrowsExactly<InvalidOperationException>(() => JsonSerializer.Deserialize("\"" + text + "\"", DetachedScalarContext.Default.PgInterval));
    }

    /// <summary>
    /// Round-trips every sign and boundary combination of the three independent components.
    /// </summary>
    [TestMethod]
    public void EveryComponentCombinationRoundTrips()
    {
        int[] months = [int.MinValue, -13, -12, -11, -1, 0, 1, 11, 12, 13, int.MaxValue];
        int[] days = [int.MinValue, -31, -1, 0, 1, 30, int.MaxValue];
        long[] microseconds = [long.MinValue, -86_400_000_001, -3_600_000_000, -60_000_001, -1_000_000, -999_999, -1, 0, 1,
            100_000, 59_999_999, 3_600_000_001, 86_400_000_000, long.MaxValue];
        int count = 0;
        foreach (int month in months)
        {
            foreach (int day in days)
            {
                foreach (long microsecond in microseconds)
                {
                    var value = new PgInterval(month, day, microsecond);
                    string text = value.ToIsoString();
                    bool sentinel = (month, day, microsecond) == (int.MaxValue, int.MaxValue, long.MaxValue) ||
                        (month, day, microsecond) == (int.MinValue, int.MinValue, long.MinValue);
                    Assert.AreEqual(!sentinel, PgInterval.TryParseIsoString(text, out PgInterval parsed), text);
                    if (!sentinel)
                    {
                        AssertSameComponents(value, parsed);
                        count++;
                    }
                }
            }
        }

        Assert.AreEqual(months.Length * days.Length * microseconds.Length - 2, count);
    }

    /// <summary>
    /// Requires every stored component to match, which comparison equality alone does not prove.
    /// </summary>
    /// <param name="expected">The original interval.</param>
    /// <param name="actual">The decoded interval.</param>
    private static void AssertSameComponents(PgInterval expected, PgInterval actual)
    {
        Assert.AreEqual((expected.Months, expected.Days, expected.Microseconds, expected.IsFinite),
            (actual.Months, actual.Days, actual.Microseconds, actual.IsFinite));
    }
}
