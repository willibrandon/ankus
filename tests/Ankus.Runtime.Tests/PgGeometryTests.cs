using System.Globalization;

namespace Ankus.Runtime.Tests;

/// <summary>
/// Verifies detached geometric ownership, coordinate normalization, formatting and binary framing.
/// </summary>
[TestClass]
public sealed class PgGeometryTests
{
    /// <summary>
    /// Path and polygon construction copies input and preserves vertex order, closure and computed bounds.
    /// </summary>
    [TestMethod]
    public void GeometricCollectionsOwnTheirVertices()
    {
        PgPoint[] input = [new(5, -4), new(-2, 7), new(1, 0)];
        var path = new PgPath(input, true);
        var polygon = new PgPolygon(input);
        input[0] = new(99, 99);
        Assert.AreSequenceEqual([new(5, -4), new(-2, 7), new(1, 0)], path);
        Assert.AreSequenceEqual(path, polygon);
        Assert.IsTrue(path.IsClosed);
        PgPath open = path.WithClosed(false);
        Assert.IsFalse(open.IsClosed);
        Assert.IsTrue(path.IsClosed);
        Assert.AreSequenceEqual(path, open);
        Assert.AreEqual(3, path.Count);
        Assert.AreEqual(3, polygon.Count);
        Assert.AreEqual(new PgPoint(5, -4), path.Points[0]);
        Assert.AreEqual(new PgPoint(-2, 7), polygon[1]);
        Assert.AreEqual(new PgBox(new(5, 7), new(-2, -4)), polygon.BoundingBox);
        Assert.ThrowsExactly<ArgumentNullException>(() => new PgPath(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => new PgPolygon(null!));
        Assert.ThrowsExactly<IndexOutOfRangeException>(() => path[-1]);
        Assert.ThrowsExactly<IndexOutOfRangeException>(() => polygon[3]);
    }

    /// <summary>
    /// Empty and singleton collections retain their distinct shape and bounding-box semantics.
    /// </summary>
    [TestMethod]
    public void EmptyAndSingletonGeometry()
    {
        var empty = new PgPath([]);
        var polygon = new PgPolygon([]);
        Assert.AreEqual(0, empty.Count);
        Assert.AreEqual("[]", empty.ToString());
        Assert.AreEqual("()", empty.WithClosed(true).ToString());
        Assert.AreEqual(0, polygon.Count);
        Assert.AreEqual(default, polygon.BoundingBox);
        var single = new PgPolygon([new(3, -2)]);
        Assert.AreEqual(new PgBox(new(3, -2), new(3, -2)), single.BoundingBox);
        Assert.AreEqual("((3,-2))", single.ToString());
    }

    /// <summary>
    /// Box normalization orders NaN above infinity while retaining signed-zero coordinate bits.
    /// </summary>
    [TestMethod]
    public void BoundsUsePostgresFloatOrdering()
    {
        var box = new PgBox(new(double.NegativeInfinity, double.NaN), new(double.PositiveInfinity, -5));
        Assert.AreEqual(double.PositiveInfinity, box.High.X);
        Assert.IsTrue(double.IsNaN(box.High.Y));
        Assert.AreEqual(new PgPoint(double.NegativeInfinity, -5), box.Low);
        var polygon = new PgPolygon([new(double.NaN, 1), new(-3, double.NegativeInfinity), new(7, double.PositiveInfinity)]);
        Assert.IsTrue(double.IsNaN(polygon.BoundingBox.High.X));
        Assert.AreEqual(new PgPoint(-3, double.NegativeInfinity), polygon.BoundingBox.Low);
        Assert.AreEqual(double.PositiveInfinity, polygon.BoundingBox.High.Y);
        var zero = new PgBox(new(-0.0, 0.0), new(0.0, -0.0));
        Assert.AreEqual(long.MinValue, BitConverter.DoubleToInt64Bits(zero.High.X));
        Assert.AreEqual(long.MinValue, BitConverter.DoubleToInt64Bits(zero.Low.Y));
    }

    /// <summary>
    /// Invariant formatting preserves precision under non-English cultures and structural equality stays exact.
    /// </summary>
    [TestMethod]
    public void GeometryFormattingAndEqualityAreDetached()
    {
        CultureInfo prior = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var point = new PgPoint(1.2345678901234567, -0.0);
            Assert.AreEqual("(1.2345678901234567,-0)", point.ToString());
            Assert.AreEqual("{1,2,-3.5}", new PgLine(1, 2, -3.5).ToString());
            Assert.AreEqual("[(1,2),(3,4)]", new PgLineSegment(new(1, 2), new(3, 4)).ToString());
            Assert.AreEqual("(3,4),(1,2)", new PgBox(new(1, 2), new(3, 4)).ToString());
            Assert.AreEqual("<(1,2),Infinity>", new PgCircle(new(1, 2), double.PositiveInfinity).ToString());
            Assert.AreNotEqual(new PgPoint(1, 2), new PgPoint(1 + 1e-8, 2));
            Assert.AreNotEqual(new PgLine(1, 2, 3), new PgLine(2, 4, 6));
            Assert.AreEqual(new PgPoint(double.NaN, 0), new PgPoint(double.NaN, -0.0));
        }
        finally
        {
            CultureInfo.CurrentCulture = prior;
        }
    }

