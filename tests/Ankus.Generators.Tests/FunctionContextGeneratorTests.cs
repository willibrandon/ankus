using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Ankus.Generators.Tests;

public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Function contexts disappear from SQL signatures without changing defaults, names, or NULL handling.
    /// </summary>
    /// <param name="parameters">The managed declaration.</param>
    /// <param name="sqlParameters">The exact SQL-visible parameters.</param>
    /// <param name="policy">The SQL NULL-input policy.</param>
    [TestMethod]
    [DataRow("Ankus.PgFunctionContext call", "", "STRICT")]
    [DataRow("Ankus.PgFunctionContext? call = null", "", "STRICT")]
    [DataRow("Ankus.PgFunctionContext call, int value", "\"value\" integer", "STRICT")]
    [DataRow("int? value, Ankus.PgFunctionContext call", "\"value\" integer", "CALLED ON NULL INPUT")]
    [DataRow("Ankus.PgMemoryContext owner, int left, Ankus.PgFunctionContext call, int right, Ankus.PgFunctionContext again",
        "\"left\" integer, \"right\" integer", "STRICT")]
    [DataRow("Ankus.PgFunctionContext call, [Ankus.PgParameter(Name = \"input\", Default = \"42\")] int value",
        "\"input\" integer DEFAULT (42)", "STRICT")]
    public void FunctionContextsPreserveSqlSignatures(string parameters, string sqlParameters, string policy)
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate(
            "public static class Functions { [Ankus.PgFunction] public static int Apply(" + parameters + ") => 42; }");
        AssertMemoryCompilationSucceeds(compilation, diagnostics);
        string sql = Assert.ContainsSingle(OperatorCastStatements(compilation));
        Assert.StartsWith("CREATE FUNCTION \"apply\"(" + sqlParameters + ") RETURNS integer AS ", sql);
        Assert.Contains(" PARALLEL UNSAFE " + policy + " SECURITY INVOKER ", sql);
        MethodDeclarationSyntax dispatcher = compilation.SyntaxTrees.SelectMany(tree => tree.GetRoot(context.CancellationToken).DescendantNodes())
            .OfType<MethodDeclarationSyntax>().Single(static method => method.Identifier.ValueText.StartsWith("ankus_managed_", StringComparison.Ordinal));
        Assert.HasCount(1, dispatcher.DescendantNodes().OfType<InvocationExpressionSyntax>().Where(static expression =>
            expression.Expression.ToString() == "global::Ankus.NativeBackend.CaptureFunction"));
        InvocationExpressionSyntax invocation = dispatcher.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Single(static expression => expression.Expression.ToString() == "global::Functions.@Apply");
        SemanticModel model = compilation.GetSemanticModel(dispatcher.SyntaxTree);
        IMethodSymbol target = Assert.IsInstanceOfType<IMethodSymbol>(model.GetSymbolInfo(invocation, context.CancellationToken).Symbol);
        Assert.AreEqual(target.Parameters.Length, invocation.ArgumentList.Arguments.Count);
        for (int index = 0; index < target.Parameters.Length; index++)
        {
            if (target.Parameters[index].Type.Name == "PgFunctionContext")
            {
                Assert.AreEqual("functionContext", invocation.ArgumentList.Arguments[index].Expression.ToString());
            }
        }
    }

    /// <summary>
    /// Invalid contexts are diagnosed instead of becoming SQL values or dropping SQL metadata silently.
    /// </summary>
    /// <param name="method">The invalid method.</param>
    /// <param name="diagnostic">The expected diagnostic identifier.</param>
    [TestMethod]
    [DataRow("public static int Apply([Ankus.PgParameter] Ankus.PgFunctionContext call) => 42;", "ANKUS004")]
    [DataRow("public static int Apply(ref Ankus.PgFunctionContext call) => 42;", "ANKUS001")]
    [DataRow("public static int Apply(Ankus.PgFunctionContext[] calls) => 42;", "ANKUS001")]
    [DataRow("public static Ankus.PgFunctionContext Apply(Ankus.PgFunctionContext call) => call;", "ANKUS001")]
    public void InvalidFunctionContextShapesAreDiagnosed(string method, string diagnostic)
    {
        (_, ImmutableArray<Diagnostic> diagnostics) = Generate("public static class Functions { [Ankus.PgFunction] " + method + " }");
        AssertVirtualContextDiagnostic(diagnostics, diagnostic);
    }

    /// <summary>
    /// Context-only differences cannot create duplicate PostgreSQL overloads.
    /// </summary>
    [TestMethod]
    public void FunctionContextErasureRejectsDuplicateSqlSignature()
    {
        (_, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            public static class Functions
            {
                [Ankus.PgFunction(Name = "apply")]
                public static int First(int value) => value;
                [Ankus.PgFunction(Name = "apply")]
                public static int Second(Ankus.PgFunctionContext call, int value) => value;
            }
            """);
        AssertVirtualContextDiagnostic(diagnostics, "ANKUS002");
    }

    /// <summary>
    /// Set factories capture their native call once before creating the iterator, never during advancement or cleanup.
    /// </summary>
    [TestMethod]
    public void SetFunctionContextIsCapturedDuringFactoryCreation()
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            public static class Functions
            {
                [Ankus.PgFunction]
                public static System.Collections.Generic.IEnumerable<int> Rows(Ankus.PgFunctionContext call, int? value)
                {
                    yield return call.Arguments[0].Read<int?>() ?? -1;
                }
            }
            """);
        AssertMemoryCompilationSucceeds(compilation, diagnostics);
        Assert.StartsWith("CREATE FUNCTION \"rows\"(\"value\" integer) RETURNS SETOF integer AS ",
            Assert.ContainsSingle(OperatorCastStatements(compilation)));
        InvocationExpressionSyntax capture = Assert.ContainsSingle(compilation.SyntaxTrees.SelectMany(tree => tree.GetRoot(context.CancellationToken).DescendantNodes())
            .OfType<InvocationExpressionSyntax>().Where(static invocation => invocation.Expression.ToString() == "global::Ankus.NativeBackend.CaptureFunction"));
        Assert.AreEqual("operation == 0", Assert.ContainsSingle(capture.Ancestors().OfType<IfStatementSyntax>()).Condition.ToString());
        Assert.AreEqual("functionCall", Assert.ContainsSingle(capture.ArgumentList.Arguments).Expression.ToString());
    }
}
