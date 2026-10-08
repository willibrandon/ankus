using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Ankus.Generators.Tests;

/// <summary>
/// Verifies stable declaration keys and actual generator caching for file-scoped namespaces used by new projects.
/// </summary>
public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// The scaffolded file-scoped namespace shape.
    /// </summary>
    private const string FileScopedSource = """
        namespace Acme.Extension;

        public static class Functions
        {
            [Ankus.PgFunction]
            public static int First() => 41;

            [Ankus.PgFunction]
            public static int Answer() => First() + 1;
        }
        """;

    /// <summary>
    /// Unrelated edits inside a file-scoped namespace keep declarations, composition and native rendering cached.
    /// Edits that move later lines update only SQL provenance.
    /// </summary>
    /// <param name="edit">The unrelated source edit.</param>
    [TestMethod]
    [DataRow("body")]
    [DataRow("member")]
    [DataRow("type")]
    [DataRow("move")]
    public void FileScopedNamespaceCachesUnrelatedEdits(string edit)
    {
        CSharpCompilation initial = ModuleCompilation(FileScopedSource);
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        (string replacement, int line) = edit switch
        {
            "body" => (FileScopedSource.Replace("=> 41;", "=> 41 + 0 + 0;", StringComparison.Ordinal), 6),
            "member" => (FileScopedSource.Replace("    [Ankus.PgFunction]\n    public static int First()",
                "    private static int Helper() => 1;\n\n    [Ankus.PgFunction]\n    public static int First()",
                StringComparison.Ordinal), 8),
            "type" => (FileScopedSource.Replace("public static class Functions",
                "internal static class Earlier\n{\n    internal const int Value = 1;\n}\n\npublic static class Functions",
                StringComparison.Ordinal), 11),
            _ => ("\n\n" + FileScopedSource, 8),
        };
        Assert.AreNotEqual(FileScopedSource, replacement);
        driver = RunModule(driver, initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(
            replacement, path: "Module.cs", cancellationToken: context.CancellationToken)), out Compilation second);

        Assert.AreEqual(IncrementalStepRunReason.Cached, TrackedFunctionEmission(driver, "first").Reason);
        Assert.AreEqual(IncrementalStepRunReason.Cached, TrackedFunctionEmission(driver, "answer").Reason);
        Assert.AreEqual(IncrementalStepRunReason.Cached, TrackedFunctionSqlEmission(driver, "answer").Reason);
        Assert.AreEqual(IncrementalStepRunReason.Cached, ModuleStep(driver, "ExtensionComposition"));
        AssertFinalRenderingCached(driver, manifest: line == 6);
        Assert.AreEqual(ManifestValue(first, "Ankus.NativeSource"), ManifestValue(second, "Ankus.NativeSource"));
        Assert.AreEqual(InstallationBody(first), InstallationBody(second));
        Assert.Contains("FUNCTION \"answer\"()", InstallationBody(second));
        Assert.Contains("-- Module.cs:6\n-- Acme.Extension.Functions.First()\n", ManifestValue(first, "Ankus.Sql"));
        Assert.Contains("-- Module.cs:" + line + "\n-- Acme.Extension.Functions.First()\n", ManifestValue(second, "Ankus.Sql"));
        Assert.Contains("-- Module.cs:" + (line + 3) + "\n-- Acme.Extension.Functions.Answer()\n", ManifestValue(second, "Ankus.Sql"));
    }

    /// <summary>
    /// Cached diagnostics inside a file-scoped namespace resolve exact current spans after earlier edits and recover after repair.
    /// </summary>
    [TestMethod]
    public void FileScopedNamespaceCachedDiagnosticsFollowCurrentSpans()
    {
        const string Source = "namespace Acme.Extension;\n\npublic static class Functions\n{\n" +
            "    [Ankus.PgFunction]\n    public static int First() => 41;\n\n" +
            "    [Ankus.PgFunction]\n    public static Missing Answer() => default!;\n}\n";
        CSharpCompilation initial = ModuleCompilation(Source);
        GeneratorDriver driver = ModuleDriver().RunGeneratorsAndUpdateCompilation(initial, out _, out ImmutableArray<Diagnostic> first,
            context.CancellationToken);
        Diagnostic previous = Assert.ContainsSingle(first);
        Assert.AreEqual("ANKUS039", previous.Id);
        string replacement = Source.Replace("=> 41;", "=> 41 + 0 + 0;", StringComparison.Ordinal)
            .Replace("    [Ankus.PgFunction]\n    public static Missing", "    private static int Helper() => 1;\n\n" +
                "    [Ankus.PgFunction]\n    public static Missing", StringComparison.Ordinal);
        SyntaxTree current = CSharpSyntaxTree.ParseText(replacement, path: "Module.cs", cancellationToken: context.CancellationToken);
        driver = driver.RunGeneratorsAndUpdateCompilation(initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), current), out _,
            out ImmutableArray<Diagnostic> errors, context.CancellationToken);
        Diagnostic error = Assert.ContainsSingle(errors);

        Assert.IsNull(Assert.ContainsSingle(driver.GetRunResult().Results).Exception);
        Assert.AreEqual(IncrementalStepRunReason.Cached, ModuleStep(driver, "ExtensionComposition"));
        Assert.AreEqual(IncrementalStepRunReason.Cached, ModuleStep(driver, "ExtensionProblems"));
        Assert.AreEqual(previous.Id, error.Id);
        Assert.AreSame(current, error.Location.SourceTree);
        Assert.AreEqual(replacement.IndexOf("Missing", StringComparison.Ordinal), error.Location.SourceSpan.Start);
        Assert.AreEqual("Missing", current.GetText(context.CancellationToken).ToString(error.Location.SourceSpan));
        Assert.AreEqual(10, error.Location.GetLineSpan().StartLinePosition.Line);

        driver = RunModule(driver, initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(
            replacement.Replace("Missing Answer() => default!;", "int Answer() => First() + 1;", StringComparison.Ordinal),
            path: "Module.cs", cancellationToken: context.CancellationToken)), out Compilation repaired);
        Assert.AreEqual(IncrementalStepRunReason.Modified, ModuleStep(driver, "ExtensionComposition"));
        Assert.Contains("FUNCTION \"answer\"()", InstallationBody(repaired));
    }

    /// <summary>
    /// File-scoped and block namespace declarations contribute only their names to member keys.
    /// </summary>
    /// <param name="fileScoped">Whether the namespace uses the file-scoped form.</param>
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void NamespaceDeclarationKeysExcludeNamespaceMembers(bool fileScoped)
    {
        string source = fileScoped ? FileScopedSource
            : FileScopedSource.Replace("namespace Acme.Extension;\n", "namespace Acme.Extension\n{", StringComparison.Ordinal) + "\n}";
        string edited = source.Replace("=> 41;", "=> 41 + 0 + 0;", StringComparison.Ordinal)
            .Replace("public static class Functions", "internal static class Earlier\n{\n}\n\npublic static class Functions",
                StringComparison.Ordinal);
        string[] before = Keys(source);
        string[] after = Keys(edited);

        Assert.HasCount(4, before);
        Assert.HasCount(5, after);
        Assert.AreSequenceEqual<string>([before[0], before[1], before[2], before[3]], [after[0], after[2], after[3], after[4]]);
        Assert.AreEqual(before[0], Keys(fileScoped ? "namespace Acme.Extension;" : "namespace Acme.Extension { }")[0]);
        Assert.AreEqual(before[0], Keys(fileScoped ? "namespace Acme.Extension { }" : "namespace Acme.Extension;")[0]);
        foreach (string key in after.Where(static (_, index) => index != 1))
        {
            Assert.DoesNotContain("41", key);
            Assert.DoesNotContain("Earlier", key);
        }

        string[] Keys(string text)
            => [.. GeneratorLocation.Members(CSharpSyntaxTree.ParseText(text, cancellationToken: context.CancellationToken)
                .GetRoot(context.CancellationToken)).Select(static member => GeneratorLocation.Key(member))];
    }
}