    /// <summary>
    /// Exact IEEE bits and each fixed geometric layout survive native transport without textual conversion.
    /// </summary>
    [TestMethod]
    public void FixedGeometryBinaryLayouts()
    {
        var point = new PgPoint(BitConverter.Int64BitsToDouble(0x7ff8000000001234), -0.0);
        NativeValue transport = NativeValue.FromPoint(point);
        try
        {
            Assert.AreEqual("7FF80000000012348000000000000000", Convert.ToHexString(transport.ReadBytes()));
            Assert.AreEqual(0x7ff8000000001234L, BitConverter.DoubleToInt64Bits(transport.ReadPoint().X));
            Assert.AreEqual(long.MinValue, BitConverter.DoubleToInt64Bits(transport.ReadPoint().Y));
        }
        finally
        {
            transport.Release();
        }

        RoundTrip(new PgLine(1, -2, 3), NativeValue.FromLine, static value => value.ReadLine());
        RoundTrip(new PgLineSegment(new(1, 2), new(3, 4)), NativeValue.FromLineSegment, static value => value.ReadLineSegment());
        RoundTrip(new PgBox(new(1, 2), new(3, 4)), NativeValue.FromBox, static value => value.ReadBox());
        RoundTrip(new PgCircle(new(1, 2), 3), NativeValue.FromCircle, static value => value.ReadCircle());
    }

    /// <summary>
    /// Collection framing retains closure, vertices and empty geometry after freeing the native buffer.
    /// </summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CollectionBinaryLayouts(bool closed)
    {
        foreach (PgPoint[] points in new PgPoint[][] { [], [new(1, 2)], [new(3, 4), new(-1, -2)] })
        {
            NativeValue path = NativeValue.FromPath(new PgPath(points, closed));
            NativeValue polygon = NativeValue.FromPolygon(new PgPolygon(points));
            PgPath copy;
            PgPolygon copyPolygon;
            try
            {
                copy = path.ReadPath();
                copyPolygon = polygon.ReadPolygon();
                Assert.HasCount(5 + 16 * points.Length, path.ReadBytes());
                Assert.HasCount(4 + 16 * points.Length, polygon.ReadBytes());
            }
            finally
            {
                path.Release();
                polygon.Release();
            }

            Assert.AreSequenceEqual(points, copy);
            Assert.AreSequenceEqual(points, copyPolygon);
            Assert.AreEqual(closed, copy.IsClosed);
        }
    }

    /// <summary>
    /// Invalid count, length and flag combinations fail before allocating a point array.
    /// </summary>
    [TestMethod]
    [DataRow("")]
    [DataRow("00000000")]
    [DataRow("00FFFFFFFF")]
    [DataRow("007FFFFFFF")]
    [DataRow("0200000000")]
    [DataRow("0000000001")]
    [DataRow("000000000000")]
    public void InvalidGeometryFramesAreRejected(string hex)
    {
        NativeValue value = NativeValue.FromBytes(Convert.FromHexString(hex));
        try
        {
            Assert.ThrowsExactly<InvalidOperationException>(() => value.ReadPath());
            Assert.ThrowsExactly<InvalidOperationException>(() => value.ReadPoint());
        }
        finally
        {
            value.Release();
        }
    }

    /// <summary>
    /// Missing text returns false while backend absence remains an operational error.
    /// </summary>
    [TestMethod]
    public void GeometryParsingRequiresBackend()
    {
        Assert.IsFalse(PgPoint.TryParse(null, out PgPoint point));
        Assert.AreEqual(default, point);
        Assert.IsFalse(PgPath.TryParse(null, out PgPath? path));
        Assert.IsNull(path);
        Assert.ThrowsExactly<InvalidOperationException>(() => PgPoint.TryParse("(1,2)", out _));
        Assert.ThrowsExactly<InvalidOperationException>(() => PgPolygon.TryParse("((1,2))", out _));
    }

    private static void RoundTrip<T>(T expected, Func<T, NativeValue> write, Func<NativeValue, T> read)
    {
        NativeValue value = write(expected);
        try
        {
            Assert.AreEqual(expected, read(value));
        }
        finally
        {
            value.Release();
        }
    }
}
