using Ankus.PgConfig;

namespace Ankus.Build.Tests;

/// <summary>
/// Verifies authored control metadata cannot contradict generated publication contracts.
/// </summary>
[TestClass]
public sealed class ExtensionControlSettingsTests
{
    private const string Generated = "default_version='1.2'\nmodule_pathname='Probe.so'\nencoding='UTF8'\nrelocatable=true\n";

    /// <summary>
    /// Author settings retain all generated identities and disable relocation for a fixed schema.
    /// </summary>
    [TestMethod]
    public void MergesControlSettingsAndFixedSchema()
    {
        (string control, bool relocatable) = ExtensionControlSettings.Merge(Generated, """
            comment = 'exact # comment'
            schema = 'fixed schema'
            requires = '"Mixed Case", helper'
            no_relocate = 'helper'
            trusted = yes
            superuser = on
            default_version = '1.2'
            module_pathname = 'Probe.so'
            encoding = 'UTF8'
            """, 16);
        IReadOnlyDictionary<string, string> values = ExtensionControlFile.Parse(control);
        Assert.HasCount(10, values);
        Assert.AreEqual("1.2", values["default_version"]);
        Assert.AreEqual("Probe.so", values["module_pathname"]);
        Assert.AreEqual("UTF8", values["encoding"]);
        Assert.AreEqual("exact # comment", values["comment"]);
        Assert.AreEqual("fixed schema", values["schema"]);
        Assert.AreEqual("\"Mixed Case\", helper", values["requires"]);
        Assert.AreEqual("helper", values["no_relocate"]);
        Assert.AreEqual("true", values["trusted"]);
        Assert.AreEqual("true", values["superuser"]);
        Assert.AreEqual("false", values["relocatable"]);
        Assert.IsFalse(relocatable);
    }

    /// <summary>
    /// Primary controls retain literal script directories independently of PostgreSQL's selected default layout.
    /// </summary>
    /// <param name="directory">The author directory value.</param>
    [TestMethod]
    [DataRow("")]
    [DataRow("nested/SQL files")]
    [DataRow("../sibling")]
    [DataRow("/absolute/scripts")]
    public void PreservesAuthoredScriptDirectory(string directory)
    {
        string authored = ExtensionControlFile.Format(new Dictionary<string, string> { ["directory"] = directory });
        (string control, bool relocatable) = ExtensionControlSettings.Merge(Generated, authored, 13);
        Assert.AreEqual(directory, ExtensionControlFile.Parse(control)["directory"]);
        Assert.IsTrue(relocatable);
    }

    /// <summary>
    /// Explicit false and generated non-relocatable SQL remain non-relocatable; omitted flags retain defaults.
    /// </summary>
    [TestMethod]
    public void PreservesRelocationAndPrivilegeDefaults()
    {
        (string unchanged, bool original) = ExtensionControlSettings.Merge(Generated, "", 13);
        Assert.IsTrue(original);
        Assert.HasCount(4, ExtensionControlFile.Parse(unchanged));
        Assert.IsFalse(ExtensionControlSettings.Merge(Generated, "relocatable=false", 18).Relocatable);
        Assert.IsFalse(ExtensionControlSettings.Merge(Generated.Replace("relocatable=true", "relocatable=false", StringComparison.Ordinal),
            "comment='fixed SQL'", 18).Relocatable);
    }

    /// <summary>
    /// PostgreSQL Boolean spellings are normalized without silently accepting ambiguous values.
    /// </summary>
    /// <param name="input">The supported Boolean spelling.</param>
    /// <param name="expected">The canonical emitted value.</param>
    [TestMethod]
    [DataRow("t", "true")]
    [DataRow("TRU", "true")]
    [DataRow("y", "true")]
    [DataRow("on", "true")]
    [DataRow("1", "true")]
    [DataRow("f", "false")]
    [DataRow("FALS", "false")]
    [DataRow("n", "false")]
    [DataRow("of", "false")]
    [DataRow("0", "false")]
    public void NormalizesPostgresBooleans(string input, string expected)
    {
        (string control, _) = ExtensionControlSettings.Merge(Generated, $"trusted='{input}'\nsuperuser='{input}'", 18);
        IReadOnlyDictionary<string, string> values = ExtensionControlFile.Parse(control);
        Assert.AreEqual(expected, values["trusted"]);
        Assert.AreEqual(expected, values["superuser"]);
    }

    /// <summary>
    /// Invalid settings fail before any publication files are written.
    /// </summary>
    /// <param name="settings">The invalid control settings.</param>
    /// <param name="major">The selected server major.</param>
    /// <param name="diagnostic">The expected specific diagnostic.</param>
    [TestMethod]
    [DataRow("default_version='2'", 18, "default_version")]
    [DataRow("module_pathname='Other.so'", 18, "module_pathname")]
    [DataRow("encoding='LATIN1'", 18, "encoding")]
    [DataRow("schema='fixed'\nrelocatable=true", 18, "relocatable")]
    [DataRow("trusted='o'", 18, "Boolean")]
    [DataRow("superuser=''", 18, "Boolean")]
    [DataRow("relocatable='maybe'", 18, "Boolean")]
    [DataRow("no_relocate='helper'", 15, "16 or later")]
    [DataRow("unknown='value'", 18, "Unknown")]
    public void RejectsConflictingOrUnsupportedSettings(string settings, int major, string diagnostic)
    {
        FormatException error = Assert.ThrowsExactly<FormatException>(() => ExtensionControlSettings.Merge(Generated, settings, major));
        Assert.Contains(diagnostic, error.Message);
    }

    /// <summary>
    /// An author cannot make fixed generated SQL relocatable.
    /// </summary>
    [TestMethod]
    public void RejectsRelocationOfFixedGeneratedSql()
    {
        FormatException error = Assert.ThrowsExactly<FormatException>(() => ExtensionControlSettings.Merge(
            Generated.Replace("relocatable=true", "relocatable=false", StringComparison.Ordinal), "relocatable=true", 18));
        Assert.Contains("non-relocatable generated SQL", error.Message);
    }
}
