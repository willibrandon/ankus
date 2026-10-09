using System.Text;

namespace Ankus.Runtime.Tests;

/// <summary>
/// Checks Rune text transport and the rejection of text that is not exactly one Unicode scalar value.
/// </summary>
[TestClass]
public sealed class PgRuneTextTests
{
    /// <summary>
    /// Runes travel as their UTF-8 bytes and read back as the same scalar value, including surrogate pairs.
    /// </summary>
    /// <param name="scalar">The Unicode scalar value.</param>
    /// <param name="hex">Its independently encoded UTF-8 bytes.</param>
    [TestMethod]
    [DataRow(0x61, "61")]
    [DataRow(0xDF, "C39F")]
    [DataRow(0x211D, "E2849D")]
    [DataRow(0x1F4A3, "F09F92A3")]
    [DataRow(0x10FFFF, "F48FBFBF")]
    public void RunesTravelAsUtf8(int scalar, string hex)
    {
        var rune = new Rune(scalar);
        NativeValue value = NativeValue.FromRune(rune);
        try
        {
            Assert.AreEqual(hex, Convert.ToHexString(value.ReadBytes()));
            Assert.AreEqual(rune, value.ReadRune());
        }
        finally
        {
            value.Release();
        }
    }

    /// <summary>
    /// Text that is empty, longer than one scalar value or malformed fails instead of being truncated or read as NULL.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="message">The expected message.</param>
    [TestMethod]
    [DataRow("", "Empty text has no character to read as a Rune.")]
    [DataRow("ab", "A Rune holds exactly one Unicode scalar value, but the text has more than one.")]
    [DataRow("é", "A Rune holds exactly one Unicode scalar value, but the text has more than one.")]
    [DataRow("\U0001F4A3\U0001F4A3", "A Rune holds exactly one Unicode scalar value, but the text has more than one.")]
    [DataRow("\uD83D", "The text begins with an unpaired surrogate, which is not a Unicode scalar value.")]
    [DataRow("\uDCA3a", "The text begins with an unpaired surrogate, which is not a Unicode scalar value.")]
    public void TextThatIsNotOneScalarValueIsRejected(string text, string message)
    {
        InvalidCastException error = Assert.ThrowsExactly<InvalidCastException>(() => SpiRow.Convert<Rune>(text));
        Assert.AreEqual(message, error.Message);
        Assert.AreEqual(message, Assert.ThrowsExactly<InvalidCastException>(() => SpiRow.Convert<Rune?>(text)).Message);
    }

    /// <summary>
    /// SPI cells convert between Runes and character text, keep SQL NULL for nullable Runes and reject it otherwise.
    /// </summary>
    [TestMethod]
    public void SpiCellsConvertBetweenRunesAndCharacterText()
    {
        Assert.AreEqual(new Rune(0x1F4A3), SpiRow.Convert<Rune>("\U0001F4A3"));
        Assert.AreEqual(new Rune('ß'), SpiRow.Convert<Rune?>("ß"));
        Assert.IsNull(SpiRow.Convert<Rune?>(null));
        Assert.AreEqual("\U0001F4A3", SpiRow.Convert<string>(new Rune(0x1F4A3)));
        Assert.ThrowsExactly<InvalidOperationException>(() => SpiRow.Convert<Rune>(null));
        Assert.AreEqual(1043u, SpiType.GetOid<Rune>());
        Assert.AreEqual(1043u, SpiType.GetOid<Rune?>());
    }

    /// <summary>
    /// Rune arrays declare varchar elements and read text or varchar elements while preserving SQL NULL elements.
    /// </summary>
    [TestMethod]
    public void RuneArraysUseVarcharElements()
    {
        var runes = new PgArray<Rune?>([null, new Rune('a'), new Rune(0x10FFFF)]);
        Assert.AreEqual(1043u, ((IPgArray)runes).ElementOid);
        PgArray<Rune?> read = SpiRow.Convert<PgArray<Rune?>>(new PgArray<string?>([null, "a", "\U0010FFFF"]));
        Assert.AreSequenceEqual(runes, read);
        Assert.AreSequenceEqual(["a", "\U0010FFFF"], SpiRow.Convert<PgArray<string>>(new PgArray<Rune>([new Rune('a'), new Rune(0x10FFFF)])));
        Assert.ThrowsExactly<InvalidCastException>(() => SpiRow.Convert<PgArray<Rune>>(new PgArray<string?>(["a", "bc"])));
    }
}
