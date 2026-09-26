using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ankus.Build.Tests;

/// <summary>
/// Verifies complete declaration identity, metadata and strict compiler-tree validation.
/// </summary>
[TestClass]
public sealed class NativeBindingHeaderParserTests
{
    private const string Ast = """
        { "kind": "TranslationUnitDecl", "inner": [
          { "kind": "FunctionDecl", "id": "fn", "name": "native_call", "mangledName": "native_export", "storageClass": "extern", "inner": [
            { "kind": "ParmVarDecl", "name": "input" }, { "kind": "C11NoReturnAttr" }, { "kind": "ColdAttr" },
            { "kind": "CompoundStmt", "inner": [{}] }
          ] },
          { "kind": "TypedefDecl", "name": "ankus_header_type_call", "inner": [
            { "kind": "TypeOfExprType", "inner": [
              { "kind": "ParenExpr", "inner": [ { "kind": "DeclRefExpr", "referencedDecl": { "id": "fn", "kind": "FunctionDecl", "name": "native_call" } } ] },
              { "kind": "FunctionProtoType", "cc": "cdecl", "variadic": true, "inner": [
                { "kind": "BuiltinType", "type": { "qualType": "void" } },
                { "kind": "PointerType", "inner": [
                  { "kind": "QualType", "qualifiers": "const volatile", "inner": [
                    { "kind": "BuiltinType", "type": { "qualType": "int" } }
                  ] }
                ] }
              ] }
            ] }
          ] }
        ] }
        """;

    /// <summary>
    /// Native declaration metadata comes from the referenced header declaration rather than a guessed Rust signature.
    /// </summary>
    [TestMethod]
    public void DeclarationMetadataAndQualifiedParametersRemainExact()
    {
        IReadOnlyDictionary<string, NativeHeaderSymbol> symbols = NativeBindingHeaderParser.Read(Ast, [new("call", "native_call", true)]);
        NativeHeaderSymbol symbol = symbols["call"];
        Assert.AreEqual("native_call", symbol.NativeName);
        Assert.AreEqual("native_export", symbol.LinkageName);
        Assert.AreEqual("extern", symbol.StorageClass);
        Assert.IsTrue(symbol.IsFunction);
        Assert.IsFalse(symbol.IsThreadLocal);
        Assert.IsTrue(symbol.DoesNotReturn);
        Assert.AreSequenceEqual<string>(["input"], symbol.ParameterNames);
        Assert.AreSequenceEqual<string>(["C11NoReturnAttr", "ColdAttr"], symbol.Attributes);
        NativeHeaderFunction function = Assert.IsInstanceOfType<NativeHeaderFunction>(symbol.Type);
        Assert.AreEqual("void invoke(const volatile int *ankus_arg0, ...)", function.Declare("invoke"));
        Assert.IsTrue(function.HasPrototype);
        Assert.IsTrue(function.IsVariadic);
        Assert.IsFalse(function.DoesNotReturn);
        string reordered = "{\"kind\":\"TranslationUnitDecl\",\"inner\":[" +
            JsonNode.Parse(Ast)!["inner"]![1] + "," + JsonNode.Parse(Ast)!["inner"]![0] + "]}";
        Assert.AreEqual(JsonSerializer.Serialize(symbols), JsonSerializer.Serialize(
            NativeBindingHeaderParser.Read(reordered, [new("call", "native_call", true)])));
    }

    /// <summary>
    /// Invalid selections fail before source text can be emitted.
    /// </summary>
    [TestMethod]
    public void SelectionAndGeneratedAliasesAreExact()
    {
        NativeHeaderRequest first = new("first", "native_first", true);
        NativeHeaderRequest second = new("second", "native_second", false);
        Assert.AreEqual(NativeBindingHeaderParser.GenerateSource("header", [first, second]),
            NativeBindingHeaderParser.GenerateSource("header", [second, first]));
        Assert.ThrowsExactly<FormatException>(() => NativeBindingHeaderParser.GenerateSource("", [first, first]));
        Assert.ThrowsExactly<FormatException>(() => NativeBindingHeaderParser.GenerateSource("", [new("bad;", "first", true)]));
        Assert.ThrowsExactly<FormatException>(() => NativeBindingHeaderParser.GenerateSource("", [new("first", "bad;", true)]));
        Assert.ThrowsExactly<ArgumentNullException>(() => NativeBindingHeaderParser.GenerateSource("", null!));
        Assert.IsEmpty(NativeBindingHeaderParser.Read("{\"kind\":\"TranslationUnitDecl\"}", []));
        Assert.ThrowsExactly<FormatException>(() => NativeBindingHeaderParser.Read(Ast, []));
        Assert.ThrowsExactly<FormatException>(() => NativeBindingHeaderParser.Read(Ast, [new("absent", "native_call", true)]));
        Assert.ThrowsExactly<FormatException>(() => NativeBindingHeaderParser.Read(Ast, [new("call", "native_call", false)]));
    }

