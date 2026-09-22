using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Ankus.Generators.Tests;

public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// All supported range subtype aliases compile as nullable scalars, vectors and shaped arrays with exact SQL types.
    /// </summary>
    [TestMethod]
    [DataRow("int", "int4range")]
    [DataRow("long", "int8range")]
    [DataRow("decimal", "numrange")]
    [DataRow("Ankus.PgNumeric", "numrange")]
    [DataRow("Ankus.PgDate", "daterange")]
    [DataRow("System.DateOnly", "daterange")]
    [DataRow("Ankus.PgTimestamp", "tsrange")]
    [DataRow("System.DateTime", "tsrange")]
    [DataRow("Ankus.PgTimestampTz", "tstzrange")]
    [DataRow("System.DateTimeOffset", "tstzrange")]
    public void RangeSignaturesCompile(string subtype, string sqlType)
    {
        string range = "Ankus.PgRange<" + subtype + ">";
        foreach (string managed in new[] { range, range + "?", range + "?[]?", "Ankus.PgArray<" + range + "?>" })
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
            string expected = managed.Contains('[', StringComparison.Ordinal) || managed.StartsWith("Ankus.PgArray", StringComparison.Ordinal) ? sqlType + "[]" : sqlType;
            string sql = ManifestValue(compilation, "Ankus.Sql");
            Assert.Contains("\"value\" " + expected, sql);
            Assert.Contains("RETURNS " + expected + " AS", sql);
        }
    }

    /// <summary>
    /// Unsupported range subtypes fail at generation instead of producing an uncallable native export.
    /// </summary>
    [TestMethod]
    [DataRow("double")]
    [DataRow("short")]
    [DataRow("Ankus.PgTime")]
    [DataRow("System.Guid")]
    public void UnsupportedRangeSubtypesAreDiagnosed(string subtype)
    {
        (_, ImmutableArray<Diagnostic> diagnostics) = Generate($$"""
            public static class Functions
            {
                [Ankus.PgFunction] public static Ankus.PgRange<{{subtype}}> Echo(Ankus.PgRange<{{subtype}}> value) => value;
            }
            """);
        Assert.IsNotEmpty(diagnostics);
        Assert.IsTrue(diagnostics.All(static diagnostic => diagnostic.Id == "ANKUS001"));
    }
}
