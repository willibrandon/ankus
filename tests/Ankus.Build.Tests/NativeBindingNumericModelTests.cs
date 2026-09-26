using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ankus.Build.Tests;

public sealed partial class NativeBindingHeaderTargetTests
{
    /// <summary>
    /// Exact signedness and floating precision survive target comparison and the worker's JSON protocol.
    /// </summary>
    /// <param name="charIsSigned">The observed plain-char interpretation.</param>
    /// <param name="wideSize">The observed wide-character byte width.</param>
    /// <param name="wideIsSigned">The observed wide-character interpretation.</param>
    /// <param name="precision">The observed long-double significand precision.</param>
    /// <param name="minimum">The observed minimum normal exponent.</param>
    /// <param name="maximum">The observed maximum normal exponent.</param>
    [TestMethod]
    [DataRow(true, 4, true, 64, -16381, 16384)]
    [DataRow(false, 2, false, 53, -1021, 1024)]
    [DataRow(false, 4, false, 113, -16381, 16384)]
    public void TargetNumericFactsArePreserved(bool charIsSigned, int wideSize, bool wideIsSigned, int precision, int minimum, int maximum)
    {
        JsonNode root = NativeNumericModelFixture.AddFacts(JsonNode.Parse(Ast)!);
        Set("char_signed", charIsSigned ? 1 : 0);
        Set("wchar_size", wideSize);
        Set("wchar_signed", wideIsSigned ? 1 : 0);
        Set("long_double_precision", precision);
        Set("long_double_min_exp", minimum);
        Set("long_double_max_exp", maximum);
        using JsonDocument document = JsonDocument.Parse(root.ToJsonString());
        NativeHeaderTarget actual = NativeBindingHeaderTarget.Read(document.RootElement, 18);
        var expected = new NativeNumericModel(charIsSigned, wideSize, wideIsSigned, 2,
            new(24, -125, 128), new(53, -1021, 1024), new(precision, minimum, maximum));
        Assert.AreEqual(expected, actual.Numeric);
        var expectedTarget = new NativeHeaderTarget(180006, "linux-x64", 8, true, 21, expected);
        string serialized = JsonSerializer.Serialize(actual, NativeBindingRecordWorker.JsonOptions);
        Assert.AreEqual(expectedTarget, JsonSerializer.Deserialize<NativeHeaderTarget>(serialized, NativeBindingRecordWorker.JsonOptions));
        Assert.AreNotEqual(expectedTarget with { Numeric = expected with { CharIsSigned = !charIsSigned } }, actual);
        Assert.AreNotEqual(expectedTarget with { Numeric = expected with { LongDouble = new(precision + 1, minimum, maximum) } }, actual);

        void Set(string name, int value) => NumericFact(root, name)["inner"]![0]!["value"] = value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Every scalar identity component is mandatory, including negative exponent bounds.
    /// </summary>
    /// <param name="name">The missing independent compiler observation.</param>
    [TestMethod]
    [DataRow("char_signed")]
    [DataRow("wchar_size")]
    [DataRow("wchar_signed")]
    [DataRow("float_radix")]
    [DataRow("float_precision")]
    [DataRow("float_min_exp")]
    [DataRow("float_max_exp")]
    [DataRow("double_precision")]
    [DataRow("double_min_exp")]
    [DataRow("double_max_exp")]
    [DataRow("long_double_precision")]
    [DataRow("long_double_min_exp")]
    [DataRow("long_double_max_exp")]
    public void MissingNumericFactsFailExplicitly(string name)
    {
        JsonNode root = NativeNumericModelFixture.AddFacts(JsonNode.Parse(Ast)!);
        root["inner"]![0]!["inner"]!.AsArray().Remove(NumericFact(root, name));
        using JsonDocument document = JsonDocument.Parse(root.ToJsonString());
        FormatException failure = Assert.ThrowsExactly<FormatException>(() => NativeBindingHeaderTarget.Read(document.RootElement, 18));
        Assert.Contains(name, failure.Message);
    }

    /// <summary>
    /// Contradictory numeric limits, malformed values and duplicate observations fail without guessing a default ABI.
    /// </summary>
    /// <param name="name">The changed compiler observation.</param>
    /// <param name="value">The invalid numeric value or duplicate marker.</param>
    [TestMethod]
    [DataRow("char_signed", "2")]
    [DataRow("char_signed", "-1")]
    [DataRow("wchar_signed", "2")]
    [DataRow("wchar_size", "0")]
    [DataRow("wchar_size", "-1")]
    [DataRow("float_radix", "1")]
    [DataRow("float_radix", "0")]
    [DataRow("float_precision", "0")]
    [DataRow("float_precision", "-1")]
    [DataRow("float_precision", "54")]
    [DataRow("double_precision", "65")]
    [DataRow("float_min_exp", "0")]
    [DataRow("float_min_exp", "-1022")]
    [DataRow("double_min_exp", "-16382")]
    [DataRow("float_max_exp", "0")]
    [DataRow("float_max_exp", "-1")]
    [DataRow("float_max_exp", "1025")]
    [DataRow("double_max_exp", "16385")]
    [DataRow("long_double_precision", "0")]
    [DataRow("long_double_min_exp", "1")]
    [DataRow("long_double_max_exp", "-1")]
    [DataRow("long_double_max_exp", "2147483648")]
    [DataRow("char_signed", "01")]
    [DataRow("float_min_exp", "-0125")]
    [DataRow("float_max_exp", "+128")]
    [DataRow("double_precision", "duplicate")]
    public void InvalidNumericFactsFailExplicitly(string name, string value)
    {
        JsonNode root = NativeNumericModelFixture.AddFacts(JsonNode.Parse(Ast)!);
        JsonNode fact = NumericFact(root, name);
        if (value == "duplicate") { root["inner"]![0]!["inner"]!.AsArray().Add(fact.DeepClone()); }
        else { fact["inner"]![0]!["value"] = value; }

        using JsonDocument document = JsonDocument.Parse(root.ToJsonString());
        Assert.ThrowsExactly<FormatException>(() => NativeBindingHeaderTarget.Read(document.RootElement, 18));
    }

    /// <summary>
    /// Missing numeric objects cannot bypass JSON contract validation or direct graph validation.
    /// </summary>
    [TestMethod]
    public void NumericModelsRejectIncompleteContracts()
    {
        NativeNumericModel valid = NativeNumericModelFixture.Binary80;
        foreach (NativeNumericModel model in new[] { null!, valid with { Float = null! }, valid with { Double = null! }, valid with { LongDouble = null! } })
        {
            Assert.ThrowsExactly<FormatException>(() => NativeBindingNumericModel.Validate(model));
        }

        var target = new NativeHeaderTarget(180006, "linux-x64", 8, true, 21, valid);
        JsonObject root = JsonNode.Parse(JsonSerializer.Serialize(target, NativeBindingRecordWorker.JsonOptions))!.AsObject();
        Assert.IsTrue(root.Remove("Numeric"));
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<NativeHeaderTarget>(root.ToJsonString(), NativeBindingRecordWorker.JsonOptions));
    }

    private static JsonNode NumericFact(JsonNode root, string name)
        => root["inner"]![0]!["inner"]!.AsArray().Single(value => value!["name"]!.GetValue<string>() == "ankus_header_" + name)!;
}