    /// <summary>
    /// Malformed, incomplete and unsupported type trees cannot become a successful native contract.
    /// </summary>
    /// <param name="change">The independent invalid compiler observation.</param>
    [TestMethod]
    [DataRow("root")]
    [DataRow("missing")]
    [DataRow("duplicate-alias")]
    [DataRow("duplicate-declaration")]
    [DataRow("reference-name")]
    [DataRow("reference-id")]
    [DataRow("reference-kind")]
    [DataRow("alias-kind")]
    [DataRow("expression")]
    [DataRow("missing-type")]
    [DataRow("extra-type")]
    [DataRow("parameter-count")]
    [DataRow("calling-convention")]
    [DataRow("register-parameters")]
    [DataRow("variadic-flag")]
    [DataRow("unknown-type")]
    [DataRow("unknown-scalar")]
    [DataRow("empty-qualifiers")]
    [DataRow("duplicate-qualifier")]
    [DataRow("unknown-qualifier")]
    [DataRow("pointer-children")]
    [DataRow("unprototyped-parameters")]
    public void InvalidCompilerObservationsFailExplicitly(string change)
    {
        JsonNode root = JsonNode.Parse(Ast)!;
        JsonArray nodes = root["inner"]!.AsArray();
        JsonNode alias = nodes[1]!;
        JsonArray parts = alias["inner"]![0]!["inner"]!.AsArray();
        JsonNode function = parts[1]!;
        JsonNode argument = function["inner"]![1]!;
        switch (change)
        {
            case "root": root["kind"] = "FunctionDecl"; break;
            case "missing": nodes.RemoveAt(1); break;
            case "duplicate-alias": nodes.Add(alias.DeepClone()); break;
            case "duplicate-declaration": nodes.Add(nodes[0]!.DeepClone()); break;
            case "reference-name": parts[0]!["inner"]![0]!["referencedDecl"]!["name"] = "another"; break;
            case "reference-id": parts[0]!["inner"]![0]!["referencedDecl"]!["id"] = "absent"; break;
            case "reference-kind": parts[0]!["inner"]![0]!["referencedDecl"]!["kind"] = "VarDecl"; break;
            case "alias-kind": alias["inner"]![0]!["kind"] = "PointerType"; break;
            case "expression": parts[0]!["kind"] = "CallExpr"; break;
            case "missing-type": parts.RemoveAt(1); break;
            case "extra-type": parts.Add(function.DeepClone()); break;
            case "parameter-count": nodes[0]!["inner"]!.AsArray().RemoveAt(0); break;
            case "calling-convention": function["cc"] = "stdcall"; break;
            case "register-parameters": function["regParm"] = 2; break;
            case "variadic-flag": function["variadic"] = "true"; break;
            case "unknown-type": argument["kind"] = "AtomicType"; break;
            case "unknown-scalar": argument["inner"]![0]!["inner"]![0]!["type"]!["qualType"] = "invented"; break;
            case "empty-qualifiers": argument["inner"]![0]!["qualifiers"] = ""; break;
            case "duplicate-qualifier": argument["inner"]![0]!["qualifiers"] = "const const"; break;
            case "unknown-qualifier": argument["inner"]![0]!["qualifiers"] = "atomic"; break;
            case "pointer-children": argument["inner"]!.AsArray().Clear(); break;
            case "unprototyped-parameters": function["kind"] = "FunctionNoProtoType"; break;
            default: Assert.Fail("Unknown invalid observation."); break;
        }

        Assert.ThrowsExactly<FormatException>(() => NativeBindingHeaderParser.Read(root.ToJsonString(), [new("call", "native_call", true)]));
    }

    /// <summary>
    /// Accepts the deepest supported parameter type and rejects its immediate successor.
    /// </summary>
    /// <param name="levels">The number of pointer levels above a scalar parameter.</param>
    /// <param name="accepted">Whether the complete type is within the nesting bound.</param>
    [TestMethod]
    [DataRow(126, true)]
    [DataRow(127, false)]
    public void NestingBoundaryIsExplicit(int levels, bool accepted)
    {
        JsonNode root = JsonNode.Parse(Ast)!;
        JsonNode element = JsonNode.Parse("{\"kind\":\"BuiltinType\",\"type\":{\"qualType\":\"int\"}}")!;
        for (int index = 0; index < levels; index++)
        {
            element = new JsonObject { ["kind"] = "PointerType", ["inner"] = new JsonArray(element) };
        }

        root["inner"]![1]!["inner"]![0]!["inner"]![1]!["inner"]![1] = element;
        string json = root.ToJsonString(new JsonSerializerOptions { MaxDepth = 512 });
        if (accepted)
        {
            NativeHeaderFunction function = Assert.IsInstanceOfType<NativeHeaderFunction>(NativeBindingHeaderParser.Read(json, [new("call", "native_call", true)])["call"].Type);
            Assert.AreEqual("void invoke(int " + new string('*', levels) + "ankus_arg0, ...)", function.Declare("invoke"));
        }
        else
        {
            Assert.ThrowsExactly<FormatException>(() => NativeBindingHeaderParser.Read(json, [new("call", "native_call", true)]));
        }
    }
}
