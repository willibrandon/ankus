using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Ankus.Generators.Tests;

/// <summary>
/// Verifies generated storage remains independent of custom SQL text conversion.
/// </summary>
public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Constructs one text codec only when text conversion begins and preserves independent CBOR bytes.
    /// </summary>
    [TestMethod]
    public void CustomTextCodecIsDeferredCachedAndSeparateFromBinary()
    {
        string[] result = RunSerializedProbe<string[]>("""
            [Ankus.PgType(TextCodec=typeof(TextCodec), BinaryProtocol=true)] public sealed record Value(int Number, string Text);
            public sealed class TextCodec : Ankus.PgTypeTextCodec<Value>
            {
                public static int Created, Parsed, Formatted;
                public TextCodec() { Created++; }
                public override Value Parse(string text) { Parsed++; return new(int.Parse(text.Split('|')[0], System.Globalization.CultureInfo.InvariantCulture), text.Split('|')[1]); }
                public override string Format(Value value) { Formatted++; return value.Number.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" + value.Text; }
            }
            """, """
            int initial = TextCodec.Created;
            Value binary = codec.Read(System.Convert.FromHexString("A2664E756D626572182A6454657874626F6B"));
            var bytes = new System.Buffers.ArrayBufferWriter<byte>();
            codec.Write(binary, bytes);
            string afterBinary = $"{TextCodec.Created}:{TextCodec.Parsed}:{TextCodec.Formatted}";
            Value parsed = codec.Parse("7|héllo 😀");
            string formatted = codec.Format(parsed);
            return new[] { initial.ToString(), binary.Number + ":" + binary.Text, System.Convert.ToHexString(bytes.WrittenSpan), afterBinary,
                formatted, $"{TextCodec.Created}:{TextCodec.Parsed}:{TextCodec.Formatted}" };
            """);
        Assert.AreSequenceEqual(["0", "42:ok", "A2664E756D626572182A6454657874626F6B", "0:0:0", "7|héllo 😀", "1:1:1"], result);
    }

    /// <summary>
    /// A failed text factory is cached without preventing subsequent CBOR operations.
    /// </summary>
    [TestMethod]
    public void CustomTextFactoryFailureDoesNotDisableBinaryStorage()
    {
        string[] result = RunSerializedProbe<string[]>("""
            [Ankus.PgType(TextCodec=typeof(TextCodec))] public sealed record Value(int Number);
            public sealed class TextCodec : Ankus.PgTypeTextCodec<Value>
            {
                public static int Attempts;
                public TextCodec() { Attempts++; throw new Ankus.PgException("P7910", "text factory failed"); }
                public override Value Parse(string text) => throw new System.InvalidOperationException("parse reached");
                public override string Format(Value value) => throw new System.InvalidOperationException("format reached");
            }
            """, """
            string Fail(System.Action action) { try { action(); return "accepted"; } catch (Ankus.PgException error) { return error.SqlState + ":" + error.Message; } }
            Value first = codec.Read(System.Convert.FromHexString("A1664E756D626572182A"));
            string before = TextCodec.Attempts.ToString();
            string parse = Fail(() => codec.Parse("42"));
            string format = Fail(() => codec.Format(first));
            var bytes = new System.Buffers.ArrayBufferWriter<byte>();
            codec.Write(first, bytes);
            return new[] { before, parse, format, TextCodec.Attempts.ToString(), codec.Read(bytes.WrittenSpan).Number.ToString(), System.Convert.ToHexString(bytes.WrittenSpan) };
            """);
        Assert.AreSequenceEqual(["0", "P7910:text factory failed", "P7910:text factory failed", "1", "42", "A1664E756D626572182A"], result);
    }

    /// <summary>
    /// Struct and enum roots use domain text while retaining their structural CBOR representation.
    /// </summary>
    /// <param name="declaration">The root declaration.</param>
    /// <param name="value">The text parser result.</param>
    /// <param name="format">The custom text expression.</param>
    /// <param name="expectedText">The independent text representation.</param>
    /// <param name="expectedBinary">The independent CBOR representation.</param>
    [TestMethod]
    [DataRow("public readonly record struct Value(int Number);", "new(42)", "\"N=\" + value.Number", "N=42", "A1664E756D626572182A")]
    [DataRow("public enum Value { Zero, Answer }", "Value.Answer", "\"!\" + value", "!Answer", "66416E73776572")]
    public void CustomTextContractsExecute(string declaration, string value, string format, string expectedText, string expectedBinary)
    {
        string[] result = RunSerializedProbe<string[]>($$"""
            [Ankus.PgType(TextCodec=typeof(TextCodec))] {{declaration}}
            public sealed class TextCodec : Ankus.PgTypeTextCodec<Value>
            {
                public override Value Parse(string text) => text == "input" ? {{value}} : throw new System.FormatException("unexpected input");
                public override string Format(Value value) => {{format}};
            }
            """, $$"""
            Value value = codec.Parse("input");
            var bytes = new System.Buffers.ArrayBufferWriter<byte>();
            codec.Write(value, bytes);
            return new[] { codec.Format(value), System.Convert.ToHexString(bytes.WrittenSpan), codec.Format(codec.Read(System.Convert.FromHexString("{{expectedBinary}}"))) };
            """);
        Assert.AreSequenceEqual([expectedText, expectedBinary, expectedText], result);
    }

    /// <summary>
    /// Closed generic text codecs are statically constructed with the exact managed root type.
    /// </summary>
    [TestMethod]
    public void CustomTextClosedGenericCodecExecutes()
    {
        string[] result = RunSerializedProbe<string[]>("""
            [Ankus.PgType(TextCodec=typeof(TextCodec<Value>))] public sealed class Value { public int Number { get; set; } = 17; }
            public sealed class TextCodec<T> : Ankus.PgTypeTextCodec<T> where T : new()
            {
                public override T Parse(string text) => new T();
                public override string Format(T value) => typeof(T).Name;
            }
            """, """
            Value value = codec.Parse("domain");
            var bytes = new System.Buffers.ArrayBufferWriter<byte>();
            codec.Write(value, bytes);
            return new[] { value.Number.ToString(), codec.Format(value), System.Convert.ToHexString(bytes.WrittenSpan) };
            """);
        Assert.AreSequenceEqual(["17", "Value", "A1664E756D62657211"], result);
    }

    /// <summary>
    /// Nested type attributes never replace the containing serializer's structural contract.
    /// </summary>
    [TestMethod]
    public void CustomTextNestedTypesRemainStructural()
    {
        string[] result = RunSerializedProbe<string[]>("""
            [Ankus.PgType] public sealed record Value(Nested Item, Other Other);
            [Ankus.PgType(TextCodec=typeof(TextCodec))] public sealed record Nested(int Number);
            [Ankus.PgType(typeof(OtherCodec))] public sealed record Other(string Text);
            public sealed class TextCodec : Ankus.PgTypeTextCodec<Nested>
            {
                public TextCodec() => throw new System.InvalidOperationException("nested text constructed");
                public override Nested Parse(string text) => throw new System.InvalidOperationException("nested parse reached");
                public override string Format(Nested value) => throw new System.InvalidOperationException("nested format reached");
            }
            public sealed class OtherCodec : Ankus.PgTypeCodec<Other>
            {
                public OtherCodec() => throw new System.InvalidOperationException("nested storage constructed");
                public override Other Parse(string text) => throw new System.InvalidOperationException();
                public override string Format(Other value) => throw new System.InvalidOperationException();
                public override Other Read(System.ReadOnlySpan<byte> value) => throw new System.InvalidOperationException();
                public override void Write(Other value, System.Buffers.IBufferWriter<byte> destination) => throw new System.InvalidOperationException();
            }
            """, """
            Value value = codec.Parse("{\"Item\":{\"Number\":7},\"Other\":{\"Text\":\"ok\"}}");
            var bytes = new System.Buffers.ArrayBufferWriter<byte>();
            codec.Write(value, bytes);
            Value decoded = codec.Read(bytes.WrittenSpan);
            return new[] { decoded.Item.Number.ToString(), decoded.Other.Text, codec.Format(decoded) };
            """);
        Assert.AreSequenceEqual(["7", "ok", "{\"Item\":{\"Number\":7},\"Other\":{\"Text\":\"ok\"}}"], result);
    }

    /// <summary>
    /// Tagged abstract roots use custom text without changing variant identity or tagged storage.
    /// </summary>
    [TestMethod]
    public void CustomTextTaggedVariantsExecute()
    {
        string[] result = RunSerializedProbe<string[]>("""
            [Ankus.PgType(TextCodec=typeof(TextCodec))]
            [System.Text.Json.Serialization.JsonDerivedType(typeof(NumberValue), 7)]
            [System.Text.Json.Serialization.JsonDerivedType(typeof(TextValue), "text")]
            public abstract record Value;
            public sealed record NumberValue(int Number) : Value;
            public sealed record TextValue(string Text) : Value;
            public sealed class TextCodec : Ankus.PgTypeTextCodec<Value>
            {
                public override Value Parse(string text) => text == "N42" ? new NumberValue(42) : new TextValue(text);
                public override string Format(Value value) => value switch { NumberValue number => "N" + number.Number, TextValue text => "T" + text.Text, _ => throw new System.InvalidOperationException() };
            }
            """, """
            Value number = codec.Parse("N42");
            var bytes = new System.Buffers.ArrayBufferWriter<byte>();
            codec.Write(number, bytes);
            Value decoded = codec.Read(System.Convert.FromHexString("A265247479706507664E756D626572182A"));
            var textBytes = new System.Buffers.ArrayBufferWriter<byte>();
            codec.Write(codec.Parse("hello"), textBytes);
            Value text = codec.Read(textBytes.WrittenSpan);
            return new[] { number.GetType().Name, codec.Format(decoded), System.Convert.ToHexString(bytes.WrittenSpan), text.GetType().Name, codec.Format(text) };
            """);
        Assert.AreSequenceEqual(["NumberValue", "N42", "A265247479706507664E756D626572182A", "TextValue", "Thello"], result);
    }

    /// <summary>
    /// Null-input customization changes only the generated input function's strictness.
    /// </summary>
    /// <param name="options">The type options.</param>
    /// <param name="strict">The expected input strictness.</param>
    [TestMethod]
    [DataRow("", true)]
    [DataRow("NullInputErrorMessage=\"required value\"", false)]
    [DataRow("TextCodec=typeof(TextCodec), NullInputErrorMessage=\"\"", false)]
    [DataRow("typeof(Codec), NullInputErrorMessage=\"héllo 😀\"", false)]
    public void CustomTextNullInputOptionsEmitOnlyInputStrictness(string options, bool strict)
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate($$"""
            [Ankus.PgType({{options}}{{(options.Length == 0 ? "" : ", ")}}BinaryProtocol=true)] public readonly record struct Value(int Number);
            public sealed class TextCodec : Ankus.PgTypeTextCodec<Value>
            {
                public override Value Parse(string text) => new(7);
                public override string Format(Value value) => "seven";
            }
            {{CustomCodecSource}}
            """);
        AssertAggregateCompilation(compilation, diagnostics);
        string sql = ManifestValue(compilation, "Ankus.Sql");
        string input = Assert.ContainsSingle(sql.Split('\n').Where(static line => line.StartsWith("CREATE FUNCTION \"value_in\"", StringComparison.Ordinal)));
        Assert.AreEqual(strict, input.Contains(" STRICT", StringComparison.Ordinal), input);
        foreach (string suffix in new[] { "out", "recv", "send" })
        {
            string function = Assert.ContainsSingle(sql.Split('\n').Where(line => line.StartsWith("CREATE FUNCTION \"value_" + suffix + "\"", StringComparison.Ordinal)));
            Assert.Contains(" STRICT", function);
        }
    }

    /// <summary>
    /// Rejects ambiguous, unconstructible or incompatible text codec and null-message contracts.
    /// </summary>
    /// <param name="source">The invalid declaration.</param>
    [TestMethod]
    [DataRow("[Ankus.PgType(typeof(FullCodec), TextCodec=typeof(Codec))] public struct Value {} public sealed class Codec : TextBase {}")]
    [DataRow("[Ankus.PgType(TextCodec=typeof(string))] public struct Value {}")]
    [DataRow("[Ankus.PgType(TextCodec=typeof(int[]))] public struct Value {}")]
    [DataRow("[Ankus.PgType(typeof(int[]))] public struct Value {}")]
    [DataRow("[Ankus.PgType(TextCodec=typeof(Codec))] public struct Value {} public abstract class Codec : TextBase {}")]
    [DataRow("[Ankus.PgType(TextCodec=typeof(Codec<>))] public struct Value {} public sealed class Codec<T> : TextBase {}")]
    [DataRow("[Ankus.PgType(TextCodec=typeof(Codec))] public struct Value {} public sealed class Codec : TextBase { private Codec() {} }")]
    [DataRow("[Ankus.PgType(TextCodec=typeof(Codec))] public struct Value {} public sealed class Codec(int number) : TextBase {}")]
    [DataRow("[Ankus.PgType(TextCodec=typeof(Codec))] public struct Value {} public sealed class Codec : TextBase { public required string Text {get;init;} }")]
    [DataRow("[Ankus.PgType(TextCodec=typeof(Codec))] public struct Value {} public sealed class Codec : Ankus.PgTypeTextCodec<int> { public override int Parse(string text)=>0; public override string Format(int value)=>\"\"; }")]
    [DataRow("[Ankus.PgType(TextCodec=typeof(Codec))] public sealed record Value(int Number); public sealed class Codec : Ankus.PgTypeTextCodec<Value?> { public override Value? Parse(string text)=>null; public override string Format(Value? value)=>\"\"; }")]
    [DataRow("[Ankus.PgType(TextCodec=typeof(Codec))] public class Value { private sealed class Codec : TextBase {} }")]
    [DataRow("[Ankus.PgType(TextCodec=typeof(Codec))] public sealed record Value(System.Uri Address); public sealed class Codec : TextBase {}")]
    [DataRow("[Ankus.PgType(NullInputErrorMessage=\"bad\\0message\")] public struct Value {}")]
    [DataRow("[Ankus.PgType(NullInputErrorMessage=\"bad\\ud800message\")] public struct Value {}")]
    public void InvalidCustomTextContractsAreDiagnosed(string source)
    {
        (_, ImmutableArray<Diagnostic> diagnostics) = Generate(source + """
            public abstract class TextBase : Ankus.PgTypeTextCodec<Value>
            {
                public override Value Parse(string text) => default!;
                public override string Format(Value value) => "value";
            }
            public sealed class FullCodec : Ankus.PgTypeCodec<Value>
            {
                public override Value Parse(string text) => default!;
                public override string Format(Value value) => "value";
                public override Value Read(System.ReadOnlySpan<byte> payload) => default!;
                public override void Write(Value value, System.Buffers.IBufferWriter<byte> destination) { }
            }
            """);
        Diagnostic diagnostic = Assert.ContainsSingle(diagnostics.Where(static value => value.Id == "ANKUS017"));
        Assert.AreEqual(DiagnosticSeverity.Error, diagnostic.Severity);
    }
}
