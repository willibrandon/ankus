using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Ankus.Generators.Tests;

public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// All geometric scalar and array declarations compile with the correct SQL and native conversion contracts.
    /// </summary>
    [TestMethod]
    [DataRow("PgPoint", "point")]
    [DataRow("PgLineSegment", "lseg")]
    [DataRow("PgLine", "line")]
    [DataRow("PgBox", "box")]
    [DataRow("PgCircle", "circle")]
    [DataRow("PgPath", "path")]
    [DataRow("PgPolygon", "polygon")]
    public void GeometrySignaturesCompile(string type, string sqlType)
    {
        foreach (string managed in new[] { "Ankus." + type, "Ankus." + type + "?", "Ankus." + type + "?[]?", "Ankus.PgArray<Ankus." + type + "?>" })
        {
            (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate($$"""
                #nullable enable
                public static class Functions
                {
                    [Ankus.PgFunction] public static {{managed}} Echo({{managed}} value) => value;
                }
                """);
            Assert.IsEmpty(diagnostics);
            Assert.IsEmpty(compilation.GetDiagnostics(context.CancellationToken).Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
            string expected = managed.Contains('[', StringComparison.Ordinal) || managed.Contains('<', StringComparison.Ordinal) ? sqlType + "[]" : sqlType;
            string sql = ManifestValue(compilation, "Ankus.Sql");
            Assert.Contains("\"value\" " + expected, sql);
            Assert.Contains("RETURNS " + expected + " AS", sql);
        }
    }
}
