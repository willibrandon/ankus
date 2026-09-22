using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Ankus.Generators.Tests;

public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// A container-only declaration emits a support function and dependent aggregate with exact SQL and exports.
    /// </summary>
    /// <param name="function">Optional support function metadata discovered through both providers.</param>
    [TestMethod]
    [DataRow("")]
    [DataRow("[Ankus.PgFunction]")]
    public void AggregateMinimalContainerEmitsExactCompilableSql(string function)
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("[Ankus.PgAggregate(InitialCondition = \"0\")] " +
            "public static class SumValues { " + function + " public static long Transition(long state, int value) => state + value; }");
        AssertAggregateCompilation(compilation, diagnostics);
        IMethodSymbol callback = AggregateCallback(compilation, "transition");
        string nativeName = callback.Name.Replace("ankus_managed_", "ankus_fn_", StringComparison.Ordinal);
        Assert.AreEqual($"CREATE FUNCTION \"sum_values_transition\"(\"state\" bigint, \"value\" integer)\n" +
            $"RETURNS bigint AS 'MODULE_PATHNAME', '{nativeName}' LANGUAGE c VOLATILE PARALLEL UNSAFE STRICT SECURITY INVOKER NOT LEAKPROOF COST 1;\n" +
            "CREATE AGGREGATE \"sum_values\"(\"value\" integer) (\n    SFUNC = \"sum_values_transition\",\n    STYPE = bigint,\n" +
            "    FINALFUNC_MODIFY = READ_ONLY,\n    INITCOND = E'0',\n    PARALLEL = UNSAFE\n);\n",
            ManifestValue(compilation, "Ankus.Sql").ReplaceLineEndings("\n"));
        Assert.AreSequenceEqual(["Pg_magic_func", nativeName, "pg_finfo_" + nativeName],
            ManifestValue(compilation, "Ankus.Exports").Split('\n', StringSplitOptions.RemoveEmptyEntries));
        Assert.AreEqual("true", ManifestValue(compilation, "Ankus.Relocatable"));
        Assert.Contains($"return ankus_aggregate_call(fcinfo, {callback.Name}, 2, required, internal_arguments, false, false);",
            ManifestValue(compilation, "Ankus.NativeSource"));
    }

    /// <summary>
    /// Aggregate lifetime encloses state reads, user invocation and adoption, and always exits before backend restoration.
    /// </summary>
    [TestMethod]
    public void AggregateInternalDispatchUsesOwnedStateAndNestedFinally()
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            public sealed class Counter { public int Value; }
            [Ankus.PgAggregate]
            public static class Owned
            {
                public static Ankus.PgAggregateState<Counter> Transition(Ankus.PgAggregateContext context,
                    Ankus.PgAggregateState<Counter>? state, int? value) => state ?? new(new Counter());
                public static int Final(Ankus.PgAggregateState<Counter>? state) => state?.Value.Value ?? 0;
            }
            """);
        AssertAggregateCompilation(compilation, diagnostics);
        MethodDeclarationSyntax callback = Assert.IsInstanceOfType<MethodDeclarationSyntax>(AggregateCallback(compilation, "transition")
            .DeclaringSyntaxReferences.Single().GetSyntax(context.CancellationToken));
        Assert.AreSequenceEqual(["arguments", "result", "error", "execute", "metadata", "metadataCount", "owner", "api"],
            callback.ParameterList.Parameters.Select(static parameter => parameter.Identifier.ValueText));
        BlockSyntax body = Assert.IsInstanceOfType<BlockSyntax>(callback.Body);
        Assert.HasCount(2, body.Statements);
        Assert.AreEqual("nint previous = global::Ankus.NativeBackend.Enter(execute);", body.Statements[0].ToString());
        TryStatementSyntax outer = Assert.IsInstanceOfType<TryStatementSyntax>(body.Statements[1]);
        Assert.AreEqual("global::Ankus.NativeBackend.Exit(previous);", Assert.ContainsSingle(outer.Finally!.Block.Statements).ToString());
        Assert.HasCount(2, outer.Block.Statements);
        Assert.Contains("new global::System.ReadOnlySpan<global::Ankus.NativeValue>(metadata, metadataCount), owner, api)", outer.Block.Statements[0].ToString());
        TryStatementSyntax inner = Assert.IsInstanceOfType<TryStatementSyntax>(outer.Block.Statements[1]);
        Assert.AreSequenceEqual([
            "global::Ankus.PgAggregateState<global::Counter> value = global::Owned.@Transition(context, global::Ankus.NativeAggregate.Read<global::Counter>(arguments[0]), (arguments[1].IsNull != 0 ? (int?)null : (int)arguments[1].Integral));",
            "*result = global::Ankus.NativeAggregate.Write(value);",
            "return 0;",
        ], inner.Block.Statements.Select(static statement => statement.ToString()));
        Assert.AreEqual("global::Ankus.NativeAggregate.Exit(context);", Assert.ContainsSingle(inner.Finally!.Block.Statements).ToString());
        CatchClauseSyntax failure = Assert.ContainsSingle(outer.Catches);
        Assert.AreEqual("global::System.Exception", failure.Declaration!.Type.ToString());
        Assert.AreSequenceEqual(["global::Ankus.NativeError.Write(exception, error);", "return 1;"], failure.Block.Statements.Select(static statement => statement.ToString()));
        string native = ManifestValue(compilation, "Ankus.NativeSource");
        Assert.Contains("const bool required[2] = {false, false};", native);
        Assert.Contains("const bool internal_arguments[2] = {true, false};", native);
    }

    /// <summary>
    /// Serializers preserve native internal signatures, including the deserializer dummy that is not a managed argument.
    /// </summary>
    [TestMethod]
    public void AggregateSerializationUsesStrictByteaAndHiddenInternalDummy()
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            [Ankus.PgAggregate(ParallelSafety = Ankus.PgParallelSafety.Safe)]
            public static class Transfer
            {
                public static Ankus.PgAggregateState<int> Transition(Ankus.PgAggregateState<int>? state, int value) => new((state?.Value ?? 0) + value);
                public static int Final(Ankus.PgAggregateState<int>? state) => state?.Value ?? 0;
                public static Ankus.PgAggregateState<int> Combine(Ankus.PgAggregateState<int>? state, Ankus.PgAggregateState<int>? other) => new((state?.Value ?? 0) + (other?.Value ?? 0));
                public static byte[] Serialize(Ankus.PgAggregateState<int> state) => System.BitConverter.GetBytes(state.Value);
                public static Ankus.PgAggregateState<int> Deserialize(Ankus.PgAggregateContext context, byte[] bytes) => new(System.BitConverter.ToInt32(bytes));
            }
            """);
        AssertAggregateCompilation(compilation, diagnostics);
        string sql = ManifestValue(compilation, "Ankus.Sql").ReplaceLineEndings("\n");
        Assert.Contains("CREATE FUNCTION \"transfer_deserialize\"(\"bytes\" bytea, internal)\nRETURNS internal", sql);
        string nativeName = AggregateCallback(compilation, "deserialize").Name.Replace("ankus_managed_", "ankus_fn_", StringComparison.Ordinal);
        Assert.Contains($"RETURNS internal AS 'MODULE_PATHNAME', '{nativeName}' LANGUAGE c VOLATILE PARALLEL UNSAFE STRICT", sql);
        Assert.Contains("CREATE FUNCTION \"transfer_serialize\"(\"state\" internal)\nRETURNS bytea", sql);
        Assert.Contains("    COMBINEFUNC = \"transfer_combine\",\n    SERIALFUNC = \"transfer_serialize\",\n    DESERIALFUNC = \"transfer_deserialize\",\n    PARALLEL = SAFE", sql);
        string managed = AggregateCallback(compilation, "deserialize").DeclaringSyntaxReferences.Single().GetSyntax(context.CancellationToken).ToString();
        Assert.Contains("global::Transfer.@Deserialize(context, arguments[0].ReadBytes())", managed);
        Assert.DoesNotContain("arguments[1]", managed);
        string native = ManifestValue(compilation, "Ankus.NativeSource");
        Assert.Contains("const bool required[2] = {true, false};", native);
        Assert.Contains("const bool internal_arguments[2] = {false, false};", native);
        Assert.Contains($"return ankus_aggregate_call(fcinfo, {AggregateCallback(compilation, "deserialize").Name}, 2, required, internal_arguments, true, true);", native);
    }

    /// <summary>
    /// Ordered direct and extra parameters occupy distinct SQL and managed callback positions with kind-specific final policy.
    /// </summary>
    /// <param name="kind">The ordered or hypothetical aggregate kind.</param>
    /// <param name="hypothetical">The expected hypothetical option.</param>
    [TestMethod]
    [DataRow("OrderedSet", false)]
    [DataRow("HypotheticalSet", true)]
    public void AggregateOrderedDirectAndExtraArgumentsHaveExactSignatures(string kind, bool hypothetical)
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            [Ankus.PgAggregate(Kind = Ankus.PgAggregateKind.
            """ + kind + """
            , InitialCondition = "0", FinalExtra = true)]
            public static class RankValues
            {
                public static int Transition(int state, int value) => state + value;
                public static long Final(int? state, int? hypothetical, int? unused) => 1;
            }
            """);
        AssertAggregateCompilation(compilation, diagnostics);
        string sql = ManifestValue(compilation, "Ankus.Sql").ReplaceLineEndings("\n");
        Assert.Contains("CREATE FUNCTION \"rank_values_final\"(\"state\" integer, \"hypothetical\" integer, \"unused\" integer)\nRETURNS bigint", sql);
        Assert.Contains("CREATE AGGREGATE \"rank_values\"(\"hypothetical\" integer ORDER BY \"value\" integer) (", sql);
        Assert.Contains("    FINALFUNC = \"rank_values_final\",\n    FINALFUNC_EXTRA,\n    FINALFUNC_MODIFY = READ_WRITE,", sql);
        Assert.AreEqual(hypothetical, sql.Contains("    HYPOTHETICAL\n", StringComparison.Ordinal));
    }

    /// <summary>
    /// Moving state can differ from ordinary state while final results and input strictness remain compatible.
    /// </summary>
    [TestMethod]
    public void AggregateMovingStateFinalExtraAndNullableInverseCompile()
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            [Ankus.PgAggregate(InitialCondition = "0", MovingInitialCondition = "0", StateSize = 8, MovingStateSize = 16,
                MovingFinalExtra = true, FinalModify = Ankus.PgAggregateFinalModify.Shareable,
                MovingFinalModify = Ankus.PgAggregateFinalModify.ReadOnly)]
            public static class Moving
            {
                public static int Transition(int state, int value) => state + value;
                public static long Final(int state) => state;
                public static long MovingTransition(long state, int value) => state + value;
                public static long? MovingInverse(long state, int value) => state - value;
                public static long MovingFinal(long? state, int? unused) => state ?? 0;
            }
            """);
        AssertAggregateCompilation(compilation, diagnostics);
        string sql = ManifestValue(compilation, "Ankus.Sql").ReplaceLineEndings("\n");
        Assert.Contains("    FINALFUNC_MODIFY = SHAREABLE,\n    SSPACE = 8,", sql);
        Assert.Contains("    MSFUNC = \"moving_moving_transition\",\n    MINVFUNC = \"moving_moving_inverse\",\n    MSTYPE = bigint,\n" +
            "    MFINALFUNC = \"moving_moving_final\",\n    MFINALFUNC_EXTRA,\n    MFINALFUNC_MODIFY = READ_ONLY,\n    MSSPACE = 16,\n    MINITCOND = E'0'", sql);
        foreach (string role in new[] { "moving_transition", "moving_inverse" })
        {
            string nativeName = AggregateCallback(compilation, role).Name.Replace("ankus_managed_", "ankus_fn_", StringComparison.Ordinal);
            Assert.Contains($"RETURNS bigint AS 'MODULE_PATHNAME', '{nativeName}' LANGUAGE c VOLATILE PARALLEL UNSAFE STRICT", sql);
        }
    }

    /// <summary>
    /// Zero-input and ordinary variadic aggregate syntax preserve star and variadic array identity.
    /// </summary>
    /// <param name="parameter">The transition's aggregate input list.</param>
    /// <param name="signature">The exact SQL aggregate signature.</param>
    [TestMethod]
    [DataRow("", "*")]
    [DataRow(", params int[] values", "VARIADIC \"values\" integer[]")]
    [DataRow(", int first, params int?[] values", "\"first\" integer, VARIADIC \"values\" integer[]")]
    public void AggregateZeroAndVariadicInputsPreserveSqlShape(string parameter, string signature)
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("[Ankus.PgAggregate(InitialCondition=\"0\")] " +
            "public static class CountValues { public static int Transition(int state" + parameter + ") => state + 1; }");
        AssertAggregateCompilation(compilation, diagnostics);
        Assert.Contains("CREATE AGGREGATE \"count_values\"(" + signature + ") (", ManifestValue(compilation, "Ankus.Sql"));
    }

    /// <summary>
    /// Initial state text escapes independently of server string settings and keeps empty separate from omitted NULL.
    /// </summary>
    /// <param name="option">The aggregate initial-state option.</param>
    /// <param name="expected">The exact initial clause, or null when omitted.</param>
    [TestMethod]
    [DataRow("", null)]
    [DataRow("InitialCondition = \"\"", "INITCOND = E''")]
    [DataRow("InitialCondition = \"café'\\\\text\"", "INITCOND = E'café''\\\\text'")]
    public void AggregateInitialConditionsPreserveNullEmptyAndEscapes(string option, string? expected)
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("[Ankus.PgAggregate(" + option + ")] " +
            "public static class TextState { public static string? Transition(string? state, string? value) => state; }");
        AssertAggregateCompilation(compilation, diagnostics);
        string sql = ManifestValue(compilation, "Ankus.Sql");
        if (expected is null)
        {
            Assert.DoesNotContain("INITCOND", sql);
        }
        else
        {
            Assert.Contains(expected, sql);
        }
    }

    /// <summary>
    /// Aggregates without the feature do not receive unused native context machinery.
    /// </summary>
    /// <param name="source">The ordinary or enum-only extension.</param>
    [TestMethod]
    [DataRow("public static class Functions { [Ankus.PgFunction] public static int Answer() => 42; }")]
    [DataRow("[Ankus.PgEnum] public enum Mood { Happy, Sad }")]
    public void NonAggregateExtensionsOmitUnusedNativeAggregateBridge(string source)
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate(source);
        AssertAggregateCompilation(compilation, diagnostics);
        Assert.DoesNotContain("ankus_aggregate_call", ManifestValue(compilation, "Ankus.NativeSource"));
    }

    /// <summary>
    /// Unsupported support signatures produce aggregate diagnostics instead of malformed generated wrappers.
    /// </summary>
    /// <param name="method">The invalid transition method.</param>
    [TestMethod]
    [DataRow("public int Transition(int state, int value) => state;")]
    [DataRow("private static int Transition(int state, int value) => state;")]
    [DataRow("public static int Transition<T>(int state, int value) => state;")]
    [DataRow("public static void Transition(int state, int value) { }")]
    [DataRow("public static async System.Threading.Tasks.Task<int> Transition(int state, int value) { await System.Threading.Tasks.Task.Yield(); return state; }")]
    [DataRow("public static int Transition() => 0;")]
    [DataRow("public static long Transition(int state, int value) => state;")]
    [DataRow("public static int Transition(ref int state, int value) => state;")]
    [DataRow("public static int Transition(int state, int value = 0) => state;")]
    [DataRow("public static int Transition(int state, [System.Runtime.InteropServices.Optional] int value) => state;")]
    [DataRow("public static int Transition(Ankus.PgAggregateContext? context, int state, int value) => state;")]
    [DataRow("public static int Transition(int state, Ankus.PgAggregateContext context, int value) => state;")]
    [DataRow("public static object Transition(object state, int value) => state;")]
    [DataRow("public static System.Collections.Generic.IEnumerable<int> Transition(int state, int value) => System.Array.Empty<int>();")]
    [DataRow("public static Ankus.PgHeapTuple? Transition(Ankus.PgHeapTuple? state, int value) => state;")]
    public void InvalidAggregateSupportSignaturesAreDiagnosed(string method)
        => AssertInvalidAggregate("[Ankus.PgAggregate(InitialCondition=\"0\")] public class Invalid { " + method + " }", "ANKUS012");

    /// <summary>
    /// Missing, ambiguous, inaccessible and conflicting declarations cannot silently become unrelated functions.
    /// </summary>
    /// <param name="source">The complete invalid aggregate.</param>
    [TestMethod]
    [DataRow("[Ankus.PgAggregate] public static class Missing { }")]
    [DataRow("[Ankus.PgAggregate(Final=\"Missing\")] public static class Missing { public static int Transition(int state,int value)=>state+value; }")]
    [DataRow("[Ankus.PgAggregate(Transition=null!)] public static class Missing { public static int Transition(int state,int value)=>state+value; }")]
    [DataRow("[Ankus.PgAggregate] public static class Ambiguous { public static int Transition(int state,int value)=>state; public static long Transition(long state,long value)=>state; }")]
    [DataRow("[Ankus.PgAggregate] public class Generic<T> { public static int Transition(int state,int value)=>state; }")]
    [DataRow("[Ankus.PgAggregate] file class Hidden { public static int Transition(int state,int value)=>state; }")]
    [DataRow("public class Outer { [Ankus.PgAggregate] private class Hidden { public static int Transition(int state,int value)=>state; } }")]
    [DataRow("[Ankus.PgAggregate] public class Conflict { [Ankus.PgOperator(\"+\")] public static int Transition(int state,int value)=>state; }")]
    [DataRow("[Ankus.PgAggregate] public class Conflict { [Ankus.PgTrigger] public static int Transition(int state,int value)=>state; }")]
    [DataRow("[Ankus.PgAggregate] public class Conflict { [Ankus.PgEventTrigger] public static int Transition(int state,int value)=>state; }")]
    public void InvalidAggregateDiscoveryAndContainersAreDiagnosed(string source) => AssertInvalidAggregate(source, "ANKUS012");

    /// <summary>
    /// Invalid aggregate-level options and transition initialization rules fail before SQL publication.
    /// </summary>
    /// <param name="options">The invalid aggregate options.</param>
    /// <param name="method">The transition implementation.</param>
    [TestMethod]
    [DataRow("Kind=(Ankus.PgAggregateKind)3", "public static int Transition(int state,int value)=>state;")]
    [DataRow("ParallelSafety=(Ankus.PgParallelSafety)3", "public static int Transition(int state,int value)=>state;")]
    [DataRow("FinalModify=(Ankus.PgAggregateFinalModify)4", "public static int Transition(int state,int value)=>state;")]
    [DataRow("MovingFinalModify=(Ankus.PgAggregateFinalModify)4", "public static int Transition(int state,int value)=>state;")]
    [DataRow("Name=\"\"", "public static int Transition(int state,int value)=>state;")]
    [DataRow("Schema=\"\"", "public static int Transition(int state,int value)=>state;")]
    [DataRow("InitialCondition=\"a\\0b\"", "public static int Transition(int state,int value)=>state;")]
    [DataRow("SortOperator=\"schema.invalid\"", "public static int Transition(int state,int value)=>state;")]
    [DataRow("SortOperator=\"pg_catalog.<\"", "public static int Transition(int state,int left,int right)=>state;")]
    [DataRow("MovingStateSize=16", "public static int Transition(int state,int value)=>state;")]
    [DataRow("MovingInitialCondition=\"0\"", "public static int Transition(int state,int value)=>state;")]
    [DataRow("", "public static int Transition(int state)=>state;")]
    [DataRow("", "public static long Transition(long state,int value)=>state;")]
    [DataRow("Kind=Ankus.PgAggregateKind.OrderedSet,InitialCondition=\"0\"", "public static int Transition(int state)=>state;")]
    [DataRow("Kind=Ankus.PgAggregateKind.OrderedSet,InitialCondition=\"0\"", "public static int Transition(int state,params int[] values)=>state;")]
    public void InvalidAggregateOptionsAndInitialContractsAreDiagnosed(string options, string method)
        => AssertInvalidAggregate("[Ankus.PgAggregate(" + options + ")] public static class Invalid { " + method + " }", "ANKUS012");

    /// <summary>
    /// Cross-callback type, extra-input, serialization and moving contracts fail as one aggregate declaration.
    /// </summary>
    /// <param name="options">The aggregate options.</param>
    /// <param name="helpers">The complete support method set.</param>
    [TestMethod]
    [DataRow("", "public static int Transition(int state,int value)=>state; public static int Final(long state)=>0;")]
    [DataRow("", "public static int Transition(int state,int value)=>state; public static int Final(int state,int direct)=>0;")]
    [DataRow("FinalExtra=true", "public static int Transition(int state,int value)=>state; public static int Final(int state,int extra)=>0;")]
    [DataRow("FinalExtra=true", "public static int Transition(int state,int value)=>state; public static int Final(int? state,long? extra)=>0;")]
    [DataRow("Kind=Ankus.PgAggregateKind.HypotheticalSet", "public static int Transition(int state,int value)=>state; public static int Final(int state,long direct)=>0;")]
    [DataRow("", "public static int Transition(int state,int value)=>state; public static int Combine(int state)=>state;")]
    [DataRow("", "public static int Transition(int state,int value)=>state; public static long Combine(int state,int other)=>state;")]
    [DataRow("", "public static int Transition(int state,int value)=>state; public static byte[] Serialize(int state)=>[];")]
    [DataRow("", "public static int Transition(int state,int value)=>state; public static byte[] Serialize(int state)=>[]; public static int Deserialize(byte[] bytes)=>0;")]
    [DataRow("", "public static int Transition(int state,int value)=>state; public static int MovingTransition(int state,int value)=>state;")]
    [DataRow("", "public static int Transition(int state,int value)=>state; public static int MovingInverse(int state,int value)=>state;")]
    [DataRow("", "public static int Transition(int state,int value)=>state; public static int MovingTransition(int state,int value)=>state; public static int MovingInverse(int? state,int? value)=>0;")]
    [DataRow("MovingInitialCondition=\"0\"", "public static int Transition(int state,int value)=>state; public static long MovingTransition(long state,int value)=>state; public static long MovingInverse(long state,int value)=>state;")]
    [DataRow("", "public static Ankus.PgAggregateState<int>? Transition(Ankus.PgAggregateState<int>? state,int value)=>state;")]
    [DataRow("", "public static Ankus.PgAggregateState<int>? Transition(Ankus.PgAggregateState<int>? state,int value)=>state; public static int Final(Ankus.PgAggregateState<int>? state)=>0; public static Ankus.PgAggregateState<int> Combine(Ankus.PgAggregateState<int> state,Ankus.PgAggregateState<int> other)=>state;")]
    public void InvalidAggregateHelperRelationshipsAreDiagnosed(string options, string helpers)
        => AssertInvalidAggregate("[Ankus.PgAggregate(" + options + ")] public static class Invalid { " + helpers + " }", "ANKUS012");

    /// <summary>
    /// Common helper settings remain independent of aggregate planner metadata and quoted names preserve exact spelling.
    /// </summary>
    [TestMethod]
    public void AggregateCallbackOverridesPreserveCommonOptionsAndQuotedNames()
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            [Ankus.PgSchema("outer")]
            public static class Parent
            {
                [Ankus.PgAggregate(Name="Quoted \" Sum", Schema="aggregate schema", Transition="Accumulate",
                    InitialCondition="0", ParallelSafety=Ankus.PgParallelSafety.Safe, StateSize=-1, SortOperator="Op Schema.<")]
                internal struct Nested
                {
                    [Ankus.PgFunction(Name="Quoted \" Helper", Schema="helper schema", CreateOrReplace=true,
                        Volatility=Ankus.PgVolatility.Immutable, ParallelSafety=Ankus.PgParallelSafety.Restricted,
                        SecurityDefiner=true, Leakproof=true, Cost=7.5, SearchPath=new[] { "pg_catalog", "Case Schema" },
                        SupportFunction="planner.support")]
                    internal static int Accumulate(Ankus.PgAggregateContext context, int state,
                        [Ankus.PgParameter(Name="Quoted \" Input")] int value) => state+value;
                }
            }
            """);
        AssertAggregateCompilation(compilation, diagnostics);
        string sql = ManifestValue(compilation, "Ankus.Sql");
        Assert.StartsWith("CREATE SCHEMA IF NOT EXISTS \"outer\";\nCREATE OR REPLACE FUNCTION \"helper schema\".\"Quoted \"\" Helper\"(\"state\" integer, \"Quoted \"\" Input\" integer)\nRETURNS integer AS ", sql);
        Assert.Contains("LANGUAGE c IMMUTABLE PARALLEL RESTRICTED STRICT SECURITY DEFINER LEAKPROOF COST 7.5 SUPPORT \"planner\".\"support\" SET search_path TO \"pg_catalog\", \"Case Schema\";\n", sql);
        Assert.EndsWith("CREATE AGGREGATE \"aggregate schema\".\"Quoted \"\" Sum\"(\"Quoted \"\" Input\" integer) (\n" +
            "    SFUNC = \"helper schema\".\"Quoted \"\" Helper\",\n    STYPE = integer,\n    FINALFUNC_MODIFY = READ_ONLY,\n" +
            "    SSPACE = -1,\n    INITCOND = E'0',\n    SORTOP = \"Op Schema\".\"<\",\n    PARALLEL = SAFE\n);\n", sql);
        Assert.AreEqual("false", ManifestValue(compilation, "Ankus.Relocatable"));
    }

    /// <summary>
    /// Schema, custom prerequisites and generated enum types precede support DDL; final SQL waits for the aggregate.
    /// </summary>
    [TestMethod]
    public void AggregateDependenciesOrderEnumCompositeAndCustomSqlDeterministically()
    {
        const string source = """
            [assembly: Ankus.PgSql("custom-type", "CREATE TYPE pets.dog AS (name text);", Requires=new[] { "schema" })]
            [assembly: Ankus.PgSql("after", "SELECT 'installed';", Requires=new[] { "aggregate" })]
            [Ankus.PgEnum] public enum Mood { Happy, Sad }
            [Ankus.PgSchema("pets", Id="schema")]
            public static class Parent
            {
                [Ankus.PgAggregate(Id="aggregate", Requires=new[] { "custom-type", "transition" }, Before=new[] { "after" })]
                public static class Collect
                {
                    [Ankus.PgFunction(Id="transition")]
                    [return: Ankus.PgCompositeType("dog", Schema="pets")]
                    public static Ankus.PgHeapTuple? Transition(
                        [Ankus.PgCompositeType("dog", Schema="pets")] Ankus.PgHeapTuple? state, Mood? value) => state;
                }
            }
            """;
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate(source);
        AssertAggregateCompilation(compilation, diagnostics);
        string sql = ManifestValue(compilation, "Ankus.Sql");
        string[] declarations = [.. sql.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(static line => line.StartsWith("CREATE ", StringComparison.Ordinal) || line.StartsWith("SELECT ", StringComparison.Ordinal))];
        Assert.AreSequenceEqual([
            "CREATE SCHEMA IF NOT EXISTS \"pets\";",
            "CREATE TYPE \"mood\" AS ENUM (E'Happy', E'Sad');",
            "CREATE TYPE pets.dog AS (name text);",
            "CREATE FUNCTION \"pets\".\"collect_transition\"(\"state\" \"pets\".\"dog\", \"value\" \"mood\")",
            "CREATE AGGREGATE \"pets\".\"collect\"(\"value\" \"mood\") (",
            "SELECT 'installed';"], declarations);
        (Compilation repeated, ImmutableArray<Diagnostic> repeatedDiagnostics) = Generate(source);
        AssertAggregateCompilation(repeated, repeatedDiagnostics);
        Assert.AreEqual(sql, ManifestValue(repeated, "Ankus.Sql"));
        Assert.AreEqual(ManifestValue(compilation, "Ankus.NativeSource"), ManifestValue(repeated, "Ankus.NativeSource"));
    }

    /// <summary>
    /// One helper method selected for two roles is emitted once when its explicit SQL identity is shared.
    /// </summary>
    [TestMethod]
    public void AggregateSharedRoleHelperDeduplicatesSqlAndManagedCallbacks()
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            [Ankus.PgAggregate(InitialCondition="0", MovingInitialCondition="0", MovingTransition="Transition")]
            public static class Shared
            {
                [Ankus.PgFunction(Name="add_value")] public static int Transition(int state,int value)=>state+value;
                public static int? MovingInverse(int state,int value)=>state-value;
            }
            """);
        AssertAggregateCompilation(compilation, diagnostics);
        Assert.HasCount(2, compilation.GetTypeByMetadataName("Ankus.Generated.ExtensionDispatchers")!.GetMembers().OfType<IMethodSymbol>());
        string[] functions = [.. ManifestValue(compilation, "Ankus.Sql").Split('\n').Where(static line => line.StartsWith("CREATE FUNCTION", StringComparison.Ordinal))];
        Assert.AreSequenceEqual(["CREATE FUNCTION \"shared_moving_inverse\"(\"state\" integer, \"value\" integer)",
            "CREATE FUNCTION \"add_value\"(\"state\" integer, \"value\" integer)"], functions);
        Assert.Contains("SFUNC = \"add_value\",", ManifestValue(compilation, "Ankus.Sql"));
        Assert.Contains("MSFUNC = \"add_value\",", ManifestValue(compilation, "Ankus.Sql"));
    }

    /// <summary>
    /// Ordinary type conversions retain enum/composite array identity and constrained numeric conversion inside aggregate wrappers.
    /// </summary>
    /// <param name="type">The managed transition state and input.</param>
    /// <param name="annotation">A parameter and return binding where necessary.</param>
    /// <param name="sqlType">The independently expected SQL identity.</param>
    [TestMethod]
    [DataRow("Ankus.PgArray<Mood?>?", "", "\"mood\"[]")]
    [DataRow("Mood?[]?", "", "\"mood\"[]")]
    [DataRow("Ankus.PgHeapTuple?", "Ankus.PgCompositeType(\"dog\", Schema=\"pets\")", "\"pets\".\"dog\"")]
    [DataRow("Ankus.PgArray<Ankus.PgHeapTuple?>?", "Ankus.PgCompositeType(\"dog\", Schema=\"pets\")", "\"pets\".\"dog\"[]")]
    [DataRow("Ankus.PgHeapTuple?[]?", "", "record[]")]
    [DataRow("Ankus.PgNumeric?", "Ankus.PgNumericPrecision(8,2)", "numeric")]
    public void AggregateOrdinaryStatesKeepContextualTypeBindings(string type, string annotation, string sqlType)
    {
        string parameter = annotation.Length == 0 ? "" : "[" + annotation + "] ";
        string result = annotation.Length == 0 ? "" : "[return: " + annotation + "] ";
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("[Ankus.PgEnum] public enum Mood { Happy, Sad } " +
            "[Ankus.PgAggregate] public static class Typed { " + result + "public static " + type + " Transition(" + parameter + type +
            " state," + parameter + type + " value)=>state; }");
        AssertAggregateCompilation(compilation, diagnostics);
        string sql = ManifestValue(compilation, "Ankus.Sql");
        Assert.Contains("CREATE FUNCTION \"typed_transition\"(\"state\" " + sqlType + ", \"value\" " + sqlType + ")\nRETURNS " + sqlType + " AS ", sql);
        Assert.Contains("CREATE AGGREGATE \"typed\"(\"value\" " + sqlType + ") (\n    SFUNC = \"typed_transition\",\n    STYPE = " + sqlType + ",", sql);
    }

    /// <summary>
    /// PostgreSQL stores final extra/modify metadata even when the corresponding final callback is absent.
    /// </summary>
    [TestMethod]
    public void AggregateAbsentFinalPreservesExplicitCatalogFlags()
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            [Ankus.PgAggregate(FinalExtra=true, FinalModify=Ankus.PgAggregateFinalModify.Shareable,
                MovingFinalExtra=true, MovingFinalModify=Ankus.PgAggregateFinalModify.ReadWrite)]
            public static class Flags { public static int Transition(int state,int value)=>state+value; }
            """);
        AssertAggregateCompilation(compilation, diagnostics);
        Assert.EndsWith("CREATE AGGREGATE \"flags\"(\"value\" integer) (\n    SFUNC = \"flags_transition\",\n    STYPE = integer,\n" +
            "    FINALFUNC_EXTRA,\n    FINALFUNC_MODIFY = SHAREABLE,\n    MFINALFUNC_EXTRA,\n    MFINALFUNC_MODIFY = READ_WRITE,\n    PARALLEL = UNSAFE\n);\n",
            ManifestValue(compilation, "Ankus.Sql"));
    }

    /// <summary>
    /// Catalog and executor select different inputs when an ordered strict aggregate has no initial state.
    /// </summary>
    /// <param name="direct">The direct argument type used by catalog validation.</param>
    /// <param name="input">The aggregated input type used by executor seeding.</param>
    [TestMethod]
    [DataRow("int", "string")]
    [DataRow("string", "int")]
    public void AggregateOrderedStrictSeedRequiresBothCatalogAndExecutorCompatibility(string direct, string input)
        => AssertInvalidAggregate("[Ankus.PgAggregate(Kind=Ankus.PgAggregateKind.OrderedSet)] public static class Unsafe { " +
            "public static int Transition(int state," + input + " value)=>state; public static int Final(int state," + direct + " direct)=>state; }", "ANKUS012");

    /// <summary>
    /// A genuine matching ordered state supports catalog-compatible strict seeding and moving declarations.
    /// </summary>
    [TestMethod]
    public void AggregateOrderedStrictSeedWithMovingCallbacksCompiles()
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            [Ankus.PgAggregate(Kind=Ankus.PgAggregateKind.OrderedSet)] public static class Ordered
            {
                public static int Transition(int state,int value)=>state;
                public static int Final(int state,int direct)=>state;
                public static int MovingTransition(int state,int value)=>state;
                public static int? MovingInverse(int state,int value)=>state;
                public static int MovingFinal(int state,int direct)=>state;
            }
            """);
        AssertAggregateCompilation(compilation, diagnostics);
        Assert.Contains("CREATE AGGREGATE \"ordered\"(\"direct\" integer ORDER BY \"value\" integer)", ManifestValue(compilation, "Ankus.Sql"));
    }

    /// <summary>
    /// Strict initial-state seeding follows directional built-in binary coercions without treating numeric conversions as bit casts.
    /// </summary>
    /// <param name="state">The CLR state type.</param>
    /// <param name="input">The CLR input type.</param>
    /// <param name="valid">Whether PostgreSQL permits the implicit binary coercion.</param>
    [TestMethod]
    [DataRow("uint", "int", true)]
    [DataRow("Ankus.PgInet", "Ankus.PgCidr", true)]
    [DataRow("int", "uint", false)]
    [DataRow("Ankus.PgCidr", "Ankus.PgInet", false)]
    [DataRow("long", "int", false)]
    [DataRow("uint[]", "int[]", false)]
    public void AggregateStrictSeedUsesDirectionalBinaryCoercions(string state, string input, bool valid)
    {
        string source = "[Ankus.PgAggregate] public static class Seed { public static " + state + " Transition(" + state + " state," + input + " value)=>state; }";
        if (!valid)
        {
            AssertInvalidAggregate(source, "ANKUS012");
            return;
        }

        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate(source);
        AssertAggregateCompilation(compilation, diagnostics);
        string sql = ManifestValue(compilation, "Ankus.Sql");
        Assert.Contains(state == "uint" ? "STYPE = oid," : "STYPE = inet,", sql);
        Assert.DoesNotContain("INITCOND", sql);
        Assert.Contains(" PARALLEL UNSAFE STRICT ", sql);
    }

    /// <summary>
    /// Ordinary functions, aggregates and support functions all share PostgreSQL's function signature namespace.
    /// </summary>
    /// <param name="ordinary">An ordinary declaration colliding with one generated SQL entity.</param>
    [TestMethod]
    [DataRow("[Ankus.PgFunction(Name=\"sum\")] public static int A(int value)=>value;")]
    [DataRow("[Ankus.PgFunction(Name=\"sum_transition\")] public static int A(int state,int value)=>value;")]
    public void AggregateSignaturesCollideWithOrdinaryFunctions(string ordinary)
        => AssertInvalidAggregate("public static class Other { " + ordinary + " } [Ankus.PgAggregate] public static class Sum { public static int Transition(int state,int value)=>state; }", "ANKUS002");

    /// <summary>
    /// The zero-input aggregate signature collides with a zero-argument ordinary function despite different SQL declaration syntax.
    /// </summary>
    [TestMethod]
    public void AggregateZeroSignatureCollidesWithOrdinaryFunction()
        => AssertInvalidAggregate("public static class Other { [Ankus.PgFunction(Name=\"sum\")] public static int A()=>0; } " +
            "[Ankus.PgAggregate(InitialCondition=\"0\")] public static class Sum { public static int Transition(int state)=>state; }", "ANKUS002");

    /// <summary>
    /// Same names in distinct schemas stay separate, and aggregate signatures can overload by SQL input type.
    /// </summary>
    [TestMethod]
    public void AggregateNamesOverloadAcrossTypesAndSchemas()
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            [Ankus.PgAggregate(Name="same", Schema="one")] public static class A { public static int Transition(int state,int value)=>state; }
            [Ankus.PgAggregate(Name="same", Schema="two")] public static class B { public static int Transition(int state,int value)=>state; }
            [Ankus.PgAggregate(Name="same", Schema="one")] public static class C { public static long Transition(long state,long value)=>state; }
            """);
        AssertAggregateCompilation(compilation, diagnostics);
        Assert.AreSequenceEqual(["CREATE AGGREGATE \"one\".\"same\"(\"value\" integer) (", "CREATE AGGREGATE \"two\".\"same\"(\"value\" integer) (",
            "CREATE AGGREGATE \"one\".\"same\"(\"value\" bigint) ("], ManifestValue(compilation, "Ankus.Sql").Split('\n')
                .Where(static line => line.StartsWith("CREATE AGGREGATE", StringComparison.Ordinal)));
    }

    /// <summary>
    /// PostgreSQL's support state consumes one of the configured hundred argument slots.
    /// </summary>
    /// <param name="count">The number of aggregate inputs, excluding state and managed context.</param>
    /// <param name="valid">Whether the callback fits the supported native ABI.</param>
    [TestMethod]
    [DataRow(99, true)]
    [DataRow(100, false)]
    public void AggregateArgumentLimitIncludesStateButExcludesContext(int count, bool valid)
    {
        string inputs = string.Join(",", Enumerable.Range(0, count).Select(static index => "int value" + index));
        string source = "[Ankus.PgAggregate] public static class Many { public static int Transition(Ankus.PgAggregateContext context,int state," + inputs + ")=>state; }";
        if (!valid)
        {
            AssertInvalidAggregate(source, "ANKUS012");
            return;
        }

        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate(source);
        AssertAggregateCompilation(compilation, diagnostics);
        Assert.Contains(" 100, required, internal_arguments, false, false);", ManifestValue(compilation, "Ankus.NativeSource"));
    }

    /// <summary>
    /// Shared PgFunction option validation rejects unsupported aggregate helper SQL settings.
    /// </summary>
    /// <param name="option">An invalid common helper option.</param>
    [TestMethod]
    [DataRow("Rows=10")]
    [DataRow("SetMode=Ankus.PgSetMode.Materialize")]
    [DataRow("Cost=0")]
    [DataRow("NullInput=Ankus.PgNullInput.CalledOnNull")]
    [DataRow("Schema=\"\"")]
    public void AggregateSupportOptionsUseSharedDiagnostics(string option)
        => AssertInvalidAggregate("[Ankus.PgAggregate] public static class Invalid { [Ankus.PgFunction(" + option +
            ")] public static int Transition(int state,int value)=>state; }", "ANKUS004");

    /// <summary>
    /// Identifier limits measure UTF-8 bytes and apply equally to aggregate and support function names.
    /// </summary>
    /// <param name="option">The attribute receiving the identifier.</param>
    /// <param name="extra">A final character crossing the exact byte limit.</param>
    [TestMethod]
    [DataRow("aggregate", "")]
    [DataRow("aggregate", "é")]
    [DataRow("helper", "")]
    [DataRow("helper", "é")]
    public void AggregateNamesEnforceUtf8Boundary(string option, string extra)
    {
        string name = new string('é', 31) + "x" + extra;
        string source = "[Ankus.PgAggregate(" + (option == "aggregate" ? "Name=\"" + name + "\"" : "") + ")] public static class Valid { " +
            (option == "helper" ? "[Ankus.PgFunction(Name=\"" + name + "\")] " : "") + "public static int Transition(int state,int value)=>state; }";
        if (extra.Length != 0)
        {
            AssertInvalidAggregate(source, "ANKUS012");
            return;
        }

        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate(source);
        AssertAggregateCompilation(compilation, diagnostics);
        Assert.Contains((option == "aggregate" ? "CREATE AGGREGATE" : "CREATE FUNCTION") + " \"" + name + "\"(", ManifestValue(compilation, "Ankus.Sql"));
    }

    /// <summary>
    /// Missing graph references, duplicate aliases and real cycles prevent partial install scripts.
    /// </summary>
    /// <param name="attributes">The aggregate dependency configuration.</param>
    /// <param name="prefix">Additional SQL declarations establishing a conflict.</param>
    /// <param name="reason">A stable identifying fragment of the graph error.</param>
    [TestMethod]
    [DataRow("Requires=new[] { \"missing\" }", "", "missing dependency 'missing'")]
    [DataRow("Id=\"duplicate\"", "[assembly: Ankus.PgSql(\"duplicate\", \"SELECT 1;\")]", "declared more than once")]
    [DataRow("Id=\"aggregate\", Requires=new[] { \"after\" }", "[assembly: Ankus.PgSql(\"after\", \"SELECT 1;\", Requires=new[] { \"aggregate\" })]", "cycle")]
    [DataRow("Id=\"\"", "", "Dependency identifiers")]
    public void AggregateDependencyFailuresPreventManifest(string attributes, string prefix, string reason)
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate(prefix + " [Ankus.PgAggregate(" + attributes +
            ")] public static class Invalid { public static int Transition(int state,int value)=>state; }");
        Assert.IsNotEmpty(diagnostics);
        Assert.IsTrue(diagnostics.All(static diagnostic => diagnostic.Id == "ANKUS005" && diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.IsTrue(diagnostics.Any(diagnostic => diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture).Contains(reason, StringComparison.OrdinalIgnoreCase)));
        Assert.IsFalse(compilation.Assembly.GetAttributes().Any(static attribute => attribute.AttributeClass?.ToDisplayString() == "System.Reflection.AssemblyMetadataAttribute" &&
            attribute.ConstructorArguments[0].Value as string == "Ankus.Sql"));
        Assert.IsEmpty(compilation.GetDiagnostics(context.CancellationToken).Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
    }

    /// <summary>
    /// Equal SQL internal spelling never masks different concrete managed payload types.
    /// </summary>
    [TestMethod]
    public void AggregateInternalPayloadMismatchIsDiagnosed()
        => AssertInvalidAggregate("""
            [Ankus.PgAggregate] public static class Invalid
            {
                public static Ankus.PgAggregateState<string>? Transition(Ankus.PgAggregateState<int>? state,int value)=>null;
                public static int Final(Ankus.PgAggregateState<string>? state)=>0;
            }
            """, "ANKUS012");

    /// <summary>
    /// Nested payload nullability survives generated state reads and variable declarations without nullable warnings.
    /// </summary>
    /// <param name="payload">The closed payload type containing annotated reference members.</param>
    [TestMethod]
    [DataRow("System.Collections.Generic.List<string?>")]
    [DataRow("System.Collections.Generic.List<(string? Text,int? Number)>")]
    [DataRow("System.Collections.Generic.Dictionary<string,System.Collections.Generic.List<string?>?>")]
    public void AggregateInternalPayloadPreservesNestedNullableAnnotations(string payload)
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("[Ankus.PgAggregate] public static class NullablePayload { " +
            "public static Ankus.PgAggregateState<" + payload + "> Transition(Ankus.PgAggregateState<" + payload + ">? state,int value)=>state ?? new(new()); " +
            "public static int Final(Ankus.PgAggregateState<" + payload + ">? state)=>0; }");
        AssertAggregateCompilation(compilation, diagnostics);
        Assert.IsEmpty(compilation.GetDiagnostics(context.CancellationToken).Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning));
        string generated = AggregateCallback(compilation, "transition").DeclaringSyntaxReferences.Single().GetSyntax(context.CancellationToken).ToString();
        Assert.Contains("string?", generated);
    }

    /// <summary>
    /// Aggregate-only internal state remains unavailable to ordinary SQL function declarations.
    /// </summary>
    [TestMethod]
    public void AggregateStateDoesNotBecomeOrdinarySqlType()
        => AssertInvalidAggregate("public static class Invalid { [Ankus.PgFunction] public static Ankus.PgAggregateState<int>? Echo(Ankus.PgAggregateState<int>? state)=>state; }", "ANKUS001");

    /// <summary>
    /// Distinct direct and aggregated parameters must not emit duplicate SQL names across their separate callbacks.
    /// </summary>
    [TestMethod]
    public void AggregateDirectAndAggregatedNamesMustBeDistinct()
        => AssertInvalidAggregate("""
            [Ankus.PgAggregate(Kind=Ankus.PgAggregateKind.OrderedSet, InitialCondition="0")]
            public static class Invalid
            {
                public static int Transition(int state,int value)=>state;
                public static int Final(int state,int value)=>state;
            }
            """, "ANKUS012");

    /// <summary>
    /// Requires clean generator diagnostics, warning-free consumer compilation, and actual managed assembly emission.
    /// </summary>
    private void AssertAggregateCompilation(Compilation compilation, ImmutableArray<Diagnostic> diagnostics)
    {
        Assert.IsEmpty(diagnostics);
        Assert.IsEmpty(compilation.GetDiagnostics(context.CancellationToken).Where(static diagnostic => diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        using var assembly = new MemoryStream();
        Assert.IsTrue(compilation.Emit(assembly, cancellationToken: context.CancellationToken).Success);
    }

    /// <summary>
    /// Requires the dedicated error against consumer source that remains valid C#.
    /// </summary>
    private void AssertInvalidAggregate(string source, string expected)
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate(source);
        Diagnostic error = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual(expected, error.Id);
        Assert.AreEqual(DiagnosticSeverity.Error, error.Severity);
        Assert.IsEmpty(compilation.GetDiagnostics(context.CancellationToken).Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
    }

    /// <summary>
    /// Resolves the aggregate-specific generated callback without depending on the assembly hash.
    /// </summary>
    private static IMethodSymbol AggregateCallback(Compilation compilation, string role)
        => Assert.ContainsSingle(compilation.GetTypeByMetadataName("Ankus.Generated.ExtensionDispatchers")!.GetMembers().OfType<IMethodSymbol>()
            .Where(method => method.Name.EndsWith("_aggregate_" + role, StringComparison.Ordinal)));
}
