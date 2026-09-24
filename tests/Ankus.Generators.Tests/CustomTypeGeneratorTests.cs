using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Ankus.Generators.Tests;

/// <summary>
/// Verifies compilable custom-type dispatch, installation ordering and invalid codec contracts.
/// </summary>
public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Declares custom types before every typed dependent contract and compiles closed codec registrations.
    /// </summary>
    /// <param name="declaration">The managed type declaration.</param>
    /// <param name="binary">Whether to emit binary send and receive functions.</param>
    [TestMethod]
    [DataRow("public readonly record struct Value(long Number);", false)]
    [DataRow("public sealed record Value(long Number);", true)]
    [DataRow("public enum Value { Zero, One }", true)]
    public void CustomTypesCompileAcrossTypedContracts(string declaration, bool binary)
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate($$"""
            [Ankus.PgSchema("types")]
            public static class Types
            {
                [Ankus.PgType(typeof(Codec), Name="value", BinaryProtocol={{binary.ToString().ToLowerInvariant()}}, Id="value-type")]
                {{declaration}}
                {{CustomCodecSource}}
                [Ankus.PgFunction] public static Value? Scalar(Value? value) => value;
                [Ankus.PgFunction] public static Ankus.PgArray<Value?> Array(Ankus.PgArray<Value?> value) => value;
                [Ankus.PgFunction] public static System.Collections.Generic.IEnumerable<Value?> Rows(Value? value) => [value];
                [Ankus.PgFunction] public static System.Collections.Generic.IEnumerable<(Value? A, Value B)> Table(Value value) => [(value,value)];
                [Ankus.PgFunction, Ankus.PgCast] public static long Convert(Value value) => 1;
                [Ankus.PgFunction, Ankus.PgOperator("===")] public static bool Equal(Value left, Value right) => true;
                [Ankus.PgAggregate] public static class First
                {
                    public static Value? Transition(Value? state, Value? value) => state ?? value;
                }
            }
            """);
        AssertAggregateCompilation(compilation, diagnostics);
        string sql = ManifestValue(compilation, "Ankus.Sql");
        Assert.Contains("CREATE TYPE \"types\".\"value\";", sql);
        Assert.Contains("INTERNALLENGTH = variable, INPUT = \"types\".\"value_in\", OUTPUT = \"types\".\"value_out\"", sql);
        Assert.Contains("RETURNS SETOF \"types\".\"value\"", sql);
        Assert.Contains("RETURNS TABLE (\"a\" \"types\".\"value\", \"b\" \"types\".\"value\")", sql);
        Assert.Contains("STYPE = \"types\".\"value\"", sql);
        Assert.Contains("CREATE CAST (\"types\".\"value\" AS bigint)", sql);
        Assert.IsLessThan(sql.IndexOf("CREATE FUNCTION \"types\".\"scalar\"", StringComparison.Ordinal),
            sql.IndexOf("STORAGE = extended", StringComparison.Ordinal));
        Assert.AreEqual(binary, sql.Contains("RECEIVE =", StringComparison.Ordinal));
        Assert.AreEqual("false", ManifestValue(compilation, "Ankus.Relocatable"));
    }

    /// <summary>
    /// Emits a working module even when the extension declares only a base type.
    /// </summary>
    [TestMethod]
    public void CustomTypeOnlyExtensionEmitsCompleteModule()
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate($$"""
            [Ankus.PgType(typeof(Codec))] public readonly record struct Value(int Number);
            {{CustomCodecSource}}
            """);
        AssertAggregateCompilation(compilation, diagnostics);
        string sql = ManifestValue(compilation, "Ankus.Sql");
        Assert.Contains("CREATE FUNCTION \"value_in\"(cstring) RETURNS \"value\"", sql);
        Assert.Contains("CREATE FUNCTION \"value_out\"(\"value\") RETURNS cstring", sql);
        Assert.AreEqual("true", ManifestValue(compilation, "Ankus.Relocatable"));
    }

    /// <summary>
    /// Rejects codecs that cannot be statically created or cannot represent the attributed type.
    /// </summary>
    /// <param name="source">The invalid type contract.</param>
    [TestMethod]
    [DataRow("[Ankus.PgType(typeof(string))] public struct Value { }")]
    [DataRow("[Ankus.PgType(typeof(Codec))] public abstract class Value { }")]
    [DataRow("[Ankus.PgType(typeof(Codec))] public ref struct Value { }")]
    [DataRow("[Ankus.PgType(typeof(Codec))] public struct Value<T> { }")]
    [DataRow("[Ankus.PgType(typeof(Codec), Name=\"\")] public struct Value { }")]
    [DataRow("[Ankus.PgType(typeof(Codec), Schema=\"\")] public struct Value { }")]
    [DataRow("[Ankus.PgType(typeof(Codec)), Ankus.PgEnum] public enum Value { A }")]
    [DataRow("[Ankus.PgType(typeof(Codec))] public struct Value { } public class Codec { private Codec() { } }")]
    public void InvalidCustomTypeContractsAreDiagnosed(string source)
    {
        (_, ImmutableArray<Diagnostic> diagnostics) = Generate(source +
            (source.Contains("class Codec", StringComparison.Ordinal) ? string.Empty : CustomCodecSource));
        Assert.Contains("ANKUS017", diagnostics.Select(static diagnostic => diagnostic.Id));
    }

    /// <summary>
    /// Keeps long type identifiers intact while generating valid helper names.
    /// </summary>
    [TestMethod]
    public void CustomTypesPreserveLongNamesAndDependencyOrder()
    {
        string name = new('n', 63);
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate($$"""
            [assembly: Ankus.PgSql("table", "CREATE TABLE data(value \"{{name}}\");", Requires=new[] {"type"}, Relocatable=true)]
            [Ankus.PgType(typeof(Codec), Name="{{name}}", Id="type", BinaryProtocol=true)] public struct Value { }
            {{CustomCodecSource}}
            """);
        AssertAggregateCompilation(compilation, diagnostics);
        string sql = ManifestValue(compilation, "Ankus.Sql");
        Assert.Contains("CREATE TYPE \"" + name + "\";", sql);
        Assert.Contains("INPUT = \"ankus_", sql);
        Assert.IsLessThan(sql.IndexOf("CREATE TABLE data", StringComparison.Ordinal), sql.IndexOf("STORAGE = extended", StringComparison.Ordinal));
    }

    /// <summary>
    /// Supplies a statically constructible codec for generated-compilation tests.
    /// </summary>
    private const string CustomCodecSource = """
        public sealed class Codec : Ankus.PgTypeCodec<Value>
        {
            public override Value Parse(string text) => default!;
            public override string Format(Value value) => "value";
            public override Value Read(System.ReadOnlySpan<byte> payload) => default!;
            public override void Write(Value value, System.Buffers.IBufferWriter<byte> destination) { }
        }
        """;

    /// <summary>
    /// A user function cannot replace generated type I/O through an identical SQL signature.
    /// </summary>
    [TestMethod]
    public void CustomTypeIoSignaturesCannotBeReplaced()
    {
        (_, ImmutableArray<Diagnostic> diagnostics) = Generate($$"""
            [Ankus.PgType(typeof(Codec))] public struct Value { }
            {{CustomCodecSource}}
            public static class Functions
            {
                [Ankus.PgFunction] public static int ValueOut(Value value) => 0;
            }
            """);
        Assert.Contains("ANKUS002", diagnostics.Select(static diagnostic => diagnostic.Id));
    }
}
