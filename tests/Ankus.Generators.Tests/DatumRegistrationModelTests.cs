using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Ankus.Generators.Tests;

/// <summary>
/// Verifies immutable lazy registration independently of compiler instances and declaration coordinates.
/// </summary>
public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Implementation edits and movement preserve registration equality while current generated code still compiles.
    /// </summary>
    /// <param name="move">Whether to move declarations instead of editing a converter body.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void DatumRegistrationModelsPreserveContractsAcrossIndependentEdits(bool move)
    {
        string source = DatumMappingSource("public readonly record struct Value(int Number);")
            .Replace("=> default!;", "=> new Value(42);", StringComparison.Ordinal) + DatumMappingMethods("Value", string.Empty);
        CSharpCompilation initial = ModuleCompilation(source);
        CSharpCompilation edited = initial.ReplaceSyntaxTree(initial.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(
            move ? "\n\n" + source : source.Replace("Value(42)", "Value(43)", StringComparison.Ordinal),
            path: "Changed.cs", cancellationToken: context.CancellationToken));
        DatumRegistrationModel first = DatumRegistrationContract(initial);
        DatumRegistrationModel second = DatumRegistrationContract(edited);

        Assert.AreNotSame(initial.GetTypeByMetadataName("Value"), edited.GetTypeByMetadataName("Value"));
        Assert.AreEqual(first, second);
        Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
        Assert.AreEqual(first.Emit(), second.Emit());
        GeneratorDriver driver = RunModule(ModuleDriver(), initial, out Compilation previous);
        RunModule(driver, edited, out Compilation current);
        Assert.Contains(first.Emit().Trim(), DatumMappingManaged(previous));
        Assert.Contains(second.Emit().Trim(), DatumMappingManaged(current));
        Assert.AreEqual(ManifestValue(previous, "Ankus.NativeSource"), ManifestValue(current, "Ankus.NativeSource"));
        Assert.AreEqual(42, RunDatumRegistrationReader(previous));
        Assert.AreEqual(move ? 42 : 43, RunDatumRegistrationReader(current));
    }

    /// <summary>
    /// Scalar shape and independent reader/writer contracts select the exact compilable registration overload.
    /// </summary>
    /// <param name="reference">Whether the managed root is a class rather than a value type.</param>
    /// <param name="read">Whether the converter implements input conversion.</param>
    /// <param name="write">Whether the converter implements output conversion.</param>
    [TestMethod]
    [DataRow(false, true, true)]
    [DataRow(false, true, false)]
    [DataRow(false, false, true)]
    [DataRow(true, true, true)]
    [DataRow(true, true, false)]
    [DataRow(true, false, true)]
    public void DatumRegistrationModelsRetainExactScalarCapabilities(bool reference, bool read, bool write)
    {
        CSharpCompilation input = ModuleCompilation(DatumMappingSource(reference ? "public sealed class Value { }" : "public struct Value { }",
            reader: read, writer: write, name: "Mixed \" Name"));
        DatumRegistrationModel model = DatumRegistrationContract(input);
        RunModule(ModuleDriver(), input, out Compilation output);

        Assert.AreEqual(!reference, model.IsValueType);
        Assert.AreEqual(read, model.CanRead);
        Assert.AreEqual(write, model.CanWrite);
        Assert.AreEqual("global::Converter", model.Converter);
        Assert.IsNull(model.Bound);
        Assert.Contains("Register" + (reference ? "Reference" : "Value") + "<global::Value>", model.Emit());
        Assert.Contains(", " + (read ? "true" : "false") + ", " + (write ? "true" : "false") + ");", model.Emit());
        Assert.Contains("\"Mixed \\\" Name\"", model.Emit());
        Assert.Contains(model.Emit().Trim(), DatumMappingManaged(output));
    }

    /// <summary>
    /// Range identity changes independently of its bound and never captures an unnecessary scalar converter spelling.
    /// </summary>
    /// <param name="fixedSchema">Whether the edited range belongs to a fixed schema rather than the extension.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void DatumRegistrationModelsRetainIndependentRangeIdentity(bool fixedSchema)
    {
        string source = "[Ankus.PgRangeType(\"ranges\")] " + DatumMappingSource();
        CSharpCompilation initial = ModuleCompilation(source);
        CSharpCompilation edited = ModuleCompilation(source.Replace("\"ranges\"", fixedSchema
            ? "\"changed\", Origin=Ankus.PgTypeOrigin.External, Schema=\"placed\"" : "\"changed\"", StringComparison.Ordinal));
        DatumTypeDeclaration scalar = DatumRegistrationDeclaration(initial);
        DatumTypeDeclaration currentScalar = DatumRegistrationDeclaration(edited);
        Assert.IsTrue(RangeTypeDeclaration.TryCreate(scalar, out DatumTypeDeclaration? range));
        Assert.IsNotNull(range);
        Assert.IsTrue(RangeTypeDeclaration.TryCreate(currentScalar, out DatumTypeDeclaration? currentRange));
        Assert.IsNotNull(currentRange);
        DatumRegistrationModel previous = range.FreezeRegistration();
        DatumRegistrationModel current = currentRange.FreezeRegistration();

        Assert.AreEqual(scalar.FreezeRegistration(), currentScalar.FreezeRegistration());
        Assert.AreNotEqual(previous, current);
        Assert.AreEqual("global::Value", current.Bound);
        Assert.IsNull(current.Converter);
        Assert.AreEqual(fixedSchema, current.External);
        Assert.AreEqual(fixedSchema ? "placed" : null, current.Schema);
        Assert.AreEqual("        global::Ankus.PgDatumRegistry.RegisterRange<global::Value>(\"changed\", " +
            (fixedSchema ? "\"placed\"" : "null") + ", global::Ankus.PgTypeOrigin." +
            (fixedSchema ? "External" : "ThisExtension") + ");" + Environment.NewLine, current.Emit());
    }

    /// <summary>
    /// Provider selection preserves Roslyn's exact type equality, including tuple labels in constructed roots.
    /// </summary>
    /// <param name="matchingNames">Whether the provider and callback use identical tuple labels.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void DatumProviderModelsPreserveTupleNamesInSemanticIdentity(bool matchingNames)
    {
        const string Source = """
            [assembly: Ankus.PgSql("mapped", "CREATE DOMAIN item AS integer;", Relocatable=true)]
            [assembly: Ankus.PgSqlTypeProvider("mapped", typeof(Value<(int X,int Y)>))]
            [Ankus.PgDatumType("item", typeof(Converter<>))]
            public struct Value<T> { }
            public sealed class Converter<T> : Ankus.IPgDatumReader<Value<T>>, Ankus.IPgDatumWriter<Value<T>>
            {
                public Value<T> Read(Ankus.PgDatum value) => default;
                public Ankus.PgDatum Write(Value<T> value, uint typeOid, Ankus.PgMemoryContext owner) => throw new System.InvalidOperationException();
            }
            public static class Functions
            {
                [Ankus.PgFunction]
                public static Value<(int Left,int Right)> Echo(Value<(int Left,int Right)> value) => value;
            }
            """;
        string source = matchingNames ? Source.Replace("int X,int Y", "int Left,int Right", StringComparison.Ordinal) : Source;
        CSharpCompilation input = ModuleCompilation(source);
        INamedTypeSymbol? functions = input.GetTypeByMetadataName("Functions");
        Assert.IsNotNull(functions);
        IMethodSymbol method = Assert.ContainsSingle(functions.GetMembers("Echo").OfType<IMethodSymbol>());
        AttributeData provider = Assert.ContainsSingle(input.Assembly.GetAttributes().Where(static attribute =>
            attribute.AttributeClass?.ToDisplayString() == "Ankus.PgSqlTypeProviderAttribute"));
        ITypeSymbol supplied = Assert.IsInstanceOfType<ITypeSymbol>(provider.ConstructorArguments[1].Value);

        Assert.AreEqual(matchingNames, SymbolEqualityComparer.Default.Equals(method.ReturnType, supplied));
        Assert.AreEqual(DeclarationIdentity.Create(method.ReturnType), DeclarationIdentity.Create(supplied));
        Assert.AreEqual(matchingNames, ManagedTypeIdentity.Create(method.ReturnType) == ManagedTypeIdentity.Create(supplied));
        if (!matchingNames)
        {
            AssertDatumMappingError(source, "ANKUS005", "requires a PgSqlTypeProvider naming its managed identity");
            return;
        }

        RunModule(ModuleDriver(), input, out Compilation output);
        string sql = InstallationBody(output);
        int domain = sql.IndexOf("CREATE DOMAIN item AS integer;", StringComparison.Ordinal);
        int function = sql.IndexOf("CREATE FUNCTION", StringComparison.Ordinal);
        Assert.IsGreaterThan(-1, domain);
        Assert.IsGreaterThan(domain, function);
        Assert.Contains("\"value\" \"item\"", sql);
        Assert.Contains("RETURNS \"item\"", sql);
        Assert.Contains("new global::Converter<", DatumMappingManaged(output));
    }

    /// <summary>
    /// Resolves one accessible concrete scalar using the same semantic validator as production discovery.
    /// </summary>
    /// <param name="compilation">The current independent compilation.</param>
    /// <returns>The validated declaration.</returns>
    private static DatumTypeDeclaration DatumRegistrationDeclaration(CSharpCompilation compilation)
    {
        INamedTypeSymbol? type = compilation.GetTypeByMetadataName("Value");
        Assert.IsNotNull(type);
        DatumTypeDeclaration? declaration = DatumTypeDeclaration.Create(type, compilation.Assembly);
        Assert.IsNotNull(declaration);
        return declaration;
    }

    /// <summary>
    /// Detaches the exact scalar registration values from a validated independent compilation.
    /// </summary>
    /// <param name="compilation">The source owning the declaration.</param>
    /// <returns>The immutable registration contract.</returns>
    private static DatumRegistrationModel DatumRegistrationContract(CSharpCompilation compilation)
        => DatumRegistrationDeclaration(compilation).FreezeRegistration();

    /// <summary>
    /// Executes the current compiled converter body without entering PostgreSQL or generating code in a consumer.
    /// </summary>
    /// <param name="compilation">The actual generated compilation.</param>
    /// <returns>The value produced by the current reader implementation.</returns>
    private int RunDatumRegistrationReader(Compilation compilation)
    {
        using var bytes = new MemoryStream();
        Microsoft.CodeAnalysis.Emit.EmitResult emitted = compilation.Emit(bytes, cancellationToken: context.CancellationToken);
        Assert.IsTrue(emitted.Success, string.Join("\n", emitted.Diagnostics));
        bytes.Position = 0;
        var load = new AssemblyLoadContext("DatumRegistrationReader", isCollectible: true);
        try
        {
            Assembly assembly = load.LoadFromStream(bytes);
            Type? converter = assembly.GetType("Converter");
            Assert.IsNotNull(converter);
            MethodInfo? method = converter.GetMethod("Read");
            Assert.IsNotNull(method);
            object? value = method.Invoke(Activator.CreateInstance(converter), [default(PgDatum)]);
            Assert.IsNotNull(value);
            PropertyInfo? number = value.GetType().GetProperty("Number");
            Assert.IsNotNull(number);
            return Assert.IsInstanceOfType<int>(number.GetValue(value));
        }
        finally
        {
            load.Unload();
        }
    }
}
