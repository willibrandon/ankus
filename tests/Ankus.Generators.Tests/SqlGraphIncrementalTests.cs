using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Ankus.Generators.Tests;

public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Cached option coordinates resolve against the current tree after an earlier body edit and an unrelated file insertion.
    /// </summary>
    /// <param name="declaration">The declaration with one invalid SQL graph or replacement option.</param>
    /// <param name="expected">The fixed diagnostic.</param>
    /// <param name="span">The exact authored value to correct.</param>
    /// <param name="step">The declaration's cached analysis step, or null for a compilation-unit attribute.</param>
    /// <param name="argument">The authored identity in the fixed message, or null when the message has none.</param>
    [TestMethod]
    [DataRow("public static class Functions { [Ankus.PgFunction(Requires = new[] { \"absent\" })] public static int F() => 1; }",
        "ANKUS498", "\"absent\"", "FunctionAnalysis", "absent")]
    [DataRow("public static class Functions { [Ankus.PgFunction(GenerateSql = false, Sql = \"SELECT 1;\")] public static int F() => 1; }",
        "ANKUS502", "false", "FunctionAnalysis", null)]
    [DataRow("public static class Functions { [Ankus.PgFunction(Sql = \"SELECT '\\0';\")] public static int F() => 1; }",
        "ANKUS503", "\"SELECT '\\0';\"", "FunctionAnalysis", null)]
    [DataRow("public static class Functions { [Ankus.PgFunction(Id = \"\\ud800\")] public static int F() => 1; }",
        "ANKUS492", "\"\\ud800\"", "FunctionAnalysis", null)]
    [DataRow("[Ankus.PgEnum(Id = \" \")] public enum Mood { Happy }", "ANKUS490", "\" \"", "EnumAnalysis", null)]
    [DataRow("[Ankus.PgType(Sql = \"-- @SEND_FUNCTION_NAME@\", BinaryProtocol = false)] public readonly record struct Value(int Number);",
        "ANKUS505", "false", "CustomTypeAnalysis", "@SEND_FUNCTION_NAME@")]
    [DataRow("[Ankus.PgSchema(\"s\", Requires = null!)] public static class Placed;", "ANKUS493", "null!", "SchemaAnalysis", null)]
    [DataRow("[Ankus.PgAggregate(Requires = [\"absent\"], InitialCondition = \"0\")] public sealed class Total : Ankus.IPgAggregate<int,int> " +
        "{ public static int Transition(Ankus.PgAggregateContext context, int state, int value) => state + value; }",
        "ANKUS498", "\"absent\"", "AggregateAnalysis", "absent")]
    [DataRow("[Ankus.PgEnum, Ankus.PgEquality, Ankus.PgOrdering(Requires = [\"absent\"])] public enum Level { Low }",
        "ANKUS498", "\"absent\"", "DatumAnalysis", "absent")]
    [DataRow("public static class Functions { [Ankus.PgOperator(\"@\", Requires = [\"\\0\"])] public static int F(int value) => value; }",
        "ANKUS495", "\"\\0\"", "OperatorCastAnalysis", null)]
    [DataRow("[assembly: Ankus.PgSql(\"a\", \"SELECT 1;\", Requires = [\"absent\"])]", "ANKUS498", "\"absent\"", null, "absent")]
    public void SqlGraphOptionDiagnosticsFollowCurrentTrees(string declaration, string expected, string span, string? step, string? argument)
    {
        // Assembly attributes must precede type declarations, so an earlier comment edit moves them instead of an earlier method body.
        string source = (declaration.StartsWith("[assembly:", StringComparison.Ordinal) ? "// Earlier => 1;\n" :
            "public static class Earlier { public static int Body() => 1; }\n") + declaration;
        CSharpCompilation input = ModuleCompilation(source);
        GeneratorDriver driver = ModuleDriver().RunGeneratorsAndUpdateCompilation(input, out _, out ImmutableArray<Diagnostic> initial, context.CancellationToken);
        Diagnostic previous = Assert.ContainsSingle(initial, string.Join(Environment.NewLine, initial));
        AssertGraphDiagnostic(previous, expected, span, argument is null ? [] : [argument]);
        SyntaxTree current = CSharpSyntaxTree.ParseText(source.Replace("=> 1;", "=> 10000;", StringComparison.Ordinal), path: "Module.cs",
            cancellationToken: context.CancellationToken);
        SyntaxTree unrelated = CSharpSyntaxTree.ParseText("internal static class Unrelated;", path: "Unrelated.cs", cancellationToken: context.CancellationToken);
        driver = driver.RunGeneratorsAndUpdateCompilation(input.RemoveAllSyntaxTrees().AddSyntaxTrees(unrelated, current), out Compilation output,
            out ImmutableArray<Diagnostic> errors, context.CancellationToken);
        Diagnostic error = Assert.ContainsSingle(errors, string.Join(Environment.NewLine, errors));

        AssertGraphDiagnostic(error, expected, span, argument is null ? [] : [argument]);
        Assert.AreSame(current, error.Location.SourceTree);
        Assert.AreEqual(previous.Location.SourceSpan.Start + 4, error.Location.SourceSpan.Start);
        AssertNoSqlManifest(output);
        if (step is not null)
        {
            Assert.Contains(ModuleStep(driver, step), [IncrementalStepRunReason.Cached, IncrementalStepRunReason.Unchanged]);
        }
    }
}
