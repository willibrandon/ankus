using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Ankus.Generators.Tests;

public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Borrowed views compile as concrete SQL scalars, standalone returns, SETOF and TABLE columns.
    /// </summary>
    /// <param name="type">The managed buffer view.</param>
    /// <param name="sqlType">The fixed PostgreSQL type.</param>
    /// <param name="optional">Whether the signature accepts and returns SQL NULL.</param>
    [TestMethod]
    [DataRow("PgByteaView", "bytea", false)]
    [DataRow("PgByteaView", "bytea", true)]
    [DataRow("PgTextView", "text", false)]
    [DataRow("PgTextView", "text", true)]
    public void BorrowedBufferSignaturesCompileWithConcreteSqlTypes(string type, string sqlType, bool optional)
    {
        string managed = "Ankus." + type + (optional ? "?" : string.Empty);
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate($$"""
            public static class Functions
            {
                [Ankus.PgFunction]
                public static {{managed}} Identity({{managed}} value) => value;
                [Ankus.PgFunction]
                public static {{managed}} Create() => null!;
                [Ankus.PgFunction]
                public static System.Collections.Generic.IEnumerable<{{managed}}> Rows({{managed}} value)
                {
                    yield return value;
                }
                [Ankus.PgFunction]
                public static System.Collections.Generic.IEnumerable<({{managed}} Cell, int Position)> Columns({{managed}} value)
                {
                    yield return (value, 7);
                }
            }
            """);
        AssertMemoryCompilationSucceeds(compilation, diagnostics);
        string[] statements = [.. OperatorCastStatements(compilation)];
        Assert.HasCount(4, statements);
        string identity = statements.Single(static statement => statement.StartsWith("CREATE FUNCTION \"identity\"(", StringComparison.Ordinal));
        Assert.Contains($"(\"value\" {sqlType}) RETURNS {sqlType} AS ", identity);
        Assert.Contains(optional ? " CALLED ON NULL INPUT " : " STRICT ", identity);
        Assert.Contains(statement => statement.Contains($"CREATE FUNCTION \"create\"() RETURNS {sqlType} AS ", StringComparison.Ordinal), statements);
        Assert.Contains(statement => statement.Contains($"CREATE FUNCTION \"rows\"(\"value\" {sqlType}) RETURNS SETOF {sqlType} AS ", StringComparison.Ordinal), statements);
        Assert.Contains(statement => statement.Contains($"CREATE FUNCTION \"columns\"(\"value\" {sqlType}) RETURNS TABLE", StringComparison.Ordinal), statements);
    }

    /// <summary>
    /// Buffer views cannot silently become copied SQL array element conversions.
    /// </summary>
    /// <param name="type">The unsupported nested view representation.</param>
    [TestMethod]
    [DataRow("Ankus.PgByteaView[]")]
    [DataRow("Ankus.PgTextView[]")]
    [DataRow("Ankus.PgArray<Ankus.PgByteaView>")]
    [DataRow("Ankus.PgArray<Ankus.PgTextView>")]
    public void BorrowedBufferArraysRequireAnExplicitSupportedRepresentation(string type)
    {
        (_, ImmutableArray<Diagnostic> diagnostics) = Generate($$"""
            public static class Functions
            {
                [Ankus.PgFunction]
                public static int Value({{type}} values) => 0;
            }
            """);
        AssertVirtualContextDiagnostic(diagnostics, "ANKUS001");
    }
}
