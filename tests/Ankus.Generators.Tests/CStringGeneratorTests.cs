using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Ankus.Generators.Tests;

public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Owned and borrowed C strings compile to concrete native cstring callbacks without text transcoding.
    /// </summary>
    /// <param name="type">The exact managed representation.</param>
    /// <param name="optional">Whether SQL NULL is accepted and returned.</param>
    [TestMethod]
    [DataRow("PgCString", false)]
    [DataRow("PgCString", true)]
    [DataRow("PgCStringView", false)]
    [DataRow("PgCStringView", true)]
    public void CStringSignaturesCompileWithConcreteTypes(string type, bool optional)
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
            }
            """);
        AssertMemoryCompilationSucceeds(compilation, diagnostics);
        string[] statements = [.. OperatorCastStatements(compilation)];
        Assert.HasCount(3, statements);
        string identity = statements.Single(static statement => statement.StartsWith("CREATE FUNCTION \"identity\"(", StringComparison.Ordinal));
        Assert.Contains("(\"value\" cstring) RETURNS cstring AS ", identity);
        Assert.Contains(optional ? " CALLED ON NULL INPUT " : " STRICT ", identity);
        Assert.Contains(statement => statement.Contains("RETURNS SETOF cstring AS ", StringComparison.Ordinal), statements);
        string generated = DatumMappingManaged(compilation);
        Assert.Contains(type == "PgCString" ? "ReadCString()" : "ReadBorrowedCString()", generated);
        Assert.Contains(type == "PgCString" ? "NativeValue.FromCString(" : "ReadOwnedCStringView()", generated);
        Assert.DoesNotContain(".ReadString()", generated);
        Assert.DoesNotContain("NativeValue.FromString(", generated);
    }

    /// <summary>
    /// C-string arrays retain their concrete element identity through owned vectors, shapes and lazy views.
    /// </summary>
    /// <param name="type">The supported managed collection.</param>
    [TestMethod]
    [DataRow("Ankus.PgCString?[]")]
    [DataRow("Ankus.PgArray<Ankus.PgCString?>")]
    [DataRow("Ankus.PgArrayView<Ankus.PgCString?>")]
    [DataRow("Ankus.PgArrayView<Ankus.PgCStringView?>")]
    public void CStringArraySignaturesKeepElementIdentity(string type)
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate($$"""
            public static class Functions
            {
                [Ankus.PgFunction]
                public static {{type}}? Identity({{type}}? value) => value;
            }
            """);
        AssertMemoryCompilationSucceeds(compilation, diagnostics);
        string statement = Assert.ContainsSingle(OperatorCastStatements(compilation));
        Assert.Contains("(\"value\" cstring[]) RETURNS cstring[] AS ", statement);
        Assert.Contains(" CALLED ON NULL INPUT ", statement);
    }
}
