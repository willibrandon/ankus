using System.Runtime.InteropServices;

namespace Ankus.Build.Tests;

/// <summary>
/// Verifies anonymous member projection, exposed type identity and invalid graph boundaries.
/// </summary>
[TestClass]
public sealed class NativeBindingRecordMemberTests
{
    /// <summary>
    /// Nested promotion preserves order, offsets, unnamed bitfields and inherited qualification without changing the graph.
    /// </summary>
    [TestMethod]
    public void NativeRecordMembersPreserveNestedStorage()
    {
        NativeRecordGraph graph = CreateGraph();
        var projection = new NativeBindingRecordMembers(graph);
        IReadOnlyList<NativeBindingRecordMember> members = projection.Get(0);
        Assert.AreSequenceEqual(["marker", "value", "", "alternative", "last"], members.Select(static member => member.Field.Name));
        Assert.AreSequenceEqual<long>([0, 48, 64, 32, 160], members.Select(static member => member.Field.OffsetBits));
        Assert.AreSequenceEqual([NativeHeaderQualifiers.None, NativeHeaderQualifiers.Const | NativeHeaderQualifiers.Volatile,
            NativeHeaderQualifiers.Const | NativeHeaderQualifiers.Volatile, NativeHeaderQualifiers.Const, NativeHeaderQualifiers.None],
            members.Select(static member => member.ParentQualifiers));
        Assert.AreEqual(5, members[1].Field.BitWidth);
        Assert.AreEqual(0, members[2].Field.BitWidth);
        Assert.IsFalse(projection.IsInternalDeclaration(0));
        Assert.IsTrue(projection.IsInternalDeclaration(1));
        Assert.IsTrue(projection.IsInternalType(2));
        Assert.IsTrue(projection.IsInternalDeclaration(2));
        Assert.AreEqual(16L, graph.Declarations[2].Fields[0].OffsetBits);
        Assert.IsTrue(graph.Declarations[0].Fields[1].IsAnonymous);
        Assert.AreSequenceEqual(members, projection.Get(0));
        Assert.AreSequenceEqual<long>([16, 32], projection.Get(2).Select(static member => member.Field.OffsetBits));
    }

    /// <summary>
    /// Independently addressable native values retain their allocation and declaration contracts.
    /// </summary>
    /// <param name="exposure">The native operation exposing an otherwise anonymous container.</param>
    [TestMethod]
    [DataRow("name")]
    [DataRow("root")]
    [DataRow("field")]
    [DataRow("alias")]
    [DataRow("pointer")]
    [DataRow("array")]
    [DataRow("vector")]
    [DataRow("atomic")]
    [DataRow("complex")]
    [DataRow("result")]
    [DataRow("parameter")]
    public void NativeRecordMembersRetainExposedTypes(string exposure)
    {
        NativeRecordGraph graph = CreateGraph();
        NativeRecordDeclaration[] declarations = [.. graph.Declarations];
        NativeRecordType extra = graph.Types[2] with { Kind = exposure, Canonical = 4, Declaration = null, Element = 2 };
        switch (exposure)
        {
            case "name":
                declarations[1] = declarations[1] with { Name = "Named" };
                break;
            case "root":
                graph = graph with { Roots = new Dictionary<string, int> { ["parent"] = 1, ["exposed"] = 2 } };
                break;
            case "field":
                declarations[0] = declarations[0] with { Fields = [.. declarations[0].Fields, new("exposed", 2, 192, null, false, "")] };
                break;
            case "alias":
                graph = graph with { Types = [.. graph.Types, extra with { Canonical = 2, Name = "Alias" }] };
                break;
            case "result":
            case "parameter":
                graph = graph with
                {
                    Types = [.. graph.Types, extra with { Kind = "function", Element = null,
                    Function = new(exposure == "result" ? 2 : 0, exposure == "parameter" ? [2] : [], false, true, 1) }]
                };
                break;
            default:
                graph = graph with { Types = [.. graph.Types, extra] };
                break;
        }

        graph = graph with { Declarations = declarations };
        var projection = new NativeBindingRecordMembers(graph);
        Assert.IsFalse(projection.IsInternalDeclaration(1));
        Assert.IsFalse(projection.IsInternalType(2));
        Assert.IsTrue(projection.IsInternalDeclaration(2));
        Assert.AreEqual("value", projection.Get(0)[1].Field.Name);
    }

    /// <summary>
    /// An empty or ordinary record projects directly without inventing a container or changing its members.
    /// </summary>
    [TestMethod]
    public void NativeRecordMembersPreserveEmptyAndOrdinaryRecords()
    {
        NativeRecordGraph graph = CreateGraph();
        NativeRecordDeclaration declaration = graph.Declarations[0] with { Fields = [] };
        var empty = new NativeBindingRecordMembers(graph with { Declarations = [declaration] });
        Assert.IsEmpty(empty.Get(0));
        Assert.IsFalse(empty.IsInternalDeclaration(0));
        NativeRecordField field = graph.Declarations[0].Fields[0];
        var ordinary = new NativeBindingRecordMembers(graph with { Declarations = [declaration with { Fields = [field] }] });
        Assert.AreSequenceEqual([new NativeBindingRecordMember(field, NativeHeaderQualifiers.None)], ordinary.Get(0));
    }

