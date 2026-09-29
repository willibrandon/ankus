using System.Text.Json.Nodes;

namespace Ankus.Build.Tests;

public sealed partial class NativeBindingHeaderParserTests
{
    /// <summary>
    /// Every redeclaration identifies the complete tag, independently of AST traversal order or unrelated same-name tags.
    /// </summary>
    /// <param name="tag">The native tag family.</param>
    /// <param name="reference">The compiler declaration used by the type reference.</param>
    [TestMethod]
    [DataRow("struct", "forward")]
    [DataRow("struct", "definition")]
    [DataRow("struct", "after")]
    [DataRow("union", "forward")]
    [DataRow("union", "definition")]
    [DataRow("union", "after")]
    [DataRow("enum", "forward")]
    [DataRow("enum", "definition")]
    [DataRow("enum", "after")]
    public void TagRedeclarationsResolveCompleteDefinitions(string tag, string reference)
    {
        JsonNode root = TagAst(tag, reference);
        NativeHeaderType expected = tag == "enum" ? new NativeHeaderEnum("Entry", true)
            : new NativeHeaderRecord("Entry", tag == "union", true);
        Assert.AreEqual(expected, ReadTag(root));
        JsonArray nodes = root["inner"]!.AsArray();
        root["inner"] = new JsonArray([.. nodes.Reverse().Select(static node => node!.DeepClone())]);
        Assert.AreEqual(expected, ReadTag(root));
        root = TagAst(tag, "unrelated");
        NativeHeaderType opaque = tag == "enum" ? new NativeHeaderEnum("Entry", false)
            : new NativeHeaderRecord("Entry", tag == "union", false);
        Assert.AreEqual(opaque, ReadTag(root));
    }

    /// <summary>
    /// Broken declaration links cannot change tag identity or invent a successful definition.
    /// </summary>
    /// <param name="change">The corrupted compiler declaration relationship.</param>
    [TestMethod]
    [DataRow("missing")]
    [DataRow("cycle")]
    [DataRow("name")]
    [DataRow("kind")]
    [DataRow("tag")]
    [DataRow("duplicate")]
    public void InvalidTagRedeclarationsAreRejected(string change)
    {
        JsonNode root = TagAst("struct", "forward");
        JsonArray nodes = root["inner"]!.AsArray();
        JsonNode forward = nodes[2]!;
        JsonNode definition = nodes[3]!;
        switch (change)
        {
            case "missing":
                definition["previousDecl"] = "missing";
                break;
            case "cycle":
                forward["previousDecl"] = "after";
                break;
            case "name":
                definition["name"] = "Different";
                break;
            case "kind":
                definition["kind"] = "EnumDecl";
                break;
            case "tag":
                definition["tagUsed"] = "union";
                break;
            case "duplicate":
                forward["completeDefinition"] = true;
                break;
            default:
                Assert.Fail("Unknown corruption.");
                break;
        }

        FormatException error = Assert.ThrowsExactly<FormatException>(() => ReadTag(root));
        Assert.Contains("tag", error.Message);
    }

    /// <summary>
    /// Fixed enum representations remain complete even without an enumerator body.
    /// </summary>
    [TestMethod]
    public void FixedEnumRedeclarationsRetainKnownCompleteness()
    {
        JsonNode root = TagAst("enum", "forward");
        JsonNode definition = root["inner"]![3]!;
        definition.AsObject().Remove("inner");
        definition["fixedUnderlyingType"] = new JsonObject { ["qualType"] = "int" };
        Assert.AreEqual(new NativeHeaderEnum("Entry", true), ReadTag(root));
        definition.AsObject().Remove("fixedUnderlyingType");
        Assert.AreEqual(new NativeHeaderEnum("Entry", false), ReadTag(root));
    }

    /// <summary>
    /// Creates a compiler tree with a forward declaration, definition, trailing declaration and unrelated opaque tag.
    /// </summary>
    private static JsonNode TagAst(string tag, string reference)
    {
        JsonNode root = JsonNode.Parse(Ast)!;
        JsonArray nodes = root["inner"]!.AsArray();
        string kind = tag == "enum" ? "EnumDecl" : "RecordDecl";
        string[] identities = ["forward", "definition", "after", "unrelated"];
        foreach (string id in identities)
        {
            var declaration = new JsonObject { ["kind"] = kind, ["id"] = id, ["name"] = "Entry" };
            if (tag != "enum")
            {
                declaration["tagUsed"] = tag;
            }

            if (id is "definition" or "after")
            {
                declaration["previousDecl"] = id == "definition" ? "forward" : "definition";
            }

            if (id == "definition")
            {
                if (tag == "enum")
                {
                    declaration["inner"] = new JsonArray(new JsonObject { ["kind"] = "EnumConstantDecl", ["name"] = "One" });
                }
                else
                {
                    declaration["completeDefinition"] = true;
                }
            }

            nodes.Add(declaration);
        }

        nodes[1]!["inner"]![0]!["inner"]![1]!["inner"]![1]!["inner"]![0]!["inner"]![0] = new JsonObject
        {
            ["kind"] = tag == "enum" ? "EnumType" : "RecordType",
            ["decl"] = new JsonObject { ["id"] = reference, ["kind"] = kind, ["name"] = "Entry" },
        };
        return root;
    }

    /// <summary>
    /// Reads the observed pointed-to tag through the production declaration parser.
    /// </summary>
    private static NativeHeaderType ReadTag(JsonNode root)
    {
        NativeHeaderFunction function = Assert.IsInstanceOfType<NativeHeaderFunction>(
            NativeBindingHeaderParser.Read(root.ToJsonString(), [new("call", "native_call", true)])["call"].Type);
        NativeHeaderPointer address = Assert.IsInstanceOfType<NativeHeaderPointer>(Assert.ContainsSingle(function.Parameters));
        return Assert.IsInstanceOfType<NativeHeaderQualified>(address.Element).Underlying;
    }
}
