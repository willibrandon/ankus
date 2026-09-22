using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Ankus.Generators.Tests;

public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// The event marker discovers exactly one callback with or without common function metadata.
    /// </summary>
    /// <param name="attributes">The marker and optional metadata in either order.</param>
    [TestMethod]
    [DataRow("[Ankus.PgEventTrigger]")]
    [DataRow("[Ankus.PgEventTrigger, Ankus.PgFunction]")]
    [DataRow("[Ankus.PgFunction, Ankus.PgEventTrigger]")]
    public void EventTriggerMarkerEmitsOneCompilableZeroArgumentFunction(string attributes)
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("public static class Functions { " + attributes +
            " public static void AuditDdl(Ankus.PgEventTriggerContext context) { } }");
        AssertEventTriggerCompilationSucceeds(compilation, diagnostics);
        IMethodSymbol callback = EventTriggerCallback(compilation);
        string nativeName = callback.Name.Replace("ankus_managed_", "ankus_fn_", StringComparison.Ordinal);
        Assert.AreEqual($"CREATE FUNCTION \"audit_ddl\"()\nRETURNS event_trigger AS 'MODULE_PATHNAME', '{nativeName}' LANGUAGE c " +
            "VOLATILE PARALLEL UNSAFE CALLED ON NULL INPUT SECURITY INVOKER NOT LEAKPROOF COST 1;\n",
            ManifestValue(compilation, "Ankus.Sql").ReplaceLineEndings("\n"));
        Assert.AreSequenceEqual(["Pg_magic_func", nativeName, "pg_finfo_" + nativeName],
            ManifestValue(compilation, "Ankus.Exports").Split('\n', StringSplitOptions.RemoveEmptyEntries));
        Assert.AreEqual("true", ManifestValue(compilation, "Ankus.Relocatable"));
        AttributeData entry = Assert.ContainsSingle(callback.GetAttributes());
        Assert.AreEqual(callback.Name, entry.NamedArguments.Single(static argument => argument.Key == "EntryPoint").Value.Value);
        Assert.AreEqual("System.Runtime.CompilerServices.CallConvCdecl",
            Assert.IsInstanceOfType<ITypeSymbol>(Assert.ContainsSingle(entry.NamedArguments.Single(static argument => argument.Key == "CallConvs").Value.Values).Value).ToDisplayString());
        string native = ManifestValue(compilation, "Ankus.NativeSource").ReplaceLineEndings("\n");
        Assert.Contains("ankus_event_trigger_call(FunctionCallInfo fcinfo, AnkusEventTriggerCallback callback)", native);
        Assert.Contains($"extern int {callback.Name}(const AnkusValue *, AnkusValue *, AnkusError *, AnkusExecute);\n" +
            $"PG_FUNCTION_INFO_V1({nativeName});\nPGDLLEXPORT Datum {nativeName}(PG_FUNCTION_ARGS)\n{{\n" +
            $"    return ankus_event_trigger_call(fcinfo, {callback.Name});\n}}", native);
        Assert.DoesNotContain("ankus_trigger_call", native);
    }

    /// <summary>
    /// The event context exits inside the managed error guard, and backend state always restores after that guard.
    /// </summary>
    [TestMethod]
    public void EventTriggerManagedDispatchNestsEventLifetimeWithinBackendErrorBoundary()
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            public static class Functions
            {
                [Ankus.PgEventTrigger]
                public static void @event(Ankus.PgEventTriggerContext context) { }
            }
            """);
        AssertEventTriggerCompilationSucceeds(compilation, diagnostics);
        MethodDeclarationSyntax callback = Assert.IsInstanceOfType<MethodDeclarationSyntax>(EventTriggerCallback(compilation)
            .DeclaringSyntaxReferences.Single().GetSyntax(context.CancellationToken));
        BlockSyntax body = Assert.IsInstanceOfType<BlockSyntax>(callback.Body);
        Assert.HasCount(2, body.Statements);
        Assert.AreEqual("nint previous = global::Ankus.NativeBackend.Enter(execute);", body.Statements[0].ToString());
        TryStatementSyntax guarded = Assert.IsInstanceOfType<TryStatementSyntax>(body.Statements[1]);
        Assert.AreEqual("global::Ankus.NativeBackend.Exit(previous);", Assert.ContainsSingle(guarded.Finally!.Block.Statements).ToString());
        Assert.HasCount(2, guarded.Block.Statements);
        LocalDeclarationStatementSyntax entry = Assert.IsInstanceOfType<LocalDeclarationStatementSyntax>(guarded.Block.Statements[0]);
        Assert.AreEqual("global::Ankus.PgEventTriggerContext", entry.Declaration.Type.ToString());
        VariableDeclaratorSyntax variable = Assert.ContainsSingle(entry.Declaration.Variables);
        Assert.AreEqual("context", variable.Identifier.ValueText);
        InvocationExpressionSyntax enter = Assert.IsInstanceOfType<InvocationExpressionSyntax>(variable.Initializer!.Value);
        Assert.AreEqual("global::Ankus.NativeEventTrigger.Enter", enter.Expression.ToString());
        Assert.AreEqual("new global::System.ReadOnlySpan<global::Ankus.NativeValue>(arguments, 2)", Assert.ContainsSingle(enter.ArgumentList.Arguments).ToString());
        TryStatementSyntax active = Assert.IsInstanceOfType<TryStatementSyntax>(guarded.Block.Statements[1]);
        Assert.IsEmpty(active.Catches);
        Assert.AreSequenceEqual(["global::Functions.@event(context);", "return 0;"], active.Block.Statements.Select(static statement => statement.ToString()));
        Assert.AreEqual("global::Ankus.NativeEventTrigger.Exit(context);", Assert.ContainsSingle(active.Finally!.Block.Statements).ToString());
        CatchClauseSyntax error = Assert.ContainsSingle(guarded.Catches);
        Assert.AreEqual("global::System.Exception", error.Declaration!.Type.ToString());
        Assert.AreSequenceEqual(["global::Ankus.NativeError.Write(exception, error);", "return 1;"], error.Block.Statements.Select(static statement => statement.ToString()));
    }

    /// <summary>
    /// Ordinary, enum-only, and row-trigger extensions do not receive an unused event entry point.
    /// </summary>
    /// <param name="source">An extension without an event callback.</param>
    [TestMethod]
    [DataRow("public static class Functions { [Ankus.PgFunction] public static int Answer() => 42; }")]
    [DataRow("[Ankus.PgEnum] public enum Mood { Happy, Sad }")]
    [DataRow("public static class Functions { [Ankus.PgTrigger] public static Ankus.PgHeapTuple? Audit(Ankus.PgTriggerContext context) => null; }")]
    public void NonEventTriggerExtensionsOmitUnusedEventEntryPoints(string source)
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate(source);
        AssertEventTriggerCompilationSucceeds(compilation, diagnostics);
        Assert.DoesNotContain("ankus_event_trigger_call", ManifestValue(compilation, "Ankus.NativeSource"));
        Assert.DoesNotContain("NativeEventTrigger.Enter", compilation.GetTypeByMetadataName("Ankus.Generated.ExtensionDispatchers")!
            .DeclaringSyntaxReferences.Single().GetSyntax(context.CancellationToken).ToString());
    }

    /// <summary>
    /// Internal nested classes, interface implementations, and aliases retain valid callback accessibility.
    /// </summary>
    /// <param name="source">The accessible callback declaration.</param>
    [TestMethod]
    [DataRow("internal class Outer { internal class Inner { [Ankus.PgEventTrigger] internal static void Audit(Ankus.PgEventTriggerContext context) { } } }")]
    [DataRow("using Context = Ankus.PgEventTriggerContext; using Marker = Ankus.PgEventTriggerAttribute; public class Functions { [Marker] public static void Audit(Context context) { } }")]
    [DataRow("public interface Functions { [Ankus.PgEventTrigger] public static void Audit(Ankus.PgEventTriggerContext context) { } }")]
    public void EventTriggerAccessibleContainersAndAliasesCompile(string source)
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate(source);
        AssertEventTriggerCompilationSucceeds(compilation, diagnostics);
        string nativeName = EventTriggerCallback(compilation).Name.Replace("ankus_managed_", "ankus_fn_", StringComparison.Ordinal);
        Assert.AreEqual($"CREATE FUNCTION \"audit\"()\nRETURNS event_trigger AS 'MODULE_PATHNAME', '{nativeName}' LANGUAGE c " +
            "VOLATILE PARALLEL UNSAFE CALLED ON NULL INPUT SECURITY INVOKER NOT LEAKPROOF COST 1;\n",
            ManifestValue(compilation, "Ankus.Sql").ReplaceLineEndings("\n"));
    }

    /// <summary>
    /// Common naming, schema, planner and privilege metadata remains exact on event trigger functions.
    /// </summary>
    [TestMethod]
    public void EventTriggerCommonOptionsPreserveExactSqlMetadata()
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            [Ankus.PgSchema("inherited", Create = false)]
            public static class Functions
            {
                [Ankus.PgEventTrigger]
                [Ankus.PgFunction(Name = "audit", Schema = "Évent \"Schema", Volatility = Ankus.PgVolatility.Stable,
                    ParallelSafety = Ankus.PgParallelSafety.Restricted, NullInput = Ankus.PgNullInput.CalledOnNull,
                    SecurityDefiner = true, Leakproof = true, CreateOrReplace = true, Cost = 2.5,
                    SearchPath = new[] { "pg_catalog", "$user", "Mixed Schema" }, SupportFunction = "planner.support")]
                public static void Audit(Ankus.PgEventTriggerContext context) { }
            }
            """);
        AssertEventTriggerCompilationSucceeds(compilation, diagnostics);
        string nativeName = EventTriggerCallback(compilation).Name.Replace("ankus_managed_", "ankus_fn_", StringComparison.Ordinal);
        Assert.AreEqual($"CREATE OR REPLACE FUNCTION \"Évent \"\"Schema\".\"audit\"()\nRETURNS event_trigger AS 'MODULE_PATHNAME', '{nativeName}' LANGUAGE c " +
            "STABLE PARALLEL RESTRICTED CALLED ON NULL INPUT SECURITY DEFINER LEAKPROOF COST 2.5 SUPPORT \"planner\".\"support\" " +
            "SET search_path TO \"pg_catalog\", \"$user\", \"Mixed Schema\";\n", ManifestValue(compilation, "Ankus.Sql").ReplaceLineEndings("\n"));
        Assert.AreEqual("false", ManifestValue(compilation, "Ankus.Relocatable"));
    }

    /// <summary>
    /// Required managed context nullability is independent of an event function's zero SQL arguments.
    /// </summary>
    /// <param name="option">The common null-input policy.</param>
    /// <param name="sql">The expected SQL clause.</param>
    [TestMethod]
    [DataRow("Inferred", "CALLED ON NULL INPUT")]
    [DataRow("Strict", "STRICT")]
    [DataRow("CalledOnNull", "CALLED ON NULL INPUT")]
    public void EventTriggerNullInputOptionsDoNotInferStrictnessFromContext(string option, string sql)
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("public static class Functions { " +
            "[Ankus.PgEventTrigger, Ankus.PgFunction(NullInput = Ankus.PgNullInput." + option + ")] " +
            "public static void Audit(Ankus.PgEventTriggerContext context) { } }");
        AssertEventTriggerCompilationSucceeds(compilation, diagnostics);
        Assert.Contains("PARALLEL UNSAFE " + sql + " SECURITY INVOKER", ManifestValue(compilation, "Ankus.Sql"));
    }

    /// <summary>
    /// Every unsupported signature reports the dedicated diagnostic against otherwise compilable consumer code.
    /// </summary>
    /// <param name="method">The invalid callback declaration.</param>
    [TestMethod]
    [DataRow("public void Audit(Ankus.PgEventTriggerContext context) { }")]
    [DataRow("private static void Audit(Ankus.PgEventTriggerContext context) { }")]
    [DataRow("protected static void Audit(Ankus.PgEventTriggerContext context) { }")]
    [DataRow("protected internal static void Audit(Ankus.PgEventTriggerContext context) { }")]
    [DataRow("public static void Audit<T>(Ankus.PgEventTriggerContext context) { }")]
    [DataRow("public static int Audit(Ankus.PgEventTriggerContext context) => 1;")]
    [DataRow("public static Ankus.PgHeapTuple? Audit(Ankus.PgEventTriggerContext context) => null;")]
    [DataRow("public static System.Threading.Tasks.Task Audit(Ankus.PgEventTriggerContext context) => System.Threading.Tasks.Task.CompletedTask;")]
    [DataRow("public static async void Audit(Ankus.PgEventTriggerContext context) { await System.Threading.Tasks.Task.Yield(); }")]
    [DataRow("public static async System.Threading.Tasks.Task Audit(Ankus.PgEventTriggerContext context) { await System.Threading.Tasks.Task.Yield(); }")]
    [DataRow("public static System.Collections.Generic.IEnumerable<int> Audit(Ankus.PgEventTriggerContext context) => System.Array.Empty<int>();")]
    [DataRow("public static void Audit() { }")]
    [DataRow("public static void Audit(Ankus.PgEventTriggerContext context, int extra) { }")]
    [DataRow("public static void Audit(Ankus.PgEventTriggerContext? context) { }")]
    [DataRow("public static void Audit(Ankus.PgTriggerContext context) { }")]
    [DataRow("public static void Audit(object context) { }")]
    [DataRow("public static void Audit(ref Ankus.PgEventTriggerContext context) { }")]
    [DataRow("public static void Audit(in Ankus.PgEventTriggerContext context) { }")]
    [DataRow("public static void Audit(out Ankus.PgEventTriggerContext context) { context = null!; }")]
    [DataRow("public static void Audit(params Ankus.PgEventTriggerContext[] context) { }")]
    [DataRow("public static void Audit(Ankus.PgEventTriggerContext context = null!) { }")]
    [DataRow("public static void Audit([System.Runtime.InteropServices.Optional] Ankus.PgEventTriggerContext context) { }")]
    [DataRow("public static ref int Audit(Ankus.PgEventTriggerContext context) => throw new System.InvalidOperationException();")]
    [DataRow("public static ref readonly int Audit(Ankus.PgEventTriggerContext context) => throw new System.InvalidOperationException();")]
    public void InvalidEventTriggerSignaturesAreDiagnosed(string method)
        => AssertInvalidEventTrigger("public class Functions { [Ankus.PgEventTrigger] " + method + " }", "ANKUS011");

    /// <summary>
    /// Generic, inaccessible, file-local and abstract declarations cannot supply concrete event callbacks.
    /// </summary>
    /// <param name="source">The complete unsupported declaration.</param>
    [TestMethod]
    [DataRow("public class Functions<T> { [Ankus.PgEventTrigger] public static void Audit(Ankus.PgEventTriggerContext context) { } }")]
    [DataRow("public class Outer<T> { public class Inner { [Ankus.PgEventTrigger] public static void Audit(Ankus.PgEventTriggerContext context) { } } }")]
    [DataRow("file class Functions { [Ankus.PgEventTrigger] public static void Audit(Ankus.PgEventTriggerContext context) { } }")]
    [DataRow("public class Outer { private class Inner { [Ankus.PgEventTrigger] public static void Audit(Ankus.PgEventTriggerContext context) { } } }")]
    [DataRow("public interface Functions { [Ankus.PgEventTrigger] static abstract void Audit(Ankus.PgEventTriggerContext context); }")]
    public void InvalidEventTriggerContainersAreDiagnosed(string source) => AssertInvalidEventTrigger(source, "ANKUS011");

    /// <summary>
    /// SQL operand, result and set metadata cannot silently acquire a meaning on event callbacks.
    /// </summary>
    /// <param name="attribute">The method or return attribute.</param>
    /// <param name="parameterAttribute">The context parameter metadata.</param>
    [TestMethod]
    [DataRow("[Ankus.PgTrigger]", "")]
    [DataRow("[Ankus.PgOperator(\"+\")]", "")]
    [DataRow("[Ankus.PgCast]", "")]
    [DataRow("[return: Ankus.PgCompositeType(\"dog\")]", "")]
    [DataRow("[return: Ankus.PgNumericPrecision(5, 2)]", "")]
    [DataRow("[return: Ankus.PgColumnNames(\"row\")]", "")]
    [DataRow("", "[Ankus.PgCompositeType(\"dog\")]")]
    [DataRow("", "[Ankus.PgNumericPrecision(5, 2)]")]
    [DataRow("", "[Ankus.PgParameter(Name = \"arg\")]")]
    [DataRow("", "[Ankus.PgParameter(Default = \"NULL\")]")]
    [DataRow("[Ankus.PgFunction(Rows = 1000)]", "")]
    [DataRow("[Ankus.PgFunction(Rows = 0)]", "")]
    [DataRow("[Ankus.PgFunction(SetMode = Ankus.PgSetMode.Auto)]", "")]
    [DataRow("[Ankus.PgFunction(SetMode = Ankus.PgSetMode.Materialize)]", "")]
    public void ConflictingEventTriggerMetadataIsDiagnosed(string attribute, string parameterAttribute)
        => AssertInvalidEventTrigger("public static class Functions { [Ankus.PgEventTrigger] " + attribute +
            " public static void Audit(" + parameterAttribute + " Ankus.PgEventTriggerContext context) { } }", "ANKUS011");

    /// <summary>
    /// Dual markers report one event diagnostic even when the signature and attribute order otherwise favor a row trigger.
    /// </summary>
    /// <param name="attributes">The marker order.</param>
    [TestMethod]
    [DataRow("[Ankus.PgTrigger, Ankus.PgEventTrigger]")]
    [DataRow("[Ankus.PgEventTrigger, Ankus.PgTrigger, Ankus.PgFunction]")]
    public void DualRowAndEventTriggerMarkersUseOneConsistentDiagnostic(string attributes)
        => AssertInvalidEventTrigger("public static class Functions { " + attributes +
            " public static Ankus.PgHeapTuple? Audit(Ankus.PgTriggerContext context) => null; }", "ANKUS011");

    /// <summary>
    /// Shared invalid names and planner options keep their established diagnostics after event validation.
    /// </summary>
    /// <param name="option">The invalid shared metadata.</param>
    /// <param name="diagnostic">The expected diagnostic identifier.</param>
    [TestMethod]
    [DataRow("Name = \"bad-name\"", "ANKUS002")]
    [DataRow("Schema = \"\"", "ANKUS004")]
    [DataRow("Cost = 0", "ANKUS004")]
    [DataRow("NullInput = (Ankus.PgNullInput)3", "ANKUS004")]
    [DataRow("ParallelSafety = (Ankus.PgParallelSafety)3", "ANKUS004")]
    [DataRow("Volatility = (Ankus.PgVolatility)3", "ANKUS004")]
    [DataRow("SearchPath = new[] { \"\" }", "ANKUS004")]
    [DataRow("SupportFunction = \"a.b.c\"", "ANKUS004")]
    public void InvalidEventTriggerCommonOptionsUseSharedDiagnostics(string option, string diagnostic)
        => AssertInvalidEventTrigger("public static class Functions { [Ankus.PgEventTrigger, Ankus.PgFunction(" + option + ")] " +
            "public static void Audit(Ankus.PgEventTriggerContext context) { } }", diagnostic);

    /// <summary>
    /// PostgreSQL's signature namespace ignores managed contexts and result pseudotypes when detecting collisions.
    /// </summary>
    /// <param name="other">The second function declaration.</param>
    /// <param name="duplicate">Whether its SQL signature duplicates the event function.</param>
    [TestMethod]
    [DataRow("[Ankus.PgFunction(Name = \"audit\")] public static int Other() => 1;", true)]
    [DataRow("[Ankus.PgTrigger, Ankus.PgFunction(Name = \"audit\")] public static Ankus.PgHeapTuple? Other(Ankus.PgTriggerContext context) => null;", true)]
    [DataRow("[Ankus.PgEventTrigger, Ankus.PgFunction(Name = \"audit\")] public static void Other(Ankus.PgEventTriggerContext context) { }", true)]
    [DataRow("[Ankus.PgFunction(Name = \"audit\")] public static int Other(int value) => value;", false)]
    [DataRow("[Ankus.PgEventTrigger, Ankus.PgFunction(Name = \"audit\", Schema = \"other\")] public static void Other(Ankus.PgEventTriggerContext context) { }", false)]
    public void EventTriggerSqlSignaturesCollideOnlyOnSchemaNameAndZeroArguments(string other, bool duplicate)
    {
        string source = "public static class Functions { [Ankus.PgEventTrigger] " +
            "public static void Audit(Ankus.PgEventTriggerContext context) { } " + other + " }";
        if (duplicate)
        {
            AssertInvalidEventTrigger(source, "ANKUS002");
            return;
        }

        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate(source);
        AssertEventTriggerCompilationSucceeds(compilation, diagnostics);
        string[] declarations = [.. ManifestValue(compilation, "Ankus.Sql").ReplaceLineEndings("\n").Split('\n')
            .Where(static line => line.StartsWith("CREATE FUNCTION", StringComparison.Ordinal))];
        Assert.AreSequenceEqual(["CREATE FUNCTION \"audit\"()", other.Contains("Schema", StringComparison.Ordinal)
            ? "CREATE FUNCTION \"other\".\"audit\"()" : "CREATE FUNCTION \"audit\"(\"value\" integer)"], declarations);
    }

    /// <summary>
    /// Explicit dependencies order schema and audit setup before the callback and database-global event attachment.
    /// </summary>
    [TestMethod]
    public void EventTriggerSqlDependenciesOrderSchemaTableFunctionAndAttachment()
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            [assembly: Ankus.PgSql("table", "CREATE TABLE audit.events(tag text);", Requires = new[] { "schema" })]
            [assembly: Ankus.PgSql("attach", "CREATE EVENT TRIGGER audit_ddl ON ddl_command_end EXECUTE FUNCTION audit.callback();", Requires = new[] { "callback" })]
            [Ankus.PgSchema("audit", Id = "schema")]
            public static class Functions
            {
                [Ankus.PgEventTrigger, Ankus.PgFunction(Id = "callback", Requires = new[] { "table" })]
                public static void Callback(Ankus.PgEventTriggerContext context) { }
            }
            """);
        AssertEventTriggerCompilationSucceeds(compilation, diagnostics);
        string nativeName = EventTriggerCallback(compilation).Name.Replace("ankus_managed_", "ankus_fn_", StringComparison.Ordinal);
        Assert.AreEqual("CREATE SCHEMA IF NOT EXISTS \"audit\";\nCREATE TABLE audit.events(tag text);\n" +
            $"CREATE FUNCTION \"audit\".\"callback\"()\nRETURNS event_trigger AS 'MODULE_PATHNAME', '{nativeName}' LANGUAGE c " +
            "VOLATILE PARALLEL UNSAFE CALLED ON NULL INPUT SECURITY INVOKER NOT LEAKPROOF COST 1;\n" +
            "CREATE EVENT TRIGGER audit_ddl ON ddl_command_end EXECUTE FUNCTION audit.callback();\n",
            ManifestValue(compilation, "Ankus.Sql").ReplaceLineEndings("\n"));
        Assert.AreEqual("false", ManifestValue(compilation, "Ankus.Relocatable"));
    }

    /// <summary>
    /// Event dependencies preserve missing, duplicate, explicit-cycle and inherited-schema-cycle diagnostics.
    /// </summary>
    /// <param name="declarations">The additional schema or custom SQL.</param>
    /// <param name="options">The event function's dependency metadata.</param>
    [TestMethod]
    [DataRow("", "Requires = new[] { \"missing\" }")]
    [DataRow("[assembly: Ankus.PgSql(\"other\", \"SELECT 1;\", Requires = new[] { \"callback\" })]", "Id = \"callback\", Requires = new[] { \"other\" }")]
    [DataRow("[assembly: Ankus.PgSql(\"callback\", \"SELECT 1;\")]", "Id = \"callback\"")]
    [DataRow("[Ankus.PgSchema(\"audit\", Requires = new[] { \"callback\" })] public static class Schema;", "Id = \"callback\", Schema = \"audit\"")]
    public void InvalidEventTriggerDependencyGraphsAreDiagnosed(string declarations, string options)
        => AssertInvalidEventTrigger(declarations + " public static class Functions { [Ankus.PgEventTrigger, Ankus.PgFunction(" + options + ")] " +
            "public static void Audit(Ankus.PgEventTriggerContext context) { } }", "ANKUS005");

    /// <summary>
    /// Event contexts cannot accidentally become ordinary SQL arguments or row-trigger contexts without the event marker.
    /// </summary>
    /// <param name="attribute">The incorrect discovery marker.</param>
    /// <param name="diagnostic">The expected diagnostic.</param>
    [TestMethod]
    [DataRow("Ankus.PgFunction", "ANKUS001")]
    [DataRow("Ankus.PgTrigger", "ANKUS010")]
    public void EventTriggerContextRequiresTheEventMarker(string attribute, string diagnostic)
        => AssertInvalidEventTrigger("public static class Functions { [" + attribute + "] " +
            "public static void Audit(Ankus.PgEventTriggerContext context) { } }", diagnostic);

    /// <summary>
    /// Discovery preserves every supported declaration kind and deterministic manifests when source order changes.
    /// </summary>
    [TestMethod]
    public void EventTriggerMixedDeclarationsPreserveDiscoveryAndDeterministicOutput()
    {
        string[] methods =
        [
            "[Ankus.PgFunction] public static int Answer() => 42;",
            "[Ankus.PgEventTrigger, Ankus.PgFunction] public static void Event(Ankus.PgEventTriggerContext context) { }",
            "[Ankus.PgTrigger] public static Ankus.PgHeapTuple? Row(Ankus.PgTriggerContext context) => null;",
            "[Ankus.PgOperator(\"@+\")] public static int Add(int left, int right) => left + right;",
            "[Ankus.PgCast] public static long Convert(int value) => value;",
            "[Ankus.PgFunction] public static System.Collections.Generic.IEnumerable<int> Rows() => System.Array.Empty<int>();",
        ];
        string prefix = "[Ankus.PgEnum] public enum Mood { Happy, Sad } public static class Functions { ";
        (Compilation first, ImmutableArray<Diagnostic> firstDiagnostics) = Generate(prefix + string.Join(" ", methods) + " }");
        (Compilation second, ImmutableArray<Diagnostic> secondDiagnostics) = Generate(prefix + string.Join(" ", methods.Reverse()) + " }");
        AssertEventTriggerCompilationSucceeds(first, firstDiagnostics);
        AssertEventTriggerCompilationSucceeds(second, secondDiagnostics);
        foreach (string key in new[] { "Ankus.Sql", "Ankus.Exports", "Ankus.NativeSource", "Ankus.Relocatable" })
        {
            Assert.AreEqual(ManifestValue(first, key), ManifestValue(second, key));
        }

        string sql = ManifestValue(first, "Ankus.Sql").ReplaceLineEndings("\n");
        Assert.AreSequenceEqual([
            "CREATE FUNCTION \"add\"(\"left\" integer, \"right\" integer)",
            "CREATE FUNCTION \"answer\"()",
            "CREATE FUNCTION \"convert\"(\"value\" integer)",
            "CREATE FUNCTION \"event\"()",
            "CREATE FUNCTION \"row\"()",
            "CREATE FUNCTION \"rows\"()",
        ], sql.Split('\n').Where(static line => line.StartsWith("CREATE FUNCTION", StringComparison.Ordinal)));
        Assert.Contains("CREATE TYPE \"mood\" AS ENUM (E'Happy', E'Sad');", sql);
        Assert.Contains("CREATE OPERATOR @+ (FUNCTION = \"add\", LEFTARG = integer, RIGHTARG = integer);", sql);
        Assert.Contains("CREATE CAST (integer AS bigint) WITH FUNCTION \"convert\"(integer);", sql);
        Assert.Contains("ankus_trigger_call", ManifestValue(first, "Ankus.NativeSource"));
        Assert.Contains("ankus_event_trigger_call", ManifestValue(first, "Ankus.NativeSource"));
        Assert.HasCount(13, ManifestValue(first, "Ankus.Exports").Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// Checks generation, compiler diagnostics, and actual assembly emission for accepted event declarations.
    /// </summary>
    private void AssertEventTriggerCompilationSucceeds(Compilation compilation, ImmutableArray<Diagnostic> diagnostics)
    {
        Assert.IsEmpty(diagnostics);
        Assert.IsEmpty(compilation.GetDiagnostics(context.CancellationToken).Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        using var assembly = new MemoryStream();
        Assert.IsTrue(compilation.Emit(assembly, cancellationToken: context.CancellationToken).Success);
    }

    /// <summary>
    /// Requires one generator error and valid consumer C# so diagnostics cannot pass through unrelated parse failures.
    /// </summary>
    private void AssertInvalidEventTrigger(string source, string expected)
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate(source);
        Diagnostic diagnostic = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual(expected, diagnostic.Id);
        Assert.AreEqual(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.IsEmpty(compilation.GetDiagnostics(context.CancellationToken).Where(static item => item.Severity == DiagnosticSeverity.Error));
    }

    /// <summary>
    /// Resolves one callback independently of its assembly-specific symbol suffix.
    /// </summary>
    private static IMethodSymbol EventTriggerCallback(Compilation compilation)
    {
        INamedTypeSymbol dispatchers = compilation.GetTypeByMetadataName("Ankus.Generated.ExtensionDispatchers")!;
        return Assert.IsInstanceOfType<IMethodSymbol>(Assert.ContainsSingle(dispatchers.GetMembers()));
    }
}