    /// <summary>
    /// Cycles, nonrecord containers and ambiguous promoted names fail explicitly and leave valid projection usable.
    /// </summary>
    /// <param name="mutation">The malformed native graph.</param>
    /// <param name="reason">The expected diagnostic.</param>
    [TestMethod]
    [DataRow("cycle", "cycle")]
    [DataRow("nonrecord", "record declaration")]
    [DataRow("duplicate", "ambiguous")]
    public void NativeRecordMembersRejectInvalidPromotion(string mutation, string reason)
    {
        NativeRecordGraph graph = CreateGraph();
        NativeRecordDeclaration[] declarations = [.. graph.Declarations];
        declarations[2] = declarations[2] with
        {
            Fields = mutation == "duplicate" ? [new("alternative", 0, 0, null, false, "")]
                : [new("", mutation == "cycle" ? 1 : 0, 0, null, true, "")]
        };
        var invalid = new NativeBindingRecordMembers(graph with { Declarations = declarations });
        FormatException failure = Assert.Throws<FormatException>(() => invalid.Get(0));
        Assert.Contains(reason, failure.Message);
        Assert.AreSequenceEqual(["marker", "value", "", "alternative", "last"],
            new NativeBindingRecordMembers(graph).Get(0).Select(static member => member.Field.Name));
    }

    /// <summary>
    /// Cumulative native offsets cannot silently wrap into another object's storage.
    /// </summary>
    [TestMethod]
    public void NativeRecordMembersRejectOffsetOverflow()
    {
        NativeRecordGraph graph = CreateGraph();
        NativeRecordDeclaration parent = graph.Declarations[0] with
        {
            Fields = [graph.Declarations[0].Fields[1] with { OffsetBits = long.MaxValue }]
        };
        var invalid = new NativeBindingRecordMembers(graph with { Declarations = [parent, .. graph.Declarations.Skip(1)] });
        Assert.Throws<OverflowException>(() => invalid.Get(0));
        Assert.AreEqual(48L, new NativeBindingRecordMembers(graph).Get(0)[1].Field.OffsetBits);
    }

    /// <summary>
    /// The nesting boundary accepts its exact limit and rejects the immediately deeper graph.
    /// </summary>
    /// <param name="depth">The number of containing records.</param>
    [TestMethod]
    [DataRow(127)]
    [DataRow(128)]
    [DataRow(129)]
    public void NativeRecordMembersEnforceNestingBoundary(int depth)
    {
        NativeRecordGraph original = CreateGraph();
        NativeRecordType[] types = [.. Enumerable.Range(0, depth).Select(index =>
            original.Types[1] with { Canonical = index, Declaration = index }), original.Types[0] with { Canonical = depth }];
        NativeRecordDeclaration[] declarations = [.. Enumerable.Range(0, depth).Select(index =>
            original.Declarations[0] with { Name = index == 0 ? "Parent" : "", Fields = index + 1 == depth
                ? [new("value", depth, 0, null, false, "")] : [new("", index + 1, 0, null, true, "")] })];
        var projection = new NativeBindingRecordMembers(original with
        {
            Types = types,
            Declarations = declarations,
            Roots = new Dictionary<string, int> { ["parent"] = 0 }
        });
        if (depth > 128)
        {
            FormatException failure = Assert.Throws<FormatException>(() => projection.Get(0));
            Assert.Contains("nesting limit", failure.Message);
        }
        else
        {
            Assert.AreSequenceEqual([new NativeBindingRecordMember(new("value", depth, 0, null, false, ""), NativeHeaderQualifiers.None)], projection.Get(0));
        }
    }

    /// <summary>
    /// Supplies two nested anonymous records with distinct offsets and qualification.
    /// </summary>
    private static NativeRecordGraph CreateGraph()
    {
        var target = new NativeHeaderTarget(180006, RuntimeInformation.RuntimeIdentifier, IntPtr.Size, BitConverter.IsLittleEndian, 21, NativeNumericModelFixture.Binary80);
        NativeRecordType[] types =
        [
            new("scalar", 0, "int", 0, 4, 4, "int", null, null, null, null, null),
            new("record", 1, "struct Parent", 0, 24, 4, "", null, 0, null, null, null),
            new("record", 2, "union", NativeHeaderQualifiers.Const, 16, 4, "", null, 1, null, null, null),
            new("record", 3, "struct", NativeHeaderQualifiers.Volatile, 8, 4, "", null, 2, null, null, null),
        ];
        NativeRecordDeclaration[] declarations =
        [
            new("struct", "Parent", true, 24, 4, [new("marker", 0, 0, null, false, ""),
                new("", 2, 32, null, true, ""), new("last", 0, 160, null, false, "")], null, []),
            new("union", "", true, 16, 4, [new("", 3, 0, null, true, ""), new("alternative", 0, 0, null, false, "")], null, []),
            new("struct", "", true, 8, 4, [new("value", 0, 16, 5, false, ""), new("", 0, 32, 0, false, "")], null, []),
        ];
        return new(target, new Dictionary<string, int> { ["parent"] = 1 }, types, declarations);
    }
}
