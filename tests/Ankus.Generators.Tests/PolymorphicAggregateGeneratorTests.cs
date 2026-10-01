using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Ankus.Generators.Tests;

/// <summary>
/// Verifies compilable polymorphic aggregate declarations and unresolved signature diagnostics.
/// </summary>
public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Compiles aggregate-only declarations with resolved native dispatch and exact SQL pseudotypes.
    /// </summary>
    /// <param name="type">The polymorphic state and input wrapper.</param>
    /// <param name="sqlType">Its SQL pseudotype.</param>
    [TestMethod]
    [DataRow("PgAnyElement", "anyelement")]
    [DataRow("PgAnyArray", "anyarray")]
    public void PolymorphicAggregateStatesCompileWithExactSqlTypes(string type, string sqlType)
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate($$"""
            [Ankus.PgAggregate]
            public sealed class First : Ankus.IPgAggregate<Ankus.{{type}}, Ankus.{{type}}>, Ankus.IPgCombinableAggregate<Ankus.{{type}}>
            {
                public static Ankus.{{type}} Transition(Ankus.PgAggregateContext context, Ankus.{{type}} state, Ankus.{{type}} value) => state;
                public static Ankus.{{type}} Combine(Ankus.PgAggregateContext context, Ankus.{{type}} state, Ankus.{{type}} other) => state;
            }
            """);
        AssertAggregateCompilation(compilation, diagnostics);
        string sql = ManifestValue(compilation, "Ankus.Sql");
        Assert.Contains("STYPE = " + sqlType, sql);
        Assert.Contains("CREATE AGGREGATE \"first\"(\"value\" " + sqlType + ")", sql);
        Assert.Contains("RETURNS " + sqlType, sql);
        string native = ManifestValue(compilation, "Ankus.NativeSource");
        Assert.Contains("static void\nankus_read_polymorphic", native.ReplaceLineEndings("\n"));
        Assert.Contains("const bool polymorphic[2] = {true, true};", native);
    }

    /// <summary>
    /// Extra final slots resolve polymorphic results even when state is managed internal storage.
    /// </summary>
    [TestMethod]
    public void PolymorphicAggregateFinalExtraCompilesWithOwnedStorage()
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            [Ankus.PgAggregate(FinalExtra = true)]
            public sealed class FirstStoredValue : Ankus.IPgAggregate<Ankus.PgAggregateState<Ankus.PgAnyElement>?, Ankus.PgAnyElement?>,
                Ankus.IPgFinalizingAggregate<Ankus.PgAggregateState<Ankus.PgAnyElement>?, System.ValueTuple, Ankus.PgAnyElement?>
            {
                public static Ankus.PgAggregateState<Ankus.PgAnyElement>? Transition(Ankus.PgAggregateContext context,
                    Ankus.PgAggregateState<Ankus.PgAnyElement>? state, Ankus.PgAnyElement? value)
                    => state ?? (value is null ? null : new(value.CopyTo(context.MemoryContext)));
                public static Ankus.PgAnyElement? Final(Ankus.PgAggregateContext context, Ankus.PgAggregateState<Ankus.PgAnyElement>? state,
                    System.ValueTuple arguments) => state?.Value;
            }
            """);
        AssertAggregateCompilation(compilation, diagnostics);
        string sql = ManifestValue(compilation, "Ankus.Sql");
        Assert.Contains("STYPE = internal", sql);
        Assert.Contains("FINALFUNC_EXTRA", sql);
        Assert.Contains("RETURNS anyelement", sql);
    }

    /// <summary>
    /// Rejects aggregate or support results whose concrete type cannot be determined by PostgreSQL.
    /// </summary>
    /// <param name="contracts">The compiler-checked aggregate capabilities.</param>
    /// <param name="body">The otherwise valid C# declaration.</param>
    [TestMethod]
    [DataRow("Ankus.IPgAggregate<Ankus.PgAnyElement?, int>",
        "public static Ankus.PgAnyElement? Transition(Ankus.PgAggregateContext context,Ankus.PgAnyElement? state,int value)=>state;")]
    [DataRow("Ankus.IPgAggregate<Ankus.PgAggregateState<int>?, Ankus.PgAnyElement?>, Ankus.IPgFinalizingAggregate<Ankus.PgAggregateState<int>?, System.ValueTuple, Ankus.PgAnyElement?>",
        "public static Ankus.PgAggregateState<int>? Transition(Ankus.PgAggregateContext context,Ankus.PgAggregateState<int>? state,Ankus.PgAnyElement? value)=>state; public static Ankus.PgAnyElement? Final(Ankus.PgAggregateContext context,Ankus.PgAggregateState<int>? state,System.ValueTuple arguments)=>null;")]
    [DataRow("Ankus.IPgAggregate<int?, int>, Ankus.IPgMovingAggregate<Ankus.PgAnyArray?, int>, Ankus.IPgMovingFinalizingAggregate<Ankus.PgAnyArray?, System.ValueTuple, int?>",
        "public static int? Transition(Ankus.PgAggregateContext context,int? state,int value)=>0; public static Ankus.PgAnyArray? MovingTransition(Ankus.PgAggregateContext context,Ankus.PgAnyArray? state,int value)=>state; public static Ankus.PgAnyArray? MovingInverse(Ankus.PgAggregateContext context,Ankus.PgAnyArray? state,int value)=>state; public static int? MovingFinal(Ankus.PgAggregateContext context,Ankus.PgAnyArray? state,System.ValueTuple arguments)=>0;")]
    public void UnresolvedPolymorphicAggregateSignaturesAreDiagnosed(string contracts, string body)
        => AssertInvalidAggregate("[Ankus.PgAggregate] public sealed class Invalid : " + contracts + " { " + body + " }", "ANKUS012");
}
