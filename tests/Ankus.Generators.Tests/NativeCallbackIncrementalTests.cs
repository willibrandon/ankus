using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Ankus.Generators.Tests;

public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Implementation, parameter-name and source-coordinate edits preserve actual callback rendering cache entries.
    /// </summary>
    /// <param name="edit">The independently irrelevant implementation or source edit.</param>
    [TestMethod]
    [DataRow("body")]
    [DataRow("move")]
    [DataRow("parameter")]
    [DataRow("overload")]
    public void NativeCallbackEmissionCachesIndependentEdits(string edit)
    {
        string source = CallbackCacheSource();
        CSharpCompilation initial = ModuleCompilation(source);
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation first);
        string original = CallbackEmission(driver, "Callback").Source;
        string changed = edit switch
        {
            "body" => source.Replace("=> second + first;", "=> second - first;", StringComparison.Ordinal),
            "move" => "\n\n" + source,
            "parameter" => source.Replace("Handle(int first, long second) => second + first;",
                "Handle(int renamed, long second) => second + renamed;", StringComparison.Ordinal),
            _ => source.Replace("private static long Handle", "private static int Handle(int value) => value;\nprivate static long Handle", StringComparison.Ordinal),
        };
        CSharpCompilation edited = initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(
            changed, path: "Moved.cs", cancellationToken: context.CancellationToken));
        driver = RunModule(driver, edited, out Compilation second);
        (string emission, IncrementalStepRunReason reason) = CallbackEmission(driver, "Callback");

        Assert.AreEqual(IncrementalStepRunReason.Cached, reason);
        Assert.AreEqual(original, emission);
        Assert.AreEqual(ManifestValue(first, "Ankus.NativeSource"), ManifestValue(second, "Ankus.NativeSource"));
        Assert.Contains(emission.ReplaceLineEndings("\n"), CallbackProperties(driver)!);
        Assert.Contains("global::Functions.@Handle(argument0, argument1)", emission);
        Assert.Contains("global::Ankus.CompilerServices.NativeRawCallback.ValidateBinding<global::Hook>()", emission);
        AssertCallbackCompiles(second);
    }

    /// <summary>
    /// Dependent lexical and native-signature changes rerender only their affected callback's actual property source.
    /// </summary>
    /// <param name="before">The initial declaration contract.</param>
    /// <param name="after">The changed declaration contract.</param>
    /// <param name="property">The resulting managed property name.</param>
    /// <param name="expected">The independently expected generated contract.</param>
    [TestMethod]
    [DataRow("nameof(Handle)", "nameof(Changed)", "Callback", "global::Functions.@Changed(argument0, argument1)")]
    [DataRow("NativeFunctionPointer(7)", "NativeFunctionPointer(9)", "Callback", "ankus_native_callback_9_")]
    [DataRow("public static partial Hook Callback", "internal static partial Hook Callback", "Callback", "internal static partial global::Hook @Callback")]
    [DataRow("public static partial Hook Callback", "public static partial Hook Renamed", "Renamed", "public static partial global::Hook @Renamed")]
    [DataRow("public static partial class Functions", "public partial struct Functions", "Callback", "public unsafe partial struct @Functions")]
    [DataRow("public static partial class Functions", "internal static partial class Functions", "Callback", "internal static unsafe partial class @Functions")]
    public void NativeCallbackEmissionTracksDependentContracts(string before, string after, string property, string expected)
    {
        string source = CallbackCacheSource();
        CSharpCompilation initial = ModuleCompilation(source);
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out _);
        string original = CallbackEmission(driver, "Callback").Source;
        string replacement = source.Replace(before, after, StringComparison.Ordinal);
        if (before == "nameof(Handle)")
        {
            replacement = replacement.Replace("long Handle(", "long Changed(", StringComparison.Ordinal);
        }

        CSharpCompilation edited = initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(
            replacement, path: "Module.cs", cancellationToken: context.CancellationToken));
        driver = RunModule(driver, edited, out Compilation output);
        (string emission, IncrementalStepRunReason reason) = CallbackEmission(driver, property);

        Assert.AreEqual(IncrementalStepRunReason.Modified, reason);
        Assert.AreEqual(IncrementalStepRunReason.Cached, CallbackEmission(driver, "OtherCallback").Reason);
        Assert.AreNotEqual(original, emission);
        Assert.Contains(expected, emission);
        Assert.Contains(emission.ReplaceLineEndings("\n"), CallbackProperties(driver)!);
        AssertCallbackCompiles(output);
    }

    /// <summary>
    /// Signature dependencies in a separate source tree update exact argument count, types, result and transport selection.
    /// </summary>
    /// <param name="result">The replacement result type.</param>
    /// <param name="parameters">The replacement argument declaration.</param>
    /// <param name="body">The replacement handler body.</param>
    /// <param name="reader">The independently expected argument transport expression.</param>
    /// <param name="writer">The independently expected result transport expression.</param>
    [TestMethod]
    [DataRow("void", "", "return;", "ValidateFrame(arguments, count, 0,", "result, resultSize, -1)")]
    [DataRow("short", "short value", "return value;", "Read<short>(arguments[0])", "NativeRawCallback.Write(result, resultSize, value)")]
    [DataRow("Payload", "Payload value", "return value;", "ReadNative<global::Payload>(arguments[0])", "NativeRawCallback.WriteNative(result, resultSize, value)")]
    [DataRow("long", "long second, int first", "return second + first;", "Read<long>(arguments[0])", "Read<int>(arguments[1])")]
    public void NativeCallbackEmissionTracksSignatureTypes(string result, string parameters, string body, string reader, string writer)
    {
        const string declaration = """
            public static partial class Functions
            {
                [Ankus.PgNativeCallback(nameof(Handle))]
                public static partial Hook Callback { get; }
            }
            """;
        string dependency = CallbackTypeSource + """
            public readonly record struct Payload(long Value) : Ankus.IPgNativeType
            {
                static int Ankus.IPgNativeType.PostgresMajor => 18;
                static string Ankus.IPgNativeType.AbiIdentity => new string('A', 64);
                static string Ankus.IPgNativeType.RuntimeIdentifier => System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier;
                static int Ankus.IPgNativeType.NativeSize => 8;
                static int Ankus.IPgNativeType.NativeAlignment => 8;
            }
            public static partial class Functions
            {
                private static long Handle(int first, long second) => second + first;
            }
            """;
        CSharpCompilation initial = ModuleCompilation(declaration).AddSyntaxTrees(CSharpSyntaxTree.ParseText(
            dependency, path: "Dependency.cs", cancellationToken: context.CancellationToken));
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out _);
        string replacement = dependency.Replace("long Invoke(int first, long second)", $"{result} Invoke({parameters})", StringComparison.Ordinal)
            .Replace("long Handle(int first, long second) => second + first;", $"{result} Handle({parameters}) {{ {body} }}", StringComparison.Ordinal);
        CSharpCompilation edited = initial.ReplaceSyntaxTree(initial.SyntaxTrees.Last(), CSharpSyntaxTree.ParseText(
            replacement, path: "Dependency.cs", cancellationToken: context.CancellationToken));
        driver = RunModule(driver, edited, out Compilation output);
        (string emission, IncrementalStepRunReason reason) = CallbackEmission(driver, "Callback");

        Assert.AreEqual(IncrementalStepRunReason.Modified, reason);
        Assert.Contains(reader, emission);
        Assert.Contains(writer, emission);
        if (result == "Payload")
        {
            Assert.Contains("NativeSize<global::Payload>()", emission);
        }

        if (result == "void")
        {
            Assert.DoesNotContain("NativeRawCallback.Write", emission);
            Assert.DoesNotContain("arguments[0]", emission);
        }

        AssertCallbackCompiles(output);
    }

    /// <summary>
    /// Cached validation failures use current syntax trees and locations, emit no callback, and disappear after repair.
    /// </summary>
    /// <param name="before">The valid contract to invalidate.</param>
    /// <param name="after">The unsupported replacement.</param>
    [TestMethod]
    [DataRow("public static partial Hook Callback", "public partial Hook Callback")]
    [DataRow("nameof(Handle)", "\"Missing\"")]
    [DataRow("NativeFunctionPointer(7)", "NativeFunctionPointer(-1)")]
    [DataRow("long Handle(int first", "long Handle(ref int first")]
    public void NativeCallbackDiagnosticsUseCurrentSourceAndRecover(string before, string after)
    {
        string valid = CallbackCacheSource(includeOther: false);
        CSharpCompilation initial = ModuleCompilation(valid);
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out _);
        string invalid = valid.Replace(before, after, StringComparison.Ordinal);
        CSharpCompilation rejected = initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(
            invalid, path: "Invalid.cs", cancellationToken: context.CancellationToken));
        driver = driver.RunGeneratorsAndUpdateCompilation(rejected, out _, out ImmutableArray<Diagnostic> diagnostics, context.CancellationToken);
        Diagnostic first = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual("ANKUS021", first.Id);
        Assert.IsNull(CallbackProperties(driver));

        CSharpCompilation moved = rejected.ReplaceSyntaxTree(rejected.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(
            "\n\n" + invalid, path: "Moved.cs", cancellationToken: context.CancellationToken));
        driver = driver.RunGeneratorsAndUpdateCompilation(moved, out _, out diagnostics, context.CancellationToken);
        Diagnostic current = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual("ANKUS021", current.Id);
        Assert.AreSame(moved.SyntaxTrees.Single(), current.Location.SourceTree);
        Assert.AreEqual(first.Location.GetLineSpan().StartLinePosition.Line + 2, current.Location.GetLineSpan().StartLinePosition.Line);
        Assert.AreEqual("Callback", current.Location.SourceTree!.GetText(context.CancellationToken).ToString(current.Location.SourceSpan));
        Assert.IsNull(CallbackProperties(driver));

        driver = RunModule(driver, initial, out Compilation repaired);
        Assert.IsNotNull(CallbackProperties(driver));
        Assert.Contains("global::Functions.@Handle(argument0, argument1)", CallbackEmission(driver, "Callback").Source);
        AssertCallbackCompiles(repaired);
    }

    /// <summary>
    /// Assembly identity changes update the registration import and dispatcher without using stale cached identities.
    /// </summary>
    [TestMethod]
    public void NativeCallbackEmissionTracksAssemblyIdentity()
    {
        CSharpCompilation initial = ModuleCompilation(CallbackCacheSource(includeOther: false));
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out _);
        string original = CallbackEmission(driver, "Callback").Source;
        driver = RunModule(driver, initial.WithAssemblyName("RenamedExtension"), out Compilation output);
        (string emission, IncrementalStepRunReason reason) = CallbackEmission(driver, "Callback");

        Assert.AreEqual(IncrementalStepRunReason.Modified, reason);
        Assert.AreNotEqual(original, emission);
        NativeCallbackModel model = Assert.ContainsSingle(Assert.ContainsSingle(driver.GetRunResult().Results)
            .TrackedSteps["NativeCallbackModel"].SelectMany(static step => step.Outputs).Select(static output => output.Value).OfType<NativeCallbackModel>());
        Assert.Contains("ankus_native_callback_7_" + model.Identity, emission);
        Assert.Contains("AnkusDispatch_" + model.Identity, emission);
        AssertCallbackCompiles(output);
    }

    /// <summary>
    /// Namespace and hiding changes update escaped containing declarations and the statically selected handler.
    /// </summary>
    /// <param name="edit">The containing namespace or inherited-name contract to change.</param>
    [TestMethod]
    [DataRow("namespace")]
    [DataRow("hiding")]
    public void NativeCallbackEmissionTracksLexicalContainers(string edit)
    {
        string source = CallbackCacheSource(includeOther: false);
        if (edit == "hiding")
        {
            source += "public class Base { public static Hook Callback => default; }";
            source = source.Replace("public static partial class Functions", "public partial class Functions : Base", StringComparison.Ordinal)
                .Replace("public static partial Hook Callback", "public new static partial Hook Callback", StringComparison.Ordinal);
        }

        CSharpCompilation initial = ModuleCompilation(source);
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out _);
        string original = CallbackEmission(driver, "Callback").Source;
        string replacement = edit == "namespace" ? source.Replace("public static partial class Functions",
            "namespace @event\n{\npublic static partial class Functions", StringComparison.Ordinal) + "\n}" :
            source.Replace("public new static partial Hook Callback", "public static partial Hook Callback", StringComparison.Ordinal)
                .Replace("public static Hook Callback =>", "public static Hook Other =>", StringComparison.Ordinal);
        CSharpCompilation edited = initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(
            replacement, path: "Module.cs", cancellationToken: context.CancellationToken));
        driver = RunModule(driver, edited, out Compilation output);
        (string emission, IncrementalStepRunReason reason) = CallbackEmission(driver, "Callback");

        Assert.AreEqual(IncrementalStepRunReason.Modified, reason);
        Assert.AreNotEqual(original, emission);
        if (edit == "namespace")
        {
            Assert.Contains("namespace @event", emission);
            Assert.Contains("global::@event.Functions.@Handle(argument0, argument1)", emission);
        }
        else
        {
            Assert.Contains("public new static partial global::Hook @Callback", original);
            Assert.Contains("public static partial global::Hook @Callback", emission);
            Assert.DoesNotContain("public new static partial", emission);
        }

        Assert.IsEmpty(output.GetDiagnostics(context.CancellationToken).Where(static diagnostic =>
            diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        AssertCallbackCompiles(output);
    }

    /// <summary>
    /// Removing an invalid property clears its cached diagnostic and a new declaration creates a callable boundary.
    /// </summary>
    [TestMethod]
    public void NativeCallbackDiagnosticsDisappearAfterRemovalAndAddition()
    {
        string valid = CallbackCacheSource(includeOther: false);
        CSharpCompilation invalid = ModuleCompilation(valid.Replace("nameof(Handle)", "\"Missing\"", StringComparison.Ordinal));
        GeneratorDriver driver = ModuleDriver().RunGeneratorsAndUpdateCompilation(invalid, out _, out ImmutableArray<Diagnostic> diagnostics,
            context.CancellationToken);
        Assert.AreEqual("ANKUS021", Assert.ContainsSingle(diagnostics).Id);
        Assert.IsNull(CallbackProperties(driver));

        CSharpCompilation empty = invalid.ReplaceSyntaxTree(invalid.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(
            CallbackTypeSource, path: "Module.cs", cancellationToken: context.CancellationToken));
        driver = RunModule(driver, empty, out Compilation output);
        Assert.IsNull(CallbackProperties(driver));
        AssertCallbackCompiles(output);

        CSharpCompilation added = empty.ReplaceSyntaxTree(empty.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(
            valid, path: "New.cs", cancellationToken: context.CancellationToken));
        driver = RunModule(driver, added, out Compilation created);
        Assert.AreEqual(IncrementalStepRunReason.New, CallbackEmission(driver, "Callback").Reason);
        Assert.Contains("global::Functions.@Handle(argument0, argument1)", CallbackProperties(driver)!);
        AssertCallbackCompiles(created);
    }

    /// <summary>
    /// Removing a property drops its import and dispatcher while preserving exact surviving callback artifacts.
    /// </summary>
    /// <param name="removeFirst">Whether removal shifts the survivor's positional syntax input.</param>
    /// <param name="lineEnding">The source text newline convention, independent of the checkout.</param>
    [TestMethod]
    [DataRow(false, "\n")]
    [DataRow(true, "\n")]
    [DataRow(false, "\r\n")]
    [DataRow(true, "\r\n")]
    public void NativeCallbackEmissionPreservesSurvivorsAfterRemoval(bool removeFirst, string lineEnding)
    {
        string source = CallbackCacheSource().ReplaceLineEndings(lineEnding);
        CSharpCompilation initial = ModuleCompilation(source);
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out _);
        string removedName = removeFirst ? "Callback" : "OtherCallback";
        string survivorName = removeFirst ? "OtherCallback" : "Callback";
        string removed = CallbackEmission(driver, removedName).Source;
        string survivor = CallbackEmission(driver, survivorName).Source;
        SyntaxTree tree = initial.SyntaxTrees.Single();
        SyntaxNode root = tree.GetRoot(context.CancellationToken);
        PropertyDeclarationSyntax declaration = root.DescendantNodes().OfType<PropertyDeclarationSyntax>()
            .Single(property => property.Identifier.ValueText == removedName);
        SyntaxNode editedRoot = root.RemoveNode(declaration, SyntaxRemoveOptions.KeepExteriorTrivia)!;
        Assert.DoesNotContain(removedName, editedRoot.DescendantNodes().OfType<PropertyDeclarationSyntax>()
            .Select(static property => property.Identifier.ValueText));
        CSharpCompilation edited = initial.ReplaceSyntaxTree(tree, tree.WithRootAndOptions(editedRoot, tree.Options));
        driver = RunModule(driver, edited, out Compilation output);

        Assert.AreEqual(survivor, CallbackEmission(driver, survivorName).Source);
        Assert.AreEqual(removeFirst ? IncrementalStepRunReason.Modified : IncrementalStepRunReason.Cached,
            CallbackEmission(driver, survivorName).Reason);
        string combined = CallbackProperties(driver)!;
        Assert.Contains(survivor.ReplaceLineEndings("\n"), combined);
        Assert.DoesNotContain(removed.ReplaceLineEndings("\n"), combined);
        Assert.DoesNotContain(" @" + removedName + "\n", combined.Replace("\r\n", "\n", StringComparison.Ordinal));
        AssertCallbackCompiles(output);

        CSharpCompilation empty = initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(
            CallbackTypeSource, path: "Module.cs", cancellationToken: context.CancellationToken));
        driver = RunModule(driver, empty, out Compilation noCallbacks);
        Assert.IsNull(CallbackProperties(driver));
        Assert.IsEmpty(noCallbacks.Assembly.GetAttributes().Where(static attribute =>
            attribute.AttributeClass?.Name == "AssemblyMetadataAttribute" && attribute.ConstructorArguments[0].Value is "Ankus.NativeCallbacks"));
        AssertCallbackCompiles(noCallbacks);
    }

    /// <summary>
    /// Creates independent callback properties whose pointer types have separate native signature dependencies.
    /// </summary>
    private static string CallbackCacheSource(bool includeOther = true)
    {
        string source = CallbackTypeSource + """

            public static partial class Functions
            {
                [Ankus.PgNativeCallback(nameof(Handle))]
                public static partial Hook Callback { get; }
                private static long Handle(int first, long second) => second + first;
            }
            """;
        if (includeOther)
        {
            source += CallbackTypeSource.Replace("Hook", "OtherHook", StringComparison.Ordinal).Replace("NativeFunctionPointer(7)", "NativeFunctionPointer(8)", StringComparison.Ordinal) + """

                public static partial class OtherFunctions
                {
                    [Ankus.PgNativeCallback(nameof(OtherHandle))]
                    public static partial OtherHook OtherCallback { get; }
                    private static long OtherHandle(int first, long second) => second;
                }
                """;
        }

        return source;
    }

    /// <summary>
    /// Reads the actual production rendering stage and its Roslyn cache reason for one managed property.
    /// </summary>
    private static (string Source, IncrementalStepRunReason Reason) CallbackEmission(GeneratorDriver driver, string property)
    {
        (object Value, IncrementalStepRunReason Reason) output = Assert.ContainsSingle(Assert.ContainsSingle(driver.GetRunResult().Results)
            .TrackedSteps["NativeCallbackEmission"].SelectMany(static step => step.Outputs).Where(value =>
                value.Reason != IncrementalStepRunReason.Removed && value.Value is string source &&
                source.Replace("\r\n", "\n", StringComparison.Ordinal).Contains(" @" + property + "\n", StringComparison.Ordinal)));
        return (Assert.IsInstanceOfType<string>(output.Value), output.Reason);
    }

    /// <summary>
    /// Returns the actual composed property output, or null when the current inventory contains no valid callbacks.
    /// </summary>
    private static string? CallbackProperties(GeneratorDriver driver)
    {
        string? source = Assert.ContainsSingle(driver.GetRunResult().Results)
            .GeneratedSources.SingleOrDefault(static generated => generated.HintName == "NativeCallbackProperties.g.cs").SourceText?.ToString();
        if (source is not null)
        {
            Assert.DoesNotContain("\r", source);
        }

        return source;
    }
}
