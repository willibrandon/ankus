using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Ankus.Generators.Tests;

public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// An initialization-only assembly has one cdecl callback, usable native exports, and no SQL function.
    /// </summary>
    /// <param name="source">A supported callback declaration.</param>
    [TestMethod]
    [DataRow("public static class Functions { [Ankus.PgInitialize] public static void Initialize() { } }")]
    [DataRow("internal class Functions { [Ankus.PgInitialize] internal static void @event() => System.GC.KeepAlive(null); }")]
    [DataRow("public class Outer { internal struct Inner { [Ankus.PgInitialize] public static void Initialize() { } } }")]
    [DataRow("public partial class Functions { [Ankus.PgInitialize] public static partial void Initialize(); } public partial class Functions { public static partial void Initialize() { } }")]
    [DataRow("public partial class Functions { public static partial void Initialize(); } public partial class Functions { [Ankus.PgInitialize] public static partial void Initialize() { } }")]
    [DataRow("public interface Functions { [Ankus.PgInitialize] public static void Initialize() { } }")]
    public void InitializationOnlyExtensionCompilesWithNativeLoaderExports(string source)
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate(source);
        AssertInitializationCompilationSucceeds(compilation, diagnostics);
        Assert.AreEqual("-- No installable objects declared.\n", ManifestValue(compilation, "Ankus.Sql").ReplaceLineEndings("\n"));
        Assert.AreSequenceEqual(["Pg_magic_func", "_PG_init"], ManifestValue(compilation, "Ankus.Exports").Split('\n', StringSplitOptions.RemoveEmptyEntries));
        Assert.AreEqual("true", ManifestValue(compilation, "Ankus.Relocatable"));
        IMethodSymbol callback = InitializationCallback(compilation);
        Assert.AreEqual(SpecialType.System_Int32, callback.ReturnType.SpecialType);
        Assert.AreSequenceEqual(["Ankus.NativeCallError*", "nint", "nint", "nint", "nint"],
            callback.Parameters.Select(static parameter => parameter.Type.ToDisplayString()));
        AttributeData entry = Assert.ContainsSingle(callback.GetAttributes());
        Assert.AreEqual("System.Runtime.InteropServices.UnmanagedCallersOnlyAttribute", entry.AttributeClass!.ToDisplayString());
        Assert.AreEqual(callback.Name, entry.NamedArguments.Single(static argument => argument.Key == "EntryPoint").Value.Value);
        Assert.AreEqual("System.Runtime.CompilerServices.CallConvCdecl",
            Assert.IsInstanceOfType<ITypeSymbol>(Assert.ContainsSingle(entry.NamedArguments.Single(static argument => argument.Key == "CallConvs").Value.Values).Value).ToDisplayString());
        string native = ManifestValue(compilation, "Ankus.NativeSource");
        Assert.Contains($"extern int {callback.Name}(AnkusError *, AnkusGucReadBinding, AnkusExecute, AnkusInitializationLog, AnkusMemoryApi *);", native);
        Assert.DoesNotContain("PG_FUNCTION_INFO_V1(", native);
        Assert.DoesNotContain("ankus_event_trigger_call", native);
        Assert.DoesNotContain("ankus_trigger_call", native);
    }

    /// <summary>
    /// The generated managed callback catches user errors and restores the enclosing backend binding on both paths.
    /// </summary>
    [TestMethod]
    public void InitializationManagedDispatchRestoresBindingWithinExceptionBoundary()
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate(
            "public static class Functions { [Ankus.PgInitialize] public static void @event() { } }");
        AssertInitializationCompilationSucceeds(compilation, diagnostics);
        MethodDeclarationSyntax callback = Assert.IsInstanceOfType<MethodDeclarationSyntax>(InitializationCallback(compilation)
            .DeclaringSyntaxReferences.Single().GetSyntax(context.CancellationToken));
        BlockSyntax body = Assert.IsInstanceOfType<BlockSyntax>(callback.Body);
        Assert.HasCount(6, body.Statements);
        Assert.AreSequenceEqual(
        [
            "nint previousBackend = global::Ankus.NativeBackend.Enter(execute);",
            "nint previousRead = global::Ankus.NativeGuc.Enter(read);",
            "nint previousLog = global::Ankus.NativeLog.Enter(log);",
        ], body.Statements.Take(3).Select(static statement => statement.ToString()));
        TryStatementSyntax guarded = AssertMemoryCallbackScope(callback, "global::Ankus.NativeLog.Exit(previousLog);",
            "global::Ankus.NativeGuc.Exit(previousRead);", "global::Ankus.NativeBackend.Exit(previousBackend);");
        Assert.AreSequenceEqual(["global::Functions.@event();", "return 0;"], guarded.Block.Statements.Skip(2).Select(static statement => statement.ToString()));
        CatchClauseSyntax failure = Assert.ContainsSingle(guarded.Catches);
        Assert.AreEqual("global::System.Exception", failure.Declaration!.Type.ToString());
        Assert.AreSequenceEqual(["global::Ankus.NativeError.Write(exception, error);", "return 1;"],
            failure.Block.Statements.Select(static statement => statement.ToString()));
    }

    /// <summary>
    /// The native loader separates native registration from retryable managed initialization.
    /// </summary>
    [TestMethod]
    public void InitializationNativeBoundaryEnablesForkSupportAndPreservesRetry()
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate(
            "public static class Functions { [Ankus.PgInitialize] public static void Initialize() { } }");
        AssertInitializationCompilationSucceeds(compilation, diagnostics);
        string native = ManifestValue(compilation, "Ankus.NativeSource").ReplaceLineEndings("\n");
        string loader = native[native.IndexOf("PGDLLEXPORT void _PG_init(void)\n", StringComparison.Ordinal)..
            native.LastIndexOf("static void\nankus_ensure_initialized(void)", StringComparison.Ordinal)];
        string[] loaderOrder =
        [
            "if (ankus_initialization_state == 1)",
            "errcode(ERRCODE_OBJECT_NOT_IN_PREREQUISITE_STATE)",
            "if (ankus_registration_complete)",
            "ankus_ensure_initialized();",
            "return;",
            "ankus_initialization_state = 1;",
            "PG_TRY();",
            "ankus_registration_complete = true;",
            "ankus_initialization_state = 0;",
            "PG_CATCH();",
            "ankus_initialization_state = 0;",
            "MemoryContextSwitchTo(caller);",
            "PG_RE_THROW();",
            "PG_END_TRY();",
            "MemoryContextSwitchTo(caller);",
            "#if defined(WIN32) && PG_VERSION_NUM < 180000",
            "if (InitializingParallelWorker)",
            "return;",
            "#endif",
            "ankus_ensure_initialized();",
        ];
        AssertOrdered(loader, loaderOrder);

        string initializer = native[native.LastIndexOf("static void\nankus_ensure_initialized(void)", StringComparison.Ordinal)..];
        string[] initializationOrder =
        [
            "if (ankus_initialization_state == 1)",
            "errcode(ERRCODE_OBJECT_NOT_IN_PREREQUISITE_STATE)",
            "if (ankus_initialization_state == 2)",
            "return;",
            "AnkusError *error = MemoryContextAllocZero(caller, sizeof(AnkusError));",
            "volatile bool snapshot_owned = false;",
            "ankus_initialization_state = 1;",
            "PG_TRY();",
            "if (IsTransactionState() && !ActiveSnapshotSet())",
            "PushActiveSnapshot(GetTransactionSnapshot());",
            "snapshot_owned = true;",
            $"int status = {InitializationCallback(compilation).Name}(error, ankus_read_guc,",
            "IsTransactionState() ? ankus_spi_execute : NULL, ankus_initialization_log, &memory);",
            "if (snapshot_owned)",
            "snapshot_owned = false;",
            "PopActiveSnapshot();",
            "if (status != 0)",
            "ankus_initialization_state = 0;",
            "ankus_raise_error(error);",
            "#ifndef WIN32",
            "if (IsPostmasterEnvironment && !IsUnderPostmaster)",
            "int32_t fork_status = RhEnableForkSupport();",
            "if (fork_status != 1)",
            "errmsg(\"Ankus runtime fork support failed: %d\", fork_status)",
            "#endif",
            "ankus_initialization_state = 2;",
            "PG_CATCH();",
            "ankus_initialization_state = 0;",
            "MemoryContextSwitchTo(caller);",
            "if (snapshot_owned)",
            "snapshot_owned = false;",
            "PopActiveSnapshot();",
            "ankus_release_error(error);",
            "pfree(error);",
            "PG_RE_THROW();",
            "PG_END_TRY();",
            "MemoryContextSwitchTo(caller);",
            "ankus_release_error(error);",
            "pfree(error);",
        ];
        AssertOrdered(initializer, initializationOrder);

        Assert.Contains("#include \"access/parallel.h\"", native);
        Assert.Contains("extern int32_t RhEnableForkSupport(void);", native);
        Assert.Contains("ankus_initialization_log", native);
        Assert.DoesNotContain("cannot run through shared_preload_libraries", native);
    }

    /// <summary>
    /// Every managed PostgreSQL entry point completes initialization deferred during Windows worker startup.
    /// </summary>
    [TestMethod]
    public void ParallelWorkersInitializeBeforeEveryManagedEntryPoint()
    {
        const string source = """
            public static class Functions
            {
                [Ankus.PgInitialize] public static void Initialize() { }
                [Ankus.PgFunction] public static int Scalar() => 42;
                [Ankus.PgFunction] public static System.Collections.Generic.IEnumerable<int> Rows() => new[] { 1 };
                [Ankus.PgTrigger] public static Ankus.PgHeapTuple? Trigger(Ankus.PgTriggerContext context) => context.New;
                [Ankus.PgEventTrigger] public static void Event(Ankus.PgEventTriggerContext context) { }
            }

            [Ankus.PgAggregate(InitialCondition = "0")]
            public static class Sum
            {
                public static int Transition(int state, int value) => state + value;
            }
            """;
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate(source);
        AssertInitializationCompilationSucceeds(compilation, diagnostics);
        string native = ManifestValue(compilation, "Ankus.NativeSource").ReplaceLineEndings("\n");
        Assert.AreEqual(7, native.Split("ankus_ensure_initialized();", StringSplitOptions.None).Length - 1);
    }

    private static void AssertOrdered(string source, IEnumerable<string> statements)
    {
        int position = 0;
        foreach (string statement in statements)
        {
            int found = source.IndexOf(statement, position, StringComparison.Ordinal);
            Assert.IsGreaterThanOrEqualTo(position, found, statement);
            position = found + statement.Length;
        }
    }

    /// <summary>
    /// Unsupported attributed methods produce an actionable generator error instead of being silently ignored.
    /// </summary>
    /// <param name="source">A valid C# declaration with an unsupported initialization contract.</param>
    [TestMethod]
    [DataRow("public class Functions { [Ankus.PgInitialize] public void Initialize() { } }")]
    [DataRow("public class Functions { [Ankus.PgInitialize] private static void Initialize() { } }")]
    [DataRow("public class Functions { [Ankus.PgInitialize] protected static void Initialize() { } }")]
    [DataRow("public class Functions { [Ankus.PgInitialize] protected internal static void Initialize() { } }")]
    [DataRow("public class Functions { [Ankus.PgInitialize] public static int Initialize() => 1; }")]
    [DataRow("public class Functions { [Ankus.PgInitialize] public static ref int Initialize() => throw new System.Exception(); }")]
    [DataRow("public class Functions { [Ankus.PgInitialize] public static async void Initialize() { await System.Threading.Tasks.Task.Yield(); } }")]
    [DataRow("public class Functions { [Ankus.PgInitialize] public static System.Threading.Tasks.Task Initialize() => System.Threading.Tasks.Task.CompletedTask; }")]
    [DataRow("public class Functions { [Ankus.PgInitialize] public static System.Threading.Tasks.ValueTask Initialize() => default; }")]
    [DataRow("public class Functions { [Ankus.PgInitialize] public static void Initialize<T>() { } }")]
    [DataRow("public class Functions { [Ankus.PgInitialize] public static void Initialize(int value) { } }")]
    [DataRow("public class Functions { [Ankus.PgInitialize] public static void Initialize(int value = 0) { } }")]
    [DataRow("public class Functions { [Ankus.PgInitialize] public static void Initialize(params int[] values) { } }")]
    [DataRow("public class Functions { [Ankus.PgInitialize] public static void Initialize(ref int value) { } }")]
    [DataRow("public class Functions { [Ankus.PgInitialize, System.Runtime.InteropServices.DllImport(\"native\")] public static extern void Initialize(); }")]
    [DataRow("public interface Functions { [Ankus.PgInitialize] public static abstract void Initialize(); }")]
    [DataRow("public interface Functions { [Ankus.PgInitialize] public static virtual void Initialize() { } }")]
    [DataRow("public partial class Functions { [Ankus.PgInitialize] static partial void Initialize(); }")]
    [DataRow("public partial class Functions { [Ankus.PgInitialize] public static partial void Initialize(); public static async partial void Initialize() { await System.Threading.Tasks.Task.Yield(); } }")]
    [DataRow("public class Functions<T> { [Ankus.PgInitialize] public static void Initialize() { } }")]
    [DataRow("public class Outer<T> { public class Functions { [Ankus.PgInitialize] public static void Initialize() { } } }")]
    [DataRow("public class Outer { private class Functions { [Ankus.PgInitialize] public static void Initialize() { } } }")]
    [DataRow("file class Functions { [Ankus.PgInitialize] public static void Initialize() { } }")]
    [DataRow("public interface IInit { static abstract void Initialize(); } public class Functions : IInit { [Ankus.PgInitialize] static void IInit.Initialize() { } }")]
    [DataRow("public class Functions { public void Method() { [Ankus.PgInitialize] static void Initialize() { } Initialize(); } }")]
    [DataRow("public class Functions { public System.Action Callback = [Ankus.PgInitialize] static () => { }; }")]
    public void InvalidInitializationDeclarationsAreRejected(string source)
        => AssertInvalidInitialization(source);

    /// <summary>
    /// SQL metadata cannot turn an initialization callback into another kind of declaration.
    /// </summary>
    /// <param name="attributes">The conflicting SQL annotation.</param>
    [TestMethod]
    [DataRow("[Ankus.PgFunction]")]
    [DataRow("[Ankus.PgTrigger]")]
    [DataRow("[Ankus.PgEventTrigger]")]
    [DataRow("[Ankus.PgOperator(\"+\")]")]
    [DataRow("[Ankus.PgCast]")]
    [DataRow("[return: Ankus.PgNumericPrecision(5)]")]
    [DataRow("[return: Ankus.PgCompositeType(\"thing\")]")]
    [DataRow("[return: Ankus.PgColumnNames(\"value\")]")]
    public void InitializationRejectsSqlFunctionAndResultMetadata(string attributes)
        => AssertInvalidInitialization("public static class Functions { [Ankus.PgInitialize] " + attributes + " public static void Initialize() { } }");

    /// <summary>
    /// An initializer cannot silently disappear through conditional call removal or require an unmanaged-only invocation.
    /// </summary>
    /// <param name="attributes">The attribute that prevents an unconditional managed invocation.</param>
    [TestMethod]
    [DataRow("[System.Diagnostics.Conditional(\"FIRST_SYMBOL\"), System.Diagnostics.Conditional(\"SECOND_SYMBOL\")]")]
    [DataRow("[System.Runtime.InteropServices.UnmanagedCallersOnly]")]
    public void InitializationRejectsAttributesThatPreventManagedInvocation(string attributes)
        => AssertInvalidInitialization("public static class Functions { [Ankus.PgInitialize] " + attributes + " public static void Initialize() { } }");

    /// <summary>
    /// Multiple callbacks across types fail deterministically without choosing one or emitting a loader entry point.
    /// </summary>
    [TestMethod]
    public void MultipleInitializationCallbacksAreRejected()
    {
        const string first = "public static class First { [Ankus.PgInitialize] public static void Initialize() { } }";
        const string second = "public static class Second { [Ankus.PgInitialize] public static void Initialize() { } }";
        foreach (string source in new[] { first + second, second + first })
        {
            (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate(source);
            Diagnostic error = Assert.ContainsSingle(diagnostics);
            Assert.AreEqual("ANKUS013", error.Id);
            Assert.Contains("only one PgInitialize", error.GetMessage(CultureInfo.InvariantCulture));
            Assert.IsEmpty(compilation.GetDiagnostics(context.CancellationToken).Where(static item => item.Severity == DiagnosticSeverity.Error));
            Assert.AreSequenceEqual(["Pg_magic_func"], ManifestValue(compilation, "Ankus.Exports").Split('\n', StringSplitOptions.RemoveEmptyEntries));
        }
    }

    /// <summary>
    /// Initialization preserves ordinary SQL graph, enum registration, schema policy, and deterministic discovery.
    /// </summary>
    [TestMethod]
    public void InitializationMixedDeclarationsPreserveSqlAndDeterministicManifest()
    {
        const string declarations = """
            [assembly: Ankus.PgSql("before", "SELECT 1;")]
            [Ankus.PgEnum] public enum Mood { Happy, Sad }
            [Ankus.PgSchema("init_tests")]
            public static class Functions
            {
                [Ankus.PgFunction(Requires = ["before"])] public static Mood Echo(Mood value) => value;
            }
            """;
        const string initialization = "public static class Startup { [Ankus.PgInitialize] public static void Initialize() { } }";
        (Compilation baseline, ImmutableArray<Diagnostic> baselineDiagnostics) = Generate(declarations);
        (Compilation mixed, ImmutableArray<Diagnostic> mixedDiagnostics) = Generate(declarations + initialization);
        AssertInitializationCompilationSucceeds(baseline, baselineDiagnostics);
        AssertInitializationCompilationSucceeds(mixed, mixedDiagnostics);
        Assert.AreEqual(ManifestValue(baseline, "Ankus.Sql"), ManifestValue(mixed, "Ankus.Sql"));
        Assert.AreEqual("false", ManifestValue(mixed, "Ankus.Relocatable"));
        Assert.AreSequenceEqual(ManifestValue(baseline, "Ankus.Exports").Split('\n', StringSplitOptions.RemoveEmptyEntries),
            ManifestValue(mixed, "Ankus.Exports").Split('\n', StringSplitOptions.RemoveEmptyEntries).Where(static name => name != "_PG_init"));
        Assert.ContainsSingle(ManifestValue(mixed, "Ankus.Exports").Split('\n').Where(static name => name == "_PG_init"));
        (Compilation repeated, ImmutableArray<Diagnostic> repeatedDiagnostics) = Generate(
            declarations.Replace("[Ankus.PgEnum]", initialization + "[Ankus.PgEnum]", StringComparison.Ordinal));
        AssertInitializationCompilationSucceeds(repeated, repeatedDiagnostics);
        foreach (string key in new[] { "Ankus.Sql", "Ankus.Exports", "Ankus.NativeSource", "Ankus.Relocatable" })
        {
            Assert.AreEqual(ManifestValue(mixed, key), ManifestValue(repeated, key));
        }
    }

    /// <summary>
    /// The unmanaged callback symbol includes assembly identity while the PostgreSQL loader export stays fixed.
    /// </summary>
    [TestMethod]
    public void InitializationCallbackSymbolsAreAssemblyScoped()
    {
        const string source = "public static class Functions { [Ankus.PgInitialize] public static void Initialize() { } }";
        var symbols = new List<string>();
        foreach (string assemblyName in new[] { "FirstExtension", "SecondExtension" })
        {
            CSharpCompilation input = CSharpCompilation.Create(assemblyName,
                [CSharpSyntaxTree.ParseText(source, cancellationToken: context.CancellationToken)], s_references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, nullableContextOptions: NullableContextOptions.Enable));
            GeneratorDriver driver = CSharpGeneratorDriver.Create(new PgFunctionGenerator().AsSourceGenerator());
            driver.RunGeneratorsAndUpdateCompilation(input, out Compilation output, out ImmutableArray<Diagnostic> diagnostics, context.CancellationToken);
            AssertInitializationCompilationSucceeds(output, diagnostics);
            symbols.Add(InitializationCallback(output).Name);
            Assert.AreSequenceEqual(["Pg_magic_func", "_PG_init"], ManifestValue(output, "Ankus.Exports").Split('\n', StringSplitOptions.RemoveEmptyEntries));
        }

        Assert.AreNotEqual(symbols[0], symbols[1]);
    }

    /// <summary>
    /// Existing declaration kinds never receive a loader export or initializer state without the initialization marker.
    /// </summary>
    /// <param name="source">An extension declaration with no managed initializer.</param>
    [TestMethod]
    [DataRow("public static class Functions { [Ankus.PgFunction] public static int Answer() => 42; }")]
    [DataRow("[Ankus.PgEnum] public enum Mood { Happy, Sad }")]
    [DataRow("[assembly: Ankus.PgSql(\"bootstrap\", \"SELECT 42;\")]")]
    public void InitializationIsAbsentUnlessExplicitlyDeclared(string source)
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate(source);
        AssertInitializationCompilationSucceeds(compilation, diagnostics);
        Assert.DoesNotContain("_PG_init", ManifestValue(compilation, "Ankus.Exports"));
        Assert.DoesNotContain("ankus_initialization_state", ManifestValue(compilation, "Ankus.NativeSource"));
    }

    /// <summary>
    /// Requires one initialization diagnostic, valid consumer C#, and no generated callback or loader export.
    /// </summary>
    private void AssertInvalidInitialization(string source)
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate(source);
        Diagnostic error = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual("ANKUS013", error.Id);
        Assert.AreEqual(DiagnosticSeverity.Error, error.Severity);
        Assert.IsEmpty(compilation.GetDiagnostics(context.CancellationToken).Where(static item => item.Severity == DiagnosticSeverity.Error));
        Assert.AreEqual("-- No installable objects declared.\n", ManifestValue(compilation, "Ankus.Sql").ReplaceLineEndings("\n"));
        Assert.AreSequenceEqual(["Pg_magic_func"], ManifestValue(compilation, "Ankus.Exports").Split('\n', StringSplitOptions.RemoveEmptyEntries));
        Assert.IsEmpty(compilation.GetTypeByMetadataName("Ankus.Generated.ExtensionDispatchers")!.GetMembers());
    }

    /// <summary>
    /// Checks compiler diagnostics and assembly emission in addition to generator diagnostics.
    /// </summary>
    private void AssertInitializationCompilationSucceeds(Compilation compilation, ImmutableArray<Diagnostic> diagnostics)
    {
        Assert.IsEmpty(diagnostics);
        Assert.IsEmpty(compilation.GetDiagnostics(context.CancellationToken).Where(static diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning));
        using var assembly = new MemoryStream();
        Assert.IsTrue(compilation.Emit(assembly, cancellationToken: context.CancellationToken).Success);
    }

    /// <summary>
    /// Finds the sole initialization-only callback through the generated semantic model.
    /// </summary>
    private static IMethodSymbol InitializationCallback(Compilation compilation)
        => Assert.IsInstanceOfType<IMethodSymbol>(Assert.ContainsSingle(compilation.GetTypeByMetadataName("Ankus.Generated.ExtensionDispatchers")!.GetMembers()));
}
