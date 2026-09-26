using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ankus.Build.Tests;

/// <summary>
/// Verifies compiler-reported target identity independently of the host operating system.
/// </summary>
[TestClass]
public sealed class NativeBindingHeaderTargetTests
{
    private const string Ast = """
        { "kind":"TranslationUnitDecl", "inner": [
          { "kind":"EnumDecl", "inner": [
            { "name":"ankus_header_pg_version", "kind":"EnumConstantDecl", "inner":[{"kind":"ConstantExpr","value":"180006"}] },
            { "name":"ankus_header_pointer_size", "kind":"EnumConstantDecl", "inner":[{"kind":"ConstantExpr","value":"8"}] },
            { "name":"ankus_header_little_endian", "kind":"EnumConstantDecl", "inner":[{"kind":"ConstantExpr","value":"1"}] },
            { "name":"ankus_header_clang_major", "kind":"EnumConstantDecl", "inner":[{"kind":"ConstantExpr","value":"21"}] }
          ] },
          { "kind":"VarDecl", "name":"ankus_header_runtime_identifier", "inner":[{"kind":"StringLiteral","value":"\"linux-x64\""}] }
        ] }
        """;

    /// <summary>
    /// The contract retains the selected target's exact version and ABI rather than the build host's identity.
    /// </summary>
    /// <param name="runtime">The compiler-reported target.</param>
    /// <param name="version">The header's exact PostgreSQL version number.</param>
    /// <param name="major">The requested PostgreSQL major.</param>
    /// <param name="width">The compiler's pointer width.</param>
    /// <param name="littleEndian">The compiler's byte order.</param>
    [TestMethod]
    [DataRow("linux-x64", 180006, 18, 8, true)]
    [DataRow("osx-arm64", 180006, 18, 8, true)]
    [DataRow("win-x64", 170011, 17, 8, true)]
    [DataRow("linux-arm", 190000, 19, 4, false)]
    public void TargetFactsArePreserved(string runtime, int version, int major, int width, bool littleEndian)
    {
        JsonNode root = JsonNode.Parse(Ast)!;
        root["inner"]![0]!["inner"]![0]!["inner"]![0]!["value"] = version.ToString(System.Globalization.CultureInfo.InvariantCulture);
        root["inner"]![0]!["inner"]![1]!["inner"]![0]!["value"] = width.ToString(System.Globalization.CultureInfo.InvariantCulture);
        root["inner"]![0]!["inner"]![2]!["inner"]![0]!["value"] = littleEndian ? "1" : "0";
        root["inner"]![1]!["inner"]![0]!["value"] = "\"" + runtime + "\"";
        using JsonDocument document = JsonDocument.Parse(root.ToJsonString());
        Assert.AreEqual(new NativeHeaderTarget(version, runtime, width, littleEndian, 21), NativeBindingHeaderTarget.Read(document.RootElement, major));
    }

    /// <summary>
    /// Missing, malformed and contradictory observations cannot become a target contract.
    /// </summary>
    /// <param name="change">The invalid target observation.</param>
    [TestMethod]
    [DataRow("missing-version")]
    [DataRow("wrong-major")]
    [DataRow("duplicate-number")]
    [DataRow("number-overflow")]
    [DataRow("number-negative")]
    [DataRow("number-not-text")]
    [DataRow("missing-value")]
    [DataRow("extra-expression")]
    [DataRow("wrong-width")]
    [DataRow("unknown-endian")]
    [DataRow("wrong-endian")]
    [DataRow("compiler-zero")]
    [DataRow("missing-target")]
    [DataRow("duplicate-target")]
    [DataRow("unquoted-target")]
    [DataRow("unknown-target")]
    [DataRow("missing-inner")]
    public void InvalidTargetFactsFailExplicitly(string change)
    {
        JsonNode root = JsonNode.Parse(Ast)!;
        JsonArray nodes = root["inner"]!.AsArray();
        JsonArray numbers = nodes[0]!["inner"]!.AsArray();
        switch (change)
        {
            case "missing-version": numbers.RemoveAt(0); break;
            case "wrong-major": numbers[0]!["inner"]![0]!["value"] = "170011"; break;
            case "duplicate-number": numbers.Add(numbers[0]!.DeepClone()); break;
            case "number-overflow": numbers[0]!["inner"]![0]!["value"] = "2147483648"; break;
            case "number-negative": numbers[0]!["inner"]![0]!["value"] = "-180006"; break;
            case "number-not-text": numbers[0]!["inner"]![0]!["value"] = 180006; break;
            case "missing-value": numbers[0]!["inner"]![0]!.AsObject().Remove("value"); break;
            case "extra-expression": numbers[0]!["inner"]!.AsArray().Add(numbers[0]!["inner"]![0]!.DeepClone()); break;
            case "wrong-width": numbers[1]!["inner"]![0]!["value"] = "4"; break;
            case "unknown-endian": numbers[2]!["inner"]![0]!["value"] = "2"; break;
            case "wrong-endian": numbers[2]!["inner"]![0]!["value"] = "0"; break;
            case "compiler-zero": numbers[3]!["inner"]![0]!["value"] = "0"; break;
            case "missing-target": nodes.RemoveAt(1); break;
            case "duplicate-target": nodes.Add(nodes[1]!.DeepClone()); break;
            case "unquoted-target": nodes[1]!["inner"]![0]!["value"] = "linux-x64"; break;
            case "unknown-target": nodes[1]!["inner"]![0]!["value"] = "\"freebsd-x64\""; break;
            case "missing-inner": root.AsObject().Remove("inner"); break;
            default: Assert.Fail("Unknown invalid target observation."); break;
        }

        using JsonDocument document = JsonDocument.Parse(root.ToJsonString());
        Assert.ThrowsExactly<FormatException>(() => NativeBindingHeaderTarget.Read(document.RootElement, 18));
    }
}
