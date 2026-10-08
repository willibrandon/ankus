using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Ankus.Generators.Tests;

/// <summary>
/// Verifies attribute applications still being typed produce Ankus diagnostics instead of generator exceptions.
/// </summary>
public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// An aggregate under a container whose schema argument is missing or null reports the schema at its attribute
    /// rather than crashing generation or silently inheriting an outer schema, and independent output is retained.
    /// </summary>
    /// <param name="schema">The incomplete container schema application.</param>
    /// <param name="span">The exact authored syntax the schema diagnostic selects.</param>
    [TestMethod]
    [DataRow("[Ankus.PgSchema]", "Ankus.PgSchema")]
    [DataRow("[Ankus.PgSchema()]", "Ankus.PgSchema()")]
    [DataRow("[Ankus.PgSchema(null!)]", "null!")]
    [DataRow("[Ankus.PgSchema(Create = false)]", "Ankus.PgSchema(Create = false)")]
    public void AggregateContainerWithoutSchemaArgumentReportsSchema(string schema, string span)
    {
        string source = "[Ankus.PgSchema(\"outer\")] public static class Outer { " + schema + " public static class Inner { " +
            "[Ankus.PgAggregate(InitialCondition = \"0\")] public sealed class Bad : Ankus.IPgAggregate<int, int> { " +
            "public static int Transition(Ankus.PgAggregateContext context, int state, int value) => state + value; } } }" +
            OtherAggregateSource;
        CSharpCompilation input = ModuleCompilation(source);
        GeneratorDriver driver = ModuleDriver().RunGeneratorsAndUpdateCompilation(input, out Compilation output,
            out ImmutableArray<Diagnostic> diagnostics, context.CancellationToken);

        AssertNoGeneratorException(driver, diagnostics);
        Assert.HasCount(2, diagnostics);
        int start = source.IndexOf(schema, StringComparison.Ordinal) + schema.IndexOf(span, StringComparison.Ordinal);
        foreach (Diagnostic diagnostic in diagnostics)
        {
            Assert.AreEqual("ANKUS050", diagnostic.Id);
            Assert.AreEqual(start, diagnostic.Location.SourceSpan.Start);
            Assert.AreEqual(span, diagnostic.Location.SourceTree!.GetText(context.CancellationToken).ToString(diagnostic.Location.SourceSpan));
        }

        string sql = InstallationBody(output);
        Assert.Contains("CREATE AGGREGATE \"other\"", sql);
        Assert.DoesNotContain("\"bad\"", sql);
        Assert.DoesNotContain("\"bad_transition\"", sql);
    }

    /// <summary>
    /// Aggregates still inherit the nearest valid container schema.
    /// </summary>
    [TestMethod]
    public void AggregateInheritsNearestContainerSchema()
    {
        string source = "[Ankus.PgSchema(\"outer\")] public static class Outer { [Ankus.PgSchema(\"inner\")] public static class Inner { " +
            "[Ankus.PgAggregate(InitialCondition = \"0\")] public sealed class Sum : Ankus.IPgAggregate<int, int> { " +
            "public static int Transition(Ankus.PgAggregateContext context, int state, int value) => state + value; } } }";
        (Compilation output, ImmutableArray<Diagnostic> diagnostics) = Generate(source);

        Assert.IsEmpty(diagnostics);
        Assert.Contains("CREATE AGGREGATE \"inner\".\"sum\"", InstallationBody(output));
    }

    /// <summary>
    /// Unresolved or malformed PgColumnNames arguments never fault generation. Bound names keep their exact table contract,
    /// and an unbound application reports the specific column-name diagnostic at the attribute.
    /// </summary>
    /// <param name="attribute">The return attribute being typed.</param>
    /// <param name="row">The iterator element type.</param>
    /// <param name="compilerError">The C# error that identifies the unfinished attribute argument.</param>
    /// <param name="id">The expected Ankus diagnostic, or empty when the bound names are valid.</param>
    [TestMethod]
    [DataRow("Ankus.PgColumnNames(\"id\", Bogus = 1)", "int", "CS0246", "")]
    [DataRow("Ankus.PgColumnNames(\"id\", \"other\", Bogus = 1)", "(int, int)", "CS0246", "")]
    [DataRow("Ankus.PgColumnNames(\"id\", Bogus = 1)", "(int Id, int Other)", "CS0246", "ANKUS400")]
    [DataRow("Ankus.PgColumnNames(\"id\", Bogus = )", "int", "CS1525", "")]
    [DataRow("Ankus.PgColumnNames(\"id\", Bogus: 1)", "int", "CS1739", "ANKUS398")]
    [DataRow("Ankus.PgColumnNames(\"id\", Bogus)", "int", "CS0103", "ANKUS398")]
    [DataRow("Ankus.PgColumnNames(\"id\", )", "int", "CS1525", "ANKUS398")]
    public void IncompleteColumnNamesReportWithoutFaulting(string attribute, string row, string compilerError, string id)
    {
        string source = "public static class Functions { [Ankus.PgFunction][return: " + attribute +
            "] public static System.Collections.Generic.IEnumerable<" + row + "> Rows() => []; " +
            "[Ankus.PgFunction] public static int Other() => 1; }";
        CSharpCompilation input = ModuleCompilation(source);
        GeneratorDriver driver = ModuleDriver().RunGeneratorsAndUpdateCompilation(input, out Compilation output,
            out ImmutableArray<Diagnostic> diagnostics, context.CancellationToken);

        AssertNoGeneratorException(driver, diagnostics);
        Assert.Contains(value => value.Id == compilerError, input.GetDiagnostics(context.CancellationToken));
        string sql = InstallationBody(output);
        Assert.Contains("FUNCTION \"other\"()", sql);
        if (id.Length == 0)
        {
            Assert.IsEmpty(diagnostics);
            Assert.Contains(row == "int" ? "RETURNS TABLE (\"id\" integer)" : "RETURNS TABLE (\"id\" integer, \"other\" integer)", sql);
            return;
        }

        Diagnostic diagnostic = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual(id, diagnostic.Id);
        Assert.AreEqual(attribute, diagnostic.Location.SourceTree!.GetText(context.CancellationToken).ToString(diagnostic.Location.SourceSpan));
        Assert.DoesNotContain("\"rows\"", sql);
    }

    /// <summary>
    /// Requires generator completion without the compiler's exception diagnostics.
    /// </summary>
    /// <param name="driver">The executed production generator.</param>
    /// <param name="diagnostics">The generator diagnostics reported to the compilation.</param>
    private static void AssertNoGeneratorException(GeneratorDriver driver, ImmutableArray<Diagnostic> diagnostics)
    {
        Assert.IsNull(Assert.ContainsSingle(driver.GetRunResult().Results).Exception);
        Assert.DoesNotContain(static value => value.Id is "CS8785" or "AD0001", diagnostics);
    }
}
