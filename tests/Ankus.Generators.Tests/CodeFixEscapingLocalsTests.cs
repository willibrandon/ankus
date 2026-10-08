using System.Collections.Immutable;
using Ankus.CodeFixes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Ankus.Generators.Tests;

public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Provides executable native-contract operations with observable out, tuple and borrowed-span values.
    /// </summary>
    private const string EscapingNativeDeclarations = """
        public static class RawEscapes
        {
            [Ankus.CompilerServices.NativeUnsafeAccess]
            public static bool Try(out int value)
            {
                value = 42;
                return true;
            }

            [Ankus.CompilerServices.NativeUnsafeAccess]
            public static (int Value, int Extra) Pair() => (42, 1);

            [Ankus.CompilerServices.NativeUnsafeAccess]
            public static System.Span<int> View(System.Span<int> value) => value;
        }
        """;

    /// <summary>
    /// Unsafe acknowledgments preserve out and deconstruction locals in their original enclosing scope.
    /// </summary>
    /// <param name="body">The valid original managed consumer body.</param>
    /// <param name="expected">The complete minimal corrected body.</param>
    [TestMethod]
    [DataRow("_ = RawEscapes.Try(out int value); return value + 1;", """
        {
            int value;
            unsafe
            {
                _ = RawEscapes.Try(out value);
            }

            return value + 1;
        }
        """)]
    [DataRow("int result = RawEscapes.Try(out var value) ? value : 0; return result + value - 41;", """
        {
            int value;
            int result;
            unsafe
            {
                result = RawEscapes.Try(out value) ? value : 0;
            }

            return result + value - 41;
        }
        """)]
    [DataRow("(int value, int extra) = RawEscapes.Pair(); return value + extra;", """
        {
            int value;
            int extra;
            unsafe
            {
                (value, extra) = RawEscapes.Pair();
            }

            return value + extra;
        }
        """)]
    [DataRow("var (value, extra) = RawEscapes.Pair(); return value + extra;", """
        {
            int value;
            int extra;
            unsafe
            {
                (value, extra) = RawEscapes.Pair();
            }

            return value + extra;
        }
        """)]
    [DataRow("int _ = 7; var (value, _) = RawEscapes.Pair(); return value + _ - 6;", """
        {
            int _ = 7;
            int value;
            unsafe
            {
                (value, var _) = RawEscapes.Pair();
            }

            return value + _ - 6;
        }
        """)]
    [DataRow("System.Span<int> value = RawEscapes.View(stackalloc int[] { 42 }); return value[0] + 1;", """
        {
            scoped System.Span<int> value;
            unsafe
            {
                value = RawEscapes.View(stackalloc int[] { 42 });
            }

            return value[0] + 1;
        }
        """)]
    [DataRow("var value = RawEscapes.View(stackalloc int[] { 42 }); return value[0] + 1;", """
        {
            scoped global::System.Span<int> value;
            unsafe
            {
                value = RawEscapes.View(stackalloc int[] { 42 });
            }

            return value[0] + 1;
        }
        """)]
    [DataRow("System.Span<int> storage = stackalloc int[] { 42 }; System.Span<int> value = RawEscapes.View(storage); return value[0] + 1;", """
        {
            System.Span<int> storage = stackalloc int[] { 42 };
            scoped System.Span<int> value;
            unsafe
            {
                value = RawEscapes.View(storage);
            }

            return value[0] + 1;
        }
        """)]
    [DataRow("System.Span<int> value = RawEscapes.View(new int[] { 42 }); return value[0] + 1;", """
        {
            System.Span<int> value;
            unsafe
            {
                value = RawEscapes.View(new int[] { 42 });
            }

            return value[0] + 1;
        }
        """)]
    [DataRow("scoped System.Span<int> value = RawEscapes.View(stackalloc int[] { 42 }); return value[0] + 1;", """
        {
            scoped System.Span<int> value;
            unsafe
            {
                value = RawEscapes.View(stackalloc int[] { 42 });
            }

            return value[0] + 1;
        }
        """)]
    public async Task NativeUnsafeFixPreservesEscapingStatementLocals(string body, string expected)
    {
        using var workspace = new AdhocWorkspace();
        Document document = CreateCodeFixDocument(workspace, EscapingNativeDeclarations +
            "public sealed class Consumer { public static int Run() { " + body + " } }");
        (Compilation before, ImmutableArray<Diagnostic> diagnostics) = await AnalyzeNativeCodeFixDocumentAsync(document);
        Assert.AreEqual(43, ExecuteNativeConsumer(before));
        Document corrected = await ApplyNativeCodeFixAsync(document, Assert.ContainsSingle(diagnostics));
        (Compilation after, ImmutableArray<Diagnostic> remaining) = await AnalyzeNativeCodeFixDocumentAsync(corrected);
        Assert.IsEmpty(remaining, "The offered action must apply its unsafe acknowledgment without losing declared locals.");
        Assert.IsEmpty(IntroducedWarnings(before, after), "The unsafe acknowledgment must not relax a local's ref-safety errors into warnings.");
        Assert.AreEqual(43, ExecuteNativeConsumer(after));
        SyntaxNode root = (await corrected.GetSyntaxRootAsync(context.CancellationToken))!;
        MethodDeclarationSyntax method = Assert.ContainsSingle(root.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Where(static declaration => declaration.Identifier.ValueText == "Run"));
        Assert.IsNotNull(method.Body);
        Assert.AreEqual(SyntaxFactory.ParseStatement(expected).NormalizeWhitespace(eol: "\n").ToFullString(),
            method.Body.NormalizeWhitespace(eol: "\n").ToFullString());
    }

    /// <summary>
    /// A heap-backed span that the method returns keeps its caller-safe declaration instead of being narrowed by scoped.
    /// </summary>
    [TestMethod]
    public async Task NativeUnsafeFixKeepsReturnableSpanUnscoped()
    {
        const string Source = """
            public sealed class Consumer
            {
                public static System.Span<int> Escape()
                {
                    System.Span<int> value = RawEscapes.View(new int[] { 42 });
                    return value;
                }

                public static int Run() => Escape()[0] + 1;
            }
            """;
        const string Expected = """
            {
                System.Span<int> value;
                unsafe
                {
                    value = RawEscapes.View(new int[] { 42 });
                }

                return value;
            }
            """;
        using var workspace = new AdhocWorkspace();
        Document document = CreateCodeFixDocument(workspace, EscapingNativeDeclarations + Source);
        (Compilation before, ImmutableArray<Diagnostic> diagnostics) = await AnalyzeNativeCodeFixDocumentAsync(document);
        Assert.AreEqual(43, ExecuteNativeConsumer(before));
        Document corrected = await ApplyNativeCodeFixAsync(document, Assert.ContainsSingle(diagnostics));
        (Compilation after, ImmutableArray<Diagnostic> remaining) = await AnalyzeNativeCodeFixDocumentAsync(corrected);
        Assert.IsEmpty(remaining);
        Assert.IsEmpty(IntroducedWarnings(before, after));
        Assert.AreEqual(43, ExecuteNativeConsumer(after));
        SyntaxNode root = (await corrected.GetSyntaxRootAsync(context.CancellationToken))!;
        MethodDeclarationSyntax method = Assert.ContainsSingle(root.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Where(static declaration => declaration.Identifier.ValueText == "Escape"));
        Assert.IsNotNull(method.Body);
        Assert.AreEqual(SyntaxFactory.ParseStatement(Expected).NormalizeWhitespace(eol: "\n").ToFullString(),
            method.Body.NormalizeWhitespace(eol: "\n").ToFullString());
    }

    /// <summary>
    /// Lists compiler warnings present after a correction but absent from the original compilation.
    /// </summary>
    /// <param name="before">The original compilation.</param>
    /// <param name="after">The corrected compilation.</param>
    /// <returns>The introduced warning identifiers and messages.</returns>
    private string[] IntroducedWarnings(Compilation before, Compilation after)
    {
        HashSet<string> original = [.. before.GetDiagnostics(context.CancellationToken)
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning)
            .Select(static diagnostic => diagnostic.Id + ": " + diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture))];
        return [.. after.GetDiagnostics(context.CancellationToken)
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning)
            .Select(static diagnostic => diagnostic.Id + ": " + diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture))
            .Where(warning => !original.Contains(warning))];
    }

    /// <summary>
    /// Fix All retains out and tuple declarations inside their distinct nested scopes and keeps native regions minimal.
    /// </summary>
    [TestMethod]
    public async Task NativeUnsafeFixAllPreservesNestedEscapingLocals()
    {
        const string Source = """
            public sealed class Consumer
            {
                public static int Run()
                {
                    int total = 0;
                    {
                        _ = RawEscapes.Try(out int value);
                        total += value;
                        {
                            var (inner, extra) = RawEscapes.Pair();
                            total += inner + extra;
                        }
                    }

                    return total;
                }
            }
            """;
        const string Expected = """
            {
                int total = 0;
                {
                    int value;
                    unsafe
                    {
                        _ = RawEscapes.Try(out value);
                    }

                    total += value;
                    {
                        int inner;
                        int extra;
                        unsafe
                        {
                            (inner, extra) = RawEscapes.Pair();
                        }

                        total += inner + extra;
                    }
                }

                return total;
            }
            """;
        using var workspace = new AdhocWorkspace();
        Document document = CreateCodeFixDocument(workspace, EscapingNativeDeclarations + Source);
        (Compilation before, ImmutableArray<Diagnostic> diagnostics) = await AnalyzeNativeCodeFixDocumentAsync(document);
        Assert.AreEqual(85, ExecuteNativeConsumer(before));
        Assert.HasCount(2, diagnostics);
        var provider = new NativeUnsafeAccessCodeFixProvider();
        var fixContext = new FixAllContext(document, provider, FixAllScope.Project, nameof(NativeUnsafeAccessCodeFixProvider),
            provider.FixableDiagnosticIds, new InjectedMetadataDiagnosticProvider(diagnostics), context.CancellationToken);
        CodeAction? action = await provider.GetFixAllProvider().GetFixAsync(fixContext);
        Assert.IsNotNull(action);
        ApplyChangesOperation change = Assert.IsInstanceOfType<ApplyChangesOperation>(
            Assert.ContainsSingle(await action.GetOperationsAsync(context.CancellationToken)));
        Document corrected = change.ChangedSolution.GetDocument(document.Id)!;
        (Compilation after, ImmutableArray<Diagnostic> remaining) = await AnalyzeNativeCodeFixDocumentAsync(corrected);
        Assert.IsEmpty(remaining);
        Assert.AreEqual(85, ExecuteNativeConsumer(after));
        SyntaxNode root = (await corrected.GetSyntaxRootAsync(context.CancellationToken))!;
        MethodDeclarationSyntax method = Assert.ContainsSingle(root.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Where(static declaration => declaration.Identifier.ValueText == "Run"));
        Assert.IsNotNull(method.Body);
        Assert.AreEqual(SyntaxFactory.ParseStatement(Expected).NormalizeWhitespace(eol: "\n").ToFullString(),
            method.Body.NormalizeWhitespace(eol: "\n").ToFullString());
        Assert.HasCount(2, method.Body.DescendantNodes().OfType<UnsafeStatementSyntax>());
    }
}
