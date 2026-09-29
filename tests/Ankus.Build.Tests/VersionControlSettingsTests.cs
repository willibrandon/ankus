using Ankus.PgConfig;

namespace Ankus.Build.Tests;

/// <summary>
/// Verifies per-version inheritance without applying current generated SQL restrictions to historical versions.
/// </summary>
[TestClass]
public sealed class VersionControlSettingsTests
{
    private const string Primary = "default_version='release'\nmodule_pathname='Probe.so'\nencoding='UTF8'\nrelocatable=true\nrequires=helper\ntrusted=false\n";

    /// <summary>
    /// Empty overrides inherit the primary; explicit empty dependencies and privilege changes remain secondary assignments.
    /// </summary>
    [TestMethod]
    public void VersionOverridesPreserveInheritanceAndExplicitClearing()
    {
        (string empty, bool inherited) = ExtensionControlSettings.MergeVersion(Primary, "# no overrides", 13, true, true);
        Assert.AreEqual("", empty);
        Assert.IsTrue(inherited);
        (string control, bool relocatable) = ExtensionControlSettings.MergeVersion(Primary,
            "requires=''\ntrusted=YES\nsuperuser=on\nrelocatable=off\ncomment='version # comment'", 18, true, true);
        IReadOnlyDictionary<string, string> values = ExtensionControlFile.Parse(control);
        Assert.HasCount(5, values);
        Assert.AreEqual("", values["requires"]);
        Assert.AreEqual("true", values["trusted"]);
        Assert.AreEqual("true", values["superuser"]);
        Assert.AreEqual("false", values["relocatable"]);
        Assert.AreEqual("version # comment", values["comment"]);
        Assert.IsFalse(relocatable);
        Assert.AreEqual("helper", ExtensionControlFile.Parse(Primary)["requires"]);
    }

    /// <summary>
    /// Fixed schemas are reflected in the actual secondary payload, while inherited fixed schemas cannot be made relocatable.
    /// </summary>
    [TestMethod]
    public void VersionSchemaDisablesRelocationAndRespectsInheritedSchema()
    {
        (string control, bool relocatable) = ExtensionControlSettings.MergeVersion(Primary, "schema='version schema'", 18, true, true);
        IReadOnlyDictionary<string, string> settings = ExtensionControlFile.Parse(control);
        Assert.AreEqual("version schema", settings["schema"]);
        Assert.AreEqual("false", settings["relocatable"]);
        Assert.IsFalse(relocatable);
        string fixedPrimary = Primary + "schema='primary schema'\nrelocatable=false\n";
        Assert.IsFalse(ExtensionControlSettings.MergeVersion(fixedPrimary, "", 18, true, true).Relocatable);
        Assert.ThrowsExactly<FormatException>(() => ExtensionControlSettings.MergeVersion(fixedPrimary, "relocatable=true", 18, false, true));
    }

    /// <summary>
    /// Other versions can name their own libraries and SQL relocation contract; the current version remains bound to generated artifacts.
    /// </summary>
    [TestMethod]
    public void HistoricalVersionsHaveIndependentNativeAndRelocationContracts()
    {
        string fixedPrimary = Primary + "relocatable=false\n";
        (string historical, bool historicalRelocatable) = ExtensionControlSettings.MergeVersion(fixedPrimary,
            "module_pathname='Older.so'\nrelocatable=true", 18, false, false);
        Assert.AreEqual("Older.so", ExtensionControlFile.Parse(historical)["module_pathname"]);
        Assert.IsTrue(historicalRelocatable);
        Assert.IsTrue(ExtensionControlSettings.MergeVersion(fixedPrimary, "relocatable=true", 18, true, true).Relocatable);
        Assert.ThrowsExactly<FormatException>(() => ExtensionControlSettings.MergeVersion(fixedPrimary, "relocatable=true", 18, true, false));
        Assert.ThrowsExactly<FormatException>(() => ExtensionControlSettings.MergeVersion(Primary, "module_pathname='Older.so'", 18, true, true));
        Assert.AreEqual("Probe.so", ExtensionControlFile.Parse(ExtensionControlSettings.MergeVersion(Primary,
            "module_pathname='Probe.so'\nencoding=UTF8", 18, true, true).Control)["module_pathname"]);
    }

    /// <summary>
    /// Secondary-only restrictions, malformed flags, encoding and selected-server gates fail before native publishing.
    /// </summary>
    /// <param name="authored">The unsupported assignment.</param>
    /// <param name="major">The PostgreSQL major.</param>
    [TestMethod]
    [DataRow("default_version=release", 18)]
    [DataRow("directory='sql'", 18)]
    [DataRow("encoding=LATIN1", 18)]
    [DataRow("relocatable=maybe", 18)]
    [DataRow("trusted=o", 18)]
    [DataRow("superuser=''", 18)]
    [DataRow("schema=fixed\nrelocatable=true", 18)]
    [DataRow("no_relocate=helper", 15)]
    [DataRow("unknown=true", 18)]
    public void RejectsInvalidVersionControlSettings(string authored, int major)
        => Assert.ThrowsExactly<FormatException>(() => ExtensionControlSettings.MergeVersion(Primary, authored, major, false, true));

    /// <summary>
    /// PostgreSQL 16 accepts no_relocate with the exact quoted dependency identity.
    /// </summary>
    [TestMethod]
    public void VersionControlsAcceptNoRelocateAtPostgres16Boundary()
    {
        string control = ExtensionControlSettings.MergeVersion(Primary,
            "requires='\"Mixed Case\"'\nno_relocate='\"Mixed Case\"'", 16, true, true).Control;
        Assert.AreEqual("\"Mixed Case\"", ExtensionControlFile.Parse(control)["no_relocate"]);
        Assert.AreEqual("\"Mixed Case\"", ExtensionControlFile.Parse(control)["requires"]);
    }
}
