using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;

namespace Ankus.Generators.Tests;

/// <summary>
/// Checks general internal signatures, generated state transport, and PostgreSQL's type-safety rule.
/// </summary>
public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Compiles scalar internal arguments and results without interpreting their native pointers.
    /// </summary>
    [TestMethod]
    public void InternalFunctionSignaturesCompileWithCheckedTransport()
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            public static class Functions
            {
                [Ankus.PgFunction]
                public static Ankus.PgInternal? Identity(Ankus.PgInternal? value) => value;
                [Ankus.PgFunction]
                public static int Read(Ankus.PgInternal value) => value.Get<int>();
                [Ankus.PgFunction]
                public static System.Collections.Generic.IEnumerable<Ankus.PgInternal?> States(Ankus.PgInternal? value)
                    => [value];
                [Ankus.PgFunction]
                public static System.Collections.Generic.IEnumerable<(Ankus.PgInternal? State, int Count)> Rows(Ankus.PgInternal? value)
                    => [(value, 1)];
            }
            """);
        AssertAggregateCompilation(compilation, diagnostics);
        string sql = ManifestValue(compilation, "Ankus.Sql");
        Assert.Contains("\"value\" internal", sql);
        Assert.Contains("RETURNS internal", sql);
        Assert.Contains("RETURNS SETOF internal", sql);
        Assert.Contains("RETURNS TABLE (\"state\" internal, \"count\" integer)", sql);
        string native = ManifestValue(compilation, "Ankus.NativeSource");
        Assert.Contains("parameter.type_oid = INTERNALOID;", native);
        Assert.Contains("(int64) (uintptr_t) PG_GETARG_DATUM(0)", native);
    }

    /// <summary>
    /// Compiles general internal aggregate state with parallel transport and the native deserializer dummy.
    /// </summary>
    [TestMethod]
    public void GeneralInternalAggregateStateCompilesWithSerialization()
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            [Ankus.PgAggregate(ParallelSafety = Ankus.PgParallelSafety.Safe)]
            public static class Total
            {
                public static Ankus.PgInternal Transition(Ankus.PgInternal? state, int value)
                    => state ?? Ankus.PgInternal.Create(value);
                public static int Final(Ankus.PgInternal? state) => state?.Get<int>() ?? 0;
                public static Ankus.PgInternal? Combine(Ankus.PgInternal? left, Ankus.PgInternal? right)
                    => left ?? (right is null ? null : Ankus.PgInternal.Create(right.Get<int>()));
                public static byte[] Serialize(Ankus.PgInternal state) => [1];
                public static Ankus.PgInternal Deserialize(byte[] bytes) => Ankus.PgInternal.Create(1);
            }
            """);
        AssertAggregateCompilation(compilation, diagnostics);
        string sql = ManifestValue(compilation, "Ankus.Sql");
        Assert.Contains("STYPE = internal", sql);
        Assert.Contains("\"bytes\" bytea, internal", sql);
        Assert.Contains("SERIALFUNC", sql);
        Assert.Contains("DESERIALFUNC", sql);
    }

    /// <summary>
    /// Rejects ordinary functions that expose internal results without an internal SQL argument.
    /// </summary>
    /// <param name="result">The otherwise valid managed result declaration.</param>
    [TestMethod]
    [DataRow("Ankus.PgInternal?")]
    [DataRow("System.Collections.Generic.IEnumerable<Ankus.PgInternal?>")]
    [DataRow("System.Collections.Generic.IEnumerable<(int Count, Ankus.PgInternal? State)>")]
    public void InternalResultsRequireInternalInputs(string result)
    {
        (_, ImmutableArray<Diagnostic> diagnostics) = Generate($$"""
            public static class Functions
            {
                [Ankus.PgFunction]
                public static {{result}} Invalid(int value) => null!;
            }
            """);
        Diagnostic error = Assert.ContainsSingle(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error, diagnostics);
        Assert.Contains("internal SQL input", error.GetMessage(CultureInfo.InvariantCulture));
    }
}
