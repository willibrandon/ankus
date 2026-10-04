using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Ankus.Generators.Tests;

public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Metadata names retain raw segments and nested generic arity independently of C# escaping.
    /// </summary>
    /// <param name="portable">Whether the definitions come from an emitted assembly.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void MetadataDefinitionNamesPreserveEscapedNamespaceSegments(bool portable)
    {
        MetadataReference dependency = ExactMappingReference("""
            namespace @class.@namespace
            {
                public class Outer<T>
                {
                    public class Inner<U> { }
                }
            }
            """, portable);
        CSharpCompilation input = ModuleCompilation(string.Empty).AddReferences(dependency);
        const string Expected = "class.namespace.Outer`1+Inner`1";
        INamedTypeSymbol? definition = input.GetTypeByMetadataName(Expected);
        Assert.IsNotNull(definition);
        Assert.AreEqual(Expected, MetadataTypeName.Create(definition));
        Assert.AreEqual("class.namespace.Outer`1", MetadataTypeName.Create(definition.ContainingType));
        Assert.AreNotEqual("class.namespace", definition.ContainingNamespace.ToDisplayString());
        Assert.IsTrue(SymbolEqualityComparer.Default.Equals(definition, input.GetTypeByMetadataName(MetadataTypeName.Create(definition))));
    }

    /// <summary>
    /// Referenced scalar and range registrations compile under ordinary, escaped and Unicode namespaces.
    /// </summary>
    /// <param name="portable">Whether the dependency is emitted metadata.</param>
    /// <param name="range">Whether the consumed type is a mapped range.</param>
    /// <param name="space">The exact C# namespace spelling.</param>
    [TestMethod]
    [DataRow(false, false, "Shared")]
    [DataRow(true, false, "Shared")]
    [DataRow(false, true, "Shared")]
    [DataRow(true, true, "Shared")]
    [DataRow(false, false, "@class.@namespace")]
    [DataRow(true, false, "@class.@namespace")]
    [DataRow(false, true, "@class.@namespace")]
    [DataRow(true, true, "@class.@namespace")]
    [DataRow(false, false, "é.Δ")]
    [DataRow(true, false, "é.Δ")]
    [DataRow(false, true, "é.Δ")]
    [DataRow(true, true, "é.Δ")]
    public void ReferencedMappedTypesPreserveNamespaceMetadataIdentity(bool portable, bool range, string space)
    {
        string source = "namespace " + space + " { " + (range ? DatumRangeSource() : DatumMappingSource()) + " }";
        MetadataReference dependency = ExactMappingReference(source, portable);
        string bound = "global::" + space + ".Value";
        string carrier = range ? "Ankus.PgRange<" + bound + ">" : bound;
        CSharpCompilation input = ModuleCompilation("public static class Functions { [Ankus.PgFunction] public static " + carrier +
            " Echo(" + carrier + " value) => value; }").AddReferences(dependency);
        GeneratorDriver driver = RunModule(ModuleDriver(), input, out Compilation output);
        DatumRegistrationModel[] models = [.. Assert.ContainsSingle(driver.GetRunResult().Results).TrackedSteps["DatumRegistrationModel"]
            .SelectMany(static step => step.Outputs).Select(static item => item.Value).OfType<DatumRegistrationModel>()];
        DatumRegistrationModel scalar = Assert.ContainsSingle(models.Where(model => model.Managed == bound));
        Assert.AreEqual("int4", scalar.Name);
        Assert.AreEqual("pg_catalog", scalar.Schema);
        Assert.AreEqual("global::" + space + ".Converter", scalar.Converter);
        Assert.IsTrue(scalar.External && scalar.CanRead && scalar.CanWrite);
        Assert.HasCount(range ? 2 : 1, models);
        if (range)
        {
            DatumRegistrationModel mappedRange = Assert.ContainsSingle(models.Where(static model => model.Bound is not null));
            Assert.AreEqual(bound, mappedRange.Bound);
            Assert.AreEqual("int4range", mappedRange.Name);
            Assert.AreEqual("pg_catalog", mappedRange.Schema);
            Assert.IsTrue(mappedRange.External);
        }

        AssertAggregateCompilation(output, []);
        Assert.IsNotEmpty(EmitDatumMappingImage(output));
    }

    /// <summary>
    /// Imported GUC labels retain exact Unicode and aliases under escaped and Unicode namespaces.
    /// </summary>
    /// <param name="portable">Whether the dependency is emitted metadata.</param>
    /// <param name="space">The exact C# namespace spelling.</param>
    [TestMethod]
    [DataRow(false, "Shared")]
    [DataRow(true, "Shared")]
    [DataRow(false, "@class.@namespace")]
    [DataRow(true, "@class.@namespace")]
    [DataRow(false, "é.Δ")]
    [DataRow(true, "é.Δ")]
    public void ReferencedGucLabelsPreserveNamespaceMetadataIdentity(bool portable, string space)
    {
        MetadataReference dependency = ExactMappingReference("namespace " + space + " { " + """
            public enum Mode : long
            {
                [Ankus.PgGucLabel("é")] First = long.MinValue,
                [Ankus.PgGucLabel("hidden", Hidden = true)] Hidden = long.MaxValue,
                [Ankus.PgGucLabel("alias")] Alias = Hidden,
            }
            }
            """, portable);
        string managed = "global::" + space + ".Mode";
        CSharpCompilation input = ModuleCompilation("public static partial class Settings { [Ankus.PgGucEnum(\"demo.mode\", " +
            managed + ".Hidden, \"Mode\")] public static partial " + managed + " Value { get; } }").AddReferences(dependency);
        GeneratorDriver driver = RunModule(ModuleDriver(), input, out Compilation output);
        GucModel model = Assert.ContainsSingle(Assert.ContainsSingle(driver.GetRunResult().Results).TrackedSteps["GucModel"]
            .SelectMany(static step => step.Outputs).Select(static item => item.Value).OfType<GucModel>());
        Assert.AreSequenceEqual([
            new GucModel.Label("First", "é", 0, false),
            new GucModel.Label("Hidden", "hidden", 1, true),
            new GucModel.Label("Alias", "alias", 1, false),
        ], model.Labels);
        Assert.AreEqual(new GucConstant(1), model.Default);
        AssertGucCompilation(output, []);
    }
}
