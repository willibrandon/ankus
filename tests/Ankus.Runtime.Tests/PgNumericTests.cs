using System.Globalization;
using System.Numerics;

namespace Ankus.Runtime.Tests;

/// <summary>Verifies numeric ownership, value ordering, display scale, and checked .NET conversions.</summary>
[TestClass]
public sealed class PgNumericTests
{
    /// <summary>Verifies decimal scale and extremes survive exact conversion without backend access.</summary>
    [TestMethod]
    public void DecimalConversionsPreserveValueAndScale()
    {
        PgNumeric scaled = PgNumeric.FromDecimal(123.4500m);
        Assert.AreEqual("123.4500", scaled.Text);
        Assert.AreEqual(4, scaled.Scale);
        Assert.AreEqual("123.45", scaled.ToNormalizedString());
        Assert.AreEqual(123.4500m, scaled.ToDecimal());
        Assert.AreSequenceEqual(decimal.GetBits(123.4500m), decimal.GetBits(scaled.ToDecimal()));
        Assert.AreEqual(decimal.MaxValue, PgNumeric.FromDecimal(decimal.MaxValue).ToDecimal());
        Assert.AreEqual(decimal.MinValue, PgNumeric.FromDecimal(decimal.MinValue).ToDecimal());
        Assert.AreEqual(0.0000000000000000000000000001m, PgNumeric.FromDecimal(0.0000000000000000000000000001m).ToDecimal());
    }

    /// <summary>Rejects the rounding and underflow which decimal parsing would otherwise perform silently.</summary>
    /// <param name="text">Canonical PostgreSQL output outside decimal's exact value set.</param>
    [TestMethod]
    [DataRow("0.00000000000000000000000000001")]
    [DataRow("8.0000000000000000000000000001")]
    [DataRow("79228162514264337593543950335.1")]
    [DataRow("79228162514264337593543950336")]
    [DataRow("-79228162514264337593543950336")]
    [DataRow("NaN")]
    [DataRow("Infinity")]
    [DataRow("-Infinity")]
    public void UnrepresentableDecimalsAreRejected(string text)
        => Assert.ThrowsExactly<OverflowException>(() => PgNumeric.FromCanonicalText(text).ToDecimal());

    /// <summary>Checks equality/hash normalization, including NaN and differently scaled zeroes.</summary>
    /// <param name="left">The first canonical numeric.</param>
    /// <param name="right">The numerically equal canonical numeric.</param>
    [TestMethod]
    [DataRow("0", "0.0000")]
    [DataRow("123.4500", "123.45")]
    [DataRow("-123.4500", "-123.45")]
    [DataRow("NaN", "NaN")]
    [DataRow("Infinity", "Infinity")]
    [DataRow("-Infinity", "-Infinity")]
    public void EqualityAndHashesIgnoreDisplayScale(string left, string right)
    {
        PgNumeric first = PgNumeric.FromCanonicalText(left);
        PgNumeric second = PgNumeric.FromCanonicalText(right);
        Assert.AreEqual(first, second);
        Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
        Assert.AreEqual(0, first.CompareTo(second));
        Assert.IsTrue(first == second);
        Assert.IsTrue(first <= second);
        Assert.IsTrue(first >= second);
    }

    /// <summary>Checks sign, integer-digit width, fractional digits, and PostgreSQL special-value ordering.</summary>
    [TestMethod]
    public void OrderingMatchesNumericMagnitudeAndSpecialValues()
    {
        string[] ordered = ["-Infinity", "-100", "-1.01", "-1", "-0.0001", "0", "0.0001", "1", "1.01", "100", "Infinity", "NaN"];
        for (int index = 1; index < ordered.Length; index++)
        {
            PgNumeric left = PgNumeric.FromCanonicalText(ordered[index - 1]);
            PgNumeric right = PgNumeric.FromCanonicalText(ordered[index]);
            Assert.IsTrue(left < right, $"{left} should sort before {right}.");
            Assert.IsTrue(right > left, $"{right} should sort after {left}.");
            Assert.IsTrue(left != right);
        }

        Assert.AreEqual("0", default(PgNumeric).Text);
        Assert.AreEqual(PgNumeric.FromDecimal(0m), default(PgNumeric));
        Assert.IsTrue(default(PgNumeric).IsFinite);
        Assert.AreEqual(0, default(PgNumeric).Sign);
        Assert.AreEqual(-1, PgNumeric.NegativeInfinity.Sign);
        Assert.AreEqual(1, PgNumeric.PositiveInfinity.Sign);
        Assert.IsNull(PgNumeric.NaN.Sign);
        Assert.IsNull(PgNumeric.NaN.Scale);
        Assert.IsNull(PgNumeric.PositiveInfinity.Scale);
        Assert.IsTrue(PgNumeric.NaN.IsNaN);
        Assert.IsFalse(PgNumeric.PositiveInfinity.IsFinite);
    }

    /// <summary>Checks integer conversions beyond decimal and refuses fractional/nonfinite input.</summary>
    [TestMethod]
    public void BigIntegersAndRedundantFractionalZerosRemainExact()
    {
        BigInteger value = BigInteger.Parse("12345678901234567890123456789012345678901234567890", CultureInfo.InvariantCulture);
        Assert.AreEqual(value, PgNumeric.FromBigInteger(value).ToBigInteger());
        Assert.AreEqual(-value, PgNumeric.FromBigInteger(-value).ToBigInteger());
        Assert.AreEqual(new BigInteger(1), PgNumeric.FromCanonicalText("1.00000000000000000000000000000000").ToBigInteger());
        Assert.AreEqual(1m, PgNumeric.FromCanonicalText("1.00000000000000000000000000000000").ToDecimal());
        Assert.ThrowsExactly<OverflowException>(() => PgNumeric.FromDecimal(1.1m).ToBigInteger());
        Assert.ThrowsExactly<OverflowException>(() => PgNumeric.NaN.ToBigInteger());
        BigInteger limit = BigInteger.Pow(10, 131072);
        Assert.AreEqual(limit - 1, PgNumeric.FromBigInteger(limit - 1).ToBigInteger());
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PgNumeric.FromBigInteger(limit));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PgNumeric.FromBigInteger(-limit));
    }

    /// <summary>Checks numeric/decimal row conversions and independent cell type metadata.</summary>
    [TestMethod]
    public void RowConversionsUseExactNumericSemantics()
    {
        var row = new SpiRow([PgNumeric.FromDecimal(12.340m)], [new("value", 1700)]);
        Assert.AreEqual(12.340m, row.Get<decimal?>(0));
        row.Set(0, 9.870m);
        Assert.AreEqual(PgNumeric.FromDecimal(9.870m), row.Get<PgNumeric?>(0));
        Assert.AreEqual(1700u, row.GetTypeOid(0));
        row.Set(0, PgNumeric.PositiveInfinity);
        Assert.ThrowsExactly<OverflowException>(() => row.Get<decimal>(0));
        row.Set<decimal?>(0, null);
        Assert.IsNull(row.Get<PgNumeric?>(0));
        Assert.ThrowsExactly<InvalidOperationException>(() => row.Get<PgNumeric>(0));
        Assert.ThrowsExactly<InvalidOperationException>(() => PgNumeric.TryParse("1", out _));
        Assert.IsFalse(PgNumeric.TryParse(null, out PgNumeric parsed));
        Assert.AreEqual(default, parsed);
    }
}
