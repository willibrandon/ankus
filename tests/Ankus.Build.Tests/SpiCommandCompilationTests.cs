using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Ankus.Build.Tests;

/// <summary>
/// Verifies compiler lowering and overload compatibility for explicitly parameterized SQL.
/// </summary>
/// <param name="context">The compiler cancellation context.</param>
[TestClass]
public sealed class SpiCommandCompilationTests(TestContext context)
{
    /// <summary>
    /// Literal and interpolated SQL can create a command without formatting parameters as text.
    /// </summary>
    /// <param name="expression">The user-authored factory expression.</param>
    [TestMethod]
    [DataRow("Spi.Sql($\"SELECT {value}\")")]
    [DataRow("Spi.Sql($\"SELECT 42\")")]
    [DataRow("Spi.Sql($\"\")")]
    public void CompilerAcceptsLiteralAndParameterizedCommands(string expression)
    {
        CSharpCompilation compilation = Compile(expression);
        Assert.IsEmpty(compilation.GetDiagnostics(context.CancellationToken));
    }

    /// <summary>
    /// C# formatting and alignment cannot silently turn a binding into SQL text.
    /// </summary>
    /// <param name="expression">The unsupported interpolation.</param>
    /// <param name="argument">The compiler-rejected formatting argument.</param>
    [TestMethod]
    [DataRow("Spi.Sql($\"SELECT {value:000}\")", "format")]
    [DataRow("Spi.Sql($\"SELECT {value,10}\")", "alignment")]
    [DataRow("Spi.Sql($\"SELECT {value,10:000}\")", "alignment")]
    public void CompilerRejectsFormattingAndAlignment(string expression, string argument)
    {
        CSharpCompilation compilation = Compile(expression);
        Diagnostic error = Assert.ContainsSingle(compilation.GetDiagnostics(context.CancellationToken));
        Assert.AreEqual(DiagnosticSeverity.Error, error.Severity);
        Assert.AreEqual("CS1739", error.Id);
        Assert.Contains(argument, error.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Existing string interpolation retains string semantics; only an explicit command selects the new overload.
    /// </summary>
    /// <param name="expression">The old or new execution expression.</param>
    /// <param name="expectedType">The first parameter's resolved type.</param>
    [TestMethod]
    [DataRow("Spi.Execute($\"SELECT {value}\")", "String")]
    [DataRow("Spi.Execute(Spi.Sql($\"SELECT {value}\"))", "SpiCommand")]
    public void CompilerPreservesExplicitOverloadChoice(string expression, string expectedType)
    {
        CSharpCompilation compilation = Compile(expression);
        Assert.IsEmpty(compilation.GetDiagnostics(context.CancellationToken));
        SyntaxTree tree = Assert.ContainsSingle(compilation.SyntaxTrees);
        MethodDeclarationSyntax declaration = Assert.ContainsSingle(tree.GetRoot(context.CancellationToken)
            .DescendantNodes().OfType<MethodDeclarationSyntax>());
        InvocationExpressionSyntax invocation = Assert.IsInstanceOfType<InvocationExpressionSyntax>(declaration.ExpressionBody?.Expression);
        IMethodSymbol method = Assert.IsInstanceOfType<IMethodSymbol>(compilation.GetSemanticModel(tree)
            .GetSymbolInfo(invocation, context.CancellationToken).Symbol);
        Assert.AreEqual("Execute", method.Name);
        Assert.AreEqual("Ankus.Spi", method.ContainingType.ToDisplayString());
        Assert.AreEqual(expectedType, method.Parameters[0].Type.Name);
    }

    /// <summary>
    /// Compiles an ordinary consumer against the actual Runtime assembly and platform references.
    /// </summary>
    /// <param name="expression">The consumer expression.</param>
    /// <returns>The compilation with the repository's supported stable language version.</returns>
    private CSharpCompilation Compile(string expression)
    {
        string source = "using Ankus; internal static class Consumer { internal static object Invoke(int value) => " + expression + "; }";
        string platform = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;
        IEnumerable<MetadataReference> references = platform.Split(Path.PathSeparator).Append(typeof(Spi).Assembly.Location)
            .Distinct(StringComparer.Ordinal).Select(static path => MetadataReference.CreateFromFile(path));
        return CSharpCompilation.Create("SpiCommandConsumer",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp14), cancellationToken: context.CancellationToken)],
            references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable,
                generalDiagnosticOption: ReportDiagnostic.Error));
    }
}
