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
            public static class First
            {
                public static Ankus.{{type}} Transition(Ankus.{{type}} state, Ankus.{{type}} value) => state;
                public static Ankus.{{type}} Combine(Ankus.{{type}} state, Ankus.{{type}} other) => state;
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
            public static class FirstStoredValue
            {
                public static Ankus.PgAggregateState<Ankus.PgAnyElement>? Transition(Ankus.PgAggregateContext context,
                    Ankus.PgAggregateState<Ankus.PgAnyElement>? state, Ankus.PgAnyElement? value)
                    => state ?? (value is null ? null : new(value.CopyTo(context.MemoryContext)));
                public static Ankus.PgAnyElement? Final(Ankus.PgAggregateState<Ankus.PgAnyElement>? state,
                    Ankus.PgAnyElement? witness) => state?.Value;
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
    /// <param name="body">The otherwise valid C# declaration.</param>
    [TestMethod]
    [DataRow("public static Ankus.PgAnyElement? Transition(Ankus.PgAnyElement? state,int value)=>state;")]
    [DataRow("public static Ankus.PgAggregateState<int>? Transition(Ankus.PgAggregateState<int>? state,Ankus.PgAnyElement? value)=>state; public static Ankus.PgAnyElement? Final(Ankus.PgAggregateState<int>? state)=>null;")]
    [DataRow("public static int Transition(int? state,int value)=>0; public static Ankus.PgAnyArray? MovingTransition(Ankus.PgAnyArray? state,int value)=>state; public static Ankus.PgAnyArray? MovingInverse(Ankus.PgAnyArray? state,int value)=>state; public static int MovingFinal(Ankus.PgAnyArray? state)=>0;")]
    public void UnresolvedPolymorphicAggregateSignaturesAreDiagnosed(string body)
        => AssertInvalidAggregate("[Ankus.PgAggregate] public static class Invalid { " + body + " }", "ANKUS012");
}
