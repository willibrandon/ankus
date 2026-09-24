using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Emit;

namespace Ankus.Generators.Tests;

public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Every managed callback family receives the independent memory capability and restores it inside the native error boundary.
    /// </summary>
    /// <param name="source">A minimal extension exercising one callback family.</param>
    /// <param name="managedParameters">The ordered managed ABI parameter types.</param>
    /// <param name="nativeParameters">The matching ordered native ABI parameter types.</param>
    [TestMethod]
    [DataRow("public static class Functions { [Ankus.PgFunction] public static string Name() => Ankus.PgMemoryContext.Current.Name; }",
        "Ankus.NativeValue*,Ankus.NativeValue*,Ankus.NativeCallError*,nint,nint",
        "const AnkusValue *, AnkusValue *, AnkusError *, AnkusExecute, AnkusMemoryApi *")]
    [DataRow("public static class Functions { [Ankus.PgFunction] public static void Run() { Ankus.PgMemoryContext.Current.Run(() => { }); } }",
        "Ankus.NativeValue*,Ankus.NativeValue*,Ankus.NativeCallError*,nint,nint",
        "const AnkusValue *, AnkusValue *, AnkusError *, AnkusExecute, AnkusMemoryApi *")]
    [DataRow("public static class Functions { [Ankus.PgOperator(\"@+\")][Ankus.PgCast] public static string Render(int value) => Ankus.PgMemoryContext.Current.Name; }",
        "Ankus.NativeValue*,Ankus.NativeValue*,Ankus.NativeCallError*,nint,nint",
        "const AnkusValue *, AnkusValue *, AnkusError *, AnkusExecute, AnkusMemoryApi *")]
    [DataRow("public static class Functions { [Ankus.PgFunction] public static System.Collections.Generic.IEnumerable<string> Names() { yield return Ankus.PgMemoryContext.Current.Name; } }",
        "int,nint*,Ankus.NativeValue*,Ankus.NativeValue*,Ankus.NativeCallError*,nint,nint",
        "int, void **, const AnkusValue *, AnkusValue *, AnkusError *, AnkusExecute, AnkusMemoryApi *")]
    [DataRow("public static class Functions { [Ankus.PgTrigger] public static Ankus.PgHeapTuple? Audit(Ankus.PgTriggerContext context) { Ankus.PgMemoryContext.Current.Run(() => { }); return context.New; } }",
        "Ankus.NativeValue*,Ankus.NativeValue*,Ankus.NativeCallError*,nint,nint",
        "const AnkusValue *, AnkusValue *, AnkusError *, AnkusExecute, AnkusMemoryApi *")]
    [DataRow("public static class Functions { [Ankus.PgEventTrigger] public static void Audit(Ankus.PgEventTriggerContext context) { Ankus.PgMemoryContext.Current.Run(() => { }); } }",
        "Ankus.NativeValue*,Ankus.NativeValue*,Ankus.NativeCallError*,nint,nint",
        "const AnkusValue *, AnkusValue *, AnkusError *, AnkusExecute, AnkusMemoryApi *")]
    [DataRow("[Ankus.PgAggregate(InitialCondition = \"0\")] public static class Total { public static int Transition(int state, int value) => Ankus.PgMemoryContext.Current.Run(() => state + value); }",
        "Ankus.NativeValue*,Ankus.NativeValue*,Ankus.NativeCallError*,nint,Ankus.NativeValue*,int,nint,nint,nint",
        "const AnkusValue *, AnkusValue *, AnkusError *, AnkusExecute, const AnkusValue *, int, void *, void *, AnkusMemoryApi *")]
    [DataRow("public static class Functions { [Ankus.PgInitialize] public static void Initialize() { Ankus.PgMemoryContext.Current.Run(() => { }); } }",
        "Ankus.NativeCallError*,nint,nint,nint,nint",
        "AnkusError *, AnkusGucReadBinding, AnkusExecute, AnkusInitializationLog, AnkusMemoryApi *")]
    [DataRow("public static partial class Settings { [Ankus.PgGucInt(\"demo.value\", 1, \"Value\", Check = nameof(Check))] public static partial int Value { get; } public static Ankus.PgGucCheckResult<int> Check(int value, Ankus.PgGucSource source) => new(Ankus.PgMemoryContext.Current.Run(() => value)); }",
        "int,Ankus.NativeValue*,Ankus.NativeValue*,int,Ankus.NativeCallError*,nint,nint,nint,nint",
        "int, AnkusValue *, AnkusValue *, int, AnkusError *, AnkusGucRead, AnkusExecute, AnkusGucLog, AnkusMemoryApi *")]
    [DataRow("public static partial class Settings { [Ankus.PgGucInt(\"demo.value\", 1, \"Value\", Assign = nameof(Assign))] public static partial int Value { get; } public static void Assign(int value, Ankus.PgGucExtra? extra) { Ankus.PgMemoryContext.Current.Run(() => { }); } }",
        "int,Ankus.NativeValue*,Ankus.NativeValue*,int,Ankus.NativeCallError*,nint,nint,nint,nint",
        "int, AnkusValue *, AnkusValue *, int, AnkusError *, AnkusGucRead, AnkusExecute, AnkusGucLog, AnkusMemoryApi *")]
    [DataRow("public static partial class Settings { [Ankus.PgGucInt(\"demo.value\", 1, \"Value\", Show = nameof(Show))] public static partial int Value { get; } public static string Show(int value, Ankus.PgGucExtra? extra) => Ankus.PgMemoryContext.Current.Name; }",
        "int,Ankus.NativeValue*,Ankus.NativeValue*,int,Ankus.NativeCallError*,nint,nint,nint,nint",
        "int, AnkusValue *, AnkusValue *, int, AnkusError *, AnkusGucRead, AnkusExecute, AnkusGucLog, AnkusMemoryApi *")]
    public void GeneratedCallbacksBindMemoryWithinExceptionBoundary(string source, string managedParameters, string nativeParameters)
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate(source);
        AssertMemoryCompilationSucceeds(compilation, diagnostics);
        IMethodSymbol callback = Assert.ContainsSingle(compilation.GetTypeByMetadataName("Ankus.Generated.ExtensionDispatchers")!
            .GetMembers().OfType<IMethodSymbol>());
        Assert.AreEqual(managedParameters, string.Join(',', callback.Parameters.Select(static parameter => parameter.Type.ToDisplayString())));
        Assert.AreEqual(SpecialType.System_Int32, callback.ReturnType.SpecialType);
        AttributeData entry = Assert.ContainsSingle(callback.GetAttributes());
        Assert.AreEqual("System.Runtime.InteropServices.UnmanagedCallersOnlyAttribute", entry.AttributeClass!.ToDisplayString());
        Assert.AreEqual(callback.Name, entry.NamedArguments.Single(static argument => argument.Key == "EntryPoint").Value.Value);
        Assert.AreEqual("System.Runtime.CompilerServices.CallConvCdecl",
            Assert.IsInstanceOfType<ITypeSymbol>(Assert.ContainsSingle(entry.NamedArguments.Single(static argument => argument.Key == "CallConvs").Value.Values).Value).ToDisplayString());
        MethodDeclarationSyntax syntax = Assert.IsInstanceOfType<MethodDeclarationSyntax>(callback.DeclaringSyntaxReferences.Single().GetSyntax(context.CancellationToken));
        string[] exits = callback.Parameters[0].Name switch
        {
            "phase" => ["global::Ankus.NativeLog.Exit(previousLog);", "global::Ankus.NativeGuc.Exit(previousRead);", "global::Ankus.NativeBackend.Exit(previousBackend);"],
            "operation" => ["global::Ankus.NativeBackend.Exit(previous, operation == 3);"],
            "error" => ["global::Ankus.NativeLog.Exit(previousLog);", "global::Ankus.NativeGuc.Exit(previousRead);", "global::Ankus.NativeBackend.Exit(previousBackend);"],
            _ => ["global::Ankus.NativeBackend.Exit(previous);"],
        };
        AssertMemoryCallbackScope(syntax, exits);
        string native = ManifestValue(compilation, "Ankus.NativeSource");
        Assert.Contains($"extern int {callback.Name}({nativeParameters});", native);
        Assert.Contains("ankus_memory_initialize(&memory);", native);
        string nativeInvocation = callback.Parameters[0].Name switch
        {
            "phase" => "&frame->error, ankus_guc_read, NULL, ankus_guc_log, &memory);",
            "operation" => "backend ? ankus_spi_execute : NULL, &memory);",
            "error" => $"int status = {callback.Name}(error, ankus_read_guc,\n" +
                       "            IsTransactionState() ? ankus_spi_execute : NULL, ankus_initialization_log, &memory);",
            _ when callback.Parameters.Any(static parameter => parameter.Name == "owner") => "scope->owner, (void *) ankus_aggregate_api, &memory);",
            _ when source.Contains("PgTrigger", StringComparison.Ordinal) || source.Contains("PgEventTrigger", StringComparison.Ordinal)
                => "status = callback(arguments, result, error, ankus_spi_execute, &memory);",
            _ => $"status = {callback.Name}(arguments, &result, &error, ankus_spi_execute, &memory);",
        };
        Assert.Contains(nativeInvocation, native);
        Assert.Contains("ankus_memory_invoke(", native);
        Assert.Contains("ankus_capture_error(data, error);", native);
    }

    /// <summary>
    /// Iterator creation, advancement, normal disposal, and abort disposal all execute under the same per-call memory guard.
    /// </summary>
    [TestMethod]
    public void SetMemoryScopeEnclosesCreationAdvancementAndBothDisposalModes()
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            public static class Functions
            {
                [Ankus.PgFunction]
                public static System.Collections.Generic.IEnumerable<int> Rows() => new[] { 1, 2 };
            }
            """);
        AssertMemoryCompilationSucceeds(compilation, diagnostics);
        IMethodSymbol callback = Assert.ContainsSingle(compilation.GetTypeByMetadataName("Ankus.Generated.ExtensionDispatchers")!
            .GetMembers().OfType<IMethodSymbol>());
        MethodDeclarationSyntax syntax = Assert.IsInstanceOfType<MethodDeclarationSyntax>(callback.DeclaringSyntaxReferences.Single().GetSyntax(context.CancellationToken));
        Assert.AreEqual("nint previous = global::Ankus.NativeBackend.Enter(execute, operation == 3);", syntax.Body!.Statements[0].ToString());
        TryStatementSyntax guarded = AssertMemoryCallbackScope(syntax, "global::Ankus.NativeBackend.Exit(previous, operation == 3);");
        IfStatementSyntax dispose = Assert.IsInstanceOfType<IfStatementSyntax>(guarded.Block.Statements[2]);
        Assert.AreEqual("operation is 2 or 3", dispose.Condition.ToString());
        Assert.AreSequenceEqual(["global::Ankus.NativeSet.Dispose(ref *iterator);", "return 0;"],
            Assert.IsInstanceOfType<BlockSyntax>(dispose.Statement).Statements.Select(static statement => statement.ToString()));
        IfStatementSyntax create = Assert.IsInstanceOfType<IfStatementSyntax>(guarded.Block.Statements[3]);
        Assert.AreEqual("operation == 0", create.Condition.ToString());
        Assert.AreSequenceEqual(["*iterator = global::Ankus.NativeSet.Create<int>(global::Functions.@Rows());", "return 0;"],
            Assert.IsInstanceOfType<BlockSyntax>(create.Statement).Statements.Select(static statement => statement.ToString()));
        IfStatementSyntax next = Assert.IsInstanceOfType<IfStatementSyntax>(guarded.Block.Statements[4]);
        Assert.AreEqual("!global::Ankus.NativeSet.MoveNext<int>(*iterator, out int value)", next.Condition.ToString());
        Assert.AreEqual("return 2;", Assert.ContainsSingle(Assert.IsInstanceOfType<BlockSyntax>(next.Statement).Statements).ToString());
    }

    /// <summary>
    /// Aggregate state release receives a fresh memory envelope independently of the original transition callback.
    /// </summary>
    [TestMethod]
    public void AggregateStateReleaseNativeAbiCarriesIndependentMemoryCapability()
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            [Ankus.PgAggregate]
            public static class Total
            {
                public static Ankus.PgAggregateState<int> Transition(Ankus.PgAggregateState<int>? state, int value)
                    => new((state?.Value ?? 0) + value);

                public static int Final(Ankus.PgAggregateState<int>? state) => state?.Value ?? 0;
            }
            """);
        AssertMemoryCompilationSucceeds(compilation, diagnostics);
        string native = ManifestValue(compilation, "Ankus.NativeSource").ReplaceLineEndings("\n");
        Assert.Contains("typedef int (*AnkusAggregateRelease)(void *, AnkusError *, AnkusExecute, AnkusMemoryApi *);", native);
        Assert.Contains("ankus_memory_initialize(&memory);\n    ankus_memory_protect(&protection, state->cleanup_owner, true);", native);
        Assert.Contains("AnkusAggregateRelease release = state->release;", native);
        Assert.Contains("status = release(handle, &error, ankus_spi_execute, &memory);", native);
        Assert.Contains("ankus_memory_protection = protection.previous;\n        ankus_aggregate_scope = previous;", native);
        Assert.Contains("state = MemoryContextAllocZero(TopMemoryContext, sizeof(AnkusAggregateState));", native);
        Assert.Contains("MemoryContextRegisterResetCallback(state->cleanup_owner, &state->reset);", native);
        Assert.Contains("((AggState *) fcinfo->context)->ss.ps.state->es_query_cxt : scope->owner;", native);
        Assert.Contains("*output = handle;", native);
        Assert.Contains("arguments[index].integral = (intptr_t) DatumGetPointer(PG_GETARG_DATUM(index));", native);
        Assert.Contains("datum = (Datum) (uintptr_t) result->integral;", native);
        Assert.Contains("ankus_aggregate_scope = previous;\n        pfree(state);", native);
        Assert.DoesNotContain("ankus_aggregate_owners", native);
    }

    /// <summary>
    /// Native-only registration must not emit an unused memory dispatcher or start a managed memory scope.
    /// </summary>
    /// <param name="source">A declaration with no author managed callback.</param>
    [TestMethod]
    [DataRow("[Ankus.PgEnum] public enum Mode { First, Last }")]
    [DataRow("[assembly: Ankus.PgGucPrefix(\"demo\")]")]
    [DataRow("public static partial class Settings { [Ankus.PgGucInt(\"demo.value\", 1, \"Value\")] public static partial int Value { get; } }")]
    public void NativeOnlyDeclarationsOmitMemoryDispatch(string source)
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate(source);
        AssertMemoryCompilationSucceeds(compilation, diagnostics);
        string native = ManifestValue(compilation, "Ankus.NativeSource");
        Assert.DoesNotContain("ankus_memory_invoke(", native);
        Assert.DoesNotContain("ankus_memory_initialize(", native);
        Assert.DoesNotContain("AnkusMemoryApi memory", native);
        INamedTypeSymbol dispatchers = compilation.GetTypeByMetadataName("Ankus.Generated.ExtensionDispatchers")!;
        Assert.IsEmpty(dispatchers.GetMembers().OfType<IMethodSymbol>().Where(static method => method.GetAttributes()
            .Any(static attribute => attribute.AttributeClass?.ToDisplayString() == "System.Runtime.InteropServices.UnmanagedCallersOnlyAttribute")));
    }

    /// <summary>
    /// Verifies memory entry precedes conversion and user code while every guarded exit restores the enclosing capabilities in reverse order.
    /// </summary>
    /// <param name="callback">The generated unmanaged callback.</param>
    /// <param name="remainingExits">The surrounding capability exits after memory restoration.</param>
    /// <returns>The guarded body for callback-specific semantic assertions.</returns>
    private static TryStatementSyntax AssertMemoryCallbackScope(MethodDeclarationSyntax callback, params string[] remainingExits)
    {
        ParameterSyntax memory = callback.ParameterList.Parameters.Last();
        Assert.AreEqual("memory", memory.Identifier.ValueText);
        Assert.AreEqual("nint", memory.Type!.ToString());
        BlockSyntax body = Assert.IsInstanceOfType<BlockSyntax>(callback.Body);
        TryStatementSyntax guarded = Assert.ContainsSingle(body.Statements.OfType<TryStatementSyntax>());
        Assert.AreSame(guarded, body.Statements.Last());
        Assert.AreSequenceEqual(["nint previousMemory = 0;", "bool memoryEntered = false;"],
            body.Statements.SkipLast(1).TakeLast(2).Select(static statement => statement.ToString()));
        Assert.AreSequenceEqual(["previousMemory = global::Ankus.NativeMemoryContext.Enter(memory);", "memoryEntered = true;"],
            guarded.Block.Statements.Take(2).Select(static statement => statement.ToString()));
        Assert.HasCount(1, body.DescendantNodes().OfType<InvocationExpressionSyntax>().Where(static invocation =>
            invocation.Expression.ToString() == "global::Ankus.NativeMemoryContext.Enter"));
        Assert.HasCount(1, body.DescendantNodes().OfType<InvocationExpressionSyntax>().Where(static invocation =>
            invocation.Expression.ToString() == "global::Ankus.NativeMemoryContext.Exit"));
        CatchClauseSyntax failure = Assert.ContainsSingle(guarded.Catches);
        Assert.AreEqual("global::System.Exception", failure.Declaration!.Type.ToString());
        Assert.AreSequenceEqual(["global::Ankus.NativeError.Write(exception, error);", "return 1;"],
            failure.Block.Statements.Select(static statement => statement.ToString()));
        FinallyClauseSyntax cleanup = Assert.IsInstanceOfType<FinallyClauseSyntax>(guarded.Finally);
        IfStatementSyntax restore = Assert.IsInstanceOfType<IfStatementSyntax>(cleanup.Block.Statements[0]);
        Assert.AreEqual("memoryEntered", restore.Condition.ToString());
        Assert.IsNull(restore.Else);
        Assert.AreEqual("global::Ankus.NativeMemoryContext.Exit(previousMemory);",
            Assert.ContainsSingle(Assert.IsInstanceOfType<BlockSyntax>(restore.Statement).Statements).ToString());
        Assert.AreSequenceEqual(remainingExits, cleanup.Block.Statements.Skip(1).Select(static statement => statement.ToString()));
        return guarded;
    }

    /// <summary>
    /// Emits the consumer assembly and rejects compiler warnings as well as generator failures.
    /// </summary>
    /// <param name="compilation">The updated consumer compilation.</param>
    /// <param name="diagnostics">The generator diagnostics.</param>
    private void AssertMemoryCompilationSucceeds(Compilation compilation, ImmutableArray<Diagnostic> diagnostics)
    {
        Assert.IsEmpty(diagnostics, string.Join(Environment.NewLine, diagnostics));
        Assert.IsEmpty(compilation.GetDiagnostics(context.CancellationToken).Where(static diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning));
        using var assembly = new MemoryStream();
        EmitResult emitted = compilation.Emit(assembly, cancellationToken: context.CancellationToken);
        Assert.IsEmpty(emitted.Diagnostics.Where(static diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning));
        Assert.IsTrue(emitted.Success);
    }
}
