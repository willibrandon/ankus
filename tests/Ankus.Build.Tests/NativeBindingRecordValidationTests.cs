using System.Text.Json;

namespace Ankus.Build.Tests;

/// <summary>
/// Rejects partial graphs and contradictory native storage before compiler observations can become published contracts.
/// </summary>
[TestClass]
public sealed class NativeBindingRecordValidationTests
{
    /// <summary>
    /// Each changed observation violates a separate shape, identity, storage or completeness invariant.
    /// </summary>
    [TestMethod]
    [DataRow("target")]
    [DataRow("missing-root")]
    [DataRow("root-edge")]
    [DataRow("canonical-edge")]
    [DataRow("canonical-chain")]
    [DataRow("unknown-kind")]
    [DataRow("pointer-width")]
    [DataRow("alignment")]
    [DataRow("array-size")]
    [DataRow("array-count")]
    [DataRow("incomplete-array")]
    [DataRow("function-storage")]
    [DataRow("parameter-edge")]
    [DataRow("calling-convention")]
    [DataRow("no-prototype")]
    [DataRow("tag-kind")]
    [DataRow("tag-size")]
    [DataRow("field-edge")]
    [DataRow("field-offset")]
    [DataRow("field-bounds")]
    [DataRow("union-offset")]
    [DataRow("anonymous-name")]
    [DataRow("anonymous-scalar")]
    [DataRow("bit-width")]
    [DataRow("named-zero-width")]
    [DataRow("opaque-fields")]
    [DataRow("unreachable")]
    [DataRow("extra-enum-metadata")]
    public void InvalidRecordGraphsFailExplicitly(string mutation)
    {
        NativeRecordGraph valid = CreateGraph();
        NativeBindingRecordValidation.Validate(valid, valid.Target, valid.Roots.Keys);
        NativeRecordType[] types = [.. valid.Types];
        NativeRecordDeclaration[] declarations = [.. valid.Declarations];
        NativeRecordField[] fields = [.. declarations[0].Fields];
        NativeRecordGraph changed = valid with { Types = types, Declarations = declarations };
        switch (mutation)
        {
            case "target": changed = changed with { Target = valid.Target with { PostgresVersion = 170011 } }; break;
            case "missing-root": changed = changed with { Roots = new Dictionary<string, int> { ["current"] = 0 } }; break;
            case "root-edge": changed = changed with { Roots = new Dictionary<string, int> { ["current"] = -1, ["call"] = 4 } }; break;
            case "canonical-edge": types[0] = types[0] with { Canonical = 99 }; break;
            case "canonical-chain": types[0] = types[0] with { Canonical = 1 }; types[1] = types[1] with { Canonical = 2 }; break;
            case "unknown-kind": types[1] = types[1] with { Kind = "reference" }; break;
            case "pointer-width": types[1] = types[1] with { Size = 4 }; break;
            case "alignment": types[2] = types[2] with { Alignment = 3 }; break;
            case "array-size": types[3] = types[3] with { Size = 13 }; break;
            case "array-count": types[3] = types[3] with { Count = -1 }; break;
            case "incomplete-array": types[3] = types[3] with { Count = null }; break;
            case "function-storage": types[4] = types[4] with { Size = 1, Alignment = 1 }; break;
            case "parameter-edge": types[4] = types[4] with { Function = types[4].Function! with { Parameters = [99] } }; break;
            case "calling-convention": types[4] = types[4] with { Function = types[4].Function! with { CallingConvention = 100 } }; break;
            case "no-prototype": types[4] = types[4] with { Function = types[4].Function! with { HasPrototype = false } }; break;
            case "tag-kind": types[0] = types[0] with { Kind = "enum" }; break;
            case "tag-size": types[0] = types[0] with { Size = 64 }; break;
            case "field-edge": fields[1] = fields[1] with { Type = 99 }; break;
            case "field-offset": fields[1] = fields[1] with { OffsetBits = 65 }; break;
            case "field-bounds": fields[1] = fields[1] with { OffsetBits = 256 }; break;
            case "union-offset": declarations[0] = declarations[0] with { Kind = "union" }; break;
            case "anonymous-name": fields[1] = fields[1] with { IsAnonymous = true }; break;
            case "anonymous-scalar": fields[1] = fields[1] with { Name = "", IsAnonymous = true }; break;
            case "bit-width": fields[3] = fields[3] with { BitWidth = 33 }; break;
            case "named-zero-width": fields[3] = fields[3] with { BitWidth = 0 }; break;
            case "opaque-fields":
                types[0] = types[0] with { Size = null, Alignment = null };
                declarations[0] = declarations[0] with { IsComplete = false, Size = null, Alignment = null };
                break;
            case "unreachable": changed = changed with { Types = [.. types, types[2] with { Canonical = 5 }] }; break;
            case "extra-enum-metadata": declarations[0] = declarations[0] with { EnumUnderlying = 2 }; break;
            default: throw new ArgumentOutOfRangeException(nameof(mutation));
        }

        declarations[0] = declarations[0] with { Fields = fields };
        Assert.ThrowsExactly<FormatException>(() => NativeBindingRecordValidation.Validate(changed, valid.Target, valid.Roots.Keys));
    }

