using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Ankus.Generators.Tests;

public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Generated callbacks bind to assembly-accessible members and nested containers across declaration families.
    /// </summary>
    /// <param name="member">The callback declaration with an access placeholder.</param>
    /// <param name="nested">Whether the protected-internal declaration is the containing type.</param>
    [TestMethod]
    [DataRow("[Ankus.PgFunction] ACCESS static int Read() => 42;", false)]
    [DataRow("[Ankus.PgFunction] ACCESS static int Read() => 42;", true)]
    [DataRow("[Ankus.PgTrigger] ACCESS static Ankus.PgHeapTuple? Read(Ankus.PgTriggerContext value) => null;", false)]
    [DataRow("[Ankus.PgTrigger] ACCESS static Ankus.PgHeapTuple? Read(Ankus.PgTriggerContext value) => null;", true)]
    [DataRow("[Ankus.PgEventTrigger] ACCESS static void Read(Ankus.PgEventTriggerContext value) { }", false)]
    [DataRow("[Ankus.PgEventTrigger] ACCESS static void Read(Ankus.PgEventTriggerContext value) { }", true)]
    [DataRow("[Ankus.PgInitialize] ACCESS static void Read() { }", false)]
    [DataRow("[Ankus.PgInitialize] ACCESS static void Read() { }", true)]
    [DataRow("[Ankus.PgModuleLoad] ACCESS static void Read() { }", false)]
    [DataRow("[Ankus.PgModuleLoad] ACCESS static void Read() { }", true)]
    [DataRow("[Ankus.PgBackgroundWorker] ACCESS static void Read(nuint value) { }", false)]
    [DataRow("[Ankus.PgBackgroundWorker] ACCESS static void Read(nuint value) { }", true)]
    [DataRow("[Ankus.PgTest] ACCESS static void Read() { }", false)]
    [DataRow("[Ankus.PgTest] ACCESS static void Read() { }", true)]
    public void AssemblyAccessibleCallbacksBindFromGeneratedSource(string member, bool nested)
    {
        string owner = nested ? "protected internal" : "public";
        string access = nested ? "public" : "protected internal";
        CSharpCompilation initial = ModuleCompilation(
            "public partial class Outer { " + owner + " partial class Callbacks { " +
            member.Replace("ACCESS", access, StringComparison.Ordinal) + " } }");
        RunModule(PgTestDriver(included: true), initial, out Compilation output);
        INamedTypeSymbol? container = output.GetTypeByMetadataName("Outer+Callbacks");
        Assert.IsNotNull(container);
        IMethodSymbol target = Assert.IsInstanceOfType<IMethodSymbol>(Assert.ContainsSingle(container.GetMembers("Read")));
        Assert.AreEqual(nested ? Accessibility.ProtectedOrInternal : Accessibility.Public, container.DeclaredAccessibility);
        Assert.AreEqual(nested ? Accessibility.Public : Accessibility.ProtectedOrInternal, target.DeclaredAccessibility);

        AssertGeneratedCallbackBinds(initial, output, target);
    }

    /// <summary>
    /// Generated partial GUC properties preserve both property and containing-type assembly access.
    /// </summary>
    /// <param name="owner">The nested partial class accessibility.</param>
    /// <param name="access">The partial property's accessibility.</param>
    [TestMethod]
    [DataRow("public", "protected internal")]
    [DataRow("protected internal", "public")]
    [DataRow("protected internal", "protected internal")]
    public void GucPartialDeclarationsPreserveAssemblyAccess(string owner, string access)
    {
        CSharpCompilation initial = ModuleCompilation($$"""
            public partial class Outer
            {
                {{owner}} partial class Settings
                {
                    [Ankus.PgGucInt("visibility.value", 7, "Visibility check")]
                    {{access}} static partial int Value { get; }
                }
            }
            """);
        RunModule(ModuleDriver(), initial, out Compilation output);
        INamedTypeSymbol? type = output.GetTypeByMetadataName("Outer+Settings");
        Assert.IsNotNull(type);
        IPropertySymbol property = Assert.IsInstanceOfType<IPropertySymbol>(Assert.ContainsSingle(type.GetMembers("Value")));
        Assert.IsNotNull(property.PartialImplementationPart);
        Assert.AreEqual(access == "public" ? Accessibility.Public : Accessibility.ProtectedOrInternal,
            property.PartialImplementationPart.DeclaredAccessibility);
        Assert.AreEqual(owner == "public" ? Accessibility.Public : Accessibility.ProtectedOrInternal, type.DeclaredAccessibility);
    }

    /// <summary>
    /// Each GUC phase binds a protected-internal method through the generated native callback.
    /// </summary>
    /// <param name="role">The GUC hook option.</param>
    /// <param name="method">The phase's typed callback.</param>
    [TestMethod]
    [DataRow("Check", "protected internal static Ankus.PgGucCheckResult<int> Read(int value, Ankus.PgGucSource source) => new(value);")]
    [DataRow("Assign", "protected internal static void Read(int value, Ankus.PgGucExtra? extra) { }")]
    [DataRow("Show", "protected internal static string Read(int value, Ankus.PgGucExtra? extra) => \"shown\";")]
    public void GucHooksRetainAssemblyAccess(string role, string method)
    {
        CSharpCompilation initial = ModuleCompilation($$"""
            public partial class Settings
            {
                [Ankus.PgGucInt("visibility.value", 7, "Visibility check", {{role}} = nameof(Read))]
                public static partial int Value { get; }
                {{method}}
            }
            """);
        RunModule(ModuleDriver(), initial, out Compilation output);
        INamedTypeSymbol? type = output.GetTypeByMetadataName("Settings");
        Assert.IsNotNull(type);
        IMethodSymbol target = Assert.IsInstanceOfType<IMethodSymbol>(Assert.ContainsSingle(type.GetMembers("Read")));
        AssertGeneratedCallbackBinds(initial, output, target);
    }

    /// <summary>
    /// SQL type and aggregate declarations remain valid when their nested container grants assembly access.
    /// </summary>
    /// <param name="declaration">The attributed type.</param>
    /// <param name="sql">The required SQL declaration.</param>
    [TestMethod]
    [DataRow("[Ankus.PgEnum] protected internal enum State { Ready, Waiting }", "CREATE TYPE \"state\" AS ENUM (E'Ready', E'Waiting');")]
    [DataRow("[Ankus.PgType] protected internal sealed record Value(int Count);", "CREATE TYPE \"value\" (")]
    [DataRow("[Ankus.PgAggregate(InitialCondition = \"0\")] protected internal sealed class SumValues : Ankus.IPgAggregate<long, int> { public static long Transition(Ankus.PgAggregateContext context, long state, int value) => state + value; }", "CREATE AGGREGATE \"sum_values\"(")]
    public void NestedSqlDeclarationsRetainAssemblyAccess(string declaration, string sql)
    {
        CSharpCompilation initial = ModuleCompilation("public class Outer { " + declaration + " }");
        RunModule(ModuleDriver(), initial, out Compilation output);
        using var bytes = new MemoryStream();
        Microsoft.CodeAnalysis.Emit.EmitResult emitted = output.Emit(bytes, cancellationToken: context.CancellationToken);
        Assert.IsTrue(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        Assert.Contains(sql, InstallationBody(output));
    }

    /// <summary>
    /// Generated serialization constructs and round-trips a protected-internal nested value without reflection.
    /// </summary>
    [TestMethod]
    public void NestedSerializedValuesRetainAssemblyAccess()
    {
        string[] values = RunSerializedProbe<string[]>("""
            public class Outer
            {
                protected internal sealed record Child(int Count);
            }
            [Ankus.PgType] internal sealed record Value(Outer.Child Data);
            """, """
            Value value = codec.Parse("{\"Data\":{\"Count\":42}}");
            var bytes = new System.Buffers.ArrayBufferWriter<byte>();
            codec.Write(value, bytes);
            Value copy = codec.Read(bytes.WrittenSpan);
            return new[] { codec.Format(copy), copy.Data.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) };
            """);
        Assert.AreSequenceEqual(["{\"Data\":{\"Count\":42}}", "42"], values);
    }

    /// <summary>
    /// Default serializers follow C# assembly access for referenced declarations without granting family-only access.
    /// </summary>
    /// <param name="boundary">The type, constructor or setter carrying the tested access modifier.</param>
    /// <param name="access">The referenced declaration's access modifier.</param>
    /// <param name="friend">Whether the reference grants access to this test assembly.</param>
    /// <param name="allowed">Whether generated source may construct the referenced value.</param>
    [TestMethod]
    [DataRow("constructor", "public", false, true)]
    [DataRow("constructor", "internal", false, false)]
    [DataRow("constructor", "internal", true, true)]
    [DataRow("constructor", "protected internal", false, false)]
    [DataRow("constructor", "protected internal", true, true)]
    [DataRow("constructor", "protected", true, false)]
    [DataRow("constructor", "private protected", true, false)]
    [DataRow("constructor", "private", true, false)]
    [DataRow("type", "public", false, true)]
    [DataRow("type", "internal", false, false)]
    [DataRow("type", "internal", true, true)]
    [DataRow("type", "protected internal", false, false)]
    [DataRow("type", "protected internal", true, true)]
    [DataRow("type", "protected", true, false)]
    [DataRow("type", "private protected", true, false)]
    [DataRow("type", "private", true, false)]
    [DataRow("setter", "public", false, true)]
    [DataRow("setter", "internal", false, false)]
    [DataRow("setter", "internal", true, true)]
    [DataRow("setter", "protected internal", false, false)]
    [DataRow("setter", "protected internal", true, true)]
    [DataRow("setter", "protected", true, false)]
    [DataRow("setter", "private protected", true, false)]
    [DataRow("setter", "private", true, false)]
    public void SerializedReferencesRespectAssemblyAccess(string boundary, string access, bool friend, bool allowed)
    {
        string typeAccess = boundary == "type" ? access : "public";
        string constructorAccess = boundary == "constructor" ? access : "public";
        string grant = friend ? "[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(\"GeneratorTest\")]" : "";
        string members = boundary == "setter"
            ? "public int Count { get; " + (access == "public" ? "" : access + " ") + "set; }"
            : constructorAccess + " Child(int count) { Count = count; } public int Count { get; }";
        CSharpCompilation library = ModuleCompilation($$"""
            {{grant}}
            public class Foreign
            {
                {{typeAccess}} class Child
                {
                    {{members}}
                }
            }
            """).WithAssemblyName("VisibilityLibrary");
        using var libraryBytes = new MemoryStream();
        Microsoft.CodeAnalysis.Emit.EmitResult libraryResult = library.Emit(libraryBytes, cancellationToken: context.CancellationToken);
        Assert.IsTrue(libraryResult.Success, string.Join(Environment.NewLine, libraryResult.Diagnostics));
        MetadataReference reference = MetadataReference.CreateFromImage(libraryBytes.ToArray());
        string construction = boundary == "setter" ? "new Foreign.Child { Count = 42 }" : "new Foreign.Child(42)";
        CSharpCompilation control = ModuleCompilation("internal static class Probe { internal static int Read() => " + construction + ".Count; }")
            .AddReferences(reference);
        using var controlBytes = new MemoryStream();
        Microsoft.CodeAnalysis.Emit.EmitResult controlResult = control.Emit(controlBytes, cancellationToken: context.CancellationToken);
        Assert.AreEqual(allowed, controlResult.Success, string.Join(Environment.NewLine, controlResult.Diagnostics));

        CSharpCompilation initial = ModuleCompilation("[Ankus.PgType] internal sealed record Value(Foreign.Child Data);").AddReferences(reference);
        ModuleDriver().RunGeneratorsAndUpdateCompilation(initial, out Compilation output, out ImmutableArray<Diagnostic> diagnostics,
            context.CancellationToken);
        if (allowed)
        {
            AssertAggregateCompilation(output, diagnostics);
            using var bytes = new MemoryStream();
            Microsoft.CodeAnalysis.Emit.EmitResult emitted = output.Emit(bytes, cancellationToken: context.CancellationToken);
            Assert.IsTrue(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        }
        else
        {
            Assert.AreEqual("ANKUS017", Assert.ContainsSingle(diagnostics).Id);
            Assert.DoesNotContain("CREATE TYPE", InstallationBody(output));
        }
    }

    /// <summary>
    /// Requires a generated invocation to resolve to the exact authored method.
    /// </summary>
    /// <param name="initial">The authored syntax trees excluded from this check.</param>
    /// <param name="output">The actual generated compilation.</param>
    /// <param name="target">The authored callback symbol.</param>
    private void AssertGeneratedCallbackBinds(Compilation initial, Compilation output, IMethodSymbol target)
    {
        bool bound = false;
        foreach (SyntaxTree tree in output.SyntaxTrees.Except(initial.SyntaxTrees))
        {
            SemanticModel model = output.GetSemanticModel(tree);
            foreach (InvocationExpressionSyntax invocation in tree.GetRoot(context.CancellationToken).DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                bound |= SymbolEqualityComparer.Default.Equals(target, model.GetSymbolInfo(invocation, context.CancellationToken).Symbol);
            }
        }

        Assert.IsTrue(bound, "The generated dispatcher must call the actual authored callback.");
    }
}
