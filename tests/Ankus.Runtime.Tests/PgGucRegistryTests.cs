namespace Ankus.Runtime.Tests;

/// <summary>
/// Verifies that run-time configuration definitions reject invalid metadata before any native call.
/// </summary>
[TestClass]
public sealed class PgGucRegistryTests
{
    /// <summary>
    /// Integer bounds must form a range containing the default.
    /// </summary>
    /// <param name="defaultValue">The default value.</param>
    /// <param name="minimum">The inclusive minimum.</param>
    /// <param name="maximum">The inclusive maximum.</param>
    /// <param name="parameter">The rejected argument.</param>
    [TestMethod]
    [DataRow(5, 6, 4, "minimum")]
    [DataRow(3, 4, 9, "defaultValue")]
    [DataRow(10, 4, 9, "defaultValue")]
    public void IntegerRangesMustContainTheDefault(int defaultValue, int minimum, int maximum, string parameter)
    {
        ArgumentOutOfRangeException error = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            PgGucRegistry.DefineInt("demo.limit", defaultValue, "Limit", minimum, maximum));
        Assert.AreEqual(parameter, error.ParamName);
    }

    /// <summary>
    /// Real defaults and bounds must be numbers forming a range containing the default.
    /// </summary>
    /// <param name="defaultValue">The default value.</param>
    /// <param name="minimum">The inclusive minimum.</param>
    /// <param name="maximum">The inclusive maximum.</param>
    /// <param name="parameter">The rejected argument.</param>
    [TestMethod]
    [DataRow(double.NaN, 0d, 1d, "defaultValue")]
    [DataRow(0.5, double.NaN, 1d, "minimum")]
    [DataRow(0.5, 0d, double.NaN, "maximum")]
    [DataRow(0.5, 1d, 0d, "minimum")]
    [DataRow(-0.5, 0d, 1d, "defaultValue")]
    [DataRow(1.5, 0d, 1d, "defaultValue")]
    public void RealRangesMustBeNumbersContainingTheDefault(double defaultValue, double minimum, double maximum, string parameter)
    {
        ArgumentOutOfRangeException error = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            PgGucRegistry.DefineReal("demo.ratio", defaultValue, "Ratio", minimum, maximum));
        Assert.AreEqual(parameter, error.ParamName);
    }

    /// <summary>
    /// Enumerated definitions need labels, nonblank label text and a label with the default ordinal.
    /// </summary>
    [TestMethod]
    public void EnumeratedDefinitionsRequireValidLabels()
    {
        Assert.AreEqual("options", Assert.ThrowsExactly<ArgumentNullException>(() =>
            PgGucRegistry.DefineEnum("demo.mode", 0, "Mode", null!)).ParamName);
        Assert.AreEqual("options", Assert.ThrowsExactly<ArgumentException>(() =>
            PgGucRegistry.DefineEnum("demo.mode", 0, "Mode", [])).ParamName);
        Assert.AreEqual("options", Assert.ThrowsExactly<ArgumentException>(() =>
            PgGucRegistry.DefineEnum("demo.mode", 0, "Mode", [new PgGucEnumOption(" ", 0)])).ParamName);
        Assert.AreEqual("defaultValue", Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            PgGucRegistry.DefineEnum("demo.mode", 2, "Mode", [new PgGucEnumOption("off", 0), new PgGucEnumOption("on", 1)])).ParamName);
    }

    /// <summary>
    /// Text reaching PostgreSQL's C strings must be present where required and contain no zero characters.
    /// </summary>
    [TestMethod]
    public void TextMustBeValidCStrings()
    {
        Assert.AreEqual("name", Assert.ThrowsExactly<ArgumentException>(() => PgGucRegistry.DefineBool(" ", true, "Enabled")).ParamName);
        Assert.AreEqual("name", Assert.ThrowsExactly<ArgumentException>(() => PgGucRegistry.DefineBool("demo.\0x", true, "Enabled")).ParamName);
        Assert.AreEqual("shortDescription", Assert.ThrowsExactly<ArgumentNullException>(() =>
            PgGucRegistry.DefineBool("demo.enabled", true, null!)).ParamName);
        Assert.AreEqual("longDescription", Assert.ThrowsExactly<ArgumentException>(() =>
            PgGucRegistry.DefineBool("demo.enabled", true, "Enabled", "long\0")).ParamName);
        Assert.AreEqual("defaultValue", Assert.ThrowsExactly<ArgumentException>(() =>
            PgGucRegistry.DefineString("demo.label", "a\0b", "Label")).ParamName);
        Assert.AreEqual("options", Assert.ThrowsExactly<ArgumentException>(() =>
            PgGucRegistry.DefineEnum("demo.mode", 0, "Mode", [new PgGucEnumOption("o\0ff", 0)])).ParamName);
    }

    /// <summary>
    /// Names follow PostgreSQL's dotted custom setting rules on every supported major.
    /// </summary>
    /// <param name="name">The rejected name.</param>
    [TestMethod]
    [DataRow("nodot")]
    [DataRow("demo.")]
    [DataRow(".demo")]
    [DataRow("demo..limit")]
    [DataRow("demo.1limit")]
    [DataRow("demo.lim it")]
    [DataRow("demo.lim-it")]
    public void NamesMustBeCustomSettingNames(string name)
        => Assert.AreEqual("name", Assert.ThrowsExactly<ArgumentException>(() => PgGucRegistry.DefineBool(name, true, "Enabled")).ParamName);

    /// <summary>
    /// Accepted names include underscores, digits, dollar signs and non-ASCII characters after the first character.
    /// </summary>
    /// <param name="name">The accepted name.</param>
    [TestMethod]
    [DataRow("demo.limit")]
    [DataRow("_demo.a1$")]
    [DataRow("démo.größe")]
    [DataRow("a.b.c")]
    public void ValidNamesReachTheBackendCheck(string name)
        => Assert.ThrowsExactly<InvalidOperationException>(() => PgGucRegistry.DefineBool(name, true, "Enabled"));

    /// <summary>
    /// Contexts, flags and units must be values PostgreSQL defines.
    /// </summary>
    [TestMethod]
    public void OptionsMustBeDefined()
    {
        Assert.AreEqual("context", Assert.ThrowsExactly<ArgumentException>(() =>
            PgGucRegistry.DefineBool("demo.enabled", true, "Enabled", context: (PgGucContext)7)).ParamName);
        Assert.AreEqual("flags", Assert.ThrowsExactly<ArgumentException>(() =>
            PgGucRegistry.DefineBool("demo.enabled", true, "Enabled", flags: (PgGucOptions)1024)).ParamName);
        Assert.AreEqual("unit", Assert.ThrowsExactly<ArgumentException>(() =>
            PgGucRegistry.DefineInt("demo.limit", 1, "Limit", unit: (PgGucUnit)9)).ParamName);
    }

    /// <summary>
    /// Valid definitions require a PostgreSQL backend callback, so a managed-only caller is rejected without native access.
    /// </summary>
    [TestMethod]
    public void ValidDefinitionsRequireABackend()
        => Assert.ThrowsExactly<InvalidOperationException>(() => PgGucRegistry.DefineInt("demo.limit", 1, "Limit", 0, 2));
}
