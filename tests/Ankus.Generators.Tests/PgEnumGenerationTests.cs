using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Ankus.Generators.Tests;

public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Enum-only extensions retain quoted identifiers, exact labels, and declaration order independently of numeric values.
    /// </summary>
    [TestMethod]
    public void EnumDeclarationsPreserveExactSqlAndSourceOrder()
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            [Ankus.PgEnum(Name = "Mood \" Kind", Schema = "Case \" Schema")]
            public enum Mood : long
            {
                [Ankus.PgEnumLabel("It's\\ready")] Ready = long.MaxValue,
                [Ankus.PgEnumLabel("")] Empty = long.MinValue,
                [Ankus.PgEnumLabel("ready")] Lower = -1,
                [Ankus.PgEnumLabel("READY")] Upper = 7,
                @event = 0
            }
            """);
        AssertEnumCompilationSucceeds(compilation, diagnostics);
        Assert.AreEqual("CREATE TYPE \"Case \"\" Schema\".\"Mood \"\" Kind\" AS ENUM (E'It''s\\\\ready', E'', E'ready', E'READY', E'event');\n",
            ManifestValue(compilation, "Ankus.Sql"));
        Assert.AreEqual("false", ManifestValue(compilation, "Ankus.Relocatable"));
        Assert.AreEqual("Pg_magic_func\n", ManifestValue(compilation, "Ankus.Exports"));
    }

    /// <summary>
    /// An enum without members still emits a relocatable enum-only extension and a valid closed registration.
    /// </summary>
    [TestMethod]
    public void EmptyEnumGeneratesStandaloneExtension()
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("[Ankus.PgEnum] internal enum HTTPStatus { }");
        AssertEnumCompilationSucceeds(compilation, diagnostics);
        Assert.AreEqual("CREATE TYPE \"http_status\" AS ENUM ();\n", ManifestValue(compilation, "Ankus.Sql"));
        Assert.AreEqual("true", ManifestValue(compilation, "Ankus.Relocatable"));
        Assert.AreEqual("Pg_magic_func\n", ManifestValue(compilation, "Ankus.Exports"));
    }

    /// <summary>
    /// Generated module initialization makes exact label conversions callable without a PostgreSQL backend.
    /// </summary>
    [TestMethod]
    public void EnumModuleInitializerRegistersExactClosedConversions()
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            [Ankus.PgEnum]
            public enum Mood : ulong
            {
                [Ankus.PgEnumLabel("maximum 🐘")] Maximum = ulong.MaxValue,
                [Ankus.PgEnumLabel("")] Empty = 3,
                @event = 1
            }
            public static class Probe
            {
                public static string Labels() => string.Join("|",
                    Ankus.PgEnums.GetLabel(Mood.Maximum),
                    ((ulong)Ankus.PgEnums.Parse<Mood>("maximum 🐘")).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Ankus.PgEnums.GetLabel(Ankus.PgEnums.Parse<Mood>("")),
                    Ankus.PgEnums.GetLabel(Mood.@event));
            }
            """);
        AssertEnumCompilationSucceeds(compilation, diagnostics);
        using var stream = new MemoryStream();
        Microsoft.CodeAnalysis.Emit.EmitResult emitted = compilation.Emit(stream, cancellationToken: context.CancellationToken);
        Assert.IsEmpty(emitted.Diagnostics.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.IsTrue(emitted.Success);
        stream.Position = 0;
        var loadContext = new AssemblyLoadContext("EnumGeneratorProbe", isCollectible: true);
        try
        {
            Assembly assembly = loadContext.LoadFromStream(stream);
            Type? probe = assembly.GetType("Probe");
            Assert.IsNotNull(probe);
            MethodInfo? method = probe.GetMethod("Labels", BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(method);
            Assert.AreEqual("maximum 🐘|18446744073709551615||event", method.Invoke(null, null));
        }
        finally
        {
            loadContext.Unload();
        }
    }

    /// <summary>
    /// Every nullable scalar, vector, shaped-array, and variadic enum signature produces compilable wrappers with its exact SQL contract.
    /// </summary>
    /// <param name="managed">The managed result and argument type.</param>
    /// <param name="variadic">Whether the argument is declared with params.</param>
    /// <param name="sqlType">The expected scalar or array SQL type.</param>
    /// <param name="strict">Whether the argument requires strict dispatch.</param>
    [TestMethod]
    [DataRow("Mood", false, "\"mood\"", true)]
    [DataRow("Mood?", false, "\"mood\"", false)]
    [DataRow("Mood[]", false, "\"mood\"[]", true)]
    [DataRow("Mood?[]", false, "\"mood\"[]", true)]
    [DataRow("Mood[]?", false, "\"mood\"[]", false)]
    [DataRow("Mood?[]?", false, "\"mood\"[]", false)]
    [DataRow("Ankus.PgArray<Mood>", false, "\"mood\"[]", true)]
    [DataRow("Ankus.PgArray<Mood?>", false, "\"mood\"[]", true)]
    [DataRow("Ankus.PgArray<Mood>?", false, "\"mood\"[]", false)]
    [DataRow("Ankus.PgArray<Mood?>?", false, "\"mood\"[]", false)]
    [DataRow("Mood[]", true, "\"mood\"[]", true)]
    [DataRow("Mood?[]", true, "\"mood\"[]", true)]
    public void EnumSignaturesCompile(string managed, bool variadic, string sqlType, bool strict)
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate($$"""
            #nullable enable
            [Ankus.PgEnum] public enum Mood { Happy, Sad }
            public static class Functions
            {
                [Ankus.PgFunction] public static {{managed}} Echo({{(variadic ? "params " : "")}}{{managed}} value) => value;
            }
            """);
        AssertEnumCompilationSucceeds(compilation, diagnostics);
        string[] statements = ManifestValue(compilation, "Ankus.Sql").Replace("\nRETURNS", " RETURNS", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.HasCount(2, statements);
        Assert.AreEqual("CREATE TYPE \"mood\" AS ENUM (E'Happy', E'Sad');", statements[0]);
        Assert.StartsWith("CREATE FUNCTION \"echo\"(" + (variadic ? "VARIADIC " : "") + "\"value\" " + sqlType + ") RETURNS " + sqlType + " AS ", statements[1]);
        Assert.Contains(strict ? " PARALLEL UNSAFE STRICT " : " PARALLEL UNSAFE CALLED ON NULL INPUT ", statements[1]);
    }

    /// <summary>
    /// All legal enum underlying widths support source-order DDL and label-based optional defaults without numeric truncation.
    /// </summary>
    /// <param name="underlying">The enum integral storage type.</param>
    /// <param name="minimum">The lowest declared member constant.</param>
    /// <param name="maximum">The highest declared member constant.</param>
    [TestMethod]
    [DataRow("sbyte", "sbyte.MinValue", "sbyte.MaxValue")]
    [DataRow("byte", "byte.MinValue", "byte.MaxValue")]
    [DataRow("short", "short.MinValue", "short.MaxValue")]
    [DataRow("ushort", "ushort.MinValue", "ushort.MaxValue")]
    [DataRow("int", "int.MinValue", "int.MaxValue")]
    [DataRow("uint", "uint.MinValue", "uint.MaxValue")]
    [DataRow("long", "long.MinValue", "long.MaxValue")]
    [DataRow("ulong", "ulong.MinValue", "ulong.MaxValue")]
    public void EnumUnderlyingTypesPreserveDefaults(string underlying, string minimum, string maximum)
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate($$"""
            [Ankus.PgEnum(Name = "Order", Schema = "Enums")]
            public enum Value : {{underlying}}
            {
                [Ankus.PgEnumLabel("highest's")] High = {{maximum}},
                [Ankus.PgEnumLabel("lowest")] Low = {{minimum}}
            }
            public static class Functions
            {
                [Ankus.PgFunction] public static Value Echo(Value high = Value.High, Value? low = Value.Low, Value? absent = null) => high;
            }
            """);
        AssertEnumCompilationSucceeds(compilation, diagnostics);
        string[] statements = ManifestValue(compilation, "Ankus.Sql").Replace("\nRETURNS", " RETURNS", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.AreEqual("CREATE TYPE \"Enums\".\"Order\" AS ENUM (E'highest''s', E'lowest');", statements[0]);
        Assert.StartsWith("CREATE FUNCTION \"echo\"(\"high\" \"Enums\".\"Order\" DEFAULT (E'highest''s'::\"Enums\".\"Order\"), \"low\" \"Enums\".\"Order\" DEFAULT (E'lowest'::\"Enums\".\"Order\"), \"absent\" \"Enums\".\"Order\" DEFAULT (NULL)) RETURNS \"Enums\".\"Order\" AS ", statements[1]);
    }

    /// <summary>
    /// Undefined optional constants require explicit SQL defaults instead of silently choosing a different label.
    /// </summary>
    /// <param name="expression">The undefined C# enum default.</param>
    [TestMethod]
    [DataRow("(Mood)99")]
    [DataRow("default")]
    public void UndefinedEnumDefaultsAreDiagnosed(string expression)
    {
        (_, ImmutableArray<Diagnostic> diagnostics) = Generate($$"""
            [Ankus.PgEnum] public enum Mood { Happy = 1 }
            public static class Functions
            {
                [Ankus.PgFunction] public static Mood Echo(Mood value = {{expression}}) => value;
            }
            """);
        Diagnostic diagnostic = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual("ANKUS004", diagnostic.Id);
        Assert.Contains("explicit PgParameter.Default", diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Declaring an explicit SQL default can intentionally replace an otherwise undefined managed enum constant.
    /// </summary>
    [TestMethod]
    public void ExplicitEnumSqlDefaultOverridesManagedConstant()
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            [Ankus.PgEnum] public enum Mood { Happy = 1 }
            public static class Functions
            {
                [Ankus.PgFunction] public static Mood Echo([Ankus.PgParameter(Default = "'Happy'::mood")] Mood value = default) => value;
            }
            """);
        AssertEnumCompilationSucceeds(compilation, diagnostics);
        Assert.Contains("\"value\" \"mood\" DEFAULT ('Happy'::mood)", ManifestValue(compilation, "Ankus.Sql"));
    }

    /// <summary>
    /// Valid enum labels and identifiers retain every byte at the PostgreSQL length boundary, while overlength values fail.
    /// </summary>
    /// <param name="target">The identifier or label being constrained.</param>
    /// <param name="byteLength">The encoded UTF-8 length, using two-byte characters to distinguish bytes from UTF-16 length.</param>
    [TestMethod]
    [DataRow("Name", 62)]
    [DataRow("Name", 63)]
    [DataRow("Name", 64)]
    [DataRow("Schema", 62)]
    [DataRow("Schema", 63)]
    [DataRow("Schema", 64)]
    [DataRow("Label", 62)]
    [DataRow("Label", 63)]
    [DataRow("Label", 64)]
    public void EnumNamesAndLabelsUseUtf8ByteLimits(string target, int byteLength)
    {
        string value = new('é', byteLength / 2);
        if (byteLength % 2 != 0)
        {
            value += 'x';
        }

        string literal = SymbolDisplay.FormatLiteral(value, quote: true);
        string attribute = target == "Label" ? "[Ankus.PgEnum]" : "[Ankus.PgEnum(" + target + " = " + literal + ")]";
        string member = target == "Label" ? "[Ankus.PgEnumLabel(" + literal + ")] Ready" : "Ready";
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate(attribute + " public enum Mood { " + member + " }");
        if (byteLength > 63)
        {
            Assert.AreEqual("ANKUS006", Assert.ContainsSingle(diagnostics).Id);
            return;
        }

        AssertEnumCompilationSucceeds(compilation, diagnostics);
        string expectedType = target == "Name" ? "\"" + value + "\"" : target == "Schema" ? "\"" + value + "\".\"mood\"" : "\"mood\"";
        string expectedLabel = target == "Label" ? value : "Ready";
        Assert.AreEqual("CREATE TYPE " + expectedType + " AS ENUM (E'" + expectedLabel + "');\n", ManifestValue(compilation, "Ankus.Sql"));
    }

    /// <summary>
    /// Invalid enum shape, inaccessible declarations, aliases, labels, and identifiers fail before extension generation.
    /// </summary>
    /// <param name="source">The invalid enum declaration.</param>
    /// <param name="message">The expected diagnostic reason.</param>
    [TestMethod]
    [DataRow("[System.Flags, Ankus.PgEnum] public enum Mood { Happy = 1, Sad = 2 }", "without Flags")]
    [DataRow("[Ankus.PgEnum] public enum Mood { Happy = 1, Sad = 1 }", "distinct numeric values")]
    [DataRow("[Ankus.PgEnum] public enum Mood { [Ankus.PgEnumLabel(\"same\")] Happy, [Ankus.PgEnumLabel(\"same\")] Sad }", "labels must be distinct")]
    [DataRow("[Ankus.PgEnum] public enum Mood { [Ankus.PgEnumLabel(null!)] Happy }", "labels must be distinct")]
    [DataRow("[Ankus.PgEnum] public enum Mood { [Ankus.PgEnumLabel(\"a\\0b\")] Happy }", "labels must be distinct")]
    [DataRow("[Ankus.PgEnum] public enum Mood { [Ankus.PgEnumLabel(\"\\ud800\")] Happy }", "labels must be distinct")]
    [DataRow("[Ankus.PgEnum] public enum Mood { [Ankus.PgEnumLabel(\"\\udc00\")] Happy }", "labels must be distinct")]
    [DataRow("[Ankus.PgEnum(Name = \"\")] public enum Mood { Happy }", "nonempty identifiers")]
    [DataRow("[Ankus.PgEnum(Name = \"a\\0b\")] public enum Mood { Happy }", "nonempty identifiers")]
    [DataRow("[Ankus.PgEnum(Name = \"\\ud800\")] public enum Mood { Happy }", "nonempty identifiers")]
    [DataRow("[Ankus.PgEnum(Schema = \"\")] public enum Mood { Happy }", "nonempty identifiers")]
    [DataRow("[Ankus.PgEnum(Schema = \"a\\0b\")] public enum Mood { Happy }", "nonempty identifiers")]
    [DataRow("[Ankus.PgEnum(Schema = \"\\ud800\")] public enum Mood { Happy }", "nonempty identifiers")]
    [DataRow("public class Container { [Ankus.PgEnum] private enum Mood { Happy } }", "accessible, non-generic")]
    [DataRow("public class Outer { private class Container { [Ankus.PgEnum] public enum Mood { Happy } } }", "accessible, non-generic")]
    [DataRow("public class Container<T> { [Ankus.PgEnum] public enum Mood { Happy } }", "accessible, non-generic")]
    [DataRow("[Ankus.PgEnum] file enum Mood { Happy }", "accessible, non-generic")]
    public void InvalidEnumDeclarationsAreDiagnosed(string source, string message)
    {
        (_, ImmutableArray<Diagnostic> diagnostics) = Generate(source);
        Diagnostic diagnostic = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual("ANKUS006", diagnostic.Id);
        Assert.Contains(message, diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Ordinary CLR enums are never implicitly exposed as PostgreSQL enum types.
    /// </summary>
    /// <param name="managed">The unregistered enum signature.</param>
    [TestMethod]
    [DataRow("Mood")]
    [DataRow("Mood?")]
    [DataRow("Mood[]")]
    [DataRow("Ankus.PgArray<Mood?>")]
    public void UnattributedEnumSignaturesAreDiagnosed(string managed)
    {
        (_, ImmutableArray<Diagnostic> diagnostics) = Generate($$"""
            public enum Mood { Happy }
            public static class Functions { [Ankus.PgFunction] public static {{managed}} Echo({{managed}} value) => value; }
            """);
        Assert.AreEqual("ANKUS001", Assert.ContainsSingle(diagnostics).Id);
    }

    /// <summary>
    /// Enum schemas inherit from the nearest container, allow explicit overrides, and keep equal names in distinct schemas independent.
    /// </summary>
    [TestMethod]
    public void EnumSchemaInheritanceAndOverridesRemainDistinct()
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            [Ankus.PgSchema("outer")]
            internal static class Outer
            {
                internal static class Nested { [Ankus.PgEnum(Name = "same")] internal enum First { One } }
                [Ankus.PgSchema("inner")]
                internal static class Inner
                {
                    [Ankus.PgEnum(Name = "same")] internal enum Second { Two }
                    [Ankus.PgEnum(Name = "same", Schema = "existing")] internal enum Third { Three }
                }
            }
            """);
        AssertEnumCompilationSucceeds(compilation, diagnostics);
        Assert.AreEqual("CREATE SCHEMA IF NOT EXISTS \"inner\";\nCREATE SCHEMA IF NOT EXISTS \"outer\";\n" +
            "CREATE TYPE \"inner\".\"same\" AS ENUM (E'Two');\nCREATE TYPE \"existing\".\"same\" AS ENUM (E'Three');\nCREATE TYPE \"outer\".\"same\" AS ENUM (E'One');\n",
            ManifestValue(compilation, "Ankus.Sql"));
        Assert.AreEqual("false", ManifestValue(compilation, "Ankus.Relocatable"));
    }

    /// <summary>
    /// Installation constraints order schema, custom SQL, enum, function and dependent SQL even when declarations are reversed.
    /// </summary>
    [TestMethod]
    public void EnumGraphOrdersCustomSqlAndFunctionDependencies()
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            [assembly: Ankus.PgSql("after", "SELECT 'after';", Requires = new[] { "function" })]
            [assembly: Ankus.PgSql("before", "SELECT 'before';", Requires = new[] { "schema" })]
            public static class Functions
            {
                [Ankus.PgFunction(Id = "function")] public static Values.Mood Echo(Values.Mood value) => value;
            }
            [Ankus.PgSchema("types", Id = "schema")]
            public static class Values
            {
                [Ankus.PgEnum(Id = "mood", Requires = new[] { "before" })] public enum Mood { Happy }
            }
            """);
        AssertEnumCompilationSucceeds(compilation, diagnostics);
        string[] statements = ManifestValue(compilation, "Ankus.Sql").Replace("\nRETURNS", " RETURNS", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.HasCount(5, statements);
        Assert.AreEqual("CREATE SCHEMA IF NOT EXISTS \"types\";", statements[0]);
        Assert.AreEqual("SELECT 'before';", statements[1]);
        Assert.AreEqual("CREATE TYPE \"types\".\"mood\" AS ENUM (E'Happy');", statements[2]);
        Assert.StartsWith("CREATE FUNCTION \"echo\"(\"value\" \"types\".\"mood\") RETURNS \"types\".\"mood\" AS ", statements[3]);
        Assert.AreEqual("SELECT 'after';", statements[4]);
    }

    /// <summary>
    /// A reverse SQL edge exposes automatic enum dependencies for arguments and results, instead of relying on incidental sort order.
    /// </summary>
    /// <param name="method">The function referencing the enum.</param>
    [TestMethod]
    [DataRow("public static int Echo(Mood value) => 1;")]
    [DataRow("public static int Echo(Mood? value) => 1;")]
    [DataRow("public static int Echo(Mood[] value) => 1;")]
    [DataRow("public static int Echo(Ankus.PgArray<Mood?> value) => 1;")]
    [DataRow("public static Mood Echo() => Mood.Happy;")]
    [DataRow("public static Mood[] Echo() => new[] { Mood.Happy };")]
    public void EnumFunctionDependenciesParticipateInCycleDetection(string method)
    {
        (_, ImmutableArray<Diagnostic> diagnostics) = Generate($$"""
            [assembly: Ankus.PgSql("reverse", "SELECT 1;", Requires = new[] { "function" }, Before = new[] { "mood" })]
            [Ankus.PgEnum(Id = "mood")] public enum Mood { Happy }
            public static class Functions { [Ankus.PgFunction(Id = "function")] {{method}} }
            """);
        Diagnostic diagnostic = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual("ANKUS005", diagnostic.Id);
        Assert.Contains("cycle", diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Duplicate type names, missing graph references, and cycles fail as SQL entity graph diagnostics.
    /// </summary>
    /// <param name="source">The invalid enum graph.</param>
    /// <param name="message">The expected diagnostic reason.</param>
    [TestMethod]
    [DataRow("[Ankus.PgEnum(Name = \"same\")] public enum One { A } [Ankus.PgEnum(Name = \"same\")] public enum Two { B }", "Duplicate PostgreSQL enum type name")]
    [DataRow("[Ankus.PgEnum(Id = \"same\")] public enum One { A } [Ankus.PgEnum(Id = \"same\")] public enum Two { B }", "declared more than once")]
    [DataRow("[Ankus.PgEnum(Requires = new[] { \"missing\" })] public enum Mood { Happy }", "missing dependency 'missing'")]
    [DataRow("[Ankus.PgEnum(Id = \"mood\", Requires = new[] { \"mood\" })] public enum Mood { Happy }", "cycle")]
    [DataRow("[Ankus.PgSchema(\"s\", Requires = new[] { \"mood\" })] public static class Values { [Ankus.PgEnum(Id = \"mood\")] public enum Mood { Happy } }", "cycle")]
    [DataRow("[Ankus.PgEnum(Id = \"\")] public enum Mood { Happy }", "nonempty text")]
    [DataRow("[Ankus.PgEnum(Requires = null!)] public enum Mood { Happy }", "invalid Requires")]
    public void InvalidEnumSqlGraphsAreDiagnosed(string source, string message)
    {
        (_, ImmutableArray<Diagnostic> diagnostics) = Generate(source);
        Diagnostic diagnostic = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual("ANKUS005", diagnostic.Id);
        Assert.Contains(message, diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Reordering independent managed enum declarations cannot alter the installation SQL or generated registration source.
    /// </summary>
    [TestMethod]
    public void EnumOutputIsDeterministicAcrossDeclarationOrder()
    {
        const string first = "[Ankus.PgEnum] public enum Alpha { Last = 99, First = -2 }";
        const string second = "[Ankus.PgEnum] public enum Zeta { Second = 2, First = 1 }";
        (Compilation left, ImmutableArray<Diagnostic> leftDiagnostics) = Generate(first + second);
        (Compilation right, ImmutableArray<Diagnostic> rightDiagnostics) = Generate(second + first);
        AssertEnumCompilationSucceeds(left, leftDiagnostics);
        AssertEnumCompilationSucceeds(right, rightDiagnostics);
        const string expected = "CREATE TYPE \"alpha\" AS ENUM (E'Last', E'First');\nCREATE TYPE \"zeta\" AS ENUM (E'Second', E'First');\n";
        Assert.AreEqual(expected, ManifestValue(left, "Ankus.Sql"));
        Assert.AreEqual(expected, ManifestValue(right, "Ankus.Sql"));
        Assert.AreSequenceEqual(left.SyntaxTrees.Skip(1).Select(static tree => tree.ToString()),
            right.SyntaxTrees.Skip(1).Select(static tree => tree.ToString()));
    }

    private void AssertEnumCompilationSucceeds(Compilation compilation, ImmutableArray<Diagnostic> diagnostics)
    {
        Assert.IsEmpty(diagnostics);
        Assert.IsEmpty(compilation.GetDiagnostics(context.CancellationToken).Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
    }
}
