using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Ankus.Generators.Tests;

/// <summary>
/// Verifies detached recursive and polymorphic storage contracts independently of compiler object identity.
/// </summary>
public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Recursive contracts remain finite and equal after body edits or movement while generated calls execute current constructors.
    /// </summary>
    /// <param name="move">Whether to move the declaration instead of changing its constructor body.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void DetachedSerializationModelsPreserveRecursiveContractsAcrossIndependentEdits(bool move)
    {
        const string Source = """
            [Ankus.PgType]
            public sealed class Value
            {
                public int Number { get; }
                public Value? Next
                {
                    get;
                    init;
                }

                [System.Text.Json.Serialization.JsonConstructor]
                public Value(int number)
                {
                    Number = number + 1;
                }
            }
            """;
        CSharpCompilation original = ModuleCompilation(Source);
        string changed = move ? "\n\n" + Source : Source.Replace("number + 1", "number + 2", StringComparison.Ordinal);
        CSharpCompilation edited = original.ReplaceSyntaxTree(original.SyntaxTrees.Single(),
            CSharpSyntaxTree.ParseText(changed, path: "Changed.cs", cancellationToken: context.CancellationToken));
        SerializationModel first = SerializationContract(original);
        SerializationModel second = SerializationContract(edited);
        Assert.AreNotSame(original.GetTypeByMetadataName("Value"), edited.GetTypeByMetadataName("Value"));
        Assert.AreEqual(first, second);
        Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
        Assert.HasCount(3, first.Nodes);
        SerializationModel.Node root = first.Nodes[0];
        Assert.IsFalse(root.CanBeNull);
        Assert.AreSequenceEqual(["Number"], root.ConstructorMembers.Select(index => root.Members[index].Name));
        int childIndex = root.Members.Single(static member => member.Name == "Next").Value;
        SerializationModel.Node child = first.Nodes[childIndex];
        Assert.IsTrue(child.CanBeNull);
        Assert.AreEqual(childIndex, child.Members.Single(static member => member.Name == "Next").Value);
        var previous = new StringBuilder();
        var current = new StringBuilder();
        first.Emit("Codec", previous);
        second.Emit("Codec", current);
        Assert.AreEqual(previous.ToString(), current.ToString());
        string[] actual = RunSerializedProbe<string[]>(changed, """
            return new[] { codec.Parse("{\"Number\":41,\"Next\":null}").Number.ToString(System.Globalization.CultureInfo.InvariantCulture) };
            """);
        Assert.AreSequenceEqual([move ? "42" : "43"], actual);
    }

    /// <summary>
    /// Discriminator type and value remain distinct comparable metadata and change actual encoded variant selection.
    /// </summary>
    /// <param name="text">Whether to change an integer discriminator into equal-looking text rather than another integer.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void DetachedSerializationModelsPreserveExactDiscriminatorIdentity(bool text)
    {
        const string Source = """
            [Ankus.PgType]
            [System.Text.Json.Serialization.JsonDerivedType(typeof(Variant), 42)]
            public abstract record Value;
            public sealed record Variant(int Number) : Value;
            """;
        string changed = Source.Replace("Variant), 42", text ? "Variant), \"42\"" : "Variant), 43", StringComparison.Ordinal);
        SerializationModel first = SerializationContract(ModuleCompilation(Source));
        SerializationModel second = SerializationContract(ModuleCompilation(changed));
        SerializationModel.Variant original = Assert.ContainsSingle(first.Nodes[0].Variants);
        SerializationModel.Variant edited = Assert.ContainsSingle(second.Nodes[0].Variants);
        Assert.AreEqual(42, original.Number);
        Assert.IsNull(original.Text);
        Assert.AreEqual(original.Shape, edited.Shape);
        Assert.AreEqual(text ? "42" : null, edited.Text);
        Assert.AreEqual(text ? null : 43, edited.Number);
        Assert.AreNotEqual(first, second);
        string[] actual = RunSerializedProbe<string[]>(changed, """
            return new[] { codec.Format(new Variant(7)) };
            """);
        Assert.AreSequenceEqual([text ? "{\"$type\":\"42\",\"Number\":7}" : "{\"$type\":43,\"Number\":7}"], actual);
    }

    /// <summary>
    /// Analyzes the attributed root while keeping compiler objects outside the returned storage model.
    /// </summary>
    /// <param name="compilation">The independently created semantic compilation.</param>
    /// <returns>The validated closed serialization graph.</returns>
    private static SerializationModel SerializationContract(CSharpCompilation compilation)
    {
        INamedTypeSymbol root = compilation.GetTypeByMetadataName("Value")!;
        SerializationModel? model = DefaultTypeSerializer.Create(root, out string? error);
        Assert.IsNull(error);
        Assert.IsNotNull(model);
        return model;
    }
}
