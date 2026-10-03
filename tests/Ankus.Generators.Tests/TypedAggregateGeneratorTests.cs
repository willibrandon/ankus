using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Ankus.Generators.Tests;

public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Compiler-resolved capabilities retain the same SQL contract across ordinary C# implementation containers.
    /// </summary>
    /// <param name="source">The implementation and attributed concrete container.</param>
    [TestMethod]
    [DataRow("""
        [Ankus.PgAggregate(InitialCondition="0")]
        public readonly struct Described : Ankus.IPgAggregate<long,int>
        {
            public static long Transition(Ankus.PgAggregateContext context,long state,int input) => state + input;
        }
        """)]
    [DataRow("""
        public abstract class Base<T> : Ankus.IPgAggregate<T,int> where T : System.Numerics.INumber<T>
        {
            public static T Transition(Ankus.PgAggregateContext context,T state,int input) => state + T.CreateChecked(input);
        }
        [Ankus.PgAggregate(InitialCondition="0")] public sealed class Described : Base<long> { }
        """)]
    [DataRow("""
        public interface IDefaultAggregate : Ankus.IPgAggregate<long,int>
        {
            static long Ankus.IPgAggregate<long,int>.Transition(Ankus.PgAggregateContext context,long state,int input) => state + input;
        }
        [Ankus.PgAggregate(InitialCondition="0")] public sealed class Described : IDefaultAggregate { }
        """)]
    [DataRow("""
        [Ankus.PgAggregate(InitialCondition="0")]
        public ref struct Described : Ankus.IPgAggregate<long,int>
        {
            public static long Transition(Ankus.PgAggregateContext context,long state,int input) => state + input;
        }
        """)]
    public void TypedAggregateContainersUseCompilerResolvedCapabilities(string source)
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate(source);
        AssertAggregateCompilation(compilation, diagnostics);
        Assert.Contains("CREATE AGGREGATE \"described\"(\"input\" integer)", InstallationBody(compilation));
        Assert.Contains("STYPE = bigint", InstallationBody(compilation));
    }

    /// <summary>
    /// Both implicit and private explicit implementations compile through the static interface contract.
    /// </summary>
    /// <param name="declaration">The implementation spelling.</param>
    [TestMethod]
    [DataRow("public static long Transition")]
    [DataRow("static long Ankus.IPgAggregate<long, int>.Transition")]
    public void TypedAggregateScalarEmitsExactSqlAndCompiles(string declaration)
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            [Ankus.PgAggregate(InitialCondition = "0")]
            public sealed class TypedTotal : Ankus.IPgAggregate<long, int>
            {
            """ + declaration + "(Ankus.PgAggregateContext context, long state, int input) => checked(state + input); }");
        AssertAggregateCompilation(compilation, diagnostics);
        string nativeName = AggregateCallback(compilation, "transition").Name.Replace("ankus_managed_", "ankus_fn_", StringComparison.Ordinal);
        Assert.AreEqual($"CREATE FUNCTION \"typed_total_transition\"(\"state\" bigint, \"input\" integer)\n" +
            $"RETURNS bigint AS 'MODULE_PATHNAME', '{nativeName}' LANGUAGE c VOLATILE PARALLEL UNSAFE STRICT SECURITY INVOKER NOT LEAKPROOF COST 1;\n" +
            "CREATE AGGREGATE \"typed_total\"(\"input\" integer) (\n    SFUNC = \"typed_total_transition\",\n    STYPE = bigint,\n" +
            "    FINALFUNC_MODIFY = READ_ONLY,\n    INITCOND = E'0',\n    PARALLEL = UNSAFE\n);\n",
            InstallationBody(compilation).ReplaceLineEndings("\n"));
    }

    /// <summary>
    /// Tuple element names, SQL types and nullability survive flattening and reconstruction, including the eighth element.
    /// </summary>
    /// <param name="arguments">The grouped managed input type.</param>
    /// <param name="sqlArguments">The exact flattened SQL arguments.</param>
    [TestMethod]
    [DataRow("(int count, string? label)", "\"count\" integer, \"label\" text")]
    [DataRow("(int a, int b, int c, int d, int e, int f, int g, string? last)",
        "\"a\" integer, \"b\" integer, \"c\" integer, \"d\" integer, \"e\" integer, \"f\" integer, \"g\" integer, \"last\" text")]
    public void TypedAggregateTupleInputsRemainSeparateSqlValues(string arguments, string sqlArguments)
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate(
            "[Ankus.PgAggregate(InitialCondition=\"0\")] public sealed class Grouped : Ankus.IPgAggregate<long, " + arguments + "> { " +
            "public static long Transition(Ankus.PgAggregateContext context, long state, " + arguments + " values) => state; }");
        AssertAggregateCompilation(compilation, diagnostics);
        string sql = InstallationBody(compilation);
        Assert.Contains("CREATE FUNCTION \"grouped_transition\"(\"state\" bigint, " + sqlArguments + ")", sql);
        Assert.Contains("CREATE AGGREGATE \"grouped\"(" + sqlArguments + ")", sql);
        Assert.Contains("CALLED ON NULL INPUT", sql);
    }

    /// <summary>
    /// Empty groups consume no SQL slots, and arrays retain one array-valued input.
    /// </summary>
    /// <param name="arguments">The CLR argument type.</param>
    /// <param name="signature">The expected SQL aggregate signature.</param>
    [TestMethod]
    [DataRow("System.ValueTuple", "*")]
    [DataRow("int[]", "\"values\" integer[]")]
    public void TypedAggregateEmptyAndArrayInputsKeepTheirShape(string arguments, string signature)
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate(
            "[Ankus.PgAggregate(InitialCondition=\"0\")] public sealed class Shape : Ankus.IPgAggregate<long, " + arguments + "> { " +
            "public static long Transition(Ankus.PgAggregateContext context, long state, " + arguments + " values) => state; }");
        AssertAggregateCompilation(compilation, diagnostics);
        Assert.Contains("CREATE AGGREGATE \"shape\"(" + signature + ")", InstallationBody(compilation));
    }

    /// <summary>
    /// An explicit one-element ValueTuple represents one SQL input and reconstructs the tuple constructor.
    /// </summary>
    [TestMethod]
    public void TypedAggregateSingleTupleInputCompiles()
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            [Ankus.PgAggregate(InitialCondition="0")]
            public sealed class Single : Ankus.IPgAggregate<long,System.ValueTuple<int>>
            {
                public static long Transition(Ankus.PgAggregateContext context,long state,System.ValueTuple<int> arguments) => state + arguments.Item1;
            }
            """);
        AssertAggregateCompilation(compilation, diagnostics);
        Assert.Contains("CREATE AGGREGATE \"single\"(\"item1\" integer)", InstallationBody(compilation));
    }

    /// <summary>
    /// Nullable interface results remain nullable even when an implementation promises a stronger result.
    /// </summary>
    [TestMethod]
    public void TypedAggregateUsesInterfaceReturnNullability()
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            [Ankus.PgAggregate]
            public sealed class TextTotal : Ankus.IPgAggregate<string?, string?>
            {
                public static string Transition(Ankus.PgAggregateContext context, string? state, string? input)
                    => (state ?? "") + (input ?? "");
            }
            """);
        AssertAggregateCompilation(compilation, diagnostics);
        Assert.Contains("CALLED ON NULL INPUT", InstallationBody(compilation));
        string generated = AggregateCallback(compilation, "transition").DeclaringSyntaxReferences.Single().GetSyntax(context.CancellationToken).ToString();
        Assert.Contains("string? value =", generated);
        Assert.Contains("result->IsNull = 1;", generated);
    }

    /// <summary>
    /// Inherited explicit methods are resolved semantically and receive distinct native entry points for each container.
    /// </summary>
    [TestMethod]
    public void TypedAggregateInheritedImplementationsHaveDistinctCallbacks()
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            public abstract class TotalBase : Ankus.IPgAggregate<long, int>
            {
                static long Ankus.IPgAggregate<long, int>.Transition(Ankus.PgAggregateContext context, long state, int input) => state + input;
            }
            [Ankus.PgAggregate(InitialCondition = "0")] public sealed class FirstTotal : TotalBase { }
            [Ankus.PgAggregate(InitialCondition = "0")] public sealed class SecondTotal : TotalBase { }
            """);
        AssertAggregateCompilation(compilation, diagnostics);
        string[] exports = ManifestValue(compilation, "Ankus.Exports").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.HasCount(5, exports);
        Assert.HasCount(5, exports.Distinct(StringComparer.Ordinal));
        string sql = InstallationBody(compilation);
        Assert.Contains("CREATE FUNCTION \"first_total_transition\"", sql);
        Assert.Contains("CREATE FUNCTION \"second_total_transition\"", sql);
    }

    /// <summary>
    /// All optional capabilities are emitted with their actual SQL signatures and static-interface dispatch.
    /// </summary>
    [TestMethod]
    public void TypedAggregateOptionalCapabilitiesCompileAndRetainSqlRoles()
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            [Ankus.PgAggregate(ParallelSafety = Ankus.PgParallelSafety.Safe)]
            public sealed class Capabilities :
                Ankus.IPgAggregate<Ankus.PgAggregateState<long>?, int?>,
                Ankus.IPgFinalizingAggregate<Ankus.PgAggregateState<long>?, System.ValueTuple, long>,
                Ankus.IPgCombinableAggregate<Ankus.PgAggregateState<long>?>,
                Ankus.IPgSerializableAggregate<Ankus.PgAggregateState<long>>,
                Ankus.IPgMovingAggregate<Ankus.PgAggregateState<long>?, int?>,
                Ankus.IPgMovingFinalizingAggregate<Ankus.PgAggregateState<long>?, System.ValueTuple, long>
            {
                public static Ankus.PgAggregateState<long>? Transition(Ankus.PgAggregateContext context, Ankus.PgAggregateState<long>? state, int? input) => new((state?.Value ?? 0) + (input ?? 0));
                public static long Final(Ankus.PgAggregateContext context, Ankus.PgAggregateState<long>? state, System.ValueTuple direct) => state?.Value ?? 0;
                public static Ankus.PgAggregateState<long>? Combine(Ankus.PgAggregateContext context, Ankus.PgAggregateState<long>? state, Ankus.PgAggregateState<long>? other) => new((state?.Value ?? 0) + (other?.Value ?? 0));
                public static byte[] Serialize(Ankus.PgAggregateContext context, Ankus.PgAggregateState<long> state) => System.BitConverter.GetBytes(state.Value);
                public static Ankus.PgAggregateState<long> Deserialize(Ankus.PgAggregateContext context, byte[] bytes) => new(System.BitConverter.ToInt64(bytes));
                public static Ankus.PgAggregateState<long>? MovingTransition(Ankus.PgAggregateContext context, Ankus.PgAggregateState<long>? state, int? input) => Transition(context, state, input);
                public static Ankus.PgAggregateState<long>? MovingInverse(Ankus.PgAggregateContext context, Ankus.PgAggregateState<long>? state, int? input) => new((state?.Value ?? 0) - (input ?? 0));
                public static long MovingFinal(Ankus.PgAggregateContext context, Ankus.PgAggregateState<long>? state, System.ValueTuple direct) => Final(context, state, direct);
            }
            """);
        AssertAggregateCompilation(compilation, diagnostics);
        string sql = InstallationBody(compilation);
        Assert.Contains("CREATE FUNCTION \"capabilities_deserialize\"(\"bytes\" bytea, internal)", sql);
        foreach ((string option, string role) in new (string, string)[]
        {
            ("SFUNC", "transition"), ("FINALFUNC", "final"), ("COMBINEFUNC", "combine"),
            ("SERIALFUNC", "serialize"), ("DESERIALFUNC", "deserialize"), ("MSFUNC", "moving_transition"),
            ("MINVFUNC", "moving_inverse"), ("MFINALFUNC", "moving_final"),
        })
        {
            Assert.Contains(option + " = \"capabilities_" + role + "\"", sql);
        }
    }

    /// <summary>
    /// Typed direct arguments and SQL-only extra slots preserve ordered and hypothetical aggregate signatures.
    /// </summary>
    /// <param name="kind">The ordered aggregate kind.</param>
    [TestMethod]
    [DataRow("OrderedSet")]
    [DataRow("HypotheticalSet")]
    public void TypedAggregateDirectAndExtraArgumentsCompile(string kind)
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            [Ankus.PgAggregate(Kind = Ankus.PgAggregateKind.
            """ + kind + """
            , InitialCondition = "0", FinalExtra = true)]
            public sealed class RankValues : Ankus.IPgAggregate<int, int>, Ankus.IPgFinalizingAggregate<int, int, long>
            {
                public static int Transition(Ankus.PgAggregateContext context, int state, int input) => state + input;
                public static long Final(Ankus.PgAggregateContext context, int state, int hypothetical) => state + hypothetical;
            }
            """);
        AssertAggregateCompilation(compilation, diagnostics);
        string sql = InstallationBody(compilation);
        Assert.Contains("CREATE FUNCTION \"rank_values_final\"(\"state\" integer, \"hypothetical\" integer, \"__ankus_extra_1\" integer)", sql);
        Assert.Contains("CREATE AGGREGATE \"rank_values\"(\"hypothetical\" integer ORDER BY \"input\" integer)", sql);
        Assert.Contains("FINALFUNC_EXTRA", sql);
        string generated = AggregateCallback(compilation, "final").DeclaringSyntaxReferences.Single().GetSyntax(context.CancellationToken).ToString();
        Assert.DoesNotContain("arguments[2]", generated);
    }

    /// <summary>
    /// A missing or mistyped mandatory transition is rejected by the C# compiler as well as aggregate validation.
    /// </summary>
    /// <param name="method">The absent or mistyped implementation.</param>
    [TestMethod]
    [DataRow("")]
    [DataRow("public static long Transition(Ankus.PgAggregateContext context, long state, string input) => state;")]
    public void TypedAggregateCompilerRejectsMissingTransition(string method)
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate(
            "[Ankus.PgAggregate(InitialCondition=\"0\")] public sealed class Broken : Ankus.IPgAggregate<long,int> { " + method + " }");
        Assert.AreEqual("ANKUS112", Assert.ContainsSingle(diagnostics).Id);
        ImmutableArray<Diagnostic> compilerDiagnostics = compilation.GetDiagnostics(context.CancellationToken);
        Assert.AreEqual("CS0535", Assert.ContainsSingle(compilerDiagnostics.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)).Id);
    }

    /// <summary>
    /// A type-safe optional method still has to match the aggregate's state and input SQL contracts.
    /// </summary>
    [TestMethod]
    public void TypedAggregateRejectsMismatchedCombineState()
        => AssertInvalidAggregate("""
            [Ankus.PgAggregate(InitialCondition = "0")]
            public sealed class WrongState : Ankus.IPgAggregate<long, int>, Ankus.IPgCombinableAggregate<int>
            {
                public static long Transition(Ankus.PgAggregateContext context, long state, int input) => state + input;
                public static int Combine(Ankus.PgAggregateContext context, int state, int other) => state + other;
            }
            """, "ANKUS100");

    /// <summary>
    /// Typed declarations reject ambiguous capability sets and conventional fallback rather than guessing a callback.
    /// </summary>
    /// <param name="source">A valid C# type with an invalid PostgreSQL aggregate contract.</param>
    /// <param name="diagnostic">The specific PostgreSQL contract diagnostic.</param>
    [TestMethod]
    [DataRow("""
        [Ankus.PgAggregate(InitialCondition="0")] public sealed class Ambiguous : Ankus.IPgAggregate<int,int>,Ankus.IPgAggregate<long,int>
        {
            static int Ankus.IPgAggregate<int,int>.Transition(Ankus.PgAggregateContext context,int state,int arguments) => state;
            static long Ankus.IPgAggregate<long,int>.Transition(Ankus.PgAggregateContext context,long state,int arguments) => state;
        }
        """, "ANKUS110")]
    [DataRow("""
        [Ankus.PgAggregate(InitialCondition="0")] public sealed class Uncontracted : Ankus.IPgAggregate<int,int>
        {
            public static int Transition(Ankus.PgAggregateContext context,int state,int arguments) => state;
            public static int Combine(int state,int other) => state + other;
        }
        """, "ANKUS111")]
    public void TypedAggregateRejectsAmbiguousOrUncontractedRoles(string source, string diagnostic)
        => AssertInvalidAggregate(source, diagnostic);

    /// <summary>
    /// Grouped reference values require explicit SQL nullability just like ordinary function arguments.
    /// </summary>
    [TestMethod]
    public void TypedAggregateRejectsObliviousTupleElement()
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            #nullable disable
            [Ankus.PgAggregate(InitialCondition="0")]
            public sealed class AmbiguousNull : Ankus.IPgAggregate<long,(int count,string text)>
            {
                public static long Transition(Ankus.PgAggregateContext context,long state,(int count,string text) arguments) => state;
            }
            """);
        Diagnostic error = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual("ANKUS024", error.Id);
        Assert.IsTrue(error.Location.IsInSource);
        Assert.Contains("string", error.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
        Assert.IsEmpty(compilation.GetDiagnostics(context.CancellationToken).Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
    }

    /// <summary>
    /// A polymorphic final result receives its input type through native extra slots without adding managed parameters.
    /// </summary>
    [TestMethod]
    public void TypedAggregatePolymorphicExtraSlotRetainsTypeIdentity()
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            [Ankus.PgAggregate(FinalExtra=true)]
            public sealed class FirstStored : Ankus.IPgAggregate<Ankus.PgAggregateState<Ankus.PgAnyElement>?,Ankus.PgAnyElement?>,
                Ankus.IPgFinalizingAggregate<Ankus.PgAggregateState<Ankus.PgAnyElement>?,System.ValueTuple,Ankus.PgAnyElement?>
            {
                public static Ankus.PgAggregateState<Ankus.PgAnyElement>? Transition(Ankus.PgAggregateContext context,
                    Ankus.PgAggregateState<Ankus.PgAnyElement>? state,Ankus.PgAnyElement? input)
                    => state ?? (input is null ? null : new(input.CopyTo(context.MemoryContext)));
                public static Ankus.PgAnyElement? Final(Ankus.PgAggregateContext context,
                    Ankus.PgAggregateState<Ankus.PgAnyElement>? state,System.ValueTuple arguments) => state?.Value;
            }
            """);
        AssertAggregateCompilation(compilation, diagnostics);
        Assert.Contains("CREATE FUNCTION \"first_stored_final\"(\"state\" internal, \"__ankus_extra_1\" anyelement)", InstallationBody(compilation));
        string native = ManifestValue(compilation, "Ankus.NativeSource");
        Assert.Contains("const bool polymorphic[2] = {false, true};", native);
        Assert.Contains("const bool required[2] = {false, false};", native);
        string managed = AggregateCallback(compilation, "final").DeclaringSyntaxReferences.Single().GetSyntax(context.CancellationToken).ToString();
        Assert.DoesNotContain("arguments[1]", managed);
    }

    /// <summary>
    /// Each tuple element independently selects a SQL name and numeric boundary constraint.
    /// </summary>
    [TestMethod]
    public void TypedAggregateTupleMetadataRetainsNamesAndNumericConstraints()
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            [Ankus.PgAggregate(InitialCondition="0")]
            public sealed class Amounts : Ankus.IPgAggregate<long,(decimal Amount,Ankus.PgNumeric Fee)>
            {
                public static long Transition(Ankus.PgAggregateContext context,long state,
                    [Ankus.PgParameter(Element="Amount",Name="price")]
                    [Ankus.PgParameter(Element="Fee",Name="charge")]
                    [Ankus.PgNumericPrecision(5,2,Element="Amount")]
                    [Ankus.PgNumericPrecision(6,3,Element="Fee")]
                    (decimal Amount,Ankus.PgNumeric Fee) arguments) => state;
            }
            """);
        AssertAggregateCompilation(compilation, diagnostics);
        Assert.Contains("CREATE AGGREGATE \"amounts\"(\"price\" numeric, \"charge\" numeric)", InstallationBody(compilation));
        string generated = AggregateCallback(compilation, "transition").DeclaringSyntaxReferences.Single().GetSyntax(context.CancellationToken).ToString();
        Assert.Contains("arguments[1].ReadNumeric().Rescale(5, 2)", generated);
        Assert.Contains("arguments[2].ReadNumeric().Rescale(6, 3)", generated);
    }

    /// <summary>
    /// Named raw and composite SQL identities attach to their selected tuple elements without changing managed grouping.
    /// </summary>
    [TestMethod]
    public void TypedAggregateTupleBindingsRetainRawAndCompositeIdentities()
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            [Ankus.PgAggregate(InitialCondition="0")]
            public sealed class BoundValues : Ankus.IPgAggregate<long,(Ankus.PgDatum Raw,Ankus.PgHeapTuple Row)>
            {
                public static long Transition(Ankus.PgAggregateContext context,long state,
                    [Ankus.PgSqlType("int4",Schema="pg_catalog",Element="Raw")]
                    [Ankus.PgCompositeType("item",Schema="app",Element="Row")]
                    (Ankus.PgDatum Raw,Ankus.PgHeapTuple Row) arguments) => state;
            }
            """);
        AssertAggregateCompilation(compilation, diagnostics);
        Assert.Contains("CREATE AGGREGATE \"bound_values\"(\"raw\" \"pg_catalog\".\"int4\", \"row\" \"app\".\"item\")", InstallationBody(compilation));
    }

    /// <summary>
    /// A selected trailing vector is variadic while ordinary array input remains a single SQL array.
    /// </summary>
    /// <param name="variadic">Whether the final tuple element collects SQL arguments.</param>
    /// <param name="prefix">The expected declaration modifier.</param>
    [TestMethod]
    [DataRow(false, "")]
    [DataRow(true, "VARIADIC ")]
    public void TypedAggregateTupleVariadicInputIsExplicit(bool variadic, string prefix)
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            [Ankus.PgAggregate(InitialCondition="0")]
            public sealed class ManyValues : Ankus.IPgAggregate<long,(int Head,int?[] Tail)>
            {
                public static long Transition(Ankus.PgAggregateContext context,long state,
                    [Ankus.PgParameter(Element="Tail",Variadic=
            """ + (variadic ? "true" : "false") + """
                    )] (int Head,int?[] Tail) arguments) => state;
            }
            """);
        AssertAggregateCompilation(compilation, diagnostics);
        string sql = InstallationBody(compilation);
        Assert.Contains("CREATE FUNCTION \"many_values_transition\"(\"state\" bigint, \"head\" integer, " + prefix + "\"tail\" integer[])", sql);
        Assert.Contains("CREATE AGGREGATE \"many_values\"(\"head\" integer, " + prefix + "\"tail\" integer[])", sql);
    }

    /// <summary>
    /// Missing, misspelled or conflicting element selections fail without silently dropping metadata.
    /// </summary>
    /// <param name="attributes">The invalid metadata on a numeric input group.</param>
    /// <param name="diagnostic">The expected declaration diagnostic.</param>
    [TestMethod]
    [DataRow("[Ankus.PgParameter(Name=\"cost\")]", "ANKUS118")]
    [DataRow("[Ankus.PgParameter(Element=\"missing\",Name=\"cost\")]", "ANKUS118")]
    [DataRow("[Ankus.PgParameter(Element=\"Amount\",Name=\"cost\"),Ankus.PgParameter(Element=\"Amount\",Name=\"price\")]", "ANKUS126")]
    [DataRow("[Ankus.PgParameter(Element=\"Amount\",Default=\"0\")]", "ANKUS127")]
    [DataRow("[Ankus.PgNumericPrecision(5,2,Element=\"Amount\"),Ankus.PgNumericPrecision(6,3,Element=\"Amount\")]", "ANKUS003")]
    [DataRow("[Ankus.PgNumericPrecision(0,2,Element=\"Amount\")]", "ANKUS003")]
    [DataRow("[Ankus.PgNumericPrecision(5,2,Element=\"Count\")]", "ANKUS003")]
    [DataRow("[Ankus.PgSqlType(\"numeric\",Element=\"Amount\")]", "ANKUS016")]
    [DataRow("[Ankus.PgParameter(Element=\"Amount\",Variadic=true)]", "ANKUS103")]
    public void TypedAggregateTupleMetadataRejectsInvalidSelections(string attributes, string diagnostic)
        => AssertInvalidAggregate("[Ankus.PgAggregate(InitialCondition=\"0\")] public sealed class Invalid : Ankus.IPgAggregate<long,(decimal Amount,int Count)> { " +
            "public static long Transition(Ankus.PgAggregateContext context,long state," + attributes + "(decimal Amount,int Count) arguments) => state; }", diagnostic);

    /// <summary>
    /// Aggregate-only selectors and duplicate scalar metadata cannot silently change ordinary SQL functions.
    /// </summary>
    /// <param name="parameter">The scalar parameter with invalid metadata.</param>
    /// <param name="diagnostic">The expected declaration error.</param>
    [TestMethod]
    [DataRow("[Ankus.PgParameter(Element=\"missing\")] int value", "ANKUS057")]
    [DataRow("[Ankus.PgParameter(Variadic=true)] int[] value", "ANKUS057")]
    [DataRow("[Ankus.PgParameter(Name=\"first\"),Ankus.PgParameter(Name=\"second\")] int value", "ANKUS057")]
    [DataRow("[Ankus.PgNumericPrecision(5,2,Element=\"missing\")] decimal value", "ANKUS003")]
    [DataRow("[Ankus.PgNumericPrecision(5,2),Ankus.PgNumericPrecision(6,3)] decimal value", "ANKUS003")]
    [DataRow("[Ankus.PgSqlType(\"int4\",Element=\"missing\")] Ankus.PgDatum value", "ANKUS016")]
    [DataRow("[Ankus.PgCompositeType(\"item\",Element=\"missing\")] Ankus.PgHeapTuple value", "ANKUS009")]
    public void TypedAggregateMetadataCannotLeakIntoScalarFunctions(string parameter, string diagnostic)
        => AssertInvalidAggregate("public static class Invalid { [Ankus.PgFunction] public static int Value(" + parameter + ") => 0; }", diagnostic);

    /// <summary>
    /// FinalExtra generated names remain valid and distinct even when an input name is maximal or a direct argument uses the default extra name.
    /// </summary>
    [TestMethod]
    public void TypedAggregateExtraSlotNamesAvoidLengthAndCollisionErrors()
    {
        string longName = new('a', 63);
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            [Ankus.PgAggregate(Kind=Ankus.PgAggregateKind.OrderedSet,InitialCondition="0",FinalExtra=true)]
            public sealed class ExtraNames : Ankus.IPgAggregate<int,int>,Ankus.IPgFinalizingAggregate<int,int,int>
            {
                public static int Transition(Ankus.PgAggregateContext context,int state,[Ankus.PgParameter(Name="
            """ + longName + """
            ")] int input) => state;
                public static int Final(Ankus.PgAggregateContext context,int state,[Ankus.PgParameter(Name="__ankus_extra_1")] int direct) => state;
            }
            """);
        AssertAggregateCompilation(compilation, diagnostics);
        Assert.Contains("CREATE FUNCTION \"extra_names_final\"(\"state\" integer, \"__ankus_extra_1\" integer, \"__ankus_extra_2\" integer)",
            InstallationBody(compilation));
    }
}