    /// <summary>
    /// Enum transport retains exact decimal spelling and rejects overflow, duplicate identities and missing representations.
    /// </summary>
    [TestMethod]
    [DataRow("18446744073709551616")]
    [DataRow("-9223372036854775809")]
    [DataRow("01")]
    [DataRow("+1")]
    [DataRow("1.0")]
    [DataRow("duplicate")]
    [DataRow("missing-representation")]
    public void InvalidRecordEnumConstantsFailExplicitly(string value)
    {
        var target = new NativeHeaderTarget(180006, "linux-x64", 8, true, 21, NativeNumericModelFixture.Binary80);
        NativeRecordType[] types =
        [
            new("enum", 0, "enum Limit", 0, 8, 8, "", null, 0, null, null, null),
            new("scalar", 1, "unsigned long long", 0, 8, 8, "unsigned long long", null, null, null, null, null),
        ];
        NativeRecordConstant[] constants = [new("Last", "18446744073709551615")];
        var declaration = new NativeRecordDeclaration("enum", "Limit", true, 8, 8, [], 1, constants);
        var graph = new NativeRecordGraph(target, new Dictionary<string, int> { ["current"] = 0 }, types, [declaration]);
        NativeBindingRecordValidation.Validate(graph, target, ["current"]);
        NativeRecordDeclaration changed = value switch
        {
            "duplicate" => declaration with { EnumValues = [constants[0], constants[0]] },
            "missing-representation" => declaration with { EnumUnderlying = null },
            _ => declaration with { EnumValues = [new("Last", value)] },
        };
        Assert.ThrowsExactly<FormatException>(() => NativeBindingRecordValidation.Validate(graph with { Declarations = [changed] }, target, ["current"]));
    }

    /// <summary>
    /// A protocol cannot silently accept missing required fields or unrelated properties.
    /// </summary>
    [TestMethod]
    [DataRow("{}")]
    [DataRow("{\"Target\":null,\"Roots\":{},\"Types\":[],\"Declarations\":[],\"Extra\":1}")]
    public void NativeRecordProtocolRejectsIncompleteJson(string json)
    {
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<NativeRecordGraph>(json, NativeBindingRecordWorker.JsonOptions));
    }

    /// <summary>
    /// Unevaluated typeof roots must retain their resolved representation and cannot invent a second canonical identity.
    /// </summary>
    [TestMethod]
    [DataRow("missing-element")]
    [DataRow("wrong-element")]
    [DataRow("self")]
    [DataRow("size")]
    [DataRow("alignment")]
    public void TypeofRecordContractsRejectContradictions(string mutation)
    {
        NativeRecordGraph original = CreateGraph();
        NativeRecordType wrapper = new("typeof", 0, "typeof (*((struct Root*)0))", 0, 32, 8, "", 0, null, null, null, null);
        var roots = new Dictionary<string, int>(original.Roots, StringComparer.Ordinal) { ["expression"] = original.Types.Count };
        NativeRecordGraph valid = original with { Types = [.. original.Types, wrapper], Roots = roots };
        NativeBindingRecordValidation.Validate(valid, valid.Target, roots.Keys);
        NativeRecordType changed = mutation switch
        {
            "missing-element" => wrapper with { Element = null },
            "wrong-element" => wrapper with { Element = 2 },
            "self" => wrapper with { Canonical = original.Types.Count, Element = original.Types.Count },
            "size" => wrapper with { Size = 16 },
            "alignment" => wrapper with { Alignment = 4 },
            _ => throw new ArgumentOutOfRangeException(nameof(mutation)),
        };
        Assert.ThrowsExactly<FormatException>(() => NativeBindingRecordValidation.Validate(
            valid with { Types = [.. original.Types, changed] }, valid.Target, roots.Keys));
        NativeBindingRecordValidation.Validate(valid, valid.Target, roots.Keys);
    }

    private static NativeRecordGraph CreateGraph()
    {
        var target = new NativeHeaderTarget(180006, "linux-x64", 8, true, 21, NativeNumericModelFixture.Binary80);
        NativeRecordType[] types =
        [
            new("record", 0, "struct Root", 0, 32, 8, "", null, 0, null, null, null),
            new("pointer", 1, "struct Root *", 0, 8, 8, "", 0, null, null, null, null),
            new("scalar", 2, "unsigned int", 0, 4, 4, "unsigned int", null, null, null, null, null),
            new("array", 3, "unsigned int[3]", 0, 12, 4, "", 2, null, 3, null, null),
            new("function", 4, "unsigned int(struct Root *)", 0, null, null, "", null, null, null, new(2, [1], false, true, 1), null),
        ];
        NativeRecordField[] fields =
        [
            new("next", 1, 0, null, false, "struct Root *next"),
            new("value", 2, 64, null, false, "unsigned int value"),
            new("values", 3, 96, null, false, "unsigned int values[3]"),
            new("flags", 2, 192, 3, false, "unsigned int flags : 3"),
        ];
        return new(target, new Dictionary<string, int> { ["current"] = 0, ["call"] = 4 }, types,
            [new("struct", "Root", true, 32, 8, fields, null, [])]);
    }
}
