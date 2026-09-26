using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ankus.Build.Tests;

/// <summary>
/// Verifies exact extraction of compiler-evaluated storage constants before contract validation.
/// </summary>
[TestClass]
public sealed class NativeBindingStorageObservationTests
{
    private const string Ast = """
        { "kind":"TranslationUnitDecl", "inner": [
          { "kind":"EnumDecl", "inner": [
            { "name":"ankus_header_pg_version", "kind":"EnumConstantDecl", "inner":[{"kind":"ConstantExpr","value":"180006"}] },
            { "name":"ankus_header_pointer_size", "kind":"EnumConstantDecl", "inner":[{"kind":"ConstantExpr","value":"8"}] },
            { "name":"ankus_header_little_endian", "kind":"EnumConstantDecl", "inner":[{"kind":"ConstantExpr","value":"1"}] },
            { "name":"ankus_header_clang_major", "kind":"EnumConstantDecl", "inner":[{"kind":"ConstantExpr","value":"21"}] }
          ] },
          { "kind":"VarDecl", "name":"ankus_header_runtime_identifier", "inner":[{"kind":"StringLiteral","value":"\"linux-x64\""}] },
          { "kind":"EnumDecl", "inner": [
            { "name":"ankus_storage_value_global_size", "kind":"EnumConstantDecl", "inner":[{"kind":"ConstantExpr","value":"4"}] },
            { "name":"ankus_storage_value_global_alignment", "kind":"EnumConstantDecl", "inner":[{"kind":"ConstantExpr","value":"4"}] },
            { "name":"ankus_storage_value_global_element", "kind":"EnumConstantDecl", "inner":[{"kind":"ConstantExpr","value":"-1"}] },
            { "name":"ankus_storage_value_global_signed", "kind":"EnumConstantDecl", "inner":[{"kind":"ConstantExpr","value":"1"}] }
          ] }
        ] }
        """;

    private static readonly NativeHeaderCatalog s_catalog = new(new(180006, "linux-x64", 8, true, 21),
        new Dictionary<string, NativeHeaderSymbol>
        {
            ["value"] = new("value", "value", false, new NativeHeaderScalar("int"), [], false, false, "extern", []),
        });

    /// <summary>
    /// Constants preserve exact values, unknown fields and target identity independently of AST ordering and implicit casts.
    /// </summary>
    [TestMethod]
    public void CompilerConstantsRetainExactStorage()
    {
        JsonNode root = JsonNode.Parse(Ast)!;
        JsonArray members = root["inner"]![2]!["inner"]!.AsArray();
        JsonNode first = members[0]!.DeepClone();
        members.RemoveAt(0);
        members.Add(first);
        JsonNode expression = first["inner"]![0]!.DeepClone();
        first["inner"]![0] = new JsonObject { ["kind"] = "ImplicitCastExpr", ["inner"] = new JsonArray(expression) };
        members.Add(new JsonObject { ["kind"] = "EnumConstantDecl", ["name"] = "unrelated_constant" });
        members.Add(new JsonObject { ["kind"] = "FullComment" });
        root["inner"]!.AsArray().Add(new JsonObject { ["kind"] = "EnumDecl" });
        using JsonDocument document = JsonDocument.Parse(root.ToJsonString());
        string output = NativeBindingStorageProbe.ReadObservations(s_catalog, document.RootElement);
        Assert.AreEqual("storage|1|180006|8|1|linux-x64|21\nvalue|value|global|4|4|-|1\n", output.ReplaceLineEndings("\n"));
        NativeHeaderStorage storage = NativeBindingStorageProbe.Read(s_catalog, output);
        Assert.AreEqual(new NativeHeaderValueStorage(4, 4, null, true), storage.Symbols["value"].Global);
        Assert.IsNull(storage.Symbols["value"].Result);
        Assert.IsEmpty(storage.Symbols["value"].Parameters);
    }

    /// <summary>
    /// Empty selections contain only target facts, without inventing measurements.
    /// </summary>
    [TestMethod]
    public void EmptyCompilerStorageRetainsTarget()
    {
        JsonNode root = JsonNode.Parse(Ast)!;
        root["inner"]!.AsArray().RemoveAt(2);
        var catalog = new NativeHeaderCatalog(s_catalog.Target, new Dictionary<string, NativeHeaderSymbol>());
        using JsonDocument document = JsonDocument.Parse(root.ToJsonString());
        string output = NativeBindingStorageProbe.ReadObservations(catalog, document.RootElement);
        Assert.AreEqual("storage|1|180006|8|1|linux-x64|21\n", output.ReplaceLineEndings("\n"));
        Assert.IsEmpty(NativeBindingStorageProbe.Read(catalog, output).Symbols);
    }

    /// <summary>
    /// Missing, duplicate, unexpected, malformed and contradictory compiler constants cannot become storage contracts.
    /// </summary>
    /// <param name="change">The invalid observation partition.</param>
    [TestMethod]
    [DataRow("missing")]
    [DataRow("duplicate")]
    [DataRow("unexpected")]
    [DataRow("missing-expression")]
    [DataRow("extra-expression")]
    [DataRow("missing-value")]
    [DataRow("numeric-value")]
    [DataRow("null-value")]
    [DataRow("null-name")]
    [DataRow("wrong-kind")]
    [DataRow("wrong-target")]
    [DataRow("invalid-sentinel")]
    [DataRow("overflow")]
    [DataRow("unexpected-stride")]
    [DataRow("wrong-signedness")]
    public void InvalidCompilerStorageFailsExplicitly(string change)
    {
        JsonNode root = JsonNode.Parse(Ast)!;
        JsonArray members = root["inner"]![2]!["inner"]!.AsArray();
        switch (change)
        {
            case "missing": members.RemoveAt(0); break;
            case "duplicate": members.Add(members[0]!.DeepClone()); break;
            case "unexpected":
                JsonNode extra = members[0]!.DeepClone();
                extra["name"] = "ankus_storage_other_global_size";
                members.Add(extra);
                break;
            case "missing-expression": members[0]!.AsObject().Remove("inner"); break;
            case "extra-expression": members[0]!["inner"]!.AsArray().Add(members[0]!["inner"]![0]!.DeepClone()); break;
            case "missing-value": members[0]!["inner"]![0]!.AsObject().Remove("value"); break;
            case "numeric-value": members[0]!["inner"]![0]!["value"] = 4; break;
            case "null-value": members[0]!["inner"]![0]!["value"] = null; break;
            case "null-name": members[0]!["name"] = null; break;
            case "wrong-kind": members[0]!["kind"] = "VarDecl"; break;
            case "wrong-target": root["inner"]![0]!["inner"]![0]!["inner"]![0]!["value"] = "180005"; break;
            case "invalid-sentinel": members[0]!["inner"]![0]!["value"] = "-2"; break;
            case "overflow": members[0]!["inner"]![0]!["value"] = "18446744073709551616"; break;
            case "unexpected-stride": members[2]!["inner"]![0]!["value"] = "1"; break;
            case "wrong-signedness": members[3]!["inner"]![0]!["value"] = "0"; break;
            default: Assert.Fail("Unknown invalid compiler storage observation."); break;
        }

        using JsonDocument document = JsonDocument.Parse(root.ToJsonString());
        Assert.ThrowsExactly<FormatException>(() => NativeBindingStorageProbe.Read(s_catalog,
            NativeBindingStorageProbe.ReadObservations(s_catalog, document.RootElement)));
    }
}
