using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Ankus.Generators.Tests;

public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Compiles scalar and set dispatch with actual SQL polymorphic identities and NULL policies.
    /// </summary>
    /// <param name="type">The managed polymorphic type.</param>
    /// <param name="sqlType">The SQL pseudotype.</param>
    /// <param name="optional">Whether SQL NULL reaches managed code.</param>
    [TestMethod]
    [DataRow("PgAnyElement", "anyelement", false)]
    [DataRow("PgAnyElement", "anyelement", true)]
    [DataRow("PgAnyArray", "anyarray", false)]
    [DataRow("PgAnyArray", "anyarray", true)]
    public void PolymorphicSignaturesCompileWithExactSqlTypes(string type, string sqlType, bool optional)
    {
        string managed = "Ankus." + type + (optional ? "?" : string.Empty);
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate($$"""
            public static class Functions
            {
                [Ankus.PgFunction]
                public static {{managed}} Identity({{managed}} value) => value;
                [Ankus.PgFunction]
                public static System.Collections.Generic.IEnumerable<{{managed}}> Rows({{managed}} value)
                {
                    yield return value;
                }
            }
            """);
        AssertMemoryCompilationSucceeds(compilation, diagnostics);
        string[] statements = [.. OperatorCastStatements(compilation)];
        Assert.HasCount(2, statements);
        Assert.Contains($"CREATE FUNCTION \"identity\"(\"value\" {sqlType}) RETURNS {sqlType} AS ", statements[0]);
        Assert.Contains($"CREATE FUNCTION \"rows\"(\"value\" {sqlType}) RETURNS SETOF {sqlType} AS ", statements[1]);
        foreach (string statement in statements)
        {
            Assert.Contains(optional ? " CALLED ON NULL INPUT " : " STRICT ", statement);
        }
    }

    /// <summary>
    /// Rejects signatures whose concrete return type PostgreSQL cannot infer and invalid wrapper arrays.
    /// </summary>
    /// <param name="method">The invalid signature.</param>
    /// <param name="diagnostic">The expected diagnostic.</param>
    [TestMethod]
    [DataRow("public static Ankus.PgAnyElement Value(int input) => null!;", "ANKUS004")]
    [DataRow("public static System.Collections.Generic.IEnumerable<Ankus.PgAnyArray> Value() => null!;", "ANKUS004")]
    [DataRow("public static int Value(Ankus.PgAnyElement[] inputs) => 0;", "ANKUS001")]
    public void InvalidPolymorphicSignaturesAreDiagnosed(string method, string diagnostic)
    {
        (_, ImmutableArray<Diagnostic> diagnostics) = Generate("public static class Functions { [Ankus.PgFunction] " + method + " }");
        AssertVirtualContextDiagnostic(diagnostics, diagnostic);
    }
}
