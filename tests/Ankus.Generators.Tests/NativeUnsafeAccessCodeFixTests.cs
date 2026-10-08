using System.Collections.Immutable;
using System.Composition.Hosting;
using System.Runtime.Loader;
using Ankus.CodeFixes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Ankus.Generators.Tests;

public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Provides real runtime-owned native contracts with deterministic managed witnesses for editor tests.
    /// </summary>
    private const string NativeFixDeclarations = """
        public static class Raw
        {
            private static int reads;
            private static int value;
            [Ankus.CompilerServices.NativeUnsafeAccess]
            public static int Read() => 41 + ++reads;
            [Ankus.CompilerServices.NativeUnsafeAccess]
            public static int Current { get => value; set => Raw.value = value; }
            [Ankus.CompilerServices.NativeUnsafeAccess]
            public static ref int Reference() => ref value;
            public static int Checked() => 42;
            public static int CheckedValue => value;
        }
        """;

    /// <summary>
    /// Real editor exports expose the native-contract correction alongside the other shipped providers.
    /// </summary>
    [TestMethod]
    public void NativeUnsafeFixIsExported()
    {
        using CompositionHost container = new ContainerConfiguration().WithAssembly(typeof(NativeUnsafeAccessCodeFixProvider).Assembly).CreateContainer();
        NativeUnsafeAccessCodeFixProvider provider = Assert.ContainsSingle(container.GetExports<CodeFixProvider>().OfType<NativeUnsafeAccessCodeFixProvider>());
        Assert.AreSequenceEqual(["ANKUS129"], provider.FixableDiagnosticIds);
        Assert.AreNotSame(WellKnownFixAllProviders.BatchFixer, provider.GetFixAllProvider());
        Assert.AreSame(provider.GetFixAllProvider(), provider.GetFixAllProvider());
    }

    /// <summary>
    /// Corrected source executes the same expressions once, retaining value, void, ref and local-scope contracts.
    /// </summary>
    /// <param name="members">The consumer declarations containing one diagnosed native operation.</param>
    [TestMethod]
    [DataRow("public static int Run() { int value = Raw.Read(); return value + 1; }")]
    [DataRow("public static int Run() => Raw.Read() + 1;")]
    [DataRow("public static int Value => Raw.Read(); public static int Run() => Value + 1;")]
    [DataRow("public static int Value { get => Raw.Read(); } public static int Run() => Value + 1;")]
    [DataRow("public int this[int index] => Raw.Read() + index; public static int Run() => new Consumer()[1];")]
    [DataRow("public static void Set() => Raw.Current = 41; public static int Run() { Set(); return Raw.CheckedValue + 2; }")]
    [DataRow("public static int Run() { System.Func<int> read = () => Raw.Read(); return read() + 1; }")]
    [DataRow("public static int Run() { System.Func<int, int> read = value => Raw.Read() + value; return read(1); }")]
    [DataRow("public static int Run() { System.Action set = () => Raw.Current = 41; set(); return Raw.CheckedValue + 2; }")]
    [DataRow("public static int Run() { int Read() => Raw.Read(); return Read() + 1; }")]
    [DataRow("public Consumer() => Value = Raw.Read(); public int Value { get; } public static int Run() => new Consumer().Value + 1;")]
    [DataRow("public static int operator +(Consumer value, int addend) => Raw.Read() + addend; public static int Run() => new Consumer() + 1;")]
    [DataRow("public static explicit operator int(Consumer value) => Raw.Read(); public static int Run() => (int)new Consumer() + 1;")]
    [DataRow("public static ref int Ref() => ref Raw.Reference(); public static int Run() { Ref() = 41; return Raw.CheckedValue + 2; }")]
    [DataRow("public static int Run() { System.Func<int> read = Raw.Read; return read() + 1; }")]
    [DataRow("public static int Run() { try { throw new System.InvalidOperationException(); } catch (System.InvalidOperationException) when (Raw.Read() == 42) { return 43; } }")]
    [DataRow("public static int Run() { using var owner = new System.IO.MemoryStream(); int value = Raw.Read(); owner.WriteByte(1); return value + (int)owner.Length; }")]
    [DataRow("public static int Run() { if ((object)Raw.Read() is int value) { return value + 1; } return 0; }")]
    public async Task NativeUnsafeFixPreservesManagedExecution(string members)
    {
        using var workspace = new AdhocWorkspace();
        Document document = CreateCodeFixDocument(workspace, NativeFixDeclarations + "public sealed class Consumer { " + members + " }");
        (Compilation before, ImmutableArray<Diagnostic> diagnostics) = await AnalyzeNativeCodeFixDocumentAsync(document);
        Diagnostic diagnostic = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual("ANKUS129", diagnostic.Id);
        Document corrected = await ApplyNativeCodeFixAsync(document, diagnostic);
        (Compilation after, ImmutableArray<Diagnostic> remaining) = await AnalyzeNativeCodeFixDocumentAsync(corrected);
        Assert.IsEmpty(remaining);
        Assert.AreEqual(43, ExecuteNativeConsumer(before));
        Assert.AreEqual(43, ExecuteNativeConsumer(after));
        SyntaxNode originalRoot = (await document.GetSyntaxRootAsync(context.CancellationToken))!;
        SyntaxNode correctedRoot = (await corrected.GetSyntaxRootAsync(context.CancellationToken))!;
        Assert.AreEqual(originalRoot.DescendantNodes().OfType<ClassDeclarationSyntax>().First().ToFullString(),
            correctedRoot.DescendantNodes().OfType<ClassDeclarationSyntax>().First().ToFullString());
        Assert.AreSequenceEqual(originalRoot.DescendantNodes().OfType<InvocationExpressionSyntax>().Select(static value => value.ToString()),
            correctedRoot.DescendantNodes().OfType<InvocationExpressionSyntax>().Select(static value => value.ToString()));
        UnsafeStatementSyntax region = Assert.ContainsSingle(correctedRoot.DescendantNodes().OfType<UnsafeStatementSyntax>());
        Assert.ContainsSingle(region.Block.Statements);
    }

    /// <summary>
    /// Arrow conversion retains comments and the original exception rather than returning a thrown value.
    /// </summary>
    [TestMethod]
    public async Task NativeUnsafeFixPreservesCommentsAndThrow()
    {
        using var workspace = new AdhocWorkspace();
        Document document = CreateCodeFixDocument(workspace, NativeFixDeclarations + """
            public sealed class Consumer
            {
                // Keep declaration comment.
                public static int Run() => /* keep arrow comment */ throw new System.InvalidOperationException(Raw.Read().ToString()); // Keep trailing comment.
            }
            """);
        (Compilation before, ImmutableArray<Diagnostic> diagnostics) = await AnalyzeNativeCodeFixDocumentAsync(document);
        Document corrected = await ApplyNativeCodeFixAsync(document, Assert.ContainsSingle(diagnostics));
        (Compilation after, ImmutableArray<Diagnostic> remaining) = await AnalyzeNativeCodeFixDocumentAsync(corrected);
        Assert.IsEmpty(remaining);
        string source = (await corrected.GetTextAsync(context.CancellationToken)).ToString();
        Assert.Contains("// Keep declaration comment.", source);
        Assert.Contains("/* keep arrow comment */", source);
        Assert.Contains("// Keep trailing comment.", source);
        Assert.AreEqual("42", Assert.ThrowsExactly<InvalidOperationException>(() => ExecuteNativeConsumer(before)).Message);
        Assert.AreEqual("42", Assert.ThrowsExactly<InvalidOperationException>(() => ExecuteNativeConsumer(after)).Message);
    }

    /// <summary>
    /// Stale diagnostics cannot mark checked calls, metadata, unsupported initializers or invalid unsafe regions.
    /// </summary>
    /// <param name="member">The current consumer source.</param>
    /// <param name="target">The expression incorrectly or incompatibly diagnosed.</param>
    [TestMethod]
    [DataRow("public static int Run() => Raw.Checked();", "Raw.Checked()")]
    [DataRow("public static string Name => nameof(Raw.Read);", "Raw.Read")]
    [DataRow("public static int Run() { unsafe { return Raw.Read(); } }", "Raw.Read()")]
    [DataRow("public static int Value = Raw.Read();", "Raw.Read()")]
    [DataRow("public static System.Collections.Generic.IEnumerable<int> Run() { yield return Raw.Read(); }", "Raw.Read()")]
    [DataRow("public static System.Linq.Expressions.Expression<System.Func<int>> Run() => () => Raw.Read();", "Raw.Read()")]
    [DataRow("public static int Run() => Foreign.Read();", "Foreign.Read()")]
    public async Task NativeUnsafeFixRejectsStaleOrInvalidContexts(string member, string target)
    {
        const string Foreign = """
            namespace Other { public sealed class NativeUnsafeAccessAttribute : System.Attribute; }
            public static class Foreign { [Other.NativeUnsafeAccess] public static int Read() => 42; }
            """;
        using var workspace = new AdhocWorkspace();
        string source = NativeFixDeclarations + Foreign + "public sealed class Consumer { " + member + " }";
        Document document = CreateCodeFixDocument(workspace, source);
        SyntaxTree tree = (await document.GetSyntaxTreeAsync(context.CancellationToken))!;
        DiagnosticDescriptor descriptor = Assert.ContainsSingle(new NativeUnsafeAccessAnalyzer().SupportedDiagnostics);
        int start = source.LastIndexOf(target, StringComparison.Ordinal);
        Diagnostic stale = Diagnostic.Create(descriptor, Location.Create(tree, new TextSpan(start, target.Length)), "Read");
        Assert.IsEmpty(await CodeFixActionsAsync(new NativeUnsafeAccessCodeFixProvider(), document, stale));
        Assert.AreEqual(source, (await document.GetTextAsync(context.CancellationToken)).ToString());
    }

    /// <summary>
    /// A pattern variable read after its statement cannot move into an unsafe block, so no ineffective correction is offered.
    /// </summary>
    /// <param name="body">The compiler-valid consumer body whose pattern variable escapes the native statement.</param>
    [TestMethod]
    [DataRow("if ((object)Raw.Read() is not int value) { return 0; } return value + 1;")]
    [DataRow("if (!((object)Raw.Read() is int value)) { return 0; } return value + 1;")]
    [DataRow("if ((object)Raw.Read() is not int { } value) { return 0; } return value + 1;")]
    public async Task NativeUnsafeFixIsNotOfferedForEscapingPatternVariables(string body)
    {
        using var workspace = new AdhocWorkspace();
        string source = NativeFixDeclarations + "public sealed class Consumer { public static int Run() { " + body + " } }";
        Document document = CreateCodeFixDocument(workspace, source);
        (Compilation before, ImmutableArray<Diagnostic> diagnostics) = await AnalyzeNativeCodeFixDocumentAsync(document);
        Assert.AreEqual(43, ExecuteNativeConsumer(before));
        Diagnostic diagnostic = Assert.ContainsSingle(diagnostics);
        Assert.IsEmpty(await CodeFixActionsAsync(new NativeUnsafeAccessCodeFixProvider(), document, diagnostic));
        var provider = new NativeUnsafeAccessCodeFixProvider();
        var fixContext = new FixAllContext(document, provider, FixAllScope.Project, nameof(NativeUnsafeAccessCodeFixProvider),
            provider.FixableDiagnosticIds, new InjectedMetadataDiagnosticProvider(diagnostics), context.CancellationToken);
        CodeAction? action = await provider.GetFixAllProvider().GetFixAsync(fixContext);
        Assert.IsNotNull(action);
        ApplyChangesOperation change = Assert.IsInstanceOfType<ApplyChangesOperation>(
            Assert.ContainsSingle(await action.GetOperationsAsync(context.CancellationToken)));
        Assert.AreEqual(source, (await change.ChangedSolution.GetDocument(document.Id)!.GetTextAsync(context.CancellationToken)).ToString(),
            "Fix All leaves an escaping pattern variable's native statement for a manual edit.");
    }

    /// <summary>
    /// The provider honors editor cancellation without registering a partially calculated correction.
    /// </summary>
    [TestMethod]
    public async Task NativeUnsafeFixHonorsCancellation()
    {
        using var workspace = new AdhocWorkspace();
        Document document = CreateCodeFixDocument(workspace, NativeFixDeclarations + "public sealed class Consumer { public static int Run() => Raw.Read(); }");
        (_, ImmutableArray<Diagnostic> diagnostics) = await AnalyzeNativeCodeFixDocumentAsync(document);
        var actions = new List<CodeAction>();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => new NativeUnsafeAccessCodeFixProvider().RegisterCodeFixesAsync(
            new CodeFixContext(document, Assert.ContainsSingle(diagnostics), (action, _) => actions.Add(action), cancellation.Token)));
        Assert.IsEmpty(actions);
    }

    /// <summary>
    /// Unsafe syntax is not offered when the project's compiler options reject it.
    /// </summary>
    [TestMethod]
    public async Task NativeUnsafeFixRequiresUnsafeCompilerSupport()
    {
        using var workspace = new AdhocWorkspace();
        Document document = CreateCodeFixDocument(workspace, NativeFixDeclarations + "public sealed class Consumer { public static int Run() => Raw.Read(); }");
        document = document.Project.WithCompilationOptions(((Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions)document.Project.CompilationOptions!)
            .WithAllowUnsafe(false)).GetDocument(document.Id)!;
        (_, ImmutableArray<Diagnostic> diagnostics) = await AnalyzeNativeCodeFixDocumentAsync(document);
        Assert.IsEmpty(await CodeFixActionsAsync(new NativeUnsafeAccessCodeFixProvider(), document, Assert.ContainsSingle(diagnostics)));
    }

    /// <summary>
    /// Unrelated compiler errors neither prevent a valid native correction nor disappear as a side effect.
    /// </summary>
    [TestMethod]
    public async Task NativeUnsafeFixPreservesUnrelatedCompilerErrors()
    {
        using var workspace = new AdhocWorkspace();
        Document document = CreateCodeFixDocument(workspace, NativeFixDeclarations + """
            public sealed class Consumer
            {
                public static int Run() => Raw.Read();
                public static int Broken() => Missing;
            }
            """);
        Compilation before = (await document.Project.GetCompilationAsync(context.CancellationToken))!;
        var analysis = new CompilationWithAnalyzers(before, [new NativeUnsafeAccessAnalyzer()],
            new CompilationWithAnalyzersOptions(new AnalyzerOptions([]), onAnalyzerException: null,
                concurrentAnalysis: true, logAnalyzerExecutionTime: false));
        Diagnostic diagnostic = Assert.ContainsSingle(await analysis.GetAnalyzerDiagnosticsAsync(context.CancellationToken));
        Document corrected = await ApplyNativeCodeFixAsync(document, diagnostic);
        Compilation after = (await corrected.Project.GetCompilationAsync(context.CancellationToken))!;
        Diagnostic beforeError = Assert.ContainsSingle(before.GetDiagnostics(context.CancellationToken)
            .Where(static value => value.Severity == DiagnosticSeverity.Error));
        Diagnostic afterError = Assert.ContainsSingle(after.GetDiagnostics(context.CancellationToken)
            .Where(static value => value.Severity == DiagnosticSeverity.Error));
        Assert.AreEqual("CS0103", beforeError.Id);
        Assert.AreEqual(beforeError.Id, afterError.Id);
        Assert.AreEqual(beforeError.GetMessage(System.Globalization.CultureInfo.InvariantCulture),
            afterError.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
        Assert.AreEqual("Missing", afterError.Location.SourceTree!.GetText(context.CancellationToken).ToString(afterError.Location.SourceSpan));
    }

    /// <summary>
    /// Fix All acknowledges multiple accesses without nesting duplicate contexts or changing unrelated documents.
    /// </summary>
    [TestMethod]
    public async Task NativeUnsafeFixAllPreservesOtherDocuments()
    {
        using var workspace = new AdhocWorkspace();
        Document first = CreateCodeFixDocument(workspace, NativeFixDeclarations + """
            public sealed class Consumer
            {
                public static int Run() { int first = Raw.Read(); int second = Raw.Current; return first + second + 1; }
                public static int Other() => Raw.Read();
            }
            """);
        Document second = workspace.AddDocument(first.Project.Id, "Second.cs", SourceText.From("public static class Second { public static int Read() => Raw.Read(); }"));
        const string OtherSource = "// Keep this document exactly.\npublic static class Untouched { public static int Read() => Raw.Checked(); }\n";
        Document untouched = workspace.AddDocument(first.Project.Id, "Untouched.cs", SourceText.From(OtherSource));
        first = workspace.CurrentSolution.GetDocument(first.Id)!;
        (_, ImmutableArray<Diagnostic> diagnostics) = await AnalyzeNativeCodeFixDocumentAsync(first);
        Assert.HasCount(4, diagnostics);
        var provider = new NativeUnsafeAccessCodeFixProvider();
        var fixContext = new FixAllContext(first, provider, FixAllScope.Project, nameof(NativeUnsafeAccessCodeFixProvider),
            provider.FixableDiagnosticIds, new InjectedMetadataDiagnosticProvider(diagnostics), context.CancellationToken);
        CodeAction action = (await provider.GetFixAllProvider().GetFixAsync(fixContext))!;
        Assert.IsNotNull(action);
        ApplyChangesOperation change = Assert.IsInstanceOfType<ApplyChangesOperation>(Assert.ContainsSingle(await action.GetOperationsAsync(context.CancellationToken)));
        Document corrected = change.ChangedSolution.GetDocument(first.Id)!;
        (Compilation after, ImmutableArray<Diagnostic> remaining) = await AnalyzeNativeCodeFixDocumentAsync(corrected);
        Assert.IsEmpty(remaining);
        Assert.AreEqual(43, ExecuteNativeConsumer(after));
        Assert.AreEqual(OtherSource, (await change.ChangedSolution.GetDocument(untouched.Id)!.GetTextAsync(context.CancellationToken)).ToString());
        Assert.HasCount(3, (await corrected.GetSyntaxRootAsync(context.CancellationToken))!.DescendantNodes().OfType<UnsafeStatementSyntax>());
        Assert.ContainsSingle((await change.ChangedSolution.GetDocument(second.Id)!.GetSyntaxRootAsync(context.CancellationToken))!
            .DescendantNodes().OfType<UnsafeStatementSyntax>());
    }

    /// <summary>
    /// Async callables acknowledge only the native statement while retaining awaits and locals in their ordinary scope.
    /// </summary>
    /// <param name="body">The compiler-valid asynchronous consumer body.</param>
    [TestMethod]
    [DataRow("await System.Threading.Tasks.Task.Yield(); return Raw.Read() + 1;")]
    [DataRow("int value = Raw.Read(); await System.Threading.Tasks.Task.Yield(); return value + 1;")]
    [DataRow("var read = Raw.Read; await System.Threading.Tasks.Task.Yield(); return read() + 1;")]
    public async Task NativeUnsafeFixKeepsAwaitOutsideNativeStatement(string body)
    {
        using var workspace = new AdhocWorkspace();
        Document document = CreateCodeFixDocument(workspace, NativeFixDeclarations +
            "public sealed class Consumer { public static async System.Threading.Tasks.Task<int> Run() { " + body + " } }");
        (Compilation before, ImmutableArray<Diagnostic> diagnostics) = await AnalyzeNativeCodeFixDocumentAsync(document);
        Document corrected = await ApplyNativeCodeFixAsync(document, Assert.ContainsSingle(diagnostics));
        (Compilation after, ImmutableArray<Diagnostic> remaining) = await AnalyzeNativeCodeFixDocumentAsync(corrected);
        Assert.IsEmpty(remaining);
        Assert.AreEqual(43, await ExecuteAsyncNativeConsumer(before));
        Assert.AreEqual(43, await ExecuteAsyncNativeConsumer(after));
        SyntaxNode root = (await corrected.GetSyntaxRootAsync(context.CancellationToken))!;
        UnsafeStatementSyntax region = Assert.ContainsSingle(root.DescendantNodes().OfType<UnsafeStatementSyntax>());
        Assert.ContainsSingle(region.Block.Statements);
        Assert.IsEmpty(region.DescendantNodes().OfType<AwaitExpressionSyntax>());
        Assert.ContainsSingle(root.DescendantNodes().OfType<AwaitExpressionSyntax>());
    }

    /// <summary>
    /// Executes an emitted asynchronous consumer until its original result is available.
    /// </summary>
    /// <param name="compilation">The compiler-valid original or corrected consumer.</param>
    /// <returns>The independently observed result after the continuation completes.</returns>
    private static async Task<int> ExecuteAsyncNativeConsumer(Compilation compilation)
    {
        using var image = new MemoryStream();
        Microsoft.CodeAnalysis.Emit.EmitResult emission = compilation.Emit(image);
        Assert.IsTrue(emission.Success, string.Join(Environment.NewLine, emission.Diagnostics));
        image.Position = 0;
        var loadContext = new AssemblyLoadContext("AsyncNativeUnsafeCodeFixWitness", isCollectible: true);
        try
        {
            System.Reflection.Assembly assembly = loadContext.LoadFromStream(image);
            Func<Task<int>> execute = assembly.GetType("Consumer")!.GetMethod("Run")!.CreateDelegate<Func<Task<int>>>();
            return await execute();
        }
        finally
        {
            loadContext.Unload();
        }
    }

    /// <summary>
    /// Applies the registered semantic action using the real workspace operation.
    /// </summary>
    /// <param name="document">The original editable source.</param>
    /// <param name="diagnostic">The actual native-contract diagnostic.</param>
    /// <returns>The corrected workspace document.</returns>
    private async Task<Document> ApplyNativeCodeFixAsync(Document document, Diagnostic diagnostic)
    {
        CodeAction action = Assert.ContainsSingle(await CodeFixActionsAsync(new NativeUnsafeAccessCodeFixProvider(), document, diagnostic));
        Assert.AreEqual("Use an unsafe block", action.Title);
        ApplyChangesOperation change = Assert.IsInstanceOfType<ApplyChangesOperation>(Assert.ContainsSingle(await action.GetOperationsAsync(context.CancellationToken)));
        return change.ChangedSolution.GetDocument(document.Id)!;
    }

    /// <summary>
    /// Executes the actual raw-access analyzer against a compiler-valid workspace compilation.
    /// </summary>
    /// <param name="document">The current source snapshot.</param>
    /// <returns>The complete compilation and native-contract diagnostics.</returns>
    private async Task<(Compilation Compilation, ImmutableArray<Diagnostic> Diagnostics)> AnalyzeNativeCodeFixDocumentAsync(Document document)
    {
        Compilation compilation = (await document.Project.GetCompilationAsync(context.CancellationToken))!;
        Assert.IsEmpty(compilation.GetDiagnostics(context.CancellationToken).Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        var analysis = new CompilationWithAnalyzers(compilation, [new NativeUnsafeAccessAnalyzer()],
            new CompilationWithAnalyzersOptions(new AnalyzerOptions([]), onAnalyzerException: null,
                concurrentAnalysis: true, logAnalyzerExecutionTime: false));
        return (compilation, await analysis.GetAnalyzerDiagnosticsAsync(context.CancellationToken));
    }

    /// <summary>
    /// Runs emitted managed code to verify observable evaluation and return behavior before and after correction.
    /// </summary>
    /// <param name="compilation">The compiler-valid original or corrected consumer.</param>
    /// <returns>The consumer's observable result.</returns>
    private static int ExecuteNativeConsumer(Compilation compilation)
    {
        using var image = new MemoryStream();
        Microsoft.CodeAnalysis.Emit.EmitResult emission = compilation.Emit(image);
        Assert.IsTrue(emission.Success, string.Join(Environment.NewLine, emission.Diagnostics));
        image.Position = 0;
        var loadContext = new AssemblyLoadContext("NativeUnsafeCodeFixWitness", isCollectible: true);
        try
        {
            System.Reflection.Assembly assembly = loadContext.LoadFromStream(image);
            Func<int> execute = assembly.GetType("Consumer")!.GetMethod("Run")!.CreateDelegate<Func<int>>();
            return execute();
        }
        finally
        {
            loadContext.Unload();
        }
    }
}
