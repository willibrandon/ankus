using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Ankus.Generators.Tests;

/// <summary>
/// Verifies shared validation preserves the construction and type contracts of both codec kinds.
/// </summary>
public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// A nullable codec contract cannot represent the exact non-null PostgreSQL value type.
    /// </summary>
    /// <param name="textCodec">Whether the codec supplies only SQL text conversion.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CustomCodecValidationRejectsNullableContracts(bool textCodec)
    {
        string attribute = textCodec ? "TextCodec=typeof(Codec)" : "typeof(Codec)";
        string baseType = textCodec ? "PgTypeTextCodec" : "PgTypeCodec";
        string binary = textCodec ? "" : """
            public override Value? Read(System.ReadOnlySpan<byte> payload) => null;
            public override void Write(Value? value, System.Buffers.IBufferWriter<byte> destination) { }
            """;
        (_, ImmutableArray<Diagnostic> diagnostics) = Generate($$"""
            [Ankus.PgType({{attribute}})] public sealed record Value(int Number);
            public sealed class Codec : Ankus.{{baseType}}<Value?>
            {
                public override Value? Parse(string text) => null;
                public override string Format(Value? value) => "null";
                {{binary}}
            }
            """);
        Diagnostic diagnostic = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual("ANKUS017", diagnostic.Id);
        Assert.Contains("exact non-nullable managed type", diagnostic.GetMessage(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Full codecs with inherited required members must advertise initialization on the selected constructor.
    /// </summary>
    [TestMethod]
    public void CustomCodecValidationRejectsUninitializedRequiredMembers()
    {
        (_, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            [Ankus.PgType(typeof(Codec))] public sealed record Value(int Number);
            public abstract class CodecBase : Ankus.PgTypeCodec<Value>
            {
                public required string Prefix { get; init; }
            }
            public sealed class Codec : CodecBase
            {
                public override Value Parse(string text) => new(42);
                public override string Format(Value value) => Prefix + value.Number;
                public override Value Read(System.ReadOnlySpan<byte> payload) => new(payload[0]);
                public override void Write(Value value, System.Buffers.IBufferWriter<byte> destination) { }
            }
            """);
        Diagnostic diagnostic = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual("ANKUS017", diagnostic.Id);
        Assert.Contains("SetsRequiredMembers", diagnostic.GetMessage(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Public and assembly-visible constructors initialize inherited required state before either codec is used.
    /// </summary>
    /// <param name="textCodec">Whether generated CBOR accompanies the custom text conversion.</param>
    /// <param name="internalConstructor">Whether the constructor has assembly visibility.</param>
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void CustomCodecValidationExecutesInitializedRequiredMembers(bool textCodec, bool internalConstructor)
    {
        string attribute = textCodec ? "TextCodec=typeof(Codec)" : "typeof(Codec)";
        string baseType = textCodec ? "PgTypeTextCodec" : "PgTypeCodec";
        string accessibility = internalConstructor ? "internal" : "public";
        string expectedBinary = textCodec ? "A1664E756D626572182A" : "2A";
        string binary = textCodec ? "" : """
            public override Value Read(System.ReadOnlySpan<byte> payload) => new(payload[0]);
            public override void Write(Value value, System.Buffers.IBufferWriter<byte> destination)
            {
                destination.GetSpan(1)[0] = checked((byte)value.Number);
                destination.Advance(1);
            }
            """;
        string[] result = RunSerializedProbe<string[]>($$"""
            [Ankus.PgType({{attribute}})] public sealed record Value(int Number);
            public abstract class CodecBase : Ankus.{{baseType}}<Value>
            {
                public required string Prefix { get; init; }
            }
            public sealed class Codec : CodecBase
            {
                [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
                {{accessibility}} Codec() { Prefix = "ready:"; }
                public override Value Parse(string text) => new(int.Parse(text, System.Globalization.CultureInfo.InvariantCulture));
                public override string Format(Value value) => Prefix + value.Number.ToString(System.Globalization.CultureInfo.InvariantCulture);
                {{binary}}
            }
            """, $$"""
            Value parsed = codec.Parse("42");
            var bytes = new System.Buffers.ArrayBufferWriter<byte>();
            codec.Write(parsed, bytes);
            Value decoded = codec.Read(System.Convert.FromHexString("{{expectedBinary}}"));
            return new[] { codec.Format(parsed), System.Convert.ToHexString(bytes.WrittenSpan), codec.Format(decoded) };
            """);
        Assert.AreSequenceEqual(["ready:42", expectedBinary, "ready:42"], result);
    }

    /// <summary>
    /// A closed generic full codec compiles in registration and executes its text and binary methods.
    /// </summary>
    [TestMethod]
    public void CustomCodecValidationExecutesClosedGenericFullCodec()
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            public interface INumber { int Number { get; set; } }
            [Ankus.PgType(typeof(Codec<Value>))] public sealed class Value : INumber { public int Number { get; set; } }
            public sealed class Codec<T> : Ankus.PgTypeCodec<T> where T : INumber, new()
            {
                public override T Parse(string text) => new() { Number = int.Parse(text, System.Globalization.CultureInfo.InvariantCulture) };
                public override string Format(T value) => "generic:" + value.Number.ToString(System.Globalization.CultureInfo.InvariantCulture);
                public override T Read(System.ReadOnlySpan<byte> payload) => new() { Number = payload[0] };
                public override void Write(T value, System.Buffers.IBufferWriter<byte> destination)
                {
                    destination.GetSpan(1)[0] = checked((byte)value.Number);
                    destination.Advance(1);
                }
            }
            public static class CodecValidationProbe
            {
                public static string[] Run()
                {
                    Ankus.PgTypeCodec<Value> codec = new Codec<Value>();
                    Value parsed = codec.Parse("42");
                    var bytes = new System.Buffers.ArrayBufferWriter<byte>();
                    codec.Write(parsed, bytes);
                    Value decoded = codec.Read(new byte[] { 7 });
                    return new[] { codec.Format(parsed), System.Convert.ToHexString(bytes.WrittenSpan), codec.Format(decoded) };
                }
            }
            """);
        AssertAggregateCompilation(compilation, diagnostics);
        using var stream = new MemoryStream();
        Microsoft.CodeAnalysis.Emit.EmitResult emitted = compilation.Emit(stream, cancellationToken: context.CancellationToken);
        Assert.IsTrue(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        stream.Position = 0;
        var loadContext = new AssemblyLoadContext("ClosedGenericCodecValidationProbe", isCollectible: true);
        try
        {
            Assembly assembly = loadContext.LoadFromStream(stream);
            Type probe = assembly.GetType("CodecValidationProbe", throwOnError: true)!;
            MethodInfo? run = probe.GetMethod("Run", BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(run);
            string[] result = Assert.IsInstanceOfType<string[]>(run.Invoke(null, null));
            Assert.AreSequenceEqual(["generic:42", "2A", "generic:7"], result);
        }
        finally
        {
            loadContext.Unload();
        }
    }

    /// <summary>
    /// Friend access permits internal codec types, constructors and closed generic arguments separately.
    /// </summary>
    /// <param name="boundary">The declaration whose visibility requires friend access.</param>
    /// <param name="friendAccess">Whether the codec assembly grants the consuming assembly access.</param>
    [TestMethod]
    [DataRow("type", false)]
    [DataRow("type", true)]
    [DataRow("constructor", false)]
    [DataRow("constructor", true)]
    [DataRow("argument", false)]
    [DataRow("argument", true)]
    public void CustomCodecValidationHonorsFriendAssemblies(string boundary, bool friendAccess)
    {
        string friendship = friendAccess ? "[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(\"GeneratorTest\")]" : "";
        string codecAccess = boundary == "type" ? "internal" : "public";
        string constructorAccess = boundary == "constructor" ? "internal" : "public";
        string argumentAccess = boundary == "argument" ? "internal" : "public";
        CSharpCompilation dependency = CSharpCompilation.Create("ExternalCodecs",
            [CSharpSyntaxTree.ParseText($$"""
                {{friendship}}
                {{argumentAccess}} sealed class Marker { }
                {{codecAccess}} sealed class ExternalCodec<T, TMarker> : Ankus.PgTypeCodec<T>
                {
                    {{constructorAccess}} ExternalCodec() { }
                    public override T Parse(string text) => default!;
                    public override string Format(T value) => "external";
                    public override T Read(System.ReadOnlySpan<byte> payload) => default!;
                    public override void Write(T value, System.Buffers.IBufferWriter<byte> destination) { }
                }
                """, cancellationToken: context.CancellationToken)], s_references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        using var stream = new MemoryStream();
        Microsoft.CodeAnalysis.Emit.EmitResult emitted = dependency.Emit(stream, cancellationToken: context.CancellationToken);
        Assert.IsTrue(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        CSharpCompilation input = CSharpCompilation.Create("GeneratorTest",
            [CSharpSyntaxTree.ParseText("[Ankus.PgType(typeof(ExternalCodec<Value, Marker>))] public sealed record Value(int Number);",
                cancellationToken: context.CancellationToken)],
            s_references.Add(MetadataReference.CreateFromImage(stream.ToArray())),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, nullableContextOptions: NullableContextOptions.Enable));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new PgFunctionGenerator().AsSourceGenerator());
        driver.RunGeneratorsAndUpdateCompilation(input, out Compilation output, out ImmutableArray<Diagnostic> diagnostics, context.CancellationToken);
        if (friendAccess)
        {
            AssertAggregateCompilation(output, diagnostics);
        }
        else
        {
            Diagnostic diagnostic = Assert.ContainsSingle(diagnostics);
            Assert.AreEqual("ANKUS017", diagnostic.Id);
            Assert.Contains("accessible parameterless constructor", diagnostic.GetMessage(CultureInfo.InvariantCulture));
        }
    }
}
