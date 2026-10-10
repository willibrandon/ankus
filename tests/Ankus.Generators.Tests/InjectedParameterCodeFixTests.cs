using System.Collections.Immutable;
using System.Composition.Hosting;
using Ankus.CodeFixes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Ankus.Generators.Tests;

public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// The IDE discovers the provider through its actual composition export rather than a test-only constructor path.
    /// </summary>
    [TestMethod]
    public void InjectedParameterFixIsExportedForEditorDiscovery()
    {
        using CompositionHost container = new ContainerConfiguration().WithAssembly(typeof(InjectedParameterCodeFixProvider).Assembly).CreateContainer();
        Lazy<CodeFixProvider, IDictionary<string, object>> export =
            Assert.ContainsSingle(container.GetExports<Lazy<CodeFixProvider, IDictionary<string, object>>>()
                .Where(static value => (string)value.Metadata["Name"] == nameof(InjectedParameterCodeFixProvider)));
        CodeFixProvider provider = export.Value;

        Assert.IsInstanceOfType<InjectedParameterCodeFixProvider>(provider);
        Assert.AreEqual(nameof(InjectedParameterCodeFixProvider), Assert.IsInstanceOfType<string>(export.Metadata["Name"]));
        Assert.AreSequenceEqual([LanguageNames.CSharp], Assert.IsInstanceOfType<IEnumerable<string>>(export.Metadata["Languages"]));
        Assert.AreSequenceEqual(["ANKUS056"], provider.FixableDiagnosticIds);
        Assert.AreSame(provider, Assert.ContainsSingle(container.GetExports<CodeFixProvider>().OfType<InjectedParameterCodeFixProvider>()));
    }

    /// <summary>
    /// An IDE correction removes only SQL metadata from injected contexts and preserves real SQL arguments and implementation code.
    /// </summary>
    /// <param name="parameter">The authored injected parameter.</param>
    /// <param name="retainedAttribute">The unrelated attribute that must remain, or null.</param>
    /// <param name="expected">The exact corrected parameter text.</param>
    [TestMethod]
    [DataRow("[Ankus.PgParameter(Name = \"ignored\")] Ankus.PgFunctionContext call", null, " Ankus.PgFunctionContext call")]
    [DataRow("[Ankus.PgParameter] Ankus.PgMemoryContext owner", null, " Ankus.PgMemoryContext owner")]
    [DataRow("[A(Name = \"ignored\")] Ankus.PgFunctionContext? call = null", null, " Ankus.PgFunctionContext? call = null")]
    [DataRow("[Keep, Ankus.PgParameter(Default = \"0\")] Ankus.PgFunctionContext call", "Keep", "[Keep] Ankus.PgFunctionContext call")]
    [DataRow("[Ankus.PgParameter, Keep] Ankus.PgMemoryContext owner", "Keep", "[Keep] Ankus.PgMemoryContext owner")]
    [DataRow("[Ankus.PgParameter][Keep] Ankus.PgFunctionContext call", "Keep", "[Keep] Ankus.PgFunctionContext call")]
    [DataRow("[Ankus.PgParameter(Name = \"first\"), Ankus.PgParameter(Default = \"second\")] Ankus.PgFunctionContext call", null,
        " Ankus.PgFunctionContext call")]
    [DataRow("/* before */ [Ankus.PgParameter] /* after */ Ankus.PgMemoryContext owner", null,
        "/* before */ /* after */ Ankus.PgMemoryContext owner")]
    public async Task InjectedParameterFixPreservesSqlAndManagedContracts(string parameter, string? retainedAttribute, string expected)
    {
        string source = """
            using A = Ankus.PgParameterAttribute;
            [System.AttributeUsage(System.AttributeTargets.Parameter)]
            public sealed class KeepAttribute : System.Attribute;
            public static class Functions
            {
                [Ankus.PgFunction]
                public static int Apply([Ankus.PgParameter(Name = "input", Default = "41")] int value,
            """ + parameter + ") => checked(value + 1);\n}";
        using var workspace = new AdhocWorkspace();
        Document document = CreateCodeFixDocument(workspace, source);
        (_, ImmutableArray<Diagnostic> before) = await GenerateCodeFixDocumentAsync(document);
        Diagnostic error = Assert.ContainsSingle(before);
        Assert.AreEqual("ANKUS056", error.Id);
        var provider = new InjectedParameterCodeFixProvider();
        List<CodeAction> actions = await CodeFixActionsAsync(provider, document, error);
        CodeAction action = Assert.ContainsSingle(actions);
        Assert.AreEqual("Remove SQL metadata from injected context", action.Title);
        ImmutableArray<CodeActionOperation> operations = await action.GetOperationsAsync(context.CancellationToken);
        ApplyChangesOperation change = Assert.IsInstanceOfType<ApplyChangesOperation>(Assert.ContainsSingle(operations));
        Document corrected = change.ChangedSolution.GetDocument(document.Id)!;
        // Only the metadata goes: no whitespace is left inside brackets or doubled where a list disappeared.
        Assert.AreEqual(source.Replace(parameter, expected, StringComparison.Ordinal),
            (await corrected.GetTextAsync(context.CancellationToken)).ToString());
        SyntaxNode originalRoot = (await document.GetSyntaxRootAsync(context.CancellationToken))!;
        SyntaxNode correctedRoot = (await corrected.GetSyntaxRootAsync(context.CancellationToken))!;
        MethodDeclarationSyntax original = Assert.ContainsSingle(originalRoot.DescendantNodes().OfType<MethodDeclarationSyntax>());
        MethodDeclarationSyntax repaired = Assert.ContainsSingle(correctedRoot.DescendantNodes().OfType<MethodDeclarationSyntax>());

        Assert.AreEqual(original.ExpressionBody!.ToFullString(), repaired.ExpressionBody!.ToFullString());
        Assert.AreEqual(original.ParameterList.Parameters[0].ToFullString(), repaired.ParameterList.Parameters[0].ToFullString());
        ParameterSyntax injected = repaired.ParameterList.Parameters[1];
        Assert.AreEqual(original.ParameterList.Parameters[1].Type!.ToString(), injected.Type!.ToString());
        Assert.AreEqual(original.ParameterList.Parameters[1].Identifier.ValueText, injected.Identifier.ValueText);
        if (original.ParameterList.Parameters[1].Default is { } originalDefault)
        {
            Assert.IsNotNull(injected.Default);
            Assert.AreEqual(originalDefault.ToString(), injected.Default.ToString());
        }
        else
        {
            Assert.IsNull(injected.Default);
        }

        Assert.AreSequenceEqual(retainedAttribute is null ? [] : [retainedAttribute],
            injected.AttributeLists.SelectMany(static list => list.Attributes).Select(static attribute => attribute.Name.ToString()));
        if (parameter.Contains("/* before */", StringComparison.Ordinal))
        {
            Assert.Contains("/* before */", correctedRoot.ToFullString());
            Assert.Contains("/* after */", correctedRoot.ToFullString());
        }

        (Compilation output, ImmutableArray<Diagnostic> after) = await GenerateCodeFixDocumentAsync(corrected);
        Assert.IsEmpty(after);
        Assert.IsEmpty(output.GetDiagnostics(context.CancellationToken).Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.StartsWith("CREATE FUNCTION \"apply\"(\"input\" integer DEFAULT (41))\nRETURNS integer AS ", InstallationBody(output));
        Assert.Contains(" STRICT ", InstallationBody(output));
        Assert.AreSequenceEqual(["ANKUS056"], provider.FixableDiagnosticIds);
        Assert.AreSame(WellKnownFixAllProviders.BatchFixer, provider.GetFixAllProvider());
    }

    /// <summary>
    /// Stale locations cannot remove ordinary SQL metadata or an unrelated attribute with the same short name.
    /// </summary>
    /// <param name="target">The parameter incorrectly targeted by a stale diagnostic.</param>
    [TestMethod]
    [DataRow("[Ankus.PgParameter(Name = \"input\")] int value")]
    [DataRow("[Other.PgParameter] Ankus.PgFunctionContext value")]
    [DataRow("[Ankus.PgParameter] ref Ankus.PgFunctionContext value")]
    public async Task InjectedParameterFixRejectsUnrelatedSemanticTargets(string target)
    {
        string source = """
            namespace Other { public sealed class PgParameterAttribute : System.Attribute; }
            public static class Functions
            {
                [Ankus.PgFunction]
                public static int Bad([Ankus.PgParameter] Ankus.PgFunctionContext context) => 42;
                [Ankus.PgFunction]
                public static int Valid(
            """ + target + ") => 42;\n}";
        using var workspace = new AdhocWorkspace();
        Document document = CreateCodeFixDocument(workspace, source);
        (_, ImmutableArray<Diagnostic> diagnostics) = await GenerateCodeFixDocumentAsync(document);
        Diagnostic original = Assert.ContainsSingle(diagnostics.Where(static diagnostic => diagnostic.Id == "ANKUS056"));
        SyntaxNode root = (await document.GetSyntaxRootAsync(context.CancellationToken))!;
        MethodDeclarationSyntax method = root.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(static value => value.Identifier.ValueText == "Valid");
        AttributeSyntax attribute = Assert.ContainsSingle(method.ParameterList.Parameters[0].AttributeLists.SelectMany(static value => value.Attributes));
        Diagnostic stale = Diagnostic.Create(original.Descriptor, attribute.GetLocation(), "value");

        Assert.IsEmpty(await CodeFixActionsAsync(new InjectedParameterCodeFixProvider(), document, stale));
        Assert.AreEqual(source, (await document.GetTextAsync(context.CancellationToken)).ToString());
    }

    /// <summary>
    /// Project-wide fixing corrects separate methods and documents while preserving unrelated source and SQL parameter metadata.
    /// </summary>
    [TestMethod]
    public async Task InjectedParameterFixAllPreservesOtherDocuments()
    {
        using var workspace = new AdhocWorkspace();
        Document first = CreateCodeFixDocument(workspace, """
            public static class First
            {
                [Ankus.PgFunction]
                public static int One([Ankus.PgParameter] Ankus.PgFunctionContext call, int value) => value + 1;
                [Ankus.PgFunction]
                public static int Two(int value, [Ankus.PgParameter(Name = "ignored")] Ankus.PgMemoryContext owner) => value + 2;
            }
            """);
        Document second = workspace.AddDocument(first.Project.Id, "Second.cs", SourceText.From("""
            public static class Second
            {
                [Ankus.PgFunction]
                public static int Three([Ankus.PgParameter] Ankus.PgMemoryContext owner,
                    [Ankus.PgParameter(Name = "input", Default = "42")] int value) => value + 3;
            }
            """));
        const string OtherSource = "// Keep this source exactly.\ninternal static class Other { internal const int Answer = 42; }\n";
        Document untouched = workspace.AddDocument(first.Project.Id, "Other.cs", SourceText.From(OtherSource));
        first = workspace.CurrentSolution.GetDocument(first.Id)!;
        (_, ImmutableArray<Diagnostic> diagnostics) = await GenerateCodeFixDocumentAsync(first);
        Assert.HasCount(3, diagnostics);
        Assert.IsTrue(diagnostics.All(static diagnostic => diagnostic.Id == "ANKUS056"));
        var provider = new InjectedParameterCodeFixProvider();
        var fixContext = new FixAllContext(first, provider, FixAllScope.Project, nameof(InjectedParameterCodeFixProvider),
            provider.FixableDiagnosticIds, new InjectedMetadataDiagnosticProvider(diagnostics), context.CancellationToken);
        CodeAction? action = await provider.GetFixAllProvider().GetFixAsync(fixContext);
        Assert.IsNotNull(action);
        ImmutableArray<CodeActionOperation> operations = await action.GetOperationsAsync(context.CancellationToken);
        ApplyChangesOperation change = Assert.IsInstanceOfType<ApplyChangesOperation>(Assert.ContainsSingle(operations));
        Document corrected = change.ChangedSolution.GetDocument(first.Id)!;
        (Compilation output, ImmutableArray<Diagnostic> after) = await GenerateCodeFixDocumentAsync(corrected);

        Assert.IsEmpty(after);
        Assert.IsEmpty(output.GetDiagnostics(context.CancellationToken).Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        string sql = InstallationBody(output);
        Assert.HasCount(3, sql.Split('\n').Where(static line => line.StartsWith("CREATE FUNCTION", StringComparison.Ordinal)));
        Assert.Contains("CREATE FUNCTION \"one\"(\"value\" integer)", sql);
        Assert.Contains("CREATE FUNCTION \"two\"(\"value\" integer)", sql);
        Assert.Contains("CREATE FUNCTION \"three\"(\"input\" integer DEFAULT (42))", sql);
        Assert.AreEqual(OtherSource, (await change.ChangedSolution.GetDocument(untouched.Id)!.GetTextAsync(context.CancellationToken)).ToString());
        string secondSource = (await change.ChangedSolution.GetDocument(second.Id)!.GetTextAsync(context.CancellationToken)).ToString();
        Assert.Contains("[Ankus.PgParameter(Name = \"input\", Default = \"42\")] int value", secondSource);
        Assert.Contains("=> value + 3;", secondSource);
    }

    /// <summary>
    /// Editor cancellation stops registration before any action is offered.
    /// </summary>
    [TestMethod]
    public async Task InjectedParameterFixHonorsCancellation()
    {
        using var workspace = new AdhocWorkspace();
        Document document = CreateCodeFixDocument(workspace,
            "public static class Functions { [Ankus.PgFunction] public static int Apply([Ankus.PgParameter] Ankus.PgMemoryContext owner) => 42; }");
        (_, ImmutableArray<Diagnostic> diagnostics) = await GenerateCodeFixDocumentAsync(document);
        Diagnostic diagnostic = Assert.ContainsSingle(diagnostics);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        await cancellation.CancelAsync();
        var actions = new List<CodeAction>();
        var provider = new InjectedParameterCodeFixProvider();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => provider.RegisterCodeFixesAsync(
            new CodeFixContext(document, diagnostic, (action, _) => actions.Add(action), cancellation.Token)));
        Assert.IsEmpty(actions);
    }

    /// <summary>
    /// Supplies the actual generator results to Roslyn's project-wide batch fixer.
    /// </summary>
    /// <param name="diagnostics">Diagnostics from the unchanged project compilation.</param>
    private sealed class InjectedMetadataDiagnosticProvider(ImmutableArray<Diagnostic> diagnostics) : FixAllContext.DiagnosticProvider
    {
        /// <summary>
        /// Selects the diagnostics attached to the requested syntax tree.
        /// </summary>
        public override async Task<IEnumerable<Diagnostic>> GetDocumentDiagnosticsAsync(Document document, CancellationToken cancellationToken)
        {
            SyntaxTree? tree = await document.GetSyntaxTreeAsync(cancellationToken);
            return diagnostics.Where(diagnostic => ReferenceEquals(diagnostic.Location.SourceTree, tree));
        }

        /// <summary>
        /// Returns no source-free project diagnostics because injected metadata always has a source location.
        /// </summary>
        public override Task<IEnumerable<Diagnostic>> GetProjectDiagnosticsAsync(Project project, CancellationToken cancellationToken)
            => Task.FromResult<IEnumerable<Diagnostic>>([]);

        /// <summary>
        /// Returns all diagnosed metadata in the original compilation.
        /// </summary>
        public override Task<IEnumerable<Diagnostic>> GetAllDiagnosticsAsync(Project project, CancellationToken cancellationToken)
            => Task.FromResult<IEnumerable<Diagnostic>>(diagnostics);
    }

    /// <summary>
    /// Creates a real C# workspace document with the same runtime references as generator contract tests.
    /// </summary>
    /// <param name="workspace">The workspace owning the document.</param>
    /// <param name="source">The extension source.</param>
    /// <returns>The editable source document.</returns>
    private static Document CreateCodeFixDocument(AdhocWorkspace workspace, string source)
    {
        Project project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(),
            "CodeFixConsumer", "CodeFixConsumer", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true,
                nullableContextOptions: NullableContextOptions.Enable),
            parseOptions: new CSharpParseOptions(LanguageVersion.CSharp14), metadataReferences: s_references));
        return workspace.AddDocument(project.Id, "Consumer.cs", SourceText.From(source));
    }

    /// <summary>
    /// Runs the shipped generator against the current workspace compilation.
    /// </summary>
    /// <param name="document">A document in the current project snapshot.</param>
    /// <returns>The complete generated compilation and generator diagnostics.</returns>
    private async Task<(Compilation Compilation, ImmutableArray<Diagnostic> Diagnostics)> GenerateCodeFixDocumentAsync(Document document)
    {
        CSharpCompilation input = Assert.IsInstanceOfType<CSharpCompilation>(await document.Project.GetCompilationAsync(context.CancellationToken));
        GeneratorDriver driver = CSharpGeneratorDriver.Create([new PgFunctionGenerator().AsSourceGenerator()],
            parseOptions: (CSharpParseOptions)document.Project.ParseOptions!);
        driver.RunGeneratorsAndUpdateCompilation(input, out Compilation output, out ImmutableArray<Diagnostic> diagnostics,
            context.CancellationToken);
        return (output, diagnostics);
    }

    /// <summary>
    /// Collects actions registered for an actual diagnostic on the current document.
    /// </summary>
    /// <param name="provider">The IDE provider under test.</param>
    /// <param name="document">The current editable source.</param>
    /// <param name="diagnostic">The compiler diagnostic to correct.</param>
    /// <returns>The offered code actions.</returns>
    private async Task<List<CodeAction>> CodeFixActionsAsync(CodeFixProvider provider, Document document, Diagnostic diagnostic)
    {
        var actions = new List<CodeAction>();
        await provider.RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic,
            (action, _) => actions.Add(action), context.CancellationToken));
        return actions;
    }
}
