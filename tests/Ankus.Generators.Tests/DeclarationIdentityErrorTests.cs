using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Ankus.Generators.Tests;

/// <summary>
/// Verifies unresolved editor types retain identities and diagnostics until compiler resolution recovers.
/// </summary>
public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Unresolved scalar, array and generic types report supported-function errors without crashing the generator.
    /// </summary>
    /// <param name="type">The unresolved signature shape.</param>
    /// <param name="result">Whether the unresolved type is the result instead of a parameter.</param>
    [TestMethod]
    [DataRow("Missing", false)]
    [DataRow("Missing", true)]
    [DataRow("Missing[]", false)]
    [DataRow("Missing[]", true)]
    [DataRow("Missing<int>", false)]
    [DataRow("Missing<int>", true)]
    public void DeclarationIdentityUnresolvedTypesReportAndRecover(string type, bool result)
    {
        string source = "public static class Functions { [Ankus.PgFunction] public static " +
            (result ? type + " Answer() => default;" : "int Answer(" + type + " value) => 42;") + " }";
        CSharpCompilation initial = ModuleCompilation(source);
        INamedTypeSymbol owner = initial.GetTypeByMetadataName("Functions") ?? throw new InvalidOperationException("Missing authored owner.");
        var method = (IMethodSymbol)Assert.ContainsSingle(owner.GetMembers("Answer"));
        ITypeSymbol unresolved = result ? method.ReturnType : Assert.ContainsSingle(method.Parameters).Type;
        DeclarationIdentity identity = DeclarationIdentity.Create(unresolved);
        GeneratorDriver driver = ModuleDriver().RunGeneratorsAndUpdateCompilation(initial, out _,
            out ImmutableArray<Diagnostic> errors, context.CancellationToken);
        Assert.AreEqual("ANKUS001", Assert.ContainsSingle(errors).Id);

        SyntaxTree tree = CSharpSyntaxTree.ParseText(source + "\n// independent edit", path: "Module.cs", cancellationToken: context.CancellationToken);
        CSharpCompilation edited = initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), tree);
        owner = edited.GetTypeByMetadataName("Functions") ?? throw new InvalidOperationException("Missing edited owner.");
        method = (IMethodSymbol)Assert.ContainsSingle(owner.GetMembers("Answer"));
        DeclarationIdentity current = DeclarationIdentity.Create(result ? method.ReturnType : Assert.ContainsSingle(method.Parameters).Type);
        Assert.AreEqual(identity, current);
        Assert.AreEqual(identity.GetHashCode(), current.GetHashCode());
        driver = driver.RunGeneratorsAndUpdateCompilation(edited, out _, out errors, context.CancellationToken);
        Diagnostic error = Assert.ContainsSingle(errors);
        Assert.AreEqual("ANKUS001", error.Id);
        Assert.AreSame(tree, error.Location.SourceTree);

        driver = RunModule(driver, edited.ReplaceSyntaxTree(tree, CSharpSyntaxTree.ParseText(
            "public static class Functions { [Ankus.PgFunction] public static int Answer() => 42; }",
            path: "Module.cs", cancellationToken: context.CancellationToken)), out Compilation repaired);
        Assert.AreEqual(42, InvokeSqlReferenceAnswer(repaired));
        Assert.Contains("CREATE FUNCTION", InstallationBody(repaired));
        Assert.AreEqual(IncrementalStepRunReason.Modified, ModuleStep(driver, "FunctionAnalysis"));
    }
}
