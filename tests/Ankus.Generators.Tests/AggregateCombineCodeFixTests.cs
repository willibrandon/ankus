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
    /// Editor composition discovers the aggregate correction alongside the existing parameter correction.
    /// </summary>
    [TestMethod]
    public void AggregateCombineFixIsExportedForEditorDiscovery()
    {
        using CompositionHost container = new ContainerConfiguration().WithAssembly(typeof(AggregateCombineCodeFixProvider).Assembly).CreateContainer();
        Lazy<CodeFixProvider, IDictionary<string, object>> export = Assert.ContainsSingle(
            container.GetExports<Lazy<CodeFixProvider, IDictionary<string, object>>>()
                .Where(static value => (string)value.Metadata["Name"] == nameof(AggregateCombineCodeFixProvider)));
        CodeFixProvider provider = Assert.IsInstanceOfType<AggregateCombineCodeFixProvider>(export.Value);

        Assert.AreSequenceEqual([LanguageNames.CSharp], Assert.IsInstanceOfType<IEnumerable<string>>(export.Metadata["Languages"]));
        Assert.AreSequenceEqual(["ANKUS111"], provider.FixableDiagnosticIds);
        Assert.AreNotSame(WellKnownFixAllProviders.BatchFixer, provider.GetFixAllProvider());
        Assert.AreSame(provider.GetFixAllProvider(), provider.GetFixAllProvider());
        Assert.AreSame(provider, Assert.ContainsSingle(container.GetExports<CodeFixProvider>().OfType<AggregateCombineCodeFixProvider>()));
    }

    /// <summary>
    /// The correction derives its state from the aggregate contract and preserves all authored method bodies and metadata.
    /// </summary>
    /// <param name="state">The exact aggregate state type, including NULL policy.</param>
    /// <param name="declaration">The attributed container shape.</param>
    [TestMethod]
    [DataRow("int", "sealed class")]
    [DataRow("int?", "readonly struct")]
    [DataRow("string?", "sealed record")]
    public async Task AggregateCombineFixPreservesTypedContracts(string state, string declaration)
    {
        string source = $$"""
            using Contract = Ankus.IPgAggregate<{{state}}, int>;
            [Ankus.PgAggregate(InitialCondition = "0")]
            public {{declaration}} Sum : /* existing contract */ Contract
            {
                public static {{state}} Transition(Ankus.PgAggregateContext context, {{state}} state, int value) => state;
                // Keep this implementation and its callback metadata.
                [Ankus.PgFunction(Name = "merge_states")]
                public static {{state}} Combine(Ankus.PgAggregateContext context, {{state}} state, {{state}} other) => state;
            }
            """;
        using var workspace = new AdhocWorkspace();
        Document document = CreateCodeFixDocument(workspace, source);
        (_, ImmutableArray<Diagnostic> before) = await GenerateCodeFixDocumentAsync(document);
        Diagnostic error = Assert.ContainsSingle(before.Where(static diagnostic => diagnostic.Id == "ANKUS111"));
        Assert.AreEqual("ANKUS111", error.Id);
        Document corrected = await ApplyAggregateCombineFixAsync(document, error);
        SyntaxNode originalRoot = (await document.GetSyntaxRootAsync(context.CancellationToken))!;
        SyntaxNode correctedRoot = (await corrected.GetSyntaxRootAsync(context.CancellationToken))!;

        Assert.AreSequenceEqual(originalRoot.DescendantNodes().OfType<MethodDeclarationSyntax>().Select(static method => method.ToFullString()),
            correctedRoot.DescendantNodes().OfType<MethodDeclarationSyntax>().Select(static method => method.ToFullString()));
        Assert.Contains("/* existing contract */", correctedRoot.ToFullString());
        (Compilation output, ImmutableArray<Diagnostic> after) = await GenerateCodeFixDocumentAsync(corrected);
        Assert.IsEmpty(after);
        Assert.IsEmpty(output.GetDiagnostics(context.CancellationToken).Where(static value => value.Severity == DiagnosticSeverity.Error));
        INamedTypeSymbol aggregate = output.GetTypeByMetadataName("Sum")!;
        INamedTypeSymbol capability = Assert.ContainsSingle(aggregate.AllInterfaces.Where(static value => value.Name == "IPgCombinableAggregate"));
        IMethodSymbol implementation = Assert.IsInstanceOfType<IMethodSymbol>(aggregate.FindImplementationForInterfaceMember(
            Assert.ContainsSingle(capability.GetMembers("Combine"))));
        Assert.AreEqual("Combine", implementation.Name);
        Assert.IsTrue(SymbolEqualityComparer.IncludeNullability.Equals(capability.TypeArguments[0], implementation.ReturnType));
        Assert.Contains("COMBINEFUNC = \"merge_states\"", InstallationBody(output));
        Assert.Contains("CREATE AGGREGATE \"sum\"", InstallationBody(output));
    }

    /// <summary>
    /// An inherited callback is repaired on each eligible derived aggregate without changing its shared base implementation.
    /// </summary>
    /// <param name="incompatibleSibling">Whether a sibling deliberately declares a different state contract.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AggregateCombineFixUpdatesInheritedConsumersAcrossDocuments(bool incompatibleSibling)
    {
        const string Parent = """
            public abstract class Parent<T>
            {
                public static T Combine(Ankus.PgAggregateContext context, T state, T other) => state;
            }
            """;
        using var workspace = new AdhocWorkspace();
        Document parent = CreateCodeFixDocument(workspace, Parent);
        Document first = workspace.AddDocument(parent.Project.Id, "First.cs", SourceText.From("""
            [Ankus.PgAggregate(InitialCondition = "0")]
            public sealed class First : Parent<int>, Ankus.IPgAggregate<int, int>
            {
                public static int Transition(Ankus.PgAggregateContext context, int state, int value) => state + value;
            }
            """));
        Document second = workspace.AddDocument(parent.Project.Id, "Second.cs", SourceText.From("""
            [Ankus.PgAggregate(InitialCondition = "0")]
            public sealed class Second : Parent<long>, Ankus.IPgAggregate<long, int>
            {
                public static long Transition(Ankus.PgAggregateContext context, long state, int value) => state + value;
            }
            """));
        const string Wrong = """
            [Ankus.PgAggregate(InitialCondition = "0")]
            public sealed class Wrong : Parent<int>, Ankus.IPgAggregate<long, int>
            {
                public static long Transition(Ankus.PgAggregateContext context, long state, int value) => state + value;
            }
            """;
        Document? wrong = incompatibleSibling ? workspace.AddDocument(parent.Project.Id, "Wrong.cs", SourceText.From(Wrong)) : null;
        parent = workspace.CurrentSolution.GetDocument(parent.Id)!;
        (_, ImmutableArray<Diagnostic> before) = await GenerateCodeFixDocumentAsync(parent);
        Diagnostic error = Assert.ContainsSingle(before);
        Assert.AreEqual("ANKUS111", error.Id);
        Document corrected = await ApplyAggregateCombineFixAsync(parent, error);
        (Compilation output, ImmutableArray<Diagnostic> after) = await GenerateCodeFixDocumentAsync(corrected);

        Assert.AreEqual(Parent, (await corrected.GetTextAsync(context.CancellationToken)).ToString());
        if (wrong is not null)
        {
            Assert.IsEmpty(after);
            Assert.AreEqual(Wrong, (await corrected.Project.Solution.GetDocument(wrong.Id)!.GetTextAsync(context.CancellationToken)).ToString());
            Assert.Contains("CREATE AGGREGATE \"wrong\"", InstallationBody(output));
            Assert.DoesNotContain("COMBINEFUNC = \"wrong_combine\"", InstallationBody(output));
        }
        else
        {
            Assert.IsEmpty(after);
        }

        Assert.IsEmpty(output.GetDiagnostics(context.CancellationToken).Where(static value => value.Severity == DiagnosticSeverity.Error));
        Assert.Contains("COMBINEFUNC = \"first_combine\"", InstallationBody(output));
        Assert.Contains("COMBINEFUNC = \"second_combine\"", InstallationBody(output));
        Assert.Contains("IPgCombinableAggregate", (await corrected.Project.Solution.GetDocument(first.Id)!.GetTextAsync(context.CancellationToken)).ToString());
        Assert.Contains("IPgCombinableAggregate", (await corrected.Project.Solution.GetDocument(second.Id)!.GetTextAsync(context.CancellationToken)).ToString());
    }

    /// <summary>
    /// An incompatible same-name helper is not guessed to be an aggregate role.
    /// </summary>
    /// <param name="callback">The same-name method that cannot implement the aggregate's combine capability.</param>
    /// <param name="roleShaped">Whether the exact callback shape identifies an invalid authored role.</param>
    [TestMethod]
    [DataRow("public static long Combine(Ankus.PgAggregateContext context, long state, long other) => state;", false)]
    [DataRow("public static int? Combine(Ankus.PgAggregateContext context, int? state, int? other) => state;", false)]
    [DataRow("private static int Combine(Ankus.PgAggregateContext context, int state, int other) => state;", true)]
    [DataRow("public static int Combine(Ankus.PgFunctionContext context, int state, int other) => state;", false)]
    [DataRow("public static int Combine<T>(Ankus.PgAggregateContext context, int state, int other) => state;", false)]
    [DataRow("public static int Combine(Ankus.PgAggregateContext context, ref int state, int other) => state;", false)]
    [DataRow("public static int Combine(Ankus.PgAggregateContext context, int state, int other = 0) => state;", true)]
    [DataRow("public static async System.Threading.Tasks.Task<int> Combine(Ankus.PgAggregateContext context, int state, int other) => await System.Threading.Tasks.Task.FromResult(state);", false)]
    public async Task AggregateCombineDiagnosticDistinguishesRoleShape(string callback, bool roleShaped)
    {
        string source = """
            [Ankus.PgAggregate(InitialCondition = "0")]
            public sealed class Sum : Ankus.IPgAggregate<int, int>
            {
                public static int Transition(Ankus.PgAggregateContext context, int state, int value) => state + value;
            """ + callback + "\n}";
        using var workspace = new AdhocWorkspace();
        Document document = CreateCodeFixDocument(workspace, source);
        (_, ImmutableArray<Diagnostic> before) = await GenerateCodeFixDocumentAsync(document);
        if (roleShaped)
        {
            Diagnostic error = Assert.ContainsSingle(before);
            Assert.AreEqual("ANKUS111", error.Id);
            Assert.IsEmpty(await CodeFixActionsAsync(new AggregateCombineCodeFixProvider(), document, error));
        }
        else
        {
            Assert.IsEmpty(before);
        }

        Assert.AreEqual(source, (await document.GetTextAsync(context.CancellationToken)).ToString());
    }

    /// <summary>
    /// Nullable reference state is not silently tightened or widened to infer an omitted capability.
    /// </summary>
    /// <param name="state">The declared state contract.</param>
    /// <param name="callbackState">The mismatched callback state.</param>
    [TestMethod]
    [DataRow("string?", "string")]
    [DataRow("string", "string?")]
    public async Task AggregateCombineDiagnosticIgnoresReferenceNullabilityMismatch(string state, string callbackState)
    {
        using var workspace = new AdhocWorkspace();
        Document document = CreateCodeFixDocument(workspace, $$"""
            [Ankus.PgAggregate(InitialCondition = "0")]
            public sealed class Sum : Ankus.IPgAggregate<{{state}}, int>
            {
                public static {{state}} Transition(Ankus.PgAggregateContext context, {{state}} state, int value) => state;
                public static {{callbackState}} Combine(Ankus.PgAggregateContext context, {{callbackState}} state, {{callbackState}} other) => state;
            }
            """);
        (_, ImmutableArray<Diagnostic> before) = await GenerateCodeFixDocumentAsync(document);
        Assert.IsEmpty(before);
    }

    /// <summary>
    /// A nested partial type receives its interface without rewriting its other declaration or its implementation.
    /// </summary>
    [TestMethod]
    public async Task AggregateCombineFixPreservesNestedPartialDeclarations()
    {
        const string Metadata = """
            public partial class Host
            {
                [Ankus.PgAggregate(InitialCondition = "0")]
                public sealed partial class Sum : Ankus.IPgAggregate<int, int>
                {
                    public static int Transition(Ankus.PgAggregateContext context, int state, int value) => state + value;
                }
            }
            """;
        using var workspace = new AdhocWorkspace();
        Document callback = CreateCodeFixDocument(workspace, """
            public partial class Host
            {
                public sealed partial class Sum
                {
                    public static int Combine(Ankus.PgAggregateContext context, int state, int other) => state + other;
                }
            }
            """);
        Document metadata = workspace.AddDocument(callback.Project.Id, "Metadata.cs", SourceText.From(Metadata));
        callback = workspace.CurrentSolution.GetDocument(callback.Id)!;
        (_, ImmutableArray<Diagnostic> before) = await GenerateCodeFixDocumentAsync(callback);
        Diagnostic error = Assert.ContainsSingle(before);
        Assert.AreEqual("ANKUS111", error.Id);
        Document corrected = await ApplyAggregateCombineFixAsync(callback, error);
        (Compilation output, ImmutableArray<Diagnostic> after) = await GenerateCodeFixDocumentAsync(corrected);

        Assert.IsEmpty(after);
        Assert.IsEmpty(output.GetDiagnostics(context.CancellationToken).Where(static value => value.Severity == DiagnosticSeverity.Error));
        Assert.AreEqual(Metadata, (await corrected.Project.Solution.GetDocument(metadata.Id)!.GetTextAsync(context.CancellationToken)).ToString());
        Assert.Contains("COMBINEFUNC = \"sum_combine\"", InstallationBody(output));
        Assert.AreEqual("Ankus.IPgCombinableAggregate<int>", Assert.ContainsSingle(
            output.GetTypeByMetadataName("Host+Sum")!.AllInterfaces.Where(static contract => contract.Name == "IPgCombinableAggregate")).ToDisplayString());
    }

    /// <summary>
    /// A project-wide fix combines independent edits and preserves unrelated source documents.
    /// </summary>
    [TestMethod]
    public async Task AggregateCombineFixAllPreservesOtherDocuments()
    {
        using var workspace = new AdhocWorkspace();
        Document first = CreateCodeFixDocument(workspace, AggregateCombineSource("First"));
        workspace.AddDocument(first.Project.Id, "Second.cs", SourceText.From(AggregateCombineSource("Second")));
        const string Other = "// Preserve this document exactly.\npublic class Other;\n";
        Document untouched = workspace.AddDocument(first.Project.Id, "Other.cs", SourceText.From(Other));
        first = workspace.CurrentSolution.GetDocument(first.Id)!;
        (_, ImmutableArray<Diagnostic> before) = await GenerateCodeFixDocumentAsync(first);
        Assert.HasCount(2, before);
        Assert.IsTrue(before.All(static value => value.Id == "ANKUS111"));
        var provider = new AggregateCombineCodeFixProvider();
        var fixContext = new FixAllContext(first, provider, FixAllScope.Project, nameof(AggregateCombineCodeFixProvider),
            provider.FixableDiagnosticIds, new InjectedMetadataDiagnosticProvider(before), context.CancellationToken);
        CodeAction? action = await provider.GetFixAllProvider().GetFixAsync(fixContext);
        Assert.IsNotNull(action);
        ApplyChangesOperation change = Assert.IsInstanceOfType<ApplyChangesOperation>(Assert.ContainsSingle(
            await action.GetOperationsAsync(context.CancellationToken)));
        Document corrected = change.ChangedSolution.GetDocument(first.Id)!;
        (Compilation output, ImmutableArray<Diagnostic> after) = await GenerateCodeFixDocumentAsync(corrected);

        Assert.IsEmpty(after);
        Assert.IsEmpty(output.GetDiagnostics(context.CancellationToken).Where(static value => value.Severity == DiagnosticSeverity.Error));
        Assert.Contains("COMBINEFUNC = \"first_combine\"", InstallationBody(output));
        Assert.Contains("COMBINEFUNC = \"second_combine\"", InstallationBody(output));
        Assert.AreEqual(Other, (await change.ChangedSolution.GetDocument(untouched.Id)!.GetTextAsync(context.CancellationToken)).ToString());
    }

    /// <summary>
    /// An unrelated compiler error remains present while a valid aggregate gains its exact combine capability.
    /// </summary>
    [TestMethod]
    public async Task AggregateCombineFixPreservesUnrelatedCompilerErrors()
    {
        using var workspace = new AdhocWorkspace();
        Document document = CreateCodeFixDocument(workspace, AggregateCombineSource("First") +
            "public static class Broken { public static int Value() => Missing; }");
        (Compilation before, ImmutableArray<Diagnostic> diagnostics) = await GenerateCodeFixDocumentAsync(document);
        Diagnostic original = Assert.ContainsSingle(before.GetDiagnostics(context.CancellationToken)
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.AreEqual("CS0103", original.Id);
        Document corrected = await ApplyAggregateCombineFixAsync(document, Assert.ContainsSingle(diagnostics));
        (Compilation output, ImmutableArray<Diagnostic> remaining) = await GenerateCodeFixDocumentAsync(corrected);
        Assert.IsEmpty(remaining);
        Diagnostic retained = Assert.ContainsSingle(output.GetDiagnostics(context.CancellationToken)
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.AreEqual(original.Id, retained.Id);
        Assert.AreEqual(original.GetMessage(System.Globalization.CultureInfo.InvariantCulture),
            retained.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
        Assert.AreEqual("Missing", retained.Location.SourceTree!.GetText(context.CancellationToken).ToString(retained.Location.SourceSpan));
        INamedTypeSymbol aggregate = output.GetTypeByMetadataName("First")!;
        INamedTypeSymbol capability = Assert.ContainsSingle(aggregate.AllInterfaces.Where(static value => value.Name == "IPgCombinableAggregate"));
        IMethodSymbol implementation = Assert.IsInstanceOfType<IMethodSymbol>(aggregate.FindImplementationForInterfaceMember(
            Assert.ContainsSingle(capability.GetMembers("Combine"))));
        Assert.AreEqual("Combine", implementation.Name);
        Assert.Contains("COMBINEFUNC = \"first_combine\"", InstallationBody(output));
    }

    /// <summary>
    /// A callback inherited from actual metadata is diagnosed on its editable aggregate and repaired without changing the reference.
    /// </summary>
    [TestMethod]
    public async Task AggregateCombineFixUsesEditableAnchorForMetadataCallback()
    {
        CSharpCompilation external = CSharpCompilation.Create("ExternalAggregateBase", [CSharpSyntaxTree.ParseText("""
            public abstract class ExternalParent<T>
            {
                public static T Combine(Ankus.PgAggregateContext context, T state, T other) => state;
            }
            """, new CSharpParseOptions(LanguageVersion.CSharp14), cancellationToken: context.CancellationToken)], s_references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        using var image = new MemoryStream();
        Microsoft.CodeAnalysis.Emit.EmitResult emission = external.Emit(image, cancellationToken: context.CancellationToken);
        Assert.IsTrue(emission.Success, string.Join(Environment.NewLine, emission.Diagnostics));
        using var workspace = new AdhocWorkspace();
        Document document = CreateCodeFixDocument(workspace, """
            [Ankus.PgAggregate(InitialCondition = "0")]
            public sealed class First : ExternalParent<int>, Ankus.IPgAggregate<int, int>
            {
                public static int Transition(Ankus.PgAggregateContext context, int state, int value) => state + value;
            }
            """);
        document = document.Project.AddMetadataReference(MetadataReference.CreateFromImage(image.ToArray())).GetDocument(document.Id)!;
        (_, ImmutableArray<Diagnostic> diagnostics) = await GenerateCodeFixDocumentAsync(document);
        Diagnostic error = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual("ANKUS111", error.Id);
        Assert.IsTrue(error.Location.IsInSource);
        Assert.AreEqual("First", error.Location.SourceTree!.GetText(context.CancellationToken).ToString(error.Location.SourceSpan));
        Document corrected = await ApplyAggregateCombineFixAsync(document, error);
        (Compilation output, ImmutableArray<Diagnostic> remaining) = await GenerateCodeFixDocumentAsync(corrected);
        Assert.IsEmpty(remaining);
        Assert.IsEmpty(output.GetDiagnostics(context.CancellationToken).Where(static value => value.Severity == DiagnosticSeverity.Error));
        INamedTypeSymbol aggregate = output.GetTypeByMetadataName("First")!;
        INamedTypeSymbol capability = Assert.ContainsSingle(aggregate.AllInterfaces.Where(static value => value.Name == "IPgCombinableAggregate"));
        IMethodSymbol implementation = Assert.IsInstanceOfType<IMethodSymbol>(aggregate.FindImplementationForInterfaceMember(
            Assert.ContainsSingle(capability.GetMembers("Combine"))));
        Assert.AreEqual("ExternalAggregateBase", implementation.ContainingAssembly.Name);
        Assert.Contains("COMBINEFUNC = \"first_combine\"", InstallationBody(output));
    }

    /// <summary>
    /// A stale diagnostic cannot change an unrelated method or duplicate a capability already supplied explicitly.
    /// </summary>
    /// <param name="other">The unrelated or already-correct aggregate declaration.</param>
    [TestMethod]
    [DataRow("public class Other { public static int Combine(Ankus.PgAggregateContext context, int state, int other) => state; }")]
    [DataRow("namespace Faux { public sealed class PgAggregateAttribute : System.Attribute; } [Faux.PgAggregate] public class Other : Ankus.IPgAggregate<int, int> { public static int Transition(Ankus.PgAggregateContext context, int state, int value) => state; public static int Combine(Ankus.PgAggregateContext context, int state, int other) => state; }")]
    [DataRow("[Ankus.PgAggregate(InitialCondition = \"0\")] public class Other : Ankus.IPgAggregate<int, int>, Ankus.IPgCombinableAggregate<int> { public static int Transition(Ankus.PgAggregateContext context, int state, int value) => state; public static int Combine(Ankus.PgAggregateContext context, int state, int other) => state; }")]
    [DataRow("[Ankus.PgAggregate(InitialCondition = \"0\")] public class Other : Ankus.IPgAggregate<int, int>, Ankus.IPgCombinableAggregate<int> { public static int Transition(Ankus.PgAggregateContext context, int state, int value) => state; static int Ankus.IPgCombinableAggregate<int>.Combine(Ankus.PgAggregateContext context, int state, int other) => state; }")]
    public async Task AggregateCombineFixRejectsStaleTargets(string other)
    {
        using var workspace = new AdhocWorkspace();
        Document document = CreateCodeFixDocument(workspace, AggregateCombineSource("First") + "\n" + other);
        (_, ImmutableArray<Diagnostic> before) = await GenerateCodeFixDocumentAsync(document);
        Diagnostic original = Assert.ContainsSingle(before);
        SyntaxNode root = (await document.GetSyntaxRootAsync(context.CancellationToken))!;
        TypeDeclarationSyntax unrelated = root.DescendantNodes().OfType<TypeDeclarationSyntax>().Single(static type => type.Identifier.ValueText == "Other");
        MethodDeclarationSyntax callback = unrelated.Members.OfType<MethodDeclarationSyntax>().Single(static method => method.Identifier.ValueText == "Combine");
        Diagnostic stale = Diagnostic.Create(original.Descriptor, callback.Identifier.GetLocation(), "Combine");

        Assert.IsEmpty(await CodeFixActionsAsync(new AggregateCombineCodeFixProvider(), document, stale));
    }

    /// <summary>
    /// Cancellation stops semantic correction before any solution edit is offered.
    /// </summary>
    [TestMethod]
    public async Task AggregateCombineFixHonorsCancellation()
    {
        using var workspace = new AdhocWorkspace();
        Document document = CreateCodeFixDocument(workspace, AggregateCombineSource("First"));
        (_, ImmutableArray<Diagnostic> before) = await GenerateCodeFixDocumentAsync(document);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        await cancellation.CancelAsync();
        var actions = new List<CodeAction>();
        var provider = new AggregateCombineCodeFixProvider();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => provider.RegisterCodeFixesAsync(
            new CodeFixContext(document, Assert.ContainsSingle(before), (action, _) => actions.Add(action), cancellation.Token)));
        Assert.IsEmpty(actions);
    }

    /// <summary>
    /// Supplies a valid integer aggregate missing only its typed combine capability.
    /// </summary>
    /// <param name="name">The distinct aggregate name.</param>
    /// <returns>The complete consumer declaration.</returns>
    private static string AggregateCombineSource(string name)
        => $$"""
            [Ankus.PgAggregate(InitialCondition = "0")]
            public sealed class {{name}} : Ankus.IPgAggregate<int, int>
            {
                public static int Transition(Ankus.PgAggregateContext context, int state, int value) => state + value;
                public static int Combine(Ankus.PgAggregateContext context, int state, int other) => state + other;
            }
            """;

    /// <summary>
    /// Applies the single semantic aggregate correction without relying on source-text replacement.
    /// </summary>
    /// <param name="document">The document containing the diagnosed callback.</param>
    /// <param name="diagnostic">The actual generator diagnostic.</param>
    /// <returns>The same document in the corrected solution.</returns>
    private async Task<Document> ApplyAggregateCombineFixAsync(Document document, Diagnostic diagnostic)
    {
        var provider = new AggregateCombineCodeFixProvider();
        CodeAction action = Assert.ContainsSingle(await CodeFixActionsAsync(provider, document, diagnostic));
        ImmutableArray<CodeActionOperation> operations = await action.GetOperationsAsync(context.CancellationToken);
        ApplyChangesOperation change = Assert.IsInstanceOfType<ApplyChangesOperation>(Assert.ContainsSingle(operations));
        return change.ChangedSolution.GetDocument(document.Id)!;
    }
}
