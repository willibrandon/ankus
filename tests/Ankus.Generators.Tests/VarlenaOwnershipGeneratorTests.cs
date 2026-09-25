using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Ankus.Generators.Tests;

/// <summary>
/// Verifies closed wrapper mappings and the distinct scalar and retained-input conversion contracts.
/// </summary>
public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Compiles bare, wrapper, vector, shaped, tuple and deferred-set forms as one SQL type.
    /// </summary>
    [TestMethod]
    public void VarlenaOwnershipSignaturesUseUnderlyingSqlTypes()
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate(VarlenaOwnershipValueSource + """
            public static class Functions
            {
                [Ankus.PgFunction] public static Value Bare(Value value)=>value;
                [Ankus.PgFunction] public static Ankus.PgVarlena<Value>? Echo(Ankus.PgVarlena<Value>? value)=>value;
                [Ankus.PgFunction] public static Ankus.PgVarlena<Value>?[]? Vector(Ankus.PgVarlena<Value>?[]? value)=>value;
                [Ankus.PgFunction] public static Ankus.PgArray<Ankus.PgVarlena<Value>?>? Shape(Ankus.PgArray<Ankus.PgVarlena<Value>?>? value)=>value;
                [Ankus.PgFunction] public static Ankus.PgHeapTuple Tuple(Ankus.PgHeapTuple value)=>value;
                [Ankus.PgFunction] public static System.Collections.Generic.IEnumerable<Ankus.PgVarlena<Value>?> Rows(Ankus.PgVarlena<Value> value)
                { _=value.Value; yield return value; yield return null; _=value.Value; yield return value; }
            }
            """);
        AssertAggregateCompilation(compilation, diagnostics);
        string sql = ManifestValue(compilation, "Ankus.Sql").ReplaceLineEndings("\n");
        Assert.Contains("CREATE FUNCTION \"echo\"(\"value\" \"value\")\nRETURNS \"value\"", sql);
        Assert.Contains("CREATE FUNCTION \"vector\"(\"value\" \"value\"[])\nRETURNS \"value\"[]", sql);
        Assert.Contains("CREATE FUNCTION \"shape\"(\"value\" \"value\"[])\nRETURNS \"value\"[]", sql);
        Assert.Contains("RETURNS SETOF \"value\"", sql);
        Assert.ContainsSingle(sql.Split('\n').Where(static line => line == "CREATE TYPE \"value\";"));
        string managed = string.Join('\n', compilation.SyntaxTrees.Select(static tree => tree.ToString()));
        Assert.Contains(".ReadVarlena<global::Value>()", managed);
        Assert.Contains(".ReadOwnedVarlena<global::Value>()", managed);
        Assert.Contains("PgTypeRegistry.RegisterNative<global::Value>", managed);
        Assert.DoesNotContain("PgTypeRegistry.RegisterReference<global::Ankus.PgVarlena", managed);
        string native = ManifestValue(compilation, "Ankus.NativeSource");
        Assert.HasCount(2, native.Split("ankus_read_varlena(Datum", StringSplitOptions.None));
    }

    /// <summary>
    /// Retained aggregate arguments use owned conversion rather than a scalar callback borrow.
    /// </summary>
    [TestMethod]
    public void VarlenaOwnershipAggregateArgumentsArePromoted()
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate(VarlenaOwnershipValueSource + """
            [Ankus.PgAggregate]
            public static class Collect
            {
                public static Ankus.PgAggregateState<System.Collections.Generic.List<Ankus.PgVarlena<Value>?>> Transition(
                    Ankus.PgAggregateState<System.Collections.Generic.List<Ankus.PgVarlena<Value>?>>? state, Ankus.PgVarlena<Value>? value)
                { state??=new(new()); state.Value.Add(value); return state; }
                public static int Final(Ankus.PgAggregateState<System.Collections.Generic.List<Ankus.PgVarlena<Value>?>>? state)
                { int total=0; if(state is not null) foreach(var value in state.Value) total+=value?.Value.Number??0; return total; }
            }
            """);
        AssertAggregateCompilation(compilation, diagnostics);
        string managed = string.Join('\n', compilation.SyntaxTrees.Select(static tree => tree.ToString()));
        Assert.Contains(".ReadOwnedVarlena<global::Value>()", managed);
        Assert.DoesNotContain(".ReadVarlena<global::Value>()", managed);
        Assert.Contains("CREATE AGGREGATE \"collect\"(\"value\" \"value\")", ManifestValue(compilation, "Ankus.Sql"));
        Assert.DoesNotContain("ankus_read_varlena(Datum", ManifestValue(compilation, "Ankus.NativeSource"));
    }

    /// <summary>
    /// Promotes every wrapper array element retained by aggregate or deferred iterator callbacks.
    /// </summary>
    /// <param name="array">The vector or shaped representation.</param>
    [TestMethod]
    [DataRow("Ankus.PgVarlena<Value>?[]")]
    [DataRow("Ankus.PgArray<Ankus.PgVarlena<Value>?>")]
    public void VarlenaOwnershipRetainedArrayArgumentsArePromoted(string array)
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate(VarlenaOwnershipValueSource + $$"""
            [Ankus.PgAggregate] public static class Collect
            {
                public static Ankus.PgAggregateState<{{array}}> Transition(Ankus.PgAggregateState<{{array}}>? state, {{array}} value)=>state??new(value);
                public static int Final(Ankus.PgAggregateState<{{array}}>? state)=>state?.Value[0]?.Value.Number??0;
            }
            public static class Functions
            {
                [Ankus.PgFunction] public static System.Collections.Generic.IEnumerable<Ankus.PgVarlena<Value>?> Rows({{array}} values)
                { foreach(var value in values) yield return value; }
            }
            """);
        AssertAggregateCompilation(compilation, diagnostics);
        string managed = string.Join('\n', compilation.SyntaxTrees.Select(static tree => tree.ToString()));
        Assert.Contains(".ReadCallbackArray<global::Ankus.PgVarlena<global::Value>?>()", managed);
        Assert.DoesNotContain("ankus_read_varlena(Datum", ManifestValue(compilation, "Ankus.NativeSource"));
    }

    /// <summary>
    /// Executes generated registration and verifies wrapper and array alternatives point to one canonical value codec.
    /// </summary>
    [TestMethod]
    public void VarlenaOwnershipGeneratedRegistrationExecutes()
    {
        string[] result = RunSerializedProbe<string[]>(VarlenaOwnershipValueSource, """
            var flags=System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic;
            var registry=typeof(Ankus.PgTypeRegistry);
            object canonical=registry.GetMethod("Find",flags)!.Invoke(null,new object[]{typeof(Value)})!;
            object wrapper=registry.GetMethod("Find",flags)!.Invoke(null,new object[]{typeof(Ankus.PgVarlena<Value>)})!;
            object vector=registry.GetMethod("FindArray",flags)!.Invoke(null,new object[]{typeof(Ankus.PgVarlena<Value>[])})!;
            object shape=registry.GetMethod("FindArray",flags)!.Invoke(null,new object[]{typeof(Ankus.PgArray<Ankus.PgVarlena<Value>>)})!;
            var propertyFlags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            bool canonicalAlternate=(bool)canonical.GetType().GetProperty("IsAlternate",propertyFlags)!.GetValue(canonical)!;
            bool wrapperAlternate=(bool)wrapper.GetType().GetProperty("IsAlternate",propertyFlags)!.GetValue(wrapper)!;
            int size=(int)wrapper.GetType().GetProperty("Size",propertyFlags)!.GetValue(wrapper)!;
            var bytes=new System.Buffers.ArrayBufferWriter<byte>(); codec.Write(new Value{Number=42},bytes);
            return new[]{canonicalAlternate.ToString(),wrapperAlternate.ToString(),object.ReferenceEquals(wrapper,vector).ToString(),
                object.ReferenceEquals(wrapper,shape).ToString(),size.ToString(),System.Convert.ToHexString(bytes.WrittenSpan)};
            """);
        Assert.AreSequenceEqual(["False", "True", "True", "True", "4", BitConverter.IsLittleEndian ? "2A000000" : "0000002A"], result);
    }

    /// <summary>
    /// Rejects unsupported wrapper roots and containers before emitting native callbacks.
    /// </summary>
    /// <param name="root">The root declarations.</param>
    /// <param name="parameter">The unsupported function parameter.</param>
    [TestMethod]
    [DataRow("public struct Value { public int Number; }", "Ankus.PgVarlena<Value>")]
    [DataRow("[Ankus.PgType] public struct Value { public int Number; }", "Ankus.PgVarlena<Value>")]
    [DataRow("[Ankus.PgType(typeof(Codec))] public struct Value { public int Number; }", "Ankus.PgVarlena<Value>")]
    [DataRow("public struct Value { public int Number; }", "Ankus.PgVarlena<int>")]
    public void InvalidVarlenaOwnershipRootsAreDiagnosed(string root, string parameter)
    {
        (_, ImmutableArray<Diagnostic> diagnostics) = Generate(root + $$"""
            public sealed class Codec : Ankus.PgTypeCodec<Value>
            {
                public override Value Parse(string text)=>default;
                public override string Format(Value value)=>"value";
                public override Value Read(System.ReadOnlySpan<byte> payload)=>default;
                public override void Write(Value value,System.Buffers.IBufferWriter<byte> output) { }
            }
            public static class Functions { [Ankus.PgFunction] public static int Read({{parameter}} value)=>0; }
            """);
        Diagnostic diagnostic = Assert.ContainsSingle(diagnostics.Where(static value => value.Id == "ANKUS001"));
        Assert.AreEqual(DiagnosticSeverity.Error, diagnostic.Severity);
    }

    /// <summary>
    /// Supplies a valid packed root with a lazy text codec for ownership contracts.
    /// </summary>
    private const string VarlenaOwnershipValueSource = """
        [Ankus.PgType(NativeLayout=true,TextCodec=typeof(TextCodec),BinaryProtocol=true)]
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential,Pack=1)]
        public struct Value { public int Number; }
        public sealed class TextCodec : Ankus.PgTypeTextCodec<Value>
        {
            public override Value Parse(string text)=>new Value{Number=int.Parse(text,System.Globalization.CultureInfo.InvariantCulture)};
            public override string Format(Value value)=>value.Number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        """;
}
