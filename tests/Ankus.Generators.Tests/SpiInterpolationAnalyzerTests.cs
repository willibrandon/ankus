using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Ankus.Generators.Tests;

/// <summary>
/// Verifies unsafe raw interpolation is rejected against real static and scoped SPI APIs.
/// </summary>
/// <param name="context">The test cancellation context.</param>
[TestClass]
public sealed class SpiInterpolationAnalyzerTests(TestContext context)
{
    /// <summary>
    /// Every raw SQL entry point rejects a runtime value formatted directly into its command text.
    /// </summary>
    /// <param name="expression">The consumer invocation.</param>
    [TestMethod]
    [DataRow("Spi.Execute($\"SELECT {value}\")")]
    [DataRow("Spi.ExecuteScalar<string>($\"SELECT {value}\")")]
    [DataRow("Spi.ExecuteScalars<string, string>($\"SELECT {value}\")")]
    [DataRow("Spi.Query($\"SELECT {value}\")")]
    [DataRow("Spi.Select($\"SELECT {value}\")")]
    [DataRow("Spi.SelectRaw($\"SELECT {value}\")")]
    [DataRow("Spi.QueryRaw($\"SELECT {value}\")")]
    [DataRow("Spi.Explain($\"SELECT {value}\")")]
    [DataRow("Spi.OpenCursor($\"SELECT {value}\")")]
    [DataRow("Spi.Prepare($\"SELECT {value}\")")]
    [DataRow("Spi.PrepareWithTypeOids($\"SELECT {value}\")")]
    [DataRow("session.Execute($\"SELECT {value}\")")]
    [DataRow("session.ExecuteScalar<string>($\"SELECT {value}\")")]
    [DataRow("session.ExecuteScalars<string, string, string>($\"SELECT {value}\")")]
    [DataRow("session.Query($\"SELECT {value}\")")]
    [DataRow("session.Select($\"SELECT {value}\")")]
    [DataRow("session.SelectRaw($\"SELECT {value}\")")]
    [DataRow("session.QueryRaw($\"SELECT {value}\")")]
    [DataRow("session.Explain($\"SELECT {value}\")")]
    [DataRow("session.OpenCursor($\"SELECT {value}\")")]
    [DataRow("session.Prepare($\"SELECT {value}\")")]
    [DataRow("session.PrepareWithTypeOids($\"SELECT {value}\")")]
    [DataRow("Spi.Query(limit: 1, commandText: $\"SELECT {value}\", readOnly: false)")]
    [DataRow("Spi.Connect(active => active.Execute($\"SELECT {value}\"))")]
    public async Task RawInterpolationIsAnError(string expression)
        => await AssertRejected(expression, "$\"SELECT {value}\"");

    /// <summary>
    /// Casts, concatenation, formatting and raw literals cannot hide direct interpolation.
    /// </summary>
    /// <param name="expression">The command expression.</param>
    [TestMethod]
    [DataRow("(string)$\"SELECT {value}\"")]
    [DataRow("\"SELECT \" + $\"{value}\"")]
    [DataRow("$\"SELECT {value}\" + \";\"")]
    [DataRow("$\"SELECT {number:000}\"")]
    [DataRow("$\"SELECT {number,10}\"")]
    [DataRow("$\"\"\"SELECT {value}\"\"\"")]
    public async Task ExpressionWrappersRetainTheError(string expression)
        => await AssertRejected("Spi.Execute(" + expression + ")", expression);

    /// <summary>
    /// Literal SQL, explicit parameters and Spi.Sql retain their supported overloads without false positives.
    /// </summary>
    /// <param name="expression">The valid consumer expression.</param>
    [TestMethod]
    [DataRow("Spi.Execute(\"SELECT 42\")")]
    [DataRow("Spi.Execute($\"SELECT 42\")")]
    [DataRow("Spi.Execute($\"SELECT '{\"safe\"}'\")")]
    [DataRow("Spi.Execute(value)")]
    [DataRow("Spi.Execute(\"SELECT $1\", SpiParameter.Create(value))")]
    [DataRow("Spi.Execute(Spi.Sql($\"SELECT {value}\"))")]
    [DataRow("session.Execute(Spi.Sql($\"SELECT {value}\"))")]
    [DataRow("Spi.OpenCursor(Spi.Sql($\"SELECT {value}\"), readOnly: false)")]
    [DataRow("session.Query(Spi.Sql($\"SELECT {value}\"), readOnly: true)")]
    [DataRow("Spi.Prepare(\"SELECT $1\", typeof(string))")]
    [DataRow("Spi.ExecuteScalar<int>(\"SELECT 42::\" + Spi.QuoteQualifiedIdentifier(\"pg_catalog\", \"int4\"))")]
    public async Task SafeCommandsRemainValid(string expression)
        => Assert.IsEmpty(await Analyze(expression));

    /// <summary>
    /// Similar names outside the actual SPI type must not become PostgreSQL compiler errors.
    /// </summary>
    [TestMethod]
    public async Task UnrelatedCommandTextOverloadsRemainValid()
        => Assert.IsEmpty(await Analyze("Other.Execute($\"SELECT {value}\")",
            "internal static class Other { internal static void Execute(string commandText) { } }"));

    /// <summary>
    /// Asserts the exact diagnostic and current expression location rather than merely counting errors.
    /// </summary>
    /// <param name="invocation">The raw SQL invocation.</param>
    /// <param name="expression">The unsafe argument's exact source text.</param>
    private async Task AssertRejected(string invocation, string expression)
    {
        Diagnostic error = Assert.ContainsSingle(await Analyze(invocation));
        Assert.AreEqual("ANKUS044", error.Id);
        Assert.AreEqual(DiagnosticSeverity.Error, error.Severity);
        Assert.AreEqual(expression, error.Location.SourceTree!.GetText(context.CancellationToken)
            .ToString(error.Location.SourceSpan));
        Assert.Contains("Spi.Sql", error.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
        Assert.AreEqual("https://willibrandon.github.io/ankus/spi/#parameterized-interpolation", error.Descriptor.HelpLinkUri);
    }

    /// <summary>
    /// Compiles an actual Runtime consumer and invokes the shipped compiler analyzer.
    /// </summary>
    /// <param name="expression">The body expression.</param>
    /// <param name="extra">Optional unrelated declarations.</param>
    /// <returns>The analyzer's diagnostics after requiring a valid C# compilation.</returns>
    private async Task<ImmutableArray<Diagnostic>> Analyze(string expression, string extra = "")
    {
        string source = "using Ankus; internal static class Consumer { internal static void Invoke(string value, int number, SpiSession session) { "
            + expression + "; } } " + extra;
        string platform = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;
        IEnumerable<MetadataReference> references = platform.Split(Path.PathSeparator).Append(typeof(Spi).Assembly.Location)
            .Distinct(StringComparer.Ordinal).Select(static path => MetadataReference.CreateFromFile(path));
        CSharpCompilation compilation = CSharpCompilation.Create("SpiAnalyzerConsumer",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp14), path: "Consumer.cs",
                cancellationToken: context.CancellationToken)], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        Assert.IsEmpty(compilation.GetDiagnostics(context.CancellationToken));
        var analysis = new CompilationWithAnalyzers(compilation, [new SpiInterpolationAnalyzer()],
            new CompilationWithAnalyzersOptions(new AnalyzerOptions([]), onAnalyzerException: null,
                concurrentAnalysis: true, logAnalyzerExecutionTime: false));
        return await analysis.GetAnalyzerDiagnosticsAsync(context.CancellationToken);
    }
}
