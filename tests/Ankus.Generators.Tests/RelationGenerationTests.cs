using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Ankus.Generators.Tests;

public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Scalar and shaped relation signatures compile into exact regclass declarations and ownership scopes.
    /// </summary>
    /// <param name="managed">The managed signature.</param>
    /// <param name="sqlType">The PostgreSQL signature.</param>
    [TestMethod]
    [DataRow("Ankus.PgRelation", "regclass")]
    [DataRow("Ankus.PgRelation?", "regclass")]
    [DataRow("Ankus.PgRelation[]", "regclass[]")]
    [DataRow("Ankus.PgRelation?[]?", "regclass[]")]
    [DataRow("Ankus.PgArray<Ankus.PgRelation?>", "regclass[]")]
    public void RelationSignaturesCompile(string managed, string sqlType)
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate($$"""
            #nullable enable
            public static class Functions
            {
                [Ankus.PgFunction] public static {{managed}} Echo({{managed}} value) => value;
            }
            """);
        Assert.IsEmpty(diagnostics, string.Join("\n", diagnostics));
        Assert.IsEmpty(compilation.GetDiagnostics(context.CancellationToken)
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        string sql = ManifestValue(compilation, "Ankus.Sql");
        Assert.Contains("\"value\" " + sqlType, sql);
        Assert.Contains("RETURNS " + sqlType + " AS", sql);
        string managedSource = string.Join("\n", compilation.SyntaxTrees.Select(static tree => tree.ToString()));
        Assert.Contains("using var relationScope = new global::Ankus.NativeRelationScope();", managedSource);
        Assert.Contains("value = relationScope.Add(", managedSource);
    }

    /// <summary>
    /// Set and table callbacks retain relation inputs until iterator disposal and own every relation result column.
    /// </summary>
    [TestMethod]
    public void RelationSetsCompileWithArgumentLifetime()
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            #nullable enable
            public static class Functions
            {
                [Ankus.PgFunction]
                public static System.Collections.Generic.IEnumerable<Ankus.PgRelation?> Values(Ankus.PgRelation? relation)
                { yield return relation; }
                [Ankus.PgFunction]
                public static System.Collections.Generic.IEnumerable<(Ankus.PgRelation? relation, Ankus.PgRelation?[] many)> Rows(Ankus.PgRelation input)
                { yield return (input.Clone(), new[] { input.Clone() }); }
            }
            """);
        Assert.IsEmpty(diagnostics, string.Join("\n", diagnostics));
        Assert.IsEmpty(compilation.GetDiagnostics(context.CancellationToken)
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        string sql = ManifestValue(compilation, "Ankus.Sql");
        Assert.Contains("RETURNS SETOF regclass", sql);
        Assert.Contains("\"relation\" regclass", sql);
        Assert.Contains("\"many\" regclass[]", sql);
        string source = string.Join("\n", compilation.SyntaxTrees.Select(static tree => tree.ToString()));
        Assert.Contains("relationScope.Detach()", source);
        Assert.Contains("resultRelations.Add(value.@relation)", source);
        Assert.Contains("resultRelations.Add(value.@many)", source);
    }

    /// <summary>
    /// Aggregate state and input relations compile with the same reference ownership as scalar callbacks.
    /// </summary>
    [TestMethod]
    public void RelationAggregateSignaturesCompile()
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            #nullable enable
            [Ankus.PgAggregate]
            public static class LastRelation
            {
                [Ankus.PgFunction]
                public static Ankus.PgRelation? Transition(Ankus.PgRelation? state, Ankus.PgRelation? value) => value ?? state;
            }
            """);
        Assert.IsEmpty(diagnostics, string.Join("\n", diagnostics));
        Assert.IsEmpty(compilation.GetDiagnostics(context.CancellationToken)
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        string sql = ManifestValue(compilation, "Ankus.Sql");
        Assert.Contains("STYPE = regclass", sql);
        string source = string.Join("\n", compilation.SyntaxTrees.Select(static tree => tree.ToString()));
        Assert.Contains("using var relationScope = new global::Ankus.NativeRelationScope();", source);
        Assert.Contains("value = relationScope.Add(", source);
    }
}
