using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Ankus.Generators.Tests;

public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Executed dispatchers inject independent context wrappers without consuming or reordering SQL datum slots.
    /// </summary>
    /// <param name="parameters">The managed parameter declaration.</param>
    /// <param name="expression">The value that observes arguments and injected wrappers.</param>
    /// <param name="sqlParameters">The exact SQL-visible parameter list.</param>
    /// <param name="expected">The value returned by the compiled dispatcher.</param>
    [TestMethod]
    [DataRow("Ankus.PgMemoryContext context, int left, int right", "context is not null ? left * 100 + right : -1", "\"left\" integer, \"right\" integer", 1729L)]
    [DataRow("int left, Ankus.PgMemoryContext context, int right", "context is not null ? left * 100 + right : -1", "\"left\" integer, \"right\" integer", 1729L)]
    [DataRow("int left, int right, Ankus.PgMemoryContext context", "context is not null ? left * 100 + right : -1", "\"left\" integer, \"right\" integer", 1729L)]
    [DataRow("Ankus.PgMemoryContext first, int left, Ankus.PgMemoryContext middle, int right, Ankus.PgMemoryContext last",
        "first is not null && middle is not null && last is not null && !object.ReferenceEquals(first, middle) && !object.ReferenceEquals(middle, last) && !object.ReferenceEquals(first, last) ? left * 100 + right : -1",
        "\"left\" integer, \"right\" integer", 1729L)]
    [DataRow("Ankus.PgMemoryContext context", "context is not null ? 42 : -1", "", 42L)]
    [DataRow("int left, int right, Ankus.PgMemoryContext? context = null", "context is not null ? left * 100 + right : -1", "\"left\" integer, \"right\" integer", 1729L)]
    public void VirtualContextsPreserveCompiledScalarArgumentOrder(string parameters, string expression, string sqlParameters, long expected)
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate($$"""
            public static class Functions
            {
                [Ankus.PgFunction]
                public static int Apply({{parameters}}) => {{expression}};
            }
            """);
        AssertMemoryCompilationSucceeds(compilation, diagnostics);
        string sql = Assert.ContainsSingle(OperatorCastStatements(compilation));
        Assert.StartsWith("CREATE FUNCTION \"apply\"(" + sqlParameters + ") RETURNS integer AS ", sql);
        Assert.Contains(" PARALLEL UNSAFE STRICT SECURITY INVOKER ", sql);
        Assert.AreSequenceEqual(new long[] { 0, expected, 0, 999, 888 }, InvokeVirtualContextCallback(compilation));
    }

    /// <summary>
    /// Set factories inject contexts independently of required and nullable SQL inputs, including a context-only factory.
    /// </summary>
    /// <param name="parameters">The managed factory parameters.</param>
    /// <param name="expression">The value produced by its sole row.</param>
    /// <param name="sqlParameters">The exact SQL-visible input declaration.</param>
    /// <param name="required">The native required-argument bitmap.</param>
    /// <param name="count">The native SQL argument count.</param>
    /// <param name="nullSecond">Whether the second SQL transport represents null.</param>
    /// <param name="expected">The first row returned by the compiled dispatcher.</param>
    [TestMethod]
    [DataRow("Ankus.PgMemoryContext first, Ankus.PgMemoryContext last",
        "!object.ReferenceEquals(first, last) ? 42 : -1", "", "false", 0, false, 42L)]
    [DataRow("Ankus.PgMemoryContext first, int left, Ankus.PgMemoryContext? middle, int? right, Ankus.PgMemoryContext last",
        "first is not null && middle is not null && last is not null ? left * 100 + (right ?? 7) : -1",
        "\"left\" integer, \"right\" integer", "true, false", 2, false, 1729L)]
    [DataRow("Ankus.PgMemoryContext first, int left, Ankus.PgMemoryContext? middle, int? right, Ankus.PgMemoryContext last",
        "first is not null && middle is not null && last is not null ? left * 100 + (right ?? 7) : -1",
        "\"left\" integer, \"right\" integer", "true, false", 2, true, 1707L)]
    public void VirtualContextsPreserveCompiledSetFactoryAndNullableArguments(string parameters, string expression,
        string sqlParameters, string required, int count, bool nullSecond, long expected)
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate($$"""
            public static class Functions
            {
                [Ankus.PgFunction]
                public static System.Collections.Generic.IEnumerable<int> Rows({{parameters}})
                {
                    yield return {{expression}};
                }
            }
            """);
        AssertMemoryCompilationSucceeds(compilation, diagnostics);
        Assert.StartsWith("CREATE FUNCTION \"rows\"(" + sqlParameters + ") RETURNS SETOF integer AS ",
            Assert.ContainsSingle(OperatorCastStatements(compilation)));
        string native = ManifestValue(compilation, "Ankus.NativeSource");
        Assert.Contains("const bool required[] = { " + required + " };", native);
        IMethodSymbol callback = Assert.ContainsSingle(compilation.GetTypeByMetadataName("Ankus.Generated.ExtensionDispatchers")!
            .GetMembers().OfType<IMethodSymbol>());
        Assert.Contains("return ankus_set_execute(fcinfo, " + callback.Name + ", 1, " +
            count.ToString(CultureInfo.InvariantCulture) + ", required, 0, false);", native);
        Assert.AreSequenceEqual(new long[] { 0, expected, 0, 999, 888 }, InvokeVirtualContextCallback(compilation, nullSecond));
    }

    /// <summary>
    /// SQL null-input policy depends on SQL values independently of context nullability and optional null defaults.
    /// </summary>
    /// <param name="parameters">The managed parameter declaration.</param>
    /// <param name="options">The explicit function options.</param>
    /// <param name="sqlParameters">The expected SQL parameters.</param>
    /// <param name="policy">The expected null-input policy.</param>
    [TestMethod]
    [DataRow("Ankus.PgMemoryContext? context", "", "", "STRICT")]
    [DataRow("Ankus.PgMemoryContext? context = null", "", "", "STRICT")]
    [DataRow("Ankus.PgMemoryContext? context, int value", "", "\"value\" integer", "STRICT")]
    [DataRow("Ankus.PgMemoryContext context, int? value", "", "\"value\" integer", "CALLED ON NULL INPUT")]
    [DataRow("Ankus.PgMemoryContext context, int? value", "NullInput = Ankus.PgNullInput.CalledOnNull", "\"value\" integer", "CALLED ON NULL INPUT")]
    [DataRow("Ankus.PgMemoryContext? context, int? value", "NullInput = Ankus.PgNullInput.Strict", "\"value\" integer", "STRICT")]
    public void VirtualContextNullabilityDoesNotChangeSqlStrictness(string parameters, string options, string sqlParameters, string policy)
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate($$"""
            public static class Functions
            {
                [Ankus.PgFunction({{options}})]
                public static int Apply({{parameters}}) => 42;
            }
            """);
        AssertMemoryCompilationSucceeds(compilation, diagnostics);
        string sql = Assert.ContainsSingle(OperatorCastStatements(compilation));
        Assert.StartsWith("CREATE FUNCTION \"apply\"(" + sqlParameters + ") RETURNS integer AS ", sql);
        Assert.Contains(" PARALLEL UNSAFE " + policy + " SECURITY INVOKER ", sql);
    }

    /// <summary>
    /// The native PostgreSQL argument limit counts SQL values while retaining every managed context parameter.
    /// </summary>
    /// <param name="count">The number of SQL integer inputs.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(100)]
    [DataRow(101)]
    public void VirtualContextsDoNotConsumePostgresArgumentLimit(int count)
    {
        string[] names = [.. Enumerable.Range(0, count).Select(static index => "value" + index.ToString(CultureInfo.InvariantCulture))];
        string parameters = string.Join(", ", names.Select(static name => "int " + name)
            .Prepend("Ankus.PgMemoryContext first").Append("Ankus.PgMemoryContext last"));
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate(
            "public static class Functions { [Ankus.PgFunction] public static int Apply(" + parameters + ") => 42; }");
        if (count == 101)
        {
            AssertVirtualContextDiagnostic(diagnostics, "ANKUS001");
            return;
        }

        AssertMemoryCompilationSucceeds(compilation, diagnostics);
        string expected = string.Join(", ", names.Select(static name => "\"" + name + "\" integer"));
        Assert.StartsWith("CREATE FUNCTION \"apply\"(" + expected + ") RETURNS integer AS ",
            Assert.ContainsSingle(OperatorCastStatements(compilation)));
        string native = ManifestValue(compilation, "Ankus.NativeSource");
        Assert.Contains("if (PG_NARGS() != " + count.ToString(CultureInfo.InvariantCulture) + ")", native);
        Assert.DoesNotContain("PG_ARGISNULL(" + count.ToString(CultureInfo.InvariantCulture) + ")", native);
    }

    /// <summary>
    /// Context erasure preserves overloads with distinct SQL types and rejects signatures differing only in contexts.
    /// </summary>
    /// <param name="secondParameters">The second overload's managed parameters.</param>
    /// <param name="duplicate">Whether both overloads have the same SQL signature.</param>
    [TestMethod]
    [DataRow("int value", true)]
    [DataRow("int value, Ankus.PgMemoryContext? other = null", true)]
    [DataRow("Ankus.PgMemoryContext a, int? value, Ankus.PgMemoryContext b", true)]
    [DataRow("Ankus.PgMemoryContext a, long value, Ankus.PgMemoryContext b", false)]
    public void VirtualContextErasureDeterminesSqlOverloadIdentity(string secondParameters, bool duplicate)
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate($$"""
            public static class Functions
            {
                [Ankus.PgFunction(Name = "apply")]
                public static int First(Ankus.PgMemoryContext context, int value) => value;
                [Ankus.PgFunction(Name = "apply")]
                public static int Second({{secondParameters}}) => 42;
            }
            """);
        if (duplicate)
        {
            AssertVirtualContextDiagnostic(diagnostics, "ANKUS002");
            return;
        }

        AssertMemoryCompilationSucceeds(compilation, diagnostics);
        string[] sql = OperatorCastStatements(compilation);
        Assert.HasCount(2, sql);
        Assert.HasCount(1, sql.Where(static statement => statement.StartsWith("CREATE FUNCTION \"apply\"(\"value\" integer)", StringComparison.Ordinal)));
        Assert.HasCount(1, sql.Where(static statement => statement.StartsWith("CREATE FUNCTION \"apply\"(\"value\" bigint)", StringComparison.Ordinal)));
    }

    /// <summary>
    /// Default expressions and SQL names remain attached to the correct inputs across virtual parameters.
    /// </summary>
    [TestMethod]
    public void VirtualContextsPreserveSqlNamesAndDefaultsAcrossManagedPositions()
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            public static class Functions
            {
                [Ankus.PgFunction]
                public static int Apply(Ankus.PgMemoryContext Left,
                    [Ankus.PgParameter(Name = "left", Default = "17")] int input,
                    Ankus.PgMemoryContext middle,
                    [Ankus.PgParameter(Name = "right")] int other = 29,
                    Ankus.PgMemoryContext? last = null) => input + other;
            }
            """);
        AssertMemoryCompilationSucceeds(compilation, diagnostics);
        Assert.StartsWith("CREATE FUNCTION \"apply\"(\"left\" integer DEFAULT (17), \"right\" integer DEFAULT ((29)::integer)) RETURNS integer AS ",
            Assert.ContainsSingle(OperatorCastStatements(compilation)));
    }

    /// <summary>
    /// A virtual parameter does not reset default ordering or make required SQL arguments callable on null.
    /// </summary>
    /// <param name="method">The invalid function declaration.</param>
    [TestMethod]
    [DataRow("[Ankus.PgFunction] public static int Apply([Ankus.PgParameter(Default = \"17\")] int first, Ankus.PgMemoryContext context, int last) => first + last;")]
    [DataRow("[Ankus.PgFunction(NullInput = Ankus.PgNullInput.CalledOnNull)] public static int Apply(Ankus.PgMemoryContext? context, int value) => value;")]
    public void VirtualContextsDoNotBypassSqlDeclarationValidation(string method)
    {
        (_, ImmutableArray<Diagnostic> diagnostics) = Generate("public static class Functions { " + method + " }");
        AssertVirtualContextDiagnostic(diagnostics, "ANKUS004");
    }

    /// <summary>
    /// The last real array argument remains SQL variadic when contexts precede or separate real inputs.
    /// </summary>
    [TestMethod]
    public void VirtualContextsPreserveVariadicSqlPosition()
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            public static class Functions
            {
                [Ankus.PgFunction]
                public static int Apply(Ankus.PgMemoryContext first, int seed,
                    Ankus.PgMemoryContext middle, params int[] values) => seed + values.Length;
            }
            """);
        AssertMemoryCompilationSucceeds(compilation, diagnostics);
        Assert.StartsWith("CREATE FUNCTION \"apply\"(\"seed\" integer, VARIADIC \"values\" integer[]) RETURNS integer AS ",
            Assert.ContainsSingle(OperatorCastStatements(compilation)));
        string native = ManifestValue(compilation, "Ankus.NativeSource");
        Assert.Contains("if (PG_NARGS() != 2)", native);
        Assert.Contains("ankus_read_array(PG_GETARG_DATUM(1), &arguments[1], &owned[1]);", native);
    }

    /// <summary>
    /// TABLE output names may match erased context names but must remain distinct from actual SQL input names.
    /// </summary>
    /// <param name="inputName">The SQL-visible input name.</param>
    /// <param name="valid">Whether the real input avoids the TABLE output name.</param>
    [TestMethod]
    [DataRow("seed", true)]
    [DataRow("result", false)]
    public void VirtualContextNamesDoNotOccupyTableOutputNamespace(string inputName, bool valid)
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate($$"""
            public static class Functions
            {
                [Ankus.PgFunction]
                public static System.Collections.Generic.IEnumerable<(int Result, string Label)> Rows(
                    Ankus.PgMemoryContext Result, [Ankus.PgParameter(Name = "{{inputName}}")] int input,
                    Ankus.PgMemoryContext Label) => new[] { (input, "value") };
            }
            """);
        if (!valid)
        {
            AssertVirtualContextDiagnostic(diagnostics, "ANKUS004");
            return;
        }

        AssertMemoryCompilationSucceeds(compilation, diagnostics);
        Assert.StartsWith("CREATE FUNCTION \"rows\"(\"seed\" integer) RETURNS TABLE (\"result\" integer, \"label\" text) AS ",
            Assert.ContainsSingle(OperatorCastStatements(compilation)));
    }

    /// <summary>
    /// Virtual arguments retain enum and composite ordering dependencies without introducing a memory-context SQL type.
    /// </summary>
    [TestMethod]
    public void VirtualContextsPreserveEnumAndCompositeTypeDependencies()
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            [assembly: Ankus.PgSql("dog", "CREATE TYPE pets.dog AS (name text);", Requires = new[] { "schema" })]
            public static class Functions
            {
                [Ankus.PgFunction(Requires = new[] { "dog" })]
                public static int Apply(Ankus.PgMemoryContext first, Mood state,
                    Ankus.PgMemoryContext middle,
                    [Ankus.PgCompositeType("dog", Schema = "pets")] Ankus.PgHeapTuple value,
                    Ankus.PgMemoryContext last) => (int)state;
            }
            [Ankus.PgEnum] public enum Mood { Happy, Sad }
            [Ankus.PgSchema("pets", Id = "schema")] public static class Types;
            """);
        AssertMemoryCompilationSucceeds(compilation, diagnostics);
        string[] sql = OperatorCastStatements(compilation);
        Assert.HasCount(4, sql);
        int function = Array.FindIndex(sql, static statement => statement.StartsWith("CREATE FUNCTION", StringComparison.Ordinal));
        int enumeration = Array.IndexOf(sql, "CREATE TYPE \"mood\" AS ENUM (E'Happy', E'Sad');");
        Assert.IsGreaterThanOrEqualTo(0, enumeration);
        Assert.IsLessThan(function, enumeration);
        int composite = Array.IndexOf(sql, "CREATE TYPE pets.dog AS (name text);");
        Assert.IsGreaterThanOrEqualTo(0, composite);
        Assert.IsLessThan(function, composite);
        int schema = Array.IndexOf(sql, "CREATE SCHEMA IF NOT EXISTS \"pets\";");
        Assert.IsGreaterThanOrEqualTo(0, schema);
        Assert.IsLessThan(composite, schema);
        Assert.StartsWith("CREATE FUNCTION \"apply\"(\"state\" \"mood\", \"value\" \"pets\".\"dog\") RETURNS integer AS ", sql[function]);
        Assert.AreEqual("false", ManifestValue(compilation, "Ankus.Relocatable"));
    }

    /// <summary>
    /// Automatic SQL type dependencies still form cycles when virtual parameters surround the dependent argument.
    /// </summary>
    /// <param name="declaration">The SQL type or schema that explicitly depends on the function.</param>
    /// <param name="parameter">The argument that implicitly requires that type or schema.</param>
    [TestMethod]
    [DataRow("[Ankus.PgEnum(Requires = new[] { \"consumer\" })] public enum Mood { Happy }", "Mood value")]
    [DataRow("[Ankus.PgSchema(\"pets\", Requires = new[] { \"consumer\" })] public static class Types;",
        "[Ankus.PgCompositeType(\"dog\", Schema = \"pets\")] Ankus.PgHeapTuple value")]
    public void VirtualContextsPreserveAutomaticSqlDependencyEdges(string declaration, string parameter)
    {
        (_, ImmutableArray<Diagnostic> diagnostics) = Generate($$"""
            {{declaration}}
            public static class Functions
            {
                [Ankus.PgFunction(Id = "consumer")]
                public static int Apply(Ankus.PgMemoryContext first, {{parameter}}, Ankus.PgMemoryContext last) => 42;
            }
            """);
        AssertVirtualContextDiagnostic(diagnostics, "ANKUS005");
        Assert.Contains("cycle", diagnostics[0].GetMessage(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Unary and binary operators select operands from the SQL-visible sequence at every virtual parameter position.
    /// </summary>
    /// <param name="parameters">The managed operator parameters.</param>
    /// <param name="operands">The exact SQL operand clause.</param>
    [TestMethod]
    [DataRow("Ankus.PgMemoryContext first, int left, long right", "LEFTARG = integer, RIGHTARG = bigint")]
    [DataRow("int left, Ankus.PgMemoryContext middle, long right", "LEFTARG = integer, RIGHTARG = bigint")]
    [DataRow("int left, long right, Ankus.PgMemoryContext last", "LEFTARG = integer, RIGHTARG = bigint")]
    [DataRow("Ankus.PgMemoryContext first, int left, Ankus.PgMemoryContext middle, long right, Ankus.PgMemoryContext last", "LEFTARG = integer, RIGHTARG = bigint")]
    [DataRow("Ankus.PgMemoryContext first, int value, Ankus.PgMemoryContext last", "RIGHTARG = integer")]
    public void VirtualContextsDoNotBecomeOperatorOperands(string parameters, string operands)
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate(
            "public static class Functions { [Ankus.PgOperator(\"@+\")] public static int Apply(" + parameters + ") => 42; }");
        AssertMemoryCompilationSucceeds(compilation, diagnostics);
        string[] sql = OperatorCastStatements(compilation);
        Assert.HasCount(2, sql);
        Assert.AreEqual("CREATE OPERATOR @+ (FUNCTION = \"apply\", " + operands + ");", sql[1]);
    }

    /// <summary>
    /// Cast source, optional type modifier, and explicitness positions ignore interleaved context parameters.
    /// </summary>
    /// <param name="parameters">The managed cast parameters.</param>
    /// <param name="signature">The exact SQL backing function signature.</param>
    [TestMethod]
    [DataRow("Ankus.PgMemoryContext first, int value", "integer")]
    [DataRow("int value, Ankus.PgMemoryContext last", "integer")]
    [DataRow("int value, Ankus.PgMemoryContext middle, int modifier", "integer, integer")]
    [DataRow("Ankus.PgMemoryContext first, int value, Ankus.PgMemoryContext middle, int modifier, bool explicitly, Ankus.PgMemoryContext last", "integer, integer, boolean")]
    public void VirtualContextsDoNotBecomeCastSourceOrModifierArguments(string parameters, string signature)
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate(
            "public static class Functions { [Ankus.PgCast] public static string Apply(" + parameters + ") => \"value\"; }");
        AssertMemoryCompilationSucceeds(compilation, diagnostics);
        string[] sql = OperatorCastStatements(compilation);
        Assert.HasCount(2, sql);
        Assert.AreEqual("CREATE CAST (integer AS text) WITH FUNCTION \"apply\"(" + signature + ");", sql[1]);
    }

    /// <summary>
    /// Virtual arguments cannot satisfy required operator operands or cast source and modifier contracts.
    /// </summary>
    /// <param name="method">The invalid attributed method.</param>
    [TestMethod]
    [DataRow("[Ankus.PgOperator(\"@+\")] public static int Apply(Ankus.PgMemoryContext context) => 1;")]
    [DataRow("[Ankus.PgCast] public static string Apply(Ankus.PgMemoryContext context) => \"value\";")]
    [DataRow("[Ankus.PgCast] public static string Apply(int value, Ankus.PgMemoryContext context, bool modifier) => \"value\";")]
    public void VirtualContextsDoNotBypassOperatorOrCastArityValidation(string method)
    {
        (_, ImmutableArray<Diagnostic> diagnostics) = Generate("public static class Functions { " + method + " }");
        AssertVirtualContextDiagnostic(diagnostics, "ANKUS007");
    }

    /// <summary>
    /// SQL-only metadata on virtual contexts reports its existing diagnostic instead of disappearing silently.
    /// </summary>
    /// <param name="attribute">The SQL-only parameter annotation.</param>
    /// <param name="expected">The expected diagnostic identifier.</param>
    [TestMethod]
    [DataRow("Ankus.PgParameter", "ANKUS004")]
    [DataRow("Ankus.PgParameter(Name = \"memory\")", "ANKUS004")]
    [DataRow("Ankus.PgParameter(Default = \"NULL\")", "ANKUS004")]
    [DataRow("Ankus.PgNumericPrecision(5, 2)", "ANKUS003")]
    [DataRow("Ankus.PgCompositeType(\"memory\")", "ANKUS009")]
    public void VirtualContextSqlMetadataIsDiagnosed(string attribute, string expected)
    {
        (_, ImmutableArray<Diagnostic> diagnostics) = Generate(
            "public static class Functions { [Ankus.PgFunction] public static int Apply([" + attribute + "] Ankus.PgMemoryContext context) => 42; }");
        AssertVirtualContextDiagnostic(diagnostics, expected);
    }

    /// <summary>
    /// Only the exact by-value context parameter is virtual; references, collections, results, and lookalikes remain unsupported.
    /// </summary>
    /// <param name="method">The unsupported function declaration.</param>
    [TestMethod]
    [DataRow("public static int Apply(ref Ankus.PgMemoryContext context) => 42;")]
    [DataRow("public static int Apply(in Ankus.PgMemoryContext context) => 42;")]
    [DataRow("public static int Apply(out Ankus.PgMemoryContext context) { context = null!; return 42; }")]
    [DataRow("public static int Apply(Ankus.PgMemoryContext[] contexts) => contexts.Length;")]
    [DataRow("public static int Apply(Ankus.PgArray<Ankus.PgMemoryContext> contexts) => 42;")]
    [DataRow("public static Ankus.PgMemoryContext Apply(Ankus.PgMemoryContext context) => context;")]
    [DataRow("public static int Apply(Other.PgMemoryContext context) => 42;")]
    [DataRow("public static int Apply<T>(Ankus.PgMemoryContext context) => 42;")]
    public void UnsupportedVirtualContextShapesAreRejected(string method)
    {
        (_, ImmutableArray<Diagnostic> diagnostics) = Generate(
            "namespace Other { public sealed class PgMemoryContext; } public static class Functions { [Ankus.PgFunction] " + method + " }");
        AssertVirtualContextDiagnostic(diagnostics, "ANKUS001");
    }

    /// <summary>
    /// An enumerable context result is rejected as an unsupported SQL set element rather than being erased.
    /// </summary>
    [TestMethod]
    public void VirtualContextsCannotBecomeSqlSetResults()
    {
        (_, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            public static class Functions
            {
                [Ankus.PgFunction]
                public static System.Collections.Generic.IEnumerable<Ankus.PgMemoryContext> Rows(Ankus.PgMemoryContext context)
                    => new[] { context };
            }
            """);
        AssertVirtualContextDiagnostic(diagnostics, "ANKUS008");
    }

    /// <summary>
    /// Dedicated trigger entry points retain their explicit context signatures instead of inheriting scalar virtual arguments.
    /// </summary>
    /// <param name="method">The unsupported trigger declaration.</param>
    /// <param name="expected">The expected trigger diagnostic.</param>
    [TestMethod]
    [DataRow("[Ankus.PgTrigger] public static Ankus.PgHeapTuple? Apply(Ankus.PgTriggerContext trigger, Ankus.PgMemoryContext context) => trigger.New;", "ANKUS010")]
    [DataRow("[Ankus.PgEventTrigger] public static void Apply(Ankus.PgMemoryContext context, Ankus.PgEventTriggerContext trigger) { }", "ANKUS011")]
    public void VirtualContextsDoNotChangeDedicatedTriggerSignatures(string method, string expected)
    {
        (_, ImmutableArray<Diagnostic> diagnostics) = Generate("public static class Functions { " + method + " }");
        AssertVirtualContextDiagnostic(diagnostics, expected);
    }

    /// <summary>
    /// Checks that a rejected context contract produces one located error with the expected public identifier.
    /// </summary>
    /// <param name="diagnostics">The generator diagnostics.</param>
    /// <param name="expected">The expected diagnostic identifier.</param>
    private static void AssertVirtualContextDiagnostic(ImmutableArray<Diagnostic> diagnostics, string expected)
    {
        Diagnostic diagnostic = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual(expected, diagnostic.Id);
        Assert.AreEqual(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.IsTrue(diagnostic.Location.IsInSource);
    }

    /// <summary>
    /// Executes an emitted unmanaged callback using a minimal context-resolution provider and verifies enclosing capability restoration.
    /// </summary>
    /// <param name="compilation">The generated consumer compilation.</param>
    /// <param name="nullSecond">Whether the second SQL transport represents null.</param>
    /// <returns>The callback status, scalar value, null flag, active provider, and restored provider identity.</returns>
    private long[] InvokeVirtualContextCallback(Compilation compilation, bool nullSecond = false)
    {
        IMethodSymbol callback = Assert.ContainsSingle(compilation.GetTypeByMetadataName("Ankus.Generated.ExtensionDispatchers")!
            .GetMembers().OfType<IMethodSymbol>());
        string invocation = callback.Parameters[0].Type.SpecialType == SpecialType.System_Int32
            ? """
                delegate* unmanaged[Cdecl]<int, nint*, Ankus.NativeValue*, Ankus.NativeValue*, Ankus.NativeCallError*, nint, nint, int> invoke =
                    (delegate* unmanaged[Cdecl]<int, nint*, Ankus.NativeValue*, Ankus.NativeValue*, Ankus.NativeCallError*, nint, nint, int>)address;
                nint iterator = 0;
                int status = invoke(0, &iterator, arguments, &result, &error, 0, (nint)inner);
                try
                {
                    if (status == 0)
                    {
                        status = invoke(1, &iterator, arguments, &result, &error, 0, (nint)inner);
                        Ankus.NativeValue first = result;
                        int completion = invoke(1, &iterator, arguments, &result, &error, 0, (nint)inner);
                        if (completion != 2)
                        {
                            status = -2;
                        }

                        result = first;
                    }
                }
                finally
                {
                    if (iterator != 0)
                    {
                        int disposal = invoke(2, &iterator, arguments, &result, &error, 0, (nint)inner);
                        if (disposal != 0 || iterator != 0)
                        {
                            status = -3;
                        }
                    }
                }
                """
            : """
                delegate* unmanaged[Cdecl]<Ankus.NativeValue*, Ankus.NativeValue*, Ankus.NativeCallError*, nint, nint, int> invoke =
                    (delegate* unmanaged[Cdecl]<Ankus.NativeValue*, Ankus.NativeValue*, Ankus.NativeCallError*, nint, nint, int>)address;
                int status = invoke(arguments, &result, &error, 0, (nint)inner);
                """;
        compilation = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText($$"""
            public static unsafe class VirtualContextProbe
            {
                private static nint s_lastProvider;

                [System.Runtime.InteropServices.UnmanagedCallersOnly(
                    CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
                private static int Resolve(nint api, void* request, nint* result, Ankus.NativeCallError* error)
                {
                    s_lastProvider = ((nint*)api)[0];
                    result[0] = 77;
                    return 0;
                }

                public static long[] Run(nint address)
                {
                    delegate* unmanaged[Cdecl]<nint, void*, nint*, Ankus.NativeCallError*, int> resolve = &Resolve;
                    nint* outer = stackalloc nint[] { 888, 77, (nint)resolve };
                    nint* inner = stackalloc nint[] { 999, 77, (nint)resolve };
                    Ankus.NativeValue* arguments = stackalloc Ankus.NativeValue[]
                    {
                        new() { Integral = 17 },
                        new() { Integral = 29, IsNull = {{(nullSecond ? "1" : "0")}} },
                    };
                    Ankus.NativeValue result = default;
                    Ankus.NativeCallError error = default;
                    nint previous = Ankus.NativeMemoryContext.Enter((nint)outer);
                    try
                    {
                        {{invocation}}
                        nint provider = s_lastProvider;
                        _ = Ankus.PgMemoryContext.Current;
                        return new long[] { status, result.Integral, result.IsNull, provider, s_lastProvider };
                    }
                    finally
                    {
                        Ankus.NativeMemoryContext.Exit(previous);
                    }
                }
            }
            """, cancellationToken: context.CancellationToken));
        AssertMemoryCompilationSucceeds(compilation, []);
        using var stream = new MemoryStream();
        Microsoft.CodeAnalysis.Emit.EmitResult emitted = compilation.Emit(stream, cancellationToken: context.CancellationToken);
        Assert.IsTrue(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        stream.Position = 0;
        var loadContext = new AssemblyLoadContext("VirtualContextGeneratorProbe", isCollectible: true);
        try
        {
            Assembly assembly = loadContext.LoadFromStream(stream);
            Type dispatchers = assembly.GetType("Ankus.Generated.ExtensionDispatchers", throwOnError: true)!;
            MethodInfo entry = dispatchers.GetMethod(callback.Name, BindingFlags.NonPublic | BindingFlags.Static)!;
            Assert.IsNotNull(entry);
            Type probe = assembly.GetType("VirtualContextProbe", throwOnError: true)!;
            MethodInfo run = probe.GetMethod("Run", BindingFlags.Public | BindingFlags.Static)!;
            Assert.IsNotNull(run);
            return Assert.IsInstanceOfType<long[]>(run.Invoke(null, [entry.MethodHandle.GetFunctionPointer()]));
        }
        finally
        {
            loadContext.Unload();
        }
    }
}
