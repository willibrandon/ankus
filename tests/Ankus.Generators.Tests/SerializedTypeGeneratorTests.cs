using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.CodeAnalysis;

namespace Ankus.Generators.Tests;

/// <summary>
/// Verifies executable generated JSON and CBOR contracts without runtime contract discovery.
/// </summary>
public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Emits complete default codecs for supported immutable and mutable C# declarations.
    /// </summary>
    /// <param name="declaration">The declaration carrying the default mapping.</param>
    [TestMethod]
    [DataRow("public readonly record struct Value(long Number, string? Text);")]
    [DataRow("public sealed record Value(long Number, string? Text);")]
    [DataRow("public sealed class Value { public long Number { get; init; } public string? Text { get; set; } }")]
    [DataRow("public struct Value { public long Number; public string? Text { get; set; } }")]
    [DataRow("public enum Value { Zero, One }")]
    public void DefaultSerializedTypesCompileAcrossObjectShapes(string declaration)
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate($$"""
            [Ankus.PgType(BinaryProtocol=true)] {{declaration}}
            public static class Functions
            {
                [Ankus.PgFunction] public static Value? Scalar(Value? value) => value;
                [Ankus.PgFunction] public static Ankus.PgArray<Value?> Array(Ankus.PgArray<Value?> value) => value;
                [Ankus.PgFunction] public static System.Collections.Generic.IEnumerable<Value?> Rows(Value? value) => [value];
            }
            """);
        AssertAggregateCompilation(compilation, diagnostics);
        string sql = ManifestValue(compilation, "Ankus.Sql");
        Assert.Contains("CREATE TYPE \"value\";", sql);
        Assert.Contains("RECEIVE =", sql);
        Assert.Contains("RETURNS SETOF \"value\"", sql);
    }

    /// <summary>
    /// Runs generated record constructors and verifies an independent definite-length CBOR map fixture.
    /// </summary>
    [TestMethod]
    public void DefaultSerializedRecordsExecuteImmutableConstructors()
    {
        string[] actual = RunSerializedProbe<string[]>("""
            [Ankus.PgType] public sealed record Value(long Number, string? Text);
            """, """
            Value value = codec.Parse("{\"Text\":\"héllo 😀\",\"Number\":-9223372036854775808}");
            var bytes = new System.Buffers.ArrayBufferWriter<byte>();
            codec.Write(new Value(42, null), bytes);
            Value decoded = codec.Read(System.Convert.FromHexString("A2664E756D626572182A6454657874F6"));
            return new[] { value.Number.ToString(System.Globalization.CultureInfo.InvariantCulture), value.Text!,
                System.Convert.ToHexString(bytes.WrittenSpan), codec.Format(decoded) };
            """);
        Assert.AreSequenceEqual(["-9223372036854775808", "héllo 😀", "A2664E756D626572182A6454657874F6", "{\"Number\":42,\"Text\":null}"], actual);
    }

    /// <summary>
    /// Exercises each generated scalar conversion and preserves byte vectors as numeric arrays.
    /// </summary>
    [TestMethod]
    public void DefaultSerializedScalarMappingsPreserveExactValues()
    {
        string[] actual = RunSerializedProbe<string[]>("""
            public enum Mode { Stopped, Ready = 7 }
            [Ankus.PgType] public sealed record Value(sbyte SByte, byte Byte, short Short, ushort UShort,
                int Int, uint UInt, long Long, ulong ULong, float Single, double Double, decimal Decimal,
                bool Flag, Mode Mode, byte[] Bytes);
            """, """
            Value value = codec.Parse("{\"SByte\":-128,\"Byte\":255,\"Short\":-32768,\"UShort\":65535,\"Int\":-2147483648,\"UInt\":4294967295,\"Long\":-9223372036854775808,\"ULong\":18446744073709551615,\"Single\":1.25,\"Double\":-2.5,\"Decimal\":1.2300,\"Flag\":true,\"Mode\":\"Ready\",\"Bytes\":[0,255]}");
            var bytes = new System.Buffers.ArrayBufferWriter<byte>();
            codec.Write(value, bytes);
            Value decoded = codec.Read(bytes.WrittenSpan);
            return new[] { codec.Format(decoded), decoded.Mode.ToString(), System.Convert.ToHexString(decoded.Bytes) };
            """);
        Assert.AreSequenceEqual([
            "{\"SByte\":-128,\"Byte\":255,\"Short\":-32768,\"UShort\":65535,\"Int\":-2147483648,\"UInt\":4294967295,\"Long\":-9223372036854775808,\"ULong\":18446744073709551615,\"Single\":1.25,\"Double\":-2.5,\"Decimal\":1.2300,\"Flag\":true,\"Mode\":\"Ready\",\"Bytes\":[0,255]}",
            "Ready", "00FF"], actual);
    }

    /// <summary>
    /// Preserves nullable containers, nullable elements, nested records, empty values and Unicode dictionary keys.
    /// </summary>
    [TestMethod]
    public void DefaultSerializedNestedNullabilityExecutes()
    {
        string[] actual = RunSerializedProbe<string[]>("""
            public sealed record Leaf(string? Text, int? Number);
            [Ankus.PgType] public sealed record Value(
                System.Collections.Generic.List<Leaf?>?[]? Lists,
                System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<Leaf?>?>? Map);
            """, """
            const string text = "{\"Lists\":[null,[],[null,{\"Text\":\"\",\"Number\":0}]],\"Map\":{\"é😀\":null,\"values\":[{\"Text\":null,\"Number\":-1}],\"A\":[{\"Text\":\"upper\",\"Number\":1}],\"a\":[{\"Text\":\"lower\",\"Number\":2}]}}";
            Value value = codec.Parse(text);
            var bytes = new System.Buffers.ArrayBufferWriter<byte>();
            codec.Write(value, bytes);
            Value decoded = codec.Read(bytes.WrittenSpan);
            return new[] { codec.Format(decoded), codec.Format(codec.Parse("{}")),
                decoded.Lists![2]![1]!.Number!.Value.ToString(), decoded.Map!["values"]![0]!.Number!.Value.ToString() };
            """);
        Assert.HasCount(4, actual);
        using JsonDocument expected = JsonDocument.Parse("{\"Lists\":[null,[],[null,{\"Text\":\"\",\"Number\":0}]],\"Map\":{\"é😀\":null,\"values\":[{\"Text\":null,\"Number\":-1}],\"A\":[{\"Text\":\"upper\",\"Number\":1}],\"a\":[{\"Text\":\"lower\",\"Number\":2}]}}");
        using JsonDocument encoded = JsonDocument.Parse(actual[0]);
        Assert.IsTrue(JsonElement.DeepEquals(expected.RootElement, encoded.RootElement), actual[0]);
        Assert.AreEqual("{\"Lists\":null,\"Map\":null}", actual[1]);
        Assert.AreEqual("0", actual[2]);
        Assert.AreEqual("-1", actual[3]);
    }

    /// <summary>
    /// Honors renamed members, explicitly ignored unsupported values and the selected immutable constructor.
    /// </summary>
    [TestMethod]
    public void DefaultSerializationAttributesPreserveContract()
    {
        string[] actual = RunSerializedProbe<string[]>("""
            [Ankus.PgType] public sealed class Value
            {
                public Value() { throw new System.InvalidOperationException("Wrong constructor."); }
                [System.Text.Json.Serialization.JsonConstructor]
                public Value(int number, string? text) { Number = number; Text = text; }
                [System.Text.Json.Serialization.JsonPropertyName("n")]
                public int Number { get; }
                [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
                public string? Text { get; }
                [System.Text.Json.Serialization.JsonIgnore]
                public System.Uri Ignored => new("https://example.com/");
            }
            """, """
            Value value = codec.Parse("{\"n\":7,\"Ignored\":{\"unknown\":[1,null]},\"unused\":true}");
            var bytes = new System.Buffers.ArrayBufferWriter<byte>();
            codec.Write(value, bytes);
            return new[] { value.Number.ToString(), codec.Format(codec.Read(bytes.WrittenSpan)) };
            """);
        Assert.AreSequenceEqual(["7", "{\"n\":7,\"Text\":null}"], actual);
    }

    /// <summary>
    /// Assigns mutable fields and init properties and preserves explicit required-member presence.
    /// </summary>
    [TestMethod]
    public void DefaultSerializedMutableMembersAndRequiredPresenceExecute()
    {
        string[] actual = RunSerializedProbe<string[]>("""
            [Ankus.PgType] public sealed class Value
            {
                public int Number;
                public required string? Text { get; init; }
                [System.Text.Json.Serialization.JsonRequired] public int? Optional { get; set; }
            }
            """, """
            Value value = codec.Parse("{\"Number\":9,\"Text\":null,\"Optional\":null}");
            string Missing(string text)
            {
                try { codec.Parse(text); return "accepted"; }
                catch (Ankus.PgException error) { return error.SqlState; }
            }
            return new[] { codec.Format(value), Missing("{\"Number\":9,\"Optional\":null}"), Missing("{\"Number\":9,\"Text\":null}") };
            """);
        Assert.AreSequenceEqual(["{\"Number\":9,\"Text\":null,\"Optional\":null}", "22P02", "22P02"], actual);
    }

    /// <summary>
    /// Preserves selected-constructor normalization while requiring the bound member's presence.
    /// </summary>
    /// <param name="requiredKeyword">Whether the contract uses C# required members.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void DefaultSerializedRequiredConstructorMembersPreserveValidation(bool requiredKeyword)
    {
        string[] actual = RunSerializedProbe<string[]>($$"""
            [Ankus.PgType] public sealed class Value
            {
                {{(requiredKeyword ? "[System.Diagnostics.CodeAnalysis.SetsRequiredMembers]" : "")}}
                public Value(string text) { Text = text.Trim(); }
                {{(requiredKeyword ? "public required string" : "[System.Text.Json.Serialization.JsonRequired] public string")}} Text { get; set; }
            }
            """, """
            Value value = codec.Parse("{\"Text\":\" x \"}");
            var bytes = new System.Buffers.ArrayBufferWriter<byte>();
            codec.Write(value, bytes);
            string missing;
            try { codec.Parse("{}"); missing = "accepted"; }
            catch (Ankus.PgException error) { missing = error.SqlState; }
            return new[] { value.Text, codec.Format(codec.Read(bytes.WrittenSpan)), missing };
            """);
        Assert.AreSequenceEqual(["x", "{\"Text\":\"x\"}", "22P02"], actual);
    }

    /// <summary>
    /// Allows the maximum recursive object depth and bounds both longer chains and cycles.
    /// </summary>
    [TestMethod]
    public void DefaultSerializedRecursiveGraphsRespectDepthAndRejectCycles()
    {
        string[] actual = RunSerializedProbe<string[]>("""
            [Ankus.PgType] public sealed class Value
            {
                public int Number { get; init; }
                public Value? Next { get; set; }
            }
            """, """
            Value? head = null;
            for (int i = 0; i < 64; i++) head = new Value { Number = i, Next = head };
            var bytes = new System.Buffers.ArrayBufferWriter<byte>();
            codec.Write(head!, bytes);
            Value? decoded = codec.Read(bytes.WrittenSpan);
            int count = 0;
            while (decoded is not null) { count++; decoded = decoded.Next; }
            string Fail(System.Action action)
            {
                try { action(); return "accepted"; }
                catch (System.InvalidOperationException) { return "bounded"; }
            }
            Value over = new() { Next = head };
            Value cycle = new();
            cycle.Next = cycle;
            string format = codec.Format(head!);
            Value parsed = codec.Parse(format);
            return new[] { count.ToString(), parsed.Number.ToString(),
                Fail(() => codec.Write(over, new System.Buffers.ArrayBufferWriter<byte>())),
                Fail(() => codec.Format(over)),
                Fail(() => codec.Write(cycle, new System.Buffers.ArrayBufferWriter<byte>())),
                Fail(() => codec.Format(cycle)) };
            """);
        Assert.AreSequenceEqual(["64", "63", "bounded", "bounded", "bounded", "bounded"], actual);
    }

    /// <summary>
    /// Refuses runtime subclasses rather than silently dropping their additional state.
    /// </summary>
    [TestMethod]
    public void DefaultSerializedRuntimeSubtypesAreRejected()
    {
        string[] actual = RunSerializedProbe<string[]>("""
            [Ankus.PgType] public class Value { public int Number { get; set; } }
            public sealed class Derived : Value { public int Extra { get; set; } }
            """, """
            Value value = new Derived { Number = 1, Extra = 9 };
            string Fail(System.Action action)
            {
                try { action(); return "accepted"; }
                catch (System.InvalidOperationException) { return "rejected"; }
            }
            return new[] { Fail(() => codec.Write(value, new System.Buffers.ArrayBufferWriter<byte>())),
                Fail(() => codec.Format(value)), codec.Format(new Value { Number = 7 }) };
            """);
        Assert.AreSequenceEqual(["rejected", "rejected", "{\"Number\":7}"], actual);
    }

    /// <summary>
    /// Rejects collection subclasses and covariant arrays instead of silently dropping subtype state.
    /// </summary>
    [TestMethod]
    public void DefaultSerializedCollectionSubtypesAreRejected()
    {
        string[] actual = RunSerializedProbe<string[]>("""
            public class Item { public int Number { get; set; } }
            public sealed class DerivedItem : Item { public int Extra { get; set; } }
            public sealed class DerivedList : System.Collections.Generic.List<string> { public int Extra { get; set; } }
            public sealed class DerivedMap : System.Collections.Generic.Dictionary<string,int> { public int Extra { get; set; } }
            [Ankus.PgType] public sealed record Value(System.Collections.Generic.List<string> List,
                System.Collections.Generic.Dictionary<string,int> Map, Item[] Items);
            """, """
            string Fail(Value value)
            {
                try { codec.Write(value, new System.Buffers.ArrayBufferWriter<byte>()); return "accepted"; }
                catch (System.InvalidOperationException) { return "rejected"; }
            }
            return new[] { Fail(new Value(new DerivedList { Extra = 1 }, new(), [])),
                Fail(new Value([], new DerivedMap { Extra = 2 }, [])),
                Fail(new Value([], new(), new DerivedItem[0])),
                codec.Format(new Value([], new(), [])) };
            """);
        Assert.AreSequenceEqual(["rejected", "rejected", "rejected", "{\"List\":[],\"Map\":{},\"Items\":[]}"], actual);
    }

    /// <summary>
    /// Keeps serialized enum names exact and rejects unknown input and unnamed managed values.
    /// </summary>
    [TestMethod]
    public void DefaultSerializedEnumNamesRejectUndefinedValues()
    {
        string[] actual = RunSerializedProbe<string[]>("""
            [Ankus.PgType] public enum Value { Zero, One = 7 }
            """, """
            string Fail(System.Action action)
            {
                try { action(); return "accepted"; }
                catch (Ankus.PgException error) { return error.SqlState; }
                catch (System.InvalidOperationException) { return "unnamed"; }
            }
            return new[] { codec.Format(codec.Parse("\"One\"")),
                Fail(() => codec.Parse("\"Unknown\"")),
                Fail(() => codec.Read(System.Convert.FromHexString("67556E6B6E6F776E"))),
                Fail(() => codec.Format((Value)2)),
                Fail(() => codec.Write((Value)2, new System.Buffers.ArrayBufferWriter<byte>())) };
            """);
        Assert.AreSequenceEqual(["\"One\"", "22P02", "22P03", "unnamed", "unnamed"], actual);
    }

    /// <summary>
    /// Uses an explicitly renamed enum label in JSON and independent CBOR storage.
    /// </summary>
    [TestMethod]
    public void DefaultSerializedRenamedEnumLabelsExecute()
    {
        string[] actual = RunSerializedProbe<string[]>("""
            [Ankus.PgType] public enum Value
            {
                [System.Text.Json.Serialization.JsonStringEnumMemberName("ready-now")] Ready = 7,
            }
            """, """
            Value parsed = codec.Parse("\"ready-now\"");
            var bytes = new System.Buffers.ArrayBufferWriter<byte>();
            codec.Write(parsed, bytes);
            return new[] { parsed.ToString(), System.Convert.ToHexString(bytes.WrittenSpan),
                codec.Format(codec.Read(System.Convert.FromHexString("6972656164792D6E6F77"))) };
            """);
        Assert.AreSequenceEqual(["Ready", "6972656164792D6E6F77", "\"ready-now\""], actual);
    }

    /// <summary>
    /// Honors required references inside nested arrays, lists and dictionaries during decoding.
    /// </summary>
    /// <param name="input">The graph containing a forbidden null.</param>
    [TestMethod]
    [DataRow("{\"Items\":null,\"List\":[],\"Map\":{}}")]
    [DataRow("{\"Items\":[null],\"List\":[],\"Map\":{}}")]
    [DataRow("{\"Items\":[],\"List\":[null],\"Map\":{}}")]
    [DataRow("{\"Items\":[],\"List\":[],\"Map\":{\"key\":null}}")]
    [DataRow("{\"Items\":[{\"Text\":null}],\"List\":[],\"Map\":{}}")]
    public void DefaultSerializedNestedRequiredReferencesRejectNull(string input)
    {
        string[] actual = RunSerializedProbe<string[]>("""
            public sealed record Item(string Text);
            [Ankus.PgType] public sealed record Value(Item[] Items, System.Collections.Generic.List<Item> List,
                System.Collections.Generic.Dictionary<string,Item> Map);
            """, $$"""
            try { codec.Parse({{Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(input, true)}}); return new[] { "accepted" }; }
            catch (Ankus.PgException error) { return new[] { error.SqlState }; }
            """);
        Assert.AreSequenceEqual(["22P02"], actual);
    }

    /// <summary>
    /// Rejects invalid field values and JSON framing while accepting a valid subsequent decode on the same codec.
    /// </summary>
    /// <param name="input">An invalid complete document or truncated prefix.</param>
    [TestMethod]
    [DataRow("null")]
    [DataRow("{}")]
    [DataRow("{\"Number\":1,\"Text\":null}")]
    [DataRow("{\"Number\":1,\"Text\":\"ok\",\"Number\":2}")]
    [DataRow("{\"Number\":2147483648,\"Text\":\"ok\"}")]
    [DataRow("{\"Number\":1.5,\"Text\":\"ok\"}")]
    [DataRow("{\"Number\":\"1\",\"Text\":\"ok\"}")]
    [DataRow("{\"Number\":1,\"Text\":\"ok\"")]
    [DataRow("{\"Number\":1,\"Text\":\"ok\"} false")]
    public void DefaultSerializedInvalidDocumentsAreRejected(string input)
    {
        string[] actual = RunSerializedProbe<string[]>("""
            [Ankus.PgType] public sealed record Value(int Number, string Text, int? Optional);
            """, $$"""
            string state;
            try { codec.Parse({{Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(input, true)}}); state = "accepted"; }
            catch (Ankus.PgException error) { state = error.SqlState; }
            return new[] { state, codec.Format(codec.Parse("{\"Number\":1,\"Text\":\"ok\"}")) };
            """);
        Assert.AreSequenceEqual(["22P02", "{\"Number\":1,\"Text\":\"ok\",\"Optional\":null}"], actual);
    }

    /// <summary>
    /// Reports unsupported or ambiguous graphs before generating lossy serializers.
    /// </summary>
    /// <param name="source">The invalid default contract.</param>
    [TestMethod]
    [DataRow("[Ankus.PgType] public sealed record Value(object Item);")]
    [DataRow("[Ankus.PgType] public sealed record Value(System.Uri Item);")]
    [DataRow("[Ankus.PgType] public sealed record Value(int[,] Items);")]
    [DataRow("[Ankus.PgType] public sealed record Value(System.Collections.Generic.Dictionary<int,string> Items);")]
    [DataRow("[Ankus.PgType] public sealed class Value { public int Number { get; } }")]
    [DataRow("[Ankus.PgType] public sealed class Value { public Value(int wrong) { Number = wrong; } public int Number { get; } }")]
    [DataRow("[Ankus.PgType] public sealed class Value { [System.Text.Json.Serialization.JsonPropertyName(\"same\")] public int A { get; set; } [System.Text.Json.Serialization.JsonPropertyName(\"same\")] public int B { get; set; } }")]
    [DataRow("[Ankus.PgType] public sealed class Value { [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] public string? Text { get; set; } }")]
    [DataRow("[Ankus.PgType] public sealed class Value { [System.Text.Json.Serialization.JsonNumberHandling(System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString)] public int Number { get; set; } }")]
    [DataRow("public class Base { public int Number { get; set; } } [Ankus.PgType] public sealed class Value : Base { }")]
    [DataRow("[Ankus.PgType] public sealed class Value { public Value(string text) { Text = text; } public string? Text { get; } }")]
    [DataRow("[Ankus.PgType] public enum Value { A = 1, B = 1 }")]
    [DataRow("[Ankus.PgType] public enum Value { [System.Text.Json.Serialization.JsonStringEnumMemberName(\"same\")] A, [System.Text.Json.Serialization.JsonStringEnumMemberName(\"same\")] B }")]
    [DataRow("[Ankus.PgType] public sealed class Value { [System.Text.Json.Serialization.JsonIgnore] public required string Text { get; init; } }")]
    [DataRow("[Ankus.PgType] public sealed class Value { public Value(string text) { Text = text; } public required string Text { get; set; } }")]
    [DataRow("[Ankus.PgType] public sealed class Value { [System.Text.Json.Serialization.JsonInclude] internal int Number { get; set; } }")]
    [DataRow("[Ankus.PgType] public sealed class Value { [System.Text.Json.Serialization.JsonInclude] private int Number { get; set; } }")]
    [DataRow("[Ankus.PgType] public sealed class Value { [Custom] public int Number { get; set; } } public sealed class CustomAttribute : System.Text.Json.Serialization.JsonConverterAttribute { }")]
    [DataRow("[Ankus.PgType] internal sealed class Value { internal required string Text { get; init; } }")]
    public void InvalidDefaultSerializationContractsAreDiagnosed(string source)
    {
        (_, ImmutableArray<Diagnostic> diagnostics) = Generate(source);
        Diagnostic diagnostic = Assert.ContainsSingle(diagnostics.Where(static value => value.Id == "ANKUS017"));
        Assert.AreEqual(DiagnosticSeverity.Error, diagnostic.Severity);
    }

    /// <summary>
    /// Generates complete type I/O for a default codec with no attributed functions.
    /// </summary>
    [TestMethod]
    public void DefaultSerializedTypeOnlyExtensionCompiles()
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("[Ankus.PgType] public readonly record struct Value(int Number);");
        AssertAggregateCompilation(compilation, diagnostics);
        string sql = ManifestValue(compilation, "Ankus.Sql");
        Assert.Contains("CREATE FUNCTION \"value_in\"(cstring) RETURNS \"value\"", sql);
        Assert.Contains("CREATE FUNCTION \"value_out\"(\"value\") RETURNS cstring", sql);
        Assert.AreEqual("true", ManifestValue(compilation, "Ankus.Relocatable"));
    }

    /// <summary>
    /// Executes the emitted codec through a statically typed probe and returns observable values to the test.
    /// </summary>
    private T RunSerializedProbe<T>(string declarations, string body)
    {
        string resultType = typeof(T) == typeof(string[]) ? "string[]" : throw new InvalidOperationException("Unsupported probe result.");
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate(declarations + $$"""

            public static class SerializedProbe
            {
                public static {{resultType}} Run(Ankus.PgTypeCodec<Value> codec)
                {
                    {{body}}
                }
            }
            """);
        Assert.IsEmpty(diagnostics, string.Join(Environment.NewLine, diagnostics));
        AssertAggregateCompilation(compilation, diagnostics);
        using var stream = new MemoryStream();
        Microsoft.CodeAnalysis.Emit.EmitResult emitted = compilation.Emit(stream, cancellationToken: context.CancellationToken);
        Assert.IsTrue(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        stream.Position = 0;
        var loadContext = new AssemblyLoadContext("SerializedTypeGeneratorProbe", isCollectible: true);
        try
        {
            Assembly assembly = loadContext.LoadFromStream(stream);
            Type valueType = assembly.GetType("Value", throwOnError: true)!;
            Type codecBase = typeof(PgTypeCodec<>).MakeGenericType(valueType);
            Type codecType = Assert.ContainsSingle(assembly.GetTypes().Where(type => !type.IsAbstract && codecBase.IsAssignableFrom(type)));
            object? codec = Activator.CreateInstance(codecType, nonPublic: true);
            Assert.IsNotNull(codec);
            Type probe = assembly.GetType("SerializedProbe", throwOnError: true)!;
            MethodInfo? run = probe.GetMethod("Run", BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(run);
            return Assert.IsInstanceOfType<T>(run.Invoke(null, [codec]));
        }
        finally
        {
            loadContext.Unload();
        }
    }
}
