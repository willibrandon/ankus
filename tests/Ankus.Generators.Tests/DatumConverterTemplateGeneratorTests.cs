using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Ankus.Generators.Tests;

/// <summary>
/// Verifies interface-directed finite converter inference and compiler-validated generic constraints.
/// </summary>
public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Default and explicit roots infer closed factories without copying root arguments by position.
    /// </summary>
    /// <param name="explicitRoot">Whether exact metadata selects both roots without callback signatures.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void DatumConverterTemplatesCloseEachSelectedRoot(bool explicitRoot)
    {
        string source = explicitRoot ? """
            [Ankus.PgDatumType(typeof(Box<int>), "int4", typeof(Converter<>), Origin=Ankus.PgTypeOrigin.External, Schema="pg_catalog")]
            [Ankus.PgDatumType(typeof(Box<long>), "int8", typeof(Converter<>), Origin=Ankus.PgTypeOrigin.External, Schema="pg_catalog")]
            """ : "[Ankus.PgDatumType(\"int4\", typeof(Converter<>), Origin=Ankus.PgTypeOrigin.External, Schema=\"pg_catalog\")]";
        source += TemplateBoxAndConverter;
        if (!explicitRoot)
        {
            source += """
                public static class Functions
                {
                    [Ankus.PgFunction] public static Box<int> Narrow(Box<int> value) => value;
                    [Ankus.PgFunction] public static Box<long>[] Wide(Box<long>[] value) => value;
                }
                """;
        }

        Compilation compilation = GenerateSqlControl(source);
        string managed = DatumMappingManaged(compilation);
        Assert.Contains("RegisterValue<global::Box<int>>(\"int4\"", managed);
        Assert.Contains("RegisterValue<global::Box<long>>(\"" + (explicitRoot ? "int8" : "int4") + "\"", managed);
        Assert.Contains("new global::Converter<int>(), true, true);", managed);
        Assert.Contains("new global::Converter<long>(), true, true);", managed);
        Assert.DoesNotContain("new global::Converter<T>()", managed);
        Assert.DoesNotContain("__DatumConverterConstraints", string.Join("\n", compilation.SyntaxTrees.Select(static tree => tree.ToString())));
    }

    /// <summary>
    /// A converter parameter can represent the complete non-generic mapped root.
    /// </summary>
    [TestMethod]
    public void DatumConverterTemplatesInferWholeNonGenericRoots()
    {
        Compilation compilation = GenerateSqlControl("""
            [Ankus.PgDatumType("int4", typeof(Converter<>), Origin=Ankus.PgTypeOrigin.External, Schema="pg_catalog")]
            public readonly record struct Value(int Number);
            public sealed class Converter<T> : Ankus.IPgDatumReader<T>
            {
                public T Read(Ankus.PgDatum value) => default!;
            }
            """);
        Assert.Contains("new global::Converter<global::Value>(), true, false);", DatumMappingManaged(compilation));
    }

    /// <summary>
    /// Swapped pattern arguments close nested containing and immediate parameters at their own levels.
    /// </summary>
    [TestMethod]
    public void DatumConverterTemplatesPreserveConstructedContainingArguments()
    {
        Compilation compilation = GenerateSqlControl("""
            [Ankus.PgDatumType(typeof(Pair<int,long>), "int4", typeof(Family<>.Converter<>), Origin=Ankus.PgTypeOrigin.External, Schema="pg_catalog")]
            public readonly record struct Pair<TFirst,TSecond>(int Number);
            public class Family<TOuter>
            {
                public sealed class Converter<TInner> : Ankus.IPgDatumReader<Pair<TInner,TOuter>>
                {
                    public Pair<TInner,TOuter> Read(Ankus.PgDatum value) => default;
                }
            }
            """);
        Assert.Contains("new global::Family<long>.Converter<int>(), true, false);", DatumMappingManaged(compilation));
        Assert.DoesNotContain("new global::Family<int>.Converter<long>()", DatumMappingManaged(compilation));
    }

    /// <summary>
    /// Independent reader and writer patterns jointly determine parameters without leaving an arbitrary tag.
    /// </summary>
    [TestMethod]
    public void DatumConverterTemplatesCombineIndependentDirectionAssignments()
    {
        Compilation compilation = GenerateSqlControl("""
            [Ankus.PgDatumType(typeof(Box<int>), "int4", typeof(Converter<,>), Origin=Ankus.PgTypeOrigin.External, Schema="pg_catalog")]
            public readonly record struct Box<T>(int Number);
            public sealed class Converter<TRead,TWrite> : Ankus.IPgDatumReader<Box<TRead>>, Ankus.IPgDatumWriter<Box<TWrite>>
            {
                public Box<TRead> Read(Ankus.PgDatum value) => default;
                public Ankus.PgDatum Write(Box<TWrite> value, uint oid, Ankus.PgMemoryContext destination) => throw new System.InvalidOperationException();
            }
            """);
        Assert.Contains("new global::Converter<int, int>(), true, true);", DatumMappingManaged(compilation));
    }

    /// <summary>
    /// Inherited array patterns retain CLR array shape without treating managed tags as SQL containers.
    /// </summary>
    /// <param name="array">The root's exact managed tag array.</param>
    /// <param name="valid">Whether its rank matches the interface pattern.</param>
    [TestMethod]
    [DataRow("int[]", true)]
    [DataRow("int[,]", false)]
    public void DatumConverterTemplatesMatchInheritedArrayPatterns(string array, bool valid)
    {
        string source = "[Ankus.PgDatumType(typeof(Box<" + array + ">), \"int4\", typeof(Converter<>), Origin=Ankus.PgTypeOrigin.External, Schema=\"pg_catalog\")]" + """
            public readonly record struct Box<T>(int Number);
            public class Base<T> : Ankus.IPgDatumReader<Box<T[]>>
            {
                public Box<T[]> Read(Ankus.PgDatum value) => default;
            }
            public sealed class Converter<T> : Base<T> { }
            """;
        if (valid)
        {
            Assert.Contains("new global::Converter<int>(), true, false);", DatumMappingManaged(GenerateSqlControl(source)));
        }
        else
        {
            AssertDatumMappingError(source, "ANKUS019", "cannot be inferred");
        }
    }

    /// <summary>
    /// Unresolved variables, repeated-variable conflicts and multiple complete closures cannot choose arbitrary factories.
    /// </summary>
    /// <param name="kind">The independently invalid inference partition.</param>
    [TestMethod]
    [DataRow("unused")]
    [DataRow("repeated")]
    [DataRow("constant")]
    [DataRow("ambiguous")]
    [DataRow("ambiguous_constrained")]
    public void DatumConverterTemplatesRejectIncompleteOrAmbiguousInference(string kind)
    {
        string converter = kind == "unused" ? "Converter<,>" : "Converter<>";
        string target = kind == "constant" ? "Pair<long,string>" : kind == "ambiguous_constrained" ? "Pair<string,int>" : "Pair<long,int>";
        string source = "[Ankus.PgDatumType(typeof(" + target + "), \"int4\", typeof(" + converter + "), Origin=Ankus.PgTypeOrigin.External, Schema=\"pg_catalog\")]" +
            "public readonly record struct Pair<TFirst,TSecond>(int Number);" + (kind switch
            {
                "unused" => "public sealed class Converter<T,TUnused> : Ankus.IPgDatumReader<Pair<T,int>> { public Pair<T,int> Read(Ankus.PgDatum value) => default; }",
                "repeated" => "public sealed class Converter<T> : Ankus.IPgDatumReader<Pair<T,T>> { public Pair<T,T> Read(Ankus.PgDatum value) => default; }",
                "constant" => "public sealed class Converter<T> : Ankus.IPgDatumReader<Pair<T,int>> { public Pair<T,int> Read(Ankus.PgDatum value) => default; }",
                "ambiguous" => """
                    public sealed class Converter<T> : Ankus.IPgDatumReader<Pair<T,int>>, Ankus.IPgDatumWriter<Pair<long,T>>
                    {
                        public Pair<T,int> Read(Ankus.PgDatum value) => default;
                        public Ankus.PgDatum Write(Pair<long,T> value, uint oid, Ankus.PgMemoryContext destination) => throw new System.InvalidOperationException();
                    }
                    """,
                "ambiguous_constrained" => """
                    public sealed class Converter<T> : Ankus.IPgDatumReader<Pair<T,int>>, Ankus.IPgDatumWriter<Pair<string,T>> where T : struct
                    {
                        public Pair<T,int> Read(Ankus.PgDatum value) => default;
                        public Ankus.PgDatum Write(Pair<string,T> value, uint oid, Ankus.PgMemoryContext destination) => throw new System.InvalidOperationException();
                    }
                    """,
                _ => throw new InvalidOperationException(),
            });
        AssertDatumMappingError(source, "ANKUS019", kind.StartsWith("ambiguous", StringComparison.Ordinal) ? "more than one inferred closed construction" : "cannot be inferred");
    }

    /// <summary>
    /// C# constraint errors and nullable constraint warnings reject inferred factories before source emission.
    /// </summary>
    /// <param name="argument">The selected concrete managed argument.</param>
    /// <param name="constraint">The converter's independent generic constraint.</param>
    /// <param name="valid">Whether C# permits the inferred construction.</param>
    [TestMethod]
    [DataRow("int", "struct", true)]
    [DataRow("string", "struct", false)]
    [DataRow("int", "class", false)]
    [DataRow("string", "class", true)]
    [DataRow("string?", "class", false)]
    [DataRow("string?", "class?", true)]
    [DataRow("int", "unmanaged", true)]
    [DataRow("Managed", "unmanaged", false)]
    [DataRow("string", "notnull", true)]
    [DataRow("string?", "notnull", false)]
    [DataRow("int?", "notnull", false)]
    [DataRow("Derived", "new()", true)]
    [DataRow("string", "new()", false)]
    [DataRow("Required", "new()", false)]
    [DataRow("RequiredFixed", "new()", true)]
    [DataRow("Derived", "Base", true)]
    [DataRow("object", "Base", false)]
    [DataRow("int", "System.IComparable<T>", true)]
    [DataRow("object", "System.IComparable<T>", false)]
    public void DatumConverterTemplatesValidateCompilerConstraints(string argument, string constraint, bool valid)
    {
        string source = """
            [Ankus.PgDatumType("int4", typeof(Converter<>), Origin=Ankus.PgTypeOrigin.External, Schema="pg_catalog")]
            public readonly record struct Box<T>(int Number);
            """ + "public sealed class Converter<T> : Ankus.IPgDatumReader<Box<T>> where T : " + constraint +
            " { public Box<T> Read(Ankus.PgDatum value) => default; }" + """
            public class Base { }
            public sealed class Derived : Base { }
            public readonly struct Managed { public string Value { get; } }
            public sealed class Required { public required int Value { get; set; } }
            public sealed class RequiredFixed
            {
                public required int Value { get; set; }
                [System.Diagnostics.CodeAnalysis.SetsRequiredMembers] public RequiredFixed() { Value = 7; }
            }
            """ + "public static class Functions { [Ankus.PgFunction] public static int Read(Box<" + argument + "> value) => value.Number; }";
        if (valid)
        {
            Compilation compilation = GenerateSqlControl(source);
            Assert.Contains("ReadMapped<global::Box<", DatumMappingManaged(compilation));
            Assert.Contains("static () => new global::Converter<", DatumMappingManaged(compilation));
            Diagnostic[] warnings = [.. compilation.GetDiagnostics(context.CancellationToken).Where(static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Warning && diagnostic.Location.SourceTree?.FilePath.EndsWith(".g.cs", StringComparison.Ordinal) == true)];
            Assert.IsEmpty(warnings, string.Join(Environment.NewLine, warnings.Select(static diagnostic => diagnostic.ToString())));
        }
        else
        {
            AssertDatumMappingError(source, "ANKUS019", "not a valid C# constructed type");
        }
    }

    /// <summary>
    /// Constraints referring to another inferred parameter use the complete constructed type.
    /// </summary>
    /// <param name="valid">Whether the derived argument is assignable to its inferred base constraint.</param>
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void DatumConverterTemplatesValidateDependentConstraints(bool valid)
    {
        string target = valid ? "Pair<Base,Derived>" : "Pair<Derived,Base>";
        string source = "[Ankus.PgDatumType(typeof(" + target + "), \"int4\", typeof(Converter<,>), Origin=Ankus.PgTypeOrigin.External, Schema=\"pg_catalog\")]" + """
            public readonly record struct Pair<TFirst,TSecond>(int Number);
            public class Base { }
            public sealed class Derived : Base { }
            public sealed class Converter<TBase,TDerived> : Ankus.IPgDatumReader<Pair<TBase,TDerived>> where TDerived : TBase
            {
                public Pair<TBase,TDerived> Read(Ankus.PgDatum value) => default;
            }
            """;
        if (valid)
        {
            Assert.Contains("new global::Converter<global::Base, global::Derived>()", DatumMappingManaged(GenerateSqlControl(source)));
        }
        else
        {
            AssertDatumMappingError(source, "ANKUS019", "not a valid C# constructed type");
        }
    }

    /// <summary>
    /// Referenced open metadata closes only the selected root using the consumer's exact symbol identity.
    /// </summary>
    [TestMethod]
    public void DatumConverterTemplatesCloseReferencedDeclarations()
    {
        MetadataReference dependency = DatumMappingReference("TemplateDependency",
            "[Ankus.PgDatumType(\"int4\", typeof(Converter<>), Origin=Ankus.PgTypeOrigin.External, Schema=\"pg_catalog\")]" + TemplateBoxAndConverter);
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = GenerateDatumMappingReference(
            "public static class Functions { [Ankus.PgFunction] public static Box<long> Echo(Box<long> value) => value; }", [dependency]);
        AssertSqlControlCompilation(compilation, diagnostics);
        Assert.Contains("new global::Converter<long>(), true, true);", DatumMappingManaged(compilation));
        Assert.DoesNotContain("new global::Converter<int>()", DatumMappingManaged(compilation));
    }

    /// <summary>
    /// Inference retains constructor, accessibility and required-member checks on the final converter.
    /// </summary>
    /// <param name="declaration">The converter declaration modifiers.</param>
    /// <param name="members">The independently invalid converter members.</param>
    /// <param name="reason">The expected contract diagnostic.</param>
    [TestMethod]
    [DataRow("public abstract class", "", "accessible, closed and concrete")]
    [DataRow("file sealed class", "", "accessible, closed and concrete")]
    [DataRow("public sealed class", "private Converter() { }", "accessible parameterless constructor")]
    [DataRow("public sealed class", "public Converter(int value) { }", "accessible parameterless constructor")]
    [DataRow("public sealed class", "public required int Value { get; set; }", "SetsRequiredMembers")]
    public void DatumConverterTemplatesRetainConstructionDiagnostics(string declaration, string members, string reason)
        => AssertDatumMappingError("""
            [Ankus.PgDatumType(typeof(Box<int>), "int4", typeof(Converter<>), Origin=Ankus.PgTypeOrigin.External, Schema="pg_catalog")]
            public readonly record struct Box<T>(int Number);
            """ + declaration + " Converter<T> : Ankus.IPgDatumReader<Box<T>> { " + members +
            " public Box<T> Read(Ankus.PgDatum value) => default; }", "ANKUS019", reason);

    /// <summary>
    /// A nested converter with no immediate type parameters retains and validates its inferred outer constraint.
    /// </summary>
    /// <param name="argument">The root's outer argument.</param>
    /// <param name="valid">Whether the containing generic constraint is satisfied.</param>
    [TestMethod]
    [DataRow("int", true)]
    [DataRow("string", false)]
    public void DatumConverterTemplatesValidateContainingConstraints(string argument, bool valid)
    {
        string source = "[Ankus.PgDatumType(typeof(Box<" + argument + ">), \"int4\", typeof(Family<>.Converter), Origin=Ankus.PgTypeOrigin.External, Schema=\"pg_catalog\")]" + """
            public readonly record struct Box<T>(int Number);
            public class Family<T> where T : struct
            {
                public sealed class Converter : Ankus.IPgDatumReader<Box<T>>
                {
                    public Box<T> Read(Ankus.PgDatum value) => default;
                }
            }
            """;
        if (valid)
        {
            Assert.Contains("new global::Family<int>.Converter(), true, false);", DatumMappingManaged(GenerateSqlControl(source)));
        }
        else
        {
            AssertDatumMappingError(source, "ANKUS019", "not a valid C# constructed type");
        }
    }

    /// <summary>
    /// An owned provider alone selects and closes its finite generic converter without a callback signature.
    /// </summary>
    [TestMethod]
    public void DatumConverterTemplatesCloseProviderOnlyRoots()
    {
        Compilation compilation = GenerateSqlControl("""
            [assembly: Ankus.PgSql("complete", "CREATE DOMAIN item AS integer;", Relocatable=true)]
            [assembly: Ankus.PgSqlTypeProvider("complete", typeof(Box<int>))]
            [Ankus.PgDatumType("item", typeof(Converter<>))]
            """ + TemplateBoxAndConverter);
        Assert.Contains("RegisterValue<global::Box<int>>(\"item\", null, global::Ankus.PgTypeOrigin.ThisExtension", DatumMappingManaged(compilation));
        Assert.Contains("new global::Converter<int>(), true, true);", DatumMappingManaged(compilation));
        Assert.AreEqual("CREATE DOMAIN item AS integer;\n", ManifestValue(compilation, "Ankus.Sql"));
        Assert.DoesNotContain("Box<long>", DatumMappingManaged(compilation));
    }

    /// <summary>
    /// A reused driver rejects changed constraints and reconstructs a different valid inferred root on the next edit.
    /// </summary>
    [TestMethod]
    public void DatumConverterTemplateEditsInvalidateConstraintsAndFactories()
    {
        const string source = """
            [Ankus.PgDatumType(typeof(Box<int>), "int4", typeof(Converter<>), Origin=Ankus.PgTypeOrigin.External, Schema="pg_catalog")]
            public readonly record struct Box<T>(int Number);
            public sealed class Converter<T> : Ankus.IPgDatumReader<Box<T>> where T : struct
            {
                public Box<T> Read(Ankus.PgDatum value) => default;
            }
            """;
        CSharpCompilation original = CSharpCompilation.Create("ConverterTemplateTest",
            [CSharpSyntaxTree.ParseText(source, cancellationToken: context.CancellationToken)], s_references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, nullableContextOptions: NullableContextOptions.Enable));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new PgFunctionGenerator().AsSourceGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(original, out Compilation first, out ImmutableArray<Diagnostic> firstDiagnostics, context.CancellationToken);
        AssertSqlControlCompilation(first, firstDiagnostics);
        Assert.Contains("new global::Converter<int>()", DatumMappingManaged(first));
        CSharpCompilation changed = original.ReplaceSyntaxTree(original.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(
            source.Replace("where T : struct", "where T : class", StringComparison.Ordinal), cancellationToken: context.CancellationToken));
        driver = driver.RunGeneratorsAndUpdateCompilation(changed, out Compilation invalid, out ImmutableArray<Diagnostic> invalidDiagnostics, context.CancellationToken);
        Assert.Contains(diagnostic => diagnostic.Id == "ANKUS019" && diagnostic.GetMessage(CultureInfo.InvariantCulture)
            .Contains("not a valid C# constructed type", StringComparison.Ordinal), invalidDiagnostics);
        Assert.IsNull(invalid.GetTypeByMetadataName("Ankus.Generated.ExtensionDispatchers"));
        Assert.IsEmpty(invalid.Assembly.GetAttributes().Where(static attribute =>
            attribute.AttributeClass?.ToDisplayString() == "System.Reflection.AssemblyMetadataAttribute"));
        changed = original.ReplaceSyntaxTree(original.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(
            source.Replace("Box<int>", "Box<long>", StringComparison.Ordinal), cancellationToken: context.CancellationToken));
        driver.RunGeneratorsAndUpdateCompilation(changed, out Compilation final, out ImmutableArray<Diagnostic> finalDiagnostics, context.CancellationToken);
        AssertSqlControlCompilation(final, finalDiagnostics);
        Assert.Contains("new global::Converter<long>()", DatumMappingManaged(final));
        Assert.DoesNotContain("new global::Converter<int>()", DatumMappingManaged(final));
    }

    /// <summary>
    /// Nullable reference callback slots retain one non-nullable converter identity and nullable generic arguments.
    /// </summary>
    [TestMethod]
    public void DatumConverterTemplatesPreserveNullableReferenceSlots()
    {
        Compilation compilation = GenerateSqlControl("""
            [Ankus.PgDatumType("int4", typeof(Converter<>), Origin=Ankus.PgTypeOrigin.External, Schema="pg_catalog")]
            public sealed record Box<T>(int Number);
            public sealed class Converter<T> : Ankus.IPgDatumReader<T>, Ankus.IPgDatumWriter<T> where T : class
            {
                public T Read(Ankus.PgDatum value) => default!;
                public Ankus.PgDatum Write(T value, uint oid, Ankus.PgMemoryContext destination) => throw new System.InvalidOperationException();
            }
            public static class Functions
            {
                [Ankus.PgFunction] public static Box<string?>? Echo(Box<string?>? value) => value;
            }
            """);
        Assert.Contains("new global::Converter<global::Box<string?>>(), true, true);", DatumMappingManaged(compilation));
        Assert.Contains("RegisterReference<global::Box<string?>>", DatumMappingManaged(compilation));
        Assert.IsEmpty(compilation.GetDiagnostics(context.CancellationToken).Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning));
    }

    /// <summary>
    /// An annotated interface variable contributes its nullable modifier without forcing a nullable converter argument.
    /// </summary>
    [TestMethod]
    public void DatumConverterTemplatesInferNullableInterfacePatterns()
    {
        Compilation compilation = GenerateSqlControl("""
            [Ankus.PgDatumType("int4", typeof(Converter<>), Origin=Ankus.PgTypeOrigin.External, Schema="pg_catalog")]
            public readonly record struct Pair<TFirst,TSecond>(int Number);
            public sealed class Converter<T> : Ankus.IPgDatumReader<Pair<T,T?>> where T : class
            {
                public Pair<T,T?> Read(Ankus.PgDatum value) => default;
            }
            public static class Functions
            {
                [Ankus.PgFunction] public static int Read(Pair<string,string?> value) => value.Number;
            }
            """);
        Assert.Contains("new global::Converter<string>(), true, false);", DatumMappingManaged(compilation));
        Assert.Contains("ReadMapped<global::Pair<string, string?>>()", DatumMappingManaged(compilation));
        Assert.IsEmpty(compilation.GetDiagnostics(context.CancellationToken).Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning));
    }

    /// <summary>
    /// Annotation variants all undergo constraint validation before one CLR registration is selected.
    /// </summary>
    /// <param name="constraint">The converter's nullable constraint.</param>
    /// <param name="valid">Whether both selected annotation variants satisfy it.</param>
    [TestMethod]
    [DataRow("notnull", false)]
    [DataRow("class?", true)]
    public void DatumConverterTemplatesValidateEveryNullableVariant(string constraint, bool valid)
    {
        string source = """
            [Ankus.PgDatumType("int4", typeof(Converter<>), Origin=Ankus.PgTypeOrigin.External, Schema="pg_catalog")]
            public readonly record struct Box<T>(int Number);
            public static class Functions
            {
                [Ankus.PgFunction] public static int Required(Box<string> value) => value.Number;
                [Ankus.PgFunction] public static int Optional(Box<string?> value) => value.Number;
            }
            """ + "public sealed class Converter<T> : Ankus.IPgDatumReader<Box<T>> where T : " + constraint +
            " { public Box<T> Read(Ankus.PgDatum value) => default; }";
        if (valid)
        {
            Compilation compilation = GenerateSqlControl(source);
            Assert.HasCount(1, DatumMappingManaged(compilation).Split('\n').Where(static line => line.Contains("RegisterValue<global::Box<", StringComparison.Ordinal)));
            Assert.IsEmpty(compilation.GetDiagnostics(context.CancellationToken).Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning));
        }
        else
        {
            AssertDatumMappingError(source, "ANKUS019", "not a valid C# constructed type");
        }
    }

    /// <summary>
    /// Supplies a generic mapped root and an open reader/writer pattern with independently closed factories.
    /// </summary>
    private const string TemplateBoxAndConverter = """
        public readonly record struct Box<T>(int Number);
        public sealed class Converter<T> : Ankus.IPgDatumReader<Box<T>>, Ankus.IPgDatumWriter<Box<T>>
        {
            public Box<T> Read(Ankus.PgDatum value) => default;
            public Ankus.PgDatum Write(Box<T> value, uint oid, Ankus.PgMemoryContext destination) => throw new System.InvalidOperationException();
        }
        """;
}
