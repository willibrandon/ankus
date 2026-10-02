using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using Ankus.PgConfig;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Ankus.Generators.Tests;

/// <summary>
/// Verifies SQL discovery and exact invocation of C# operator and conversion declarations.
/// </summary>
public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Operator syntax produces the same owned backing function and dependency graph as ordinary methods.
    /// </summary>
    /// <param name="conversion">Whether to declare a conversion instead of binary arithmetic.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CSharpOperatorsDeclareBackingSql(bool conversion)
    {
        string method = conversion
            ? "[Ankus.PgCast] public static implicit operator int(Metric value) => value.Value;"
            : "[Ankus.PgOperator(\"@+\")] public static Metric operator +(Metric left, Metric right) => new(left.Value + right.Value);";
        CSharpCompilation initial = ModuleCompilation("[Ankus.PgType] public readonly record struct Metric(int Value) { " + method + " }");
        RunModule(ModuleDriver(), initial, out Compilation output);
        ExtensionSchemaGraph graph = ExtensionSchemaGraph.Parse(ManifestValue(output, "Ankus.SqlGraph"));
        ExtensionSchemaItem function = Assert.ContainsSingle(graph.Items.Where(item => item.Kind == "function" &&
            item.Names.Contains(conversion ? "op_implicit" : "op_addition")));
        ExtensionSchemaItem attached = Assert.ContainsSingle(graph.Items.Where(item => item.Kind == (conversion ? "cast" : "operator")));

        Assert.AreEqual(function.Id, Assert.ContainsSingle(attached.Dependencies));
        Assert.Contains(conversion ? "CREATE CAST" : "CREATE OPERATOR @+", InstallationBody(output));
        Assert.Contains("global::System.Runtime.CompilerServices.UnsafeAccessor", output.SyntaxTrees.Single(static tree =>
            tree.FilePath.EndsWith("ExtensionDispatchers.g.cs", StringComparison.Ordinal)).ToString());
    }

    /// <summary>
    /// The generated bridge calls the attributed method exactly, including checked and boolean operator forms.
    /// </summary>
    /// <param name="declaration">The attributed callable and any companion required by C#.</param>
    /// <param name="expected">The independently specified invocation result.</param>
    /// <param name="reference">Whether the owner is a reference type rather than a value type.</param>
    [TestMethod]
    [DataRow("[Ankus.PgFunction(Name = \"value\")] public static Metric operator +(Metric left, Metric right) => new(left.Value + right.Value);", 42L, false)]
    [DataRow("[Ankus.PgFunction(Name = \"value\")] public static Metric operator +(Metric left, Metric right) => new(left.Value + right.Value);", 42L, true)]
    [DataRow("public static Metric operator +(Metric left, Metric right) => new(-1); [Ankus.PgFunction(Name = \"value\")] public static Metric operator checked +(Metric left, Metric right) => new(left.Value + right.Value + 100);", 142L, false)]
    [DataRow("[Ankus.PgFunction(Name = \"value\")] public static Metric operator -(Metric value) => new(-value.Value);", -20L, false)]
    [DataRow("[Ankus.PgFunction(Name = \"value\")] public static bool operator true(Metric value) => value.Value > 0; public static bool operator false(Metric value) => value.Value <= 0;", 1L, false)]
    [DataRow("public static bool operator true(Metric value) => value.Value > 0; [Ankus.PgFunction(Name = \"value\")] public static bool operator false(Metric value) => value.Value <= 0;", 0L, false)]
    [DataRow("[Ankus.PgFunction(Name = \"value\")] public static implicit operator int(Metric value) => value.Value;", 20L, false)]
    [DataRow("[Ankus.PgFunction(Name = \"value\")] public static explicit operator long(Metric value) => value.Value + 100L;", 120L, true)]
    [DataRow("public static explicit operator int(Metric value) => -1; [Ankus.PgFunction(Name = \"value\")] public static explicit operator checked int(Metric value) => value.Value + 200;", 220L, false)]
    [DataRow("[Ankus.PgFunction(Name = \"value\")] public static implicit operator Metric(int value) => new(value);", 42L, false)]
    public void CSharpOperatorsInvokeExactMethod(string declaration, long expected, bool reference)
    {
        CSharpCompilation initial = ModuleCompilation("[Ankus.PgType] public " +
            (reference ? "sealed record" : "readonly record struct") + " Metric(int Value) { " + declaration + " }");
        RunModule(ModuleDriver(), initial, out Compilation output);

        Assert.AreEqual(expected, InvokeCSharpOperator(output, "value"));
    }

    /// <summary>
    /// Conversion overloads with identical inputs retain different exact result signatures and callback identities.
    /// </summary>
    [TestMethod]
    public void CSharpConversionsRetainResultOverloadIdentity()
    {
        CSharpCompilation initial = ModuleCompilation("""
            [Ankus.PgType]
            public readonly record struct Metric(int Value)
            {
                [Ankus.PgCast]
                [Ankus.PgFunction(Name = "integer_value")]
                public static explicit operator int(Metric value) => value.Value;

                [Ankus.PgCast]
                [Ankus.PgFunction(Name = "bigint_value")]
                public static explicit operator long(Metric value) => value.Value + 100L;
            }
            """);
        RunModule(ModuleDriver(), initial, out Compilation output);
        ExtensionSchemaGraph graph = ExtensionSchemaGraph.Parse(ManifestValue(output, "Ankus.SqlGraph"));
        ExtensionSchemaItem[] functions = [.. graph.Items.Where(item => item.Kind == "function" &&
            (item.Names.Contains("integer_value") || item.Names.Contains("bigint_value")))];

        Assert.HasCount(2, functions);
        Assert.AreNotEqual(functions[0].Id, functions[1].Id);
        Assert.HasCount(2, graph.Items.Where(static item => item.Kind == "cast"));
        Assert.AreEqual(20L, InvokeCSharpOperator(output, "integer_value"));
        Assert.AreEqual(120L, InvokeCSharpOperator(output, "bigint_value"));
    }

    /// <summary>
    /// Operator contracts retain precise diagnostics at the authored result, owner or abstract keyword.
    /// </summary>
    /// <param name="source">The valid C# declaration with an invalid PostgreSQL calling contract.</param>
    /// <param name="id">The expected semantic diagnostic.</param>
    /// <param name="span">The exact source text that must be highlighted.</param>
    [TestMethod]
    [DataRow("public class Metric { [Ankus.PgFunction] public static System.Uri operator +(Metric left, int right) => new(\"https://example.com\"); }", "ANKUS039", "System.Uri")]
    [DataRow("public class Metric { [Ankus.PgFunction] public static implicit operator System.Threading.Tasks.Task<int>(Metric value) => System.Threading.Tasks.Task.FromResult(42); }", "ANKUS031", "System.Threading.Tasks.Task<int>")]
    [DataRow("public class Metric<T> { [Ankus.PgFunction] public static int operator +(Metric<T> left, int right) => 42; }", "ANKUS035", "<T>")]
    [DataRow("public class Outer { private class Metric { [Ankus.PgFunction] public static int operator +(Metric left, int right) => 42; } }", "ANKUS034", "Metric")]
    [DataRow("public interface Metric<T> where T : Metric<T> { [Ankus.PgFunction] public static abstract bool operator ==(T left, T right); public static abstract bool operator !=(T left, T right); }", "ANKUS036", "abstract")]
    public void CSharpOperatorsReportPreciseDiagnostics(string source, string id, string span)
    {
        CSharpCompilation initial = ModuleCompilation(source);
        ImmutableArray<Diagnostic> original = initial.GetDiagnostics(context.CancellationToken);
        Assert.IsEmpty(original.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), string.Join("\n", original));
        GeneratorDriver driver = ModuleDriver().RunGeneratorsAndUpdateCompilation(initial, out Compilation output,
            out ImmutableArray<Diagnostic> diagnostics, context.CancellationToken);
        Diagnostic error = Assert.ContainsSingle(diagnostics);

        Assert.AreEqual(id, error.Id);
        Assert.AreEqual(span, initial.SyntaxTrees.Single().GetText(context.CancellationToken).ToString(error.Location.SourceSpan));
        Assert.AreEqual(DiagnosticSeverity.Error, error.Severity);
        Assert.IsNotEmpty(error.Descriptor.HelpLinkUri);
        Assert.IsNull(Assert.ContainsSingle(driver.GetRunResult().Results).Exception);
        Assert.AreEqual("-- No installable objects declared.\n", InstallationBody(output));
        Assert.AreSequenceEqual(["Pg_magic_func"], ManifestValue(output, "Ankus.Exports").Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// Body-only edits keep exact binding and SQL cached while invoking the new compiled implementation.
    /// </summary>
    /// <param name="conversion">Whether the special method is a conversion.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CSharpOperatorsReuseCachedBridgeAfterBodyEdit(bool conversion)
    {
        string source = "[Ankus.PgType] public readonly record struct Metric(int Value) { [Ankus.PgFunction(Name = \"value\")] " +
            (conversion ? "public static implicit operator int(Metric value) => value.Value;" :
                "public static Metric operator +(Metric left, Metric right) => new(left.Value + right.Value);") + " }";
        CSharpCompilation initial = ModuleCompilation(source);
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        FunctionEmission original = TrackedFunctionEmission(driver, "value").Emission;
        string changed = conversion ? source.Replace("=> value.Value;", "=> value.Value + 1;", StringComparison.Ordinal) :
            source.Replace("left.Value + right.Value", "left.Value + right.Value + 1", StringComparison.Ordinal);
        driver = RunModule(driver, initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(changed,
            path: "Module.cs", cancellationToken: context.CancellationToken)), out Compilation second);

        Assert.AreEqual(IncrementalStepRunReason.Cached, TrackedFunctionEmission(driver, "value").Reason);
        Assert.AreEqual(original, TrackedFunctionEmission(driver, "value").Emission);
        Assert.AreEqual(IncrementalStepRunReason.Cached, TrackedFunctionSqlEmission(driver, "value").Reason);
        Assert.AreEqual(conversion ? 20L : 42L, InvokeCSharpOperator(first, "value"));
        Assert.AreEqual(conversion ? 21L : 43L, InvokeCSharpOperator(second, "value"));
    }

    /// <summary>
    /// Nullable operator inputs retain called-on-null SQL and pass a real null to the exact managed overload.
    /// </summary>
    /// <param name="reference">Whether the nullable input is a class or a nullable value type.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CSharpOperatorsPreserveNullableArguments(bool reference)
    {
        string source = "[Ankus.PgType] public " + (reference ? "sealed record" : "readonly record struct") +
            " Metric(int Value) { [Ankus.PgFunction(Name = \"value\")] public static Metric operator -(Metric? value) => new(value?.Value ?? 100); }";
        RunModule(ModuleDriver(), ModuleCompilation(source), out Compilation output);

        Assert.IsEmpty(output.GetDiagnostics(context.CancellationToken).Where(static diagnostic =>
            diagnostic.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error));
        ExtensionSchemaGraph graph = ExtensionSchemaGraph.Parse(ManifestValue(output, "Ankus.SqlGraph"));
        Assert.DoesNotContain("STRICT", Assert.ContainsSingle(graph.Items.Where(static item => item.Kind == "function" && item.Names.Contains("value"))).Sql);
        Assert.AreEqual(100L, InvokeCSharpOperator(output, "value", nullArgument: true));
        Assert.AreEqual(20L, InvokeCSharpOperator(output, "value"));
    }

    /// <summary>
    /// Reference nullability is preserved on the exact bridge's input and result without compiler warnings.
    /// </summary>
    [TestMethod]
    public void CSharpOperatorsPreserveNullableReferenceResults()
    {
        RunModule(ModuleDriver(), ModuleCompilation("""
            [Ankus.PgType]
            public sealed record Metric(int Value)
            {
                [Ankus.PgFunction(Name = "value")]
                public static Metric? operator !(Metric? value) => value;
            }
            """), out Compilation output);

        Assert.IsEmpty(output.GetDiagnostics(context.CancellationToken).Where(static diagnostic =>
            diagnostic.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error));
        InvokeCSharpOperator(output, "value", nullArgument: true, expectNull: true);
        Assert.AreEqual(20L, InvokeCSharpOperator(output, "value"));
    }

    /// <summary>
    /// A special method used as a SQL function retains synchronous set dispatch and exact enumerable binding.
    /// </summary>
    [TestMethod]
    public void CSharpOperatorsReturnSynchronousSets()
    {
        RunModule(ModuleDriver(), ModuleCompilation("""
            [Ankus.PgType]
            public readonly record struct Metric(int Value)
            {
                [Ankus.PgFunction(Name = "value")]
                public static System.Collections.Generic.IEnumerable<int> operator ~(Metric value) => new[] { value.Value, 42 };
            }
            """), out Compilation output);

        Assert.Contains("RETURNS SETOF integer", InstallationBody(output));
        Assert.AreEqual(62L, InvokeCSharpOperator(output, "value"));
    }

    /// <summary>
    /// Executes the generated static bridge from the actual compiled dispatcher rather than the authored method.
    /// </summary>
    /// <param name="compilation">The compiled generator output.</param>
    /// <param name="name">The backing function's SQL name identifying its bridge.</param>
    /// <param name="nullArgument">Whether the first actual operand is null.</param>
    /// <param name="expectNull">Whether the generated bridge must return null.</param>
    /// <returns>The scalar value or the custom result's stored value.</returns>
    private long InvokeCSharpOperator(Compilation compilation, string name, bool nullArgument = false, bool expectNull = false)
    {
        using var bytes = new MemoryStream();
        Microsoft.CodeAnalysis.Emit.EmitResult emitted = compilation.Emit(bytes, cancellationToken: context.CancellationToken);
        Assert.IsTrue(emitted.Success, string.Join("\n", emitted.Diagnostics));
        bytes.Position = 0;
        var load = new AssemblyLoadContext("CSharpOperatorProbe", isCollectible: true);
        try
        {
            Assembly assembly = load.LoadFromStream(bytes);
            Type metric = assembly.GetType("Metric", throwOnError: true)!;
            Type dispatchers = assembly.GetType("Ankus.Generated.ExtensionDispatchers", throwOnError: true)!;
            MethodInfo bridge = Assert.ContainsSingle(dispatchers.GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
                .Where(method => method.Name.EndsWith("_" + name + "_call", StringComparison.Ordinal)));
            object?[] arguments = [.. bridge.GetParameters().Select((parameter, index) => index == 0 ?
                metric.IsValueType ? Activator.CreateInstance(metric) : null :
                index == 1 && nullArgument ? null :
                parameter.ParameterType == typeof(int) ? 42 : Activator.CreateInstance(metric, index == 1 ? 20 : 22))];
            object? result = bridge.Invoke(null, arguments);
            if (expectNull)
            {
                Assert.IsNull(result);
                return 0;
            }

            if (metric.IsInstanceOfType(result))
            {
                result = metric.GetProperty("Value")!.GetValue(result);
            }

            if (result is IEnumerable<int> sequence)
            {
                Assert.AreSequenceEqual([20, 42], sequence);
                return sequence.Sum();
            }

            return Convert.ToInt64(result, CultureInfo.InvariantCulture);
        }
        finally
        {
            load.Unload();
        }
    }
}
