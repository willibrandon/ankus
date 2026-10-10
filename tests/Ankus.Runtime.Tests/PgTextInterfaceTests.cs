using System.Globalization;

namespace Ankus.Runtime.Tests;

/// <summary>
/// Verifies the .NET formatting contracts of value types whose PostgreSQL text needs no backend.
/// </summary>
[TestClass]
public sealed class PgTextInterfaceTests
{
    /// <summary>
    /// Interpolation, <see cref="IFormattable"/> and span formatting all produce PostgreSQL's canonical text, and a
    /// destination that is too small reports failure instead of truncating.
    /// </summary>
    [TestMethod]
    public void FormattingProducesCanonicalText()
    {
        var point = new PgPoint(1.5, -2);
        PgNumeric numeric = PgNumeric.FromDecimal(12.50m);
        var segment = new PgLineSegment(point, new PgPoint(0, 0));
        Assert.AreEqual("(1.5,-2)", $"{point}");
        Assert.AreEqual("12.50", numeric.ToString(null, CultureInfo.GetCultureInfo("de-DE")));
        Assert.AreEqual("[(1.5,-2),(0,0)]", string.Create(CultureInfo.InvariantCulture, $"{segment}"));
        Span<char> buffer = stackalloc char[8];
        Assert.IsTrue(numeric.TryFormat(buffer, out int written, default, null));
        Assert.AreEqual("12.50", buffer[..written].ToString());
        Assert.IsFalse(segment.TryFormat(buffer, out written, default, null));
        Assert.AreEqual(0, written);
        Assert.Contains("one canonical form", Assert.ThrowsExactly<FormatException>(() => numeric.ToString("N2", null)).Message);
        char[] destination = new char[16];
        Assert.ThrowsExactly<FormatException>(() => point.TryFormat(destination, out _, "G", null));
        var path = new PgPath([point, new PgPoint(0, 0)], isClosed: true);
        Assert.AreEqual("((1.5,-2),(0,0))", $"{path}");
        Assert.AreEqual("((1.5,-2),(0,0))", new PgPolygon(path).ToString(string.Empty, CultureInfo.InvariantCulture));
    }
}
