using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Ankus.Generators.Tests;

public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Every supported conversion family compiles as a set element with the same PostgreSQL datum identity.
    /// </summary>
    /// <param name="managed">The declared element type.</param>
    /// <param name="sqlType">The exact PostgreSQL element type.</param>
    [TestMethod]
    [DataRow("bool", "boolean")]
    [DataRow("sbyte", "\"char\"")]
    [DataRow("short", "smallint")]
    [DataRow("int", "integer")]
    [DataRow("long", "bigint")]
    [DataRow("uint", "oid")]
    [DataRow("float", "real")]
    [DataRow("double", "double precision")]
    [DataRow("int?", "integer")]
    [DataRow("bool?", "boolean")]
    [DataRow("string", "text")]
    [DataRow("string?", "text")]
    [DataRow("byte[]", "bytea")]
    [DataRow("byte[]?", "bytea")]
    [DataRow("System.Guid", "uuid")]
    [DataRow("Ankus.PgJson", "json")]
    [DataRow("Ankus.PgJsonb?", "jsonb")]
    [DataRow("decimal", "numeric")]
    [DataRow("Ankus.PgNumeric?", "numeric")]
    [DataRow("System.DateOnly", "date")]
    [DataRow("Ankus.PgDate?", "date")]
    [DataRow("System.TimeOnly", "time")]
    [DataRow("Ankus.PgTime?", "time")]
    [DataRow("Ankus.PgTimeTz", "timetz")]
    [DataRow("System.DateTime", "timestamp")]
    [DataRow("Ankus.PgTimestamp?", "timestamp")]
    [DataRow("System.DateTimeOffset", "timestamptz")]
    [DataRow("Ankus.PgTimestampTz?", "timestamptz")]
    [DataRow("System.TimeSpan", "interval")]
    [DataRow("Ankus.PgInterval?", "interval")]
    [DataRow("Ankus.PgInet", "inet")]
    [DataRow("Ankus.PgCidr?", "cidr")]
    [DataRow("System.Net.IPAddress?", "inet")]
    [DataRow("System.Net.IPNetwork", "cidr")]
    [DataRow("Ankus.PgPoint", "point")]
    [DataRow("Ankus.PgLine", "line")]
    [DataRow("Ankus.PgLineSegment", "lseg")]
    [DataRow("Ankus.PgBox", "box")]
    [DataRow("Ankus.PgCircle", "circle")]
    [DataRow("Ankus.PgPath", "path")]
    [DataRow("Ankus.PgPolygon", "polygon")]
    [DataRow("Ankus.PgRange<int>", "int4range")]
    [DataRow("Ankus.PgRange<Ankus.PgDate>?", "daterange")]
    [DataRow("int?[]?", "integer[]")]
    [DataRow("string?[]", "text[]")]
    [DataRow("byte[]?[]?", "bytea[]")]
    [DataRow("Ankus.PgArray<Ankus.PgNumeric?>", "numeric[]")]
    [DataRow("Ankus.PgArray<Ankus.PgPoint?>?", "point[]")]
    [DataRow("Ankus.PgArray<Ankus.PgRange<int>?>", "int4range[]")]
    public void SetElementConversionFamiliesCompile(string managed, string sqlType)
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate($$"""
            public static class Functions
            {
                [Ankus.PgFunction]
                public static System.Collections.Generic.IEnumerable<{{managed}}> Rows()
                    => System.Array.Empty<{{managed}}>();
            }
            """);
        AssertSetCompilationSucceeds(compilation, diagnostics);
        string sql = SetFunctionSql(compilation);
        Assert.StartsWith("CREATE FUNCTION \"rows\"() RETURNS SETOF " + sqlType + " AS ", sql);
        Assert.EndsWith(" ROWS 1000;", sql);
        Assert.AreEqual("true", ManifestValue(compilation, "Ankus.Relocatable"));
    }

    /// <summary>
    /// Existing enumerable-shaped scalar mappings remain scalar unless the declared return type is IEnumerable.
    /// </summary>
    /// <param name="managed">The existing scalar return type.</param>
    /// <param name="sqlType">The existing scalar PostgreSQL mapping.</param>
    [TestMethod]
    [DataRow("string", "text")]
    [DataRow("byte[]", "bytea")]
    [DataRow("int[]", "integer[]")]
    [DataRow("Ankus.PgArray<int>", "integer[]")]
    [DataRow("Ankus.PgPath", "path")]
    [DataRow("Ankus.PgPolygon", "polygon")]
    public void SetDiscoveryPreservesExistingScalarMappings(string managed, string sqlType)
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate($$"""
            public static class Functions
            {
                [Ankus.PgFunction] public static {{managed}} Rows({{managed}} value) => value;
            }
            """);
        AssertSetCompilationSucceeds(compilation, diagnostics);
        string sql = SetFunctionSql(compilation);
        Assert.StartsWith("CREATE FUNCTION \"rows\"(\"value\" " + sqlType + ") RETURNS " + sqlType + " AS ", sql);
        Assert.DoesNotContain("SETOF", sql);
        Assert.DoesNotContain(" ROWS ", sql);
    }

    /// <summary>
    /// Nullable sequences and nullable elements remain separate compilable contracts without changing PostgreSQL element identity.
    /// </summary>
    /// <param name="element">The element type and its nullability.</param>
    /// <param name="sequenceNullable">Whether the sequence reference may be null.</param>
    /// <param name="sqlType">The PostgreSQL element type.</param>
    [TestMethod]
    [DataRow("int", false, "integer")]
    [DataRow("int", true, "integer")]
    [DataRow("int?", false, "integer")]
    [DataRow("int?", true, "integer")]
    [DataRow("string?", false, "text")]
    [DataRow("string?", true, "text")]
    public void SetSequenceAndElementNullabilityCompileIndependently(string element, bool sequenceNullable, string sqlType)
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate($$"""
            public static class Functions
            {
                [Ankus.PgFunction]
                public static System.Collections.Generic.IEnumerable<{{element}}>{{(sequenceNullable ? "?" : "")}} Rows()
                    => {{(sequenceNullable ? "null" : "System.Array.Empty<" + element + ">()")}};
            }
            """);
        AssertSetCompilationSucceeds(compilation, diagnostics);
        Assert.StartsWith("CREATE FUNCTION \"rows\"() RETURNS SETOF " + sqlType + " AS ", SetFunctionSql(compilation));
    }

    /// <summary>
    /// Named tuples preserve SQL column order and map names using the existing snake-case convention.
    /// </summary>
    [TestMethod]
    public void TableTupleColumnsGenerateExactNamesAndTypes()
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            public static class Functions
            {
                [Ankus.PgFunction]
                public static System.Collections.Generic.IEnumerable<(int HTTPCode, string? DisplayName, bool? @event)> Rows()
                    => new (int, string?, bool?)[] { (201, "created", null) };
            }
            """);
        AssertSetCompilationSucceeds(compilation, diagnostics);
        Assert.StartsWith("CREATE FUNCTION \"rows\"() RETURNS TABLE (\"http_code\" integer, \"display_name\" text, \"event\" boolean) AS ",
            SetFunctionSql(compilation));
    }

    /// <summary>
    /// Explicit column names override tuple names exactly and allow unnamed tuples or a scalar one-column TABLE.
    /// </summary>
    /// <param name="element">The scalar or tuple element type.</param>
    /// <param name="names">The column-name attribute argument list.</param>
    /// <param name="columns">The exact TABLE column declaration.</param>
    [TestMethod]
    [DataRow("int", "\"Only Value\"", "\"Only Value\" integer")]
    [DataRow("int?", "\"Maybe\"", "\"Maybe\" integer")]
    [DataRow("System.ValueTuple<int>", "\"Only Value\"", "\"Only Value\" integer")]
    [DataRow("System.ValueTuple<string?>", "\"Text\"", "\"Text\" text")]
    [DataRow("(int, string?)", "\"Number\", \"Display \\\" Text\"", "\"Number\" integer, \"Display \"\" Text\" text")]
    [DataRow("(int Original, string? Label)", "\"Replacement\", \"évent\"", "\"Replacement\" integer, \"évent\" text")]
    [DataRow("(int A, int B)", "\"Id\", \"id\"", "\"Id\" integer, \"id\" integer")]
    public void TableColumnOverridesSupportScalarAndTupleRows(string element, string names, string columns)
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate($$"""
            public static class Functions
            {
                [Ankus.PgFunction]
                [return: Ankus.PgColumnNames({{names}})]
                public static System.Collections.Generic.IEnumerable<{{element}}> Rows()
                    => System.Array.Empty<{{element}}>();
            }
            """);
        AssertSetCompilationSucceeds(compilation, diagnostics);
        Assert.StartsWith("CREATE FUNCTION \"rows\"() RETURNS TABLE (" + columns + ") AS ", SetFunctionSql(compilation));
    }

    /// <summary>
    /// Long tuples flatten ValueTuple.Rest and retain every named column beyond the seventh element.
    /// </summary>
    /// <param name="arity">The number of named output columns.</param>
    [TestMethod]
    [DataRow(2)]
    [DataRow(7)]
    [DataRow(8)]
    [DataRow(32)]
    [DataRow(1663)]
    [DataRow(1664)]
    public void LongTableTuplesCompileEveryOutputColumn(int arity)
    {
        string element = "(" + string.Join(", ", Enumerable.Range(1, arity).Select(static index =>
            "int Column" + index.ToString(CultureInfo.InvariantCulture))) + ")";
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate($$"""
            public static class Functions
            {
                [Ankus.PgFunction]
                public static System.Collections.Generic.IEnumerable<{{element}}> Rows()
                    => System.Array.Empty<{{element}}>();
            }
            """);
        AssertSetCompilationSucceeds(compilation, diagnostics);
        string columns = string.Join(", ", Enumerable.Range(1, arity).Select(static index =>
            "\"column" + index.ToString(CultureInfo.InvariantCulture) + "\" integer"));
        Assert.StartsWith("CREATE FUNCTION \"rows\"() RETURNS TABLE (" + columns + ") AS ", SetFunctionSql(compilation));
        using var assembly = new MemoryStream();
        Microsoft.CodeAnalysis.Emit.EmitResult emitted = compilation.Emit(assembly, cancellationToken: context.CancellationToken);
        Assert.IsEmpty(emitted.Diagnostics.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.IsTrue(emitted.Success);
    }

    /// <summary>
    /// The first tuple wider than PostgreSQL's record limit fails before generating unusable native tuple construction.
    /// </summary>
    [TestMethod]
    public void TableTupleBeyondPostgresRecordLimitIsDiagnosed()
    {
        string element = "(" + string.Join(", ", Enumerable.Range(1, 1665).Select(static index =>
            "int Column" + index.ToString(CultureInfo.InvariantCulture))) + ")";
        (_, ImmutableArray<Diagnostic> diagnostics) = Generate($$"""
            public static class Functions
            {
                [Ankus.PgFunction]
                public static System.Collections.Generic.IEnumerable<{{element}}> Rows() => System.Array.Empty<{{element}}>();
            }
            """);
        Assert.AreEqual("ANKUS008", Assert.ContainsSingle(diagnostics).Id);
    }

    /// <summary>
    /// Named row fields exercise the scalar, reference, array, geometry, range, numeric, network and temporal output converters together.
    /// </summary>
    [TestMethod]
    public void TableFieldsComposeAllConversionFamilies()
    {
        const string element = "(bool? Truth, sbyte Tiny, uint Oid, float Single, double Double, string? Text, byte[]? Bytes, " +
            "System.Guid Uuid, Ankus.PgJson Json, Ankus.PgJsonb Jsonb, decimal Number, Ankus.PgNumeric? Exact, " +
            "System.DateOnly Date, Ankus.PgTimeTz Time, System.DateTime Stamp, System.DateTimeOffset Instant, " +
            "System.TimeSpan Duration, Ankus.PgInterval Interval, Ankus.PgInet Inet, System.Net.IPNetwork Cidr, " +
            "Ankus.PgPoint Point, Ankus.PgPath? Path, Ankus.PgRange<int>? Range, Ankus.PgArray<string?>? Values)";
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate($$"""
            public static class Functions
            {
                [Ankus.PgFunction]
                public static System.Collections.Generic.IEnumerable<{{element}}> Rows()
                    => System.Array.Empty<{{element}}>();
            }
            """);
        AssertSetCompilationSucceeds(compilation, diagnostics);
        Assert.StartsWith("CREATE FUNCTION \"rows\"() RETURNS TABLE (\"truth\" boolean, \"tiny\" \"char\", \"oid\" oid, " +
            "\"single\" real, \"double\" double precision, \"text\" text, \"bytes\" bytea, \"uuid\" uuid, \"json\" json, \"jsonb\" jsonb, " +
            "\"number\" numeric, \"exact\" numeric, \"date\" date, \"time\" timetz, \"stamp\" timestamp, \"instant\" timestamptz, " +
            "\"duration\" interval, \"interval\" interval, \"inet\" inet, \"cidr\" cidr, \"point\" point, \"path\" path, " +
            "\"range\" int4range, \"values\" text[]) AS ", SetFunctionSql(compilation));
    }

    /// <summary>
    /// Output identifiers preserve valid Unicode at the PostgreSQL byte boundary and fail before truncation above it.
    /// </summary>
    /// <param name="bytes">The encoded UTF-8 length.</param>
    [TestMethod]
    [DataRow(62)]
    [DataRow(63)]
    [DataRow(64)]
    public void TableColumnNamesUseUtf8LengthLimits(int bytes)
    {
        string name = new('é', bytes / 2);
        if (bytes % 2 != 0)
        {
            name += 'x';
        }

        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate($$"""
            public static class Functions
            {
                [Ankus.PgFunction][return: Ankus.PgColumnNames({{SymbolDisplay.FormatLiteral(name, true)}})]
                public static System.Collections.Generic.IEnumerable<int> Rows() => System.Array.Empty<int>();
            }
            """);
        if (bytes == 64)
        {
            Assert.AreEqual("ANKUS008", Assert.ContainsSingle(diagnostics).Id);
        }
        else
        {
            AssertSetCompilationSucceeds(compilation, diagnostics);
            Assert.StartsWith("CREATE FUNCTION \"rows\"() RETURNS TABLE (\"" + name + "\" integer) AS ", SetFunctionSql(compilation));
        }
    }

    /// <summary>
    /// Invalid names, nested/nullable rows, missing tuple names and unsupported cell types fail with the set contract diagnostic.
    /// </summary>
    /// <param name="attributes">Return-column attributes, if any.</param>
    /// <param name="element">The unsupported element or row shape.</param>
    [TestMethod]
    [DataRow("", "(int, string)")]
    [DataRow("", "(int Named, string)")]
    [DataRow("", "System.ValueTuple<int>")]
    [DataRow("", "(int Id, string Name)?")]
    [DataRow("", "((int Id, string Name) Nested, int Count)")]
    [DataRow("", "(int URL, string Url)")]
    [DataRow("", "System.Uri")]
    [DataRow("", "(int Id, System.Uri Address)")]
    [DataRow("", "System.Collections.Generic.IEnumerable<int>")]
    [DataRow("", "(int Id, System.Collections.Generic.IEnumerable<int> Values)")]
    [DataRow("", "int[,]")]
    [DataRow("[return: Ankus.PgColumnNames()]", "int")]
    [DataRow("[return: Ankus.PgColumnNames(null!)]", "int")]
    [DataRow("[return: Ankus.PgColumnNames(\"\")]", "int")]
    [DataRow("[return: Ankus.PgColumnNames(\"bad\\0name\")]", "int")]
    [DataRow("[return: Ankus.PgColumnNames(\"\\ud800\")]", "int")]
    [DataRow("[return: Ankus.PgColumnNames(\"one\", \"two\")]", "int")]
    [DataRow("[return: Ankus.PgColumnNames(\"one\")]", "(int A, int B)")]
    [DataRow("[return: Ankus.PgColumnNames(\"same\", \"same\")]", "(int A, int B)")]
    [DataRow("[return: Ankus.PgColumnNames(\"a\", null!)]", "(int A, int B)")]
    [DataRow("[return: Ankus.PgColumnNames(\"row\")]", "(int Id, string Name)?")]
    public void InvalidSetRowShapesAndNamesAreDiagnosed(string attributes, string element)
    {
        (_, ImmutableArray<Diagnostic> diagnostics) = Generate($$"""
            public static class Functions
            {
                [Ankus.PgFunction]{{attributes}}
                public static System.Collections.Generic.IEnumerable<{{element}}> Rows() => System.Array.Empty<{{element}}>();
            }
            """);
        Diagnostic diagnostic = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual("ANKUS008", diagnostic.Id);
        Assert.AreEqual(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.IsTrue(diagnostic.Location.IsInSource);
    }

    /// <summary>
    /// Output column names cannot collide with normalized input names, including explicit parameter-name overrides.
    /// </summary>
    /// <param name="parameters">The input parameter declarations.</param>
    /// <param name="attributes">Explicit output names when needed.</param>
    /// <param name="element">The table element shape.</param>
    [TestMethod]
    [DataRow("int id", "", "(int Id, string Label)")]
    [DataRow("int HTTPCode", "", "(int HttpCode, string Label)")]
    [DataRow("[Ankus.PgParameter(Name = \"chosen\")] int source", "[return: Ankus.PgColumnNames(\"chosen\")]", "int")]
    public void TableOutputNamesCannotDuplicateInputNames(string parameters, string attributes, string element)
    {
        (_, ImmutableArray<Diagnostic> diagnostics) = Generate($$"""
            public static class Functions
            {
                [Ankus.PgFunction]{{attributes}}
                public static System.Collections.Generic.IEnumerable<{{element}}> Rows({{parameters}}) => System.Array.Empty<{{element}}>();
            }
            """);
        Assert.AreEqual("ANKUS004", Assert.ContainsSingle(diagnostics).Id);
    }

    /// <summary>
    /// Column-name metadata applies only to set results and cannot silently change a scalar function signature.
    /// </summary>
    /// <param name="method">The incorrectly attributed method.</param>
    [TestMethod]
    [DataRow("[return: Ankus.PgColumnNames(\"value\")] public static int Rows() => 1;")]
    [DataRow("[return: Ankus.PgColumnNames(\"value\")] public static int[] Rows() => [1];")]
    [DataRow("[return: Ankus.PgColumnNames(\"value\")] public static void Rows() { }")]
    public void ColumnNamesOnScalarResultsAreDiagnosed(string method)
    {
        (_, ImmutableArray<Diagnostic> diagnostics) = Generate("public static class Functions { [Ankus.PgFunction] " + method + " }");
        Assert.AreEqual("ANKUS008", Assert.ContainsSingle(diagnostics).Id);
    }

    /// <summary>
    /// Set-specific modes compile independently of their identical SQL return type and preserve explicit row estimates.
    /// </summary>
    /// <param name="mode">The explicitly requested mode.</param>
    [TestMethod]
    [DataRow("Auto")]
    [DataRow("ValuePerCall")]
    [DataRow("Materialize")]
    public void SetModesPreserveRowsAndFunctionExecutionOptions(string mode)
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate($$"""
            [Ankus.PgSchema("result_schema")]
            public static class Functions
            {
                [Ankus.PgFunction(Name = "mapped_rows", Rows = 12.5, SetMode = Ankus.PgSetMode.{{mode}},
                    Volatility = Ankus.PgVolatility.Stable, ParallelSafety = Ankus.PgParallelSafety.Restricted,
                    Cost = 2.5, SecurityDefiner = true, SearchPath = new[] { "pg_catalog" })]
                public static System.Collections.Generic.IEnumerable<int?> Rows(int? value = null) => new int?[] { value };
            }
            """);
        AssertSetCompilationSucceeds(compilation, diagnostics);
        string sql = SetFunctionSql(compilation, "\"result_schema\".\"mapped_rows\"");
        Assert.StartsWith("CREATE FUNCTION \"result_schema\".\"mapped_rows\"(\"value\" integer DEFAULT (NULL)) RETURNS SETOF integer AS ", sql);
        Assert.Contains(" STABLE PARALLEL RESTRICTED CALLED ON NULL INPUT SECURITY DEFINER NOT LEAKPROOF COST 2.5", sql);
        Assert.Contains(" ROWS 12.5 SET search_path TO ", sql);
        Assert.Contains(" SET search_path TO \"pg_catalog\"", sql);
        Assert.AreEqual("false", ManifestValue(compilation, "Ankus.Relocatable"));
    }

    /// <summary>
    /// Row estimates preserve valid positive finite values at PostgreSQL's representable float boundaries.
    /// </summary>
    /// <param name="expression">The attribute's row-estimate expression.</param>
    /// <param name="expected">The exact emitted invariant-culture row estimate.</param>
    [TestMethod]
    [DataRow("1d", "1")]
    [DataRow("0.5d", "0.5")]
    [DataRow("(double)float.Epsilon", "1.401298464324817E-45")]
    [DataRow("(double)float.MaxValue", "3.4028234663852886E+38")]
    public void SetRowsAcceptPositiveRepresentableBoundaries(string expression, string expected)
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate($$"""
            public static class Functions
            {
                [Ankus.PgFunction(Rows = {{expression}})]
                public static System.Collections.Generic.IEnumerable<int> Rows() => System.Array.Empty<int>();
            }
            """);
        AssertSetCompilationSucceeds(compilation, diagnostics);
        Assert.EndsWith(" ROWS " + expected + ";", SetFunctionSql(compilation));
    }

    /// <summary>
    /// Set modes and planner row estimates reject undefined, nonpositive, nonfinite and unrepresentable values.
    /// </summary>
    /// <param name="options">The invalid function option assignment.</param>
    [TestMethod]
    [DataRow("Rows = 0")]
    [DataRow("Rows = -1")]
    [DataRow("Rows = double.NaN")]
    [DataRow("Rows = double.PositiveInfinity")]
    [DataRow("Rows = double.NegativeInfinity")]
    [DataRow("Rows = double.Epsilon")]
    [DataRow("Rows = double.MaxValue")]
    [DataRow("SetMode = (Ankus.PgSetMode)(-1)")]
    [DataRow("SetMode = (Ankus.PgSetMode)3")]
    public void InvalidSetOptionsAreDiagnosed(string options)
    {
        (_, ImmutableArray<Diagnostic> diagnostics) = Generate($$"""
            public static class Functions
            {
                [Ankus.PgFunction({{options}})]
                public static System.Collections.Generic.IEnumerable<int> Rows() => System.Array.Empty<int>();
            }
            """);
        Assert.AreEqual("ANKUS004", Assert.ContainsSingle(diagnostics).Id);
    }

    /// <summary>
    /// Explicitly setting any set-only option on an ordinary scalar function is invalid, including default-valued options.
    /// </summary>
    /// <param name="options">The set-only option assignment.</param>
    [TestMethod]
    [DataRow("Rows = 1")]
    [DataRow("Rows = 1000")]
    [DataRow("SetMode = Ankus.PgSetMode.Auto")]
    [DataRow("SetMode = Ankus.PgSetMode.ValuePerCall")]
    [DataRow("SetMode = Ankus.PgSetMode.Materialize")]
    public void SetOptionsOnScalarFunctionsAreDiagnosed(string options)
    {
        (_, ImmutableArray<Diagnostic> diagnostics) = Generate("public static class Functions { [Ankus.PgFunction(" + options +
            ")] public static int Rows() => 1; }");
        Assert.AreEqual("ANKUS004", Assert.ContainsSingle(diagnostics).Id);
    }

    /// <summary>
    /// Scalar numeric set elements accept return precision constraints without changing their SQL datum type.
    /// </summary>
    /// <param name="element">The scalar numeric element representation.</param>
    [TestMethod]
    [DataRow("decimal")]
    [DataRow("decimal?")]
    [DataRow("Ankus.PgNumeric")]
    [DataRow("Ankus.PgNumeric?")]
    public void NumericSetElementsAcceptReturnPrecision(string element)
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate($$"""
            public static class Functions
            {
                [Ankus.PgFunction][return: Ankus.PgNumericPrecision(6, 2)]
                public static System.Collections.Generic.IEnumerable<{{element}}> Rows() => System.Array.Empty<{{element}}>();
            }
            """);
        AssertSetCompilationSucceeds(compilation, diagnostics);
        Assert.StartsWith("CREATE FUNCTION \"rows\"() RETURNS SETOF numeric AS ", SetFunctionSql(compilation));
    }

    /// <summary>
    /// A single return precision annotation cannot ambiguously constrain table columns or nonnumeric set elements.
    /// </summary>
    /// <param name="columns">Optional explicit TABLE column names.</param>
    /// <param name="element">The invalidly constrained set element.</param>
    [TestMethod]
    [DataRow("", "int")]
    [DataRow("", "decimal[]")]
    [DataRow("", "(decimal First, decimal Second)")]
    [DataRow("[return: Ankus.PgColumnNames(\"number\")]", "decimal")]
    public void NumericPrecisionRejectsTableAndNonnumericSetResults(string columns, string element)
    {
        (_, ImmutableArray<Diagnostic> diagnostics) = Generate($$"""
            public static class Functions
            {
                [Ankus.PgFunction][return: Ankus.PgNumericPrecision(6, 2)]{{columns}}
                public static System.Collections.Generic.IEnumerable<{{element}}> Rows() => System.Array.Empty<{{element}}>();
            }
            """);
        Assert.AreEqual("ANKUS003", Assert.ContainsSingle(diagnostics).Id);
    }

    /// <summary>
    /// Related operators and casts cannot become set-returning when ordinary function discovery accepts IEnumerable results.
    /// </summary>
    /// <param name="attribute">The incompatible operator or cast declaration.</param>
    [TestMethod]
    [DataRow("[Ankus.PgOperator(\"@\")]")]
    [DataRow("[Ankus.PgCast]")]
    [DataRow("[Ankus.PgFunction][Ankus.PgOperator(\"@\")]")]
    [DataRow("[Ankus.PgFunction][Ankus.PgCast]")]
    public void OperatorsAndCastsRejectSetResults(string attribute)
    {
        (_, ImmutableArray<Diagnostic> diagnostics) = Generate("public static class Functions { " + attribute +
            " public static System.Collections.Generic.IEnumerable<int> Rows(int value) => new[] { value }; }");
        Assert.AreEqual("ANKUS007", Assert.ContainsSingle(diagnostics).Id);
    }

    /// <summary>
    /// Set return support does not make sets legal inputs or admit asynchronous and inaccessible Native AOT callbacks.
    /// </summary>
    /// <param name="method">The unsupported ordinary function contract.</param>
    [TestMethod]
    [DataRow("public static int Rows(System.Collections.Generic.IEnumerable<int> values) => 1;")]
    [DataRow("public static System.Collections.Generic.IAsyncEnumerable<int> Rows() => null!;")]
    [DataRow("public static System.Threading.Tasks.Task<int> Rows() => System.Threading.Tasks.Task.FromResult(1);")]
    [DataRow("private static System.Collections.Generic.IEnumerable<int> Rows() => System.Array.Empty<int>();")]
    [DataRow("public System.Collections.Generic.IEnumerable<int> Rows() => System.Array.Empty<int>();")]
    [DataRow("public static System.Collections.Generic.IEnumerable<int> Rows<T>() => System.Array.Empty<int>();")]
    public void SetSupportRetainsOrdinaryFunctionRestrictions(string method)
    {
        (_, ImmutableArray<Diagnostic> diagnostics) = Generate("public class Functions { [Ankus.PgFunction] " + method + " }");
        Assert.AreEqual("ANKUS001", Assert.ContainsSingle(diagnostics).Id);
    }

    /// <summary>
    /// PostgreSQL function identity remains based on input types rather than scalar, set, or table result shape.
    /// </summary>
    /// <param name="second">The second method with the same SQL input signature.</param>
    [TestMethod]
    [DataRow("public static int Rows(int? value) => 1;")]
    [DataRow("public static System.Collections.Generic.IEnumerable<string> Rows(int value) => System.Array.Empty<string>();")]
    [DataRow("public static System.Collections.Generic.IEnumerable<(int Id, string Label)> Rows(int value) => System.Array.Empty<(int, string)>();")]
    public void SetFunctionIdentityIgnoresReturnShape(string second)
    {
        (_, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            public static class First
            {
                [Ankus.PgFunction] public static System.Collections.Generic.IEnumerable<int> Rows(int value) => new[] { value };
            }
            """ + "public static class Second { [Ankus.PgFunction] " + second + " }");
        Assert.AreEqual("ANKUS002", Assert.ContainsSingle(diagnostics).Id);
    }

    /// <summary>
    /// Every output-only enum and enum-array column contributes an installation dependency before the table function.
    /// </summary>
    [TestMethod]
    public void TableGraphDependsOnEveryOutputEnumAndCustomSql()
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            [assembly: Ankus.PgSql("after", "SELECT 'after';", Requires = new[] { "rows" })]
            [assembly: Ankus.PgSql("before", "SELECT 'before';", Requires = new[] { "schema" })]
            public static class Functions
            {
                [Ankus.PgFunction(Id = "rows")]
                public static System.Collections.Generic.IEnumerable<(Values.First State, Ankus.PgArray<Values.Second?>? Others)> Rows()
                    => System.Array.Empty<(Values.First, Ankus.PgArray<Values.Second?>?)>();
            }

            [Ankus.PgSchema("states", Id = "schema")]
            public static class Values
            {
                [Ankus.PgEnum(Id = "first", Requires = new[] { "before" })] public enum First { Ready }
                [Ankus.PgEnum(Id = "second", Requires = new[] { "first" })] public enum Second { Waiting }
            }
            """);
        AssertSetCompilationSucceeds(compilation, diagnostics);
        string sql = ManifestValue(compilation, "Ankus.Sql");
        Assert.StartsWith("CREATE SCHEMA IF NOT EXISTS \"states\";\nSELECT 'before';\n" +
            "CREATE TYPE \"states\".\"first\" AS ENUM (E'Ready');\nCREATE TYPE \"states\".\"second\" AS ENUM (E'Waiting');\nCREATE FUNCTION \"rows\"()", sql);
        Assert.StartsWith("CREATE FUNCTION \"rows\"() RETURNS TABLE (\"state\" \"states\".\"first\", \"others\" \"states\".\"second\"[]) AS ", SetFunctionSql(compilation));
        Assert.EndsWith("SELECT 'after';\n", sql);
    }

    /// <summary>
    /// Scalar sets and explicitly named one-column tables preserve enum identity and its creation dependency.
    /// </summary>
    /// <param name="element">The enum element representation.</param>
    /// <param name="sqlType">The scalar or array SQL identity.</param>
    /// <param name="table">Whether the result is an explicitly named one-column TABLE.</param>
    [TestMethod]
    [DataRow("Mood", "\"mood\"", false)]
    [DataRow("Mood?", "\"mood\"", false)]
    [DataRow("Mood?[]?", "\"mood\"[]", false)]
    [DataRow("Ankus.PgArray<Mood?>?", "\"mood\"[]", false)]
    [DataRow("Mood?", "\"mood\"", true)]
    [DataRow("Ankus.PgArray<Mood?>?", "\"mood\"[]", true)]
    public void SetEnumsPreserveExactIdentityAndDependencies(string element, string sqlType, bool table)
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate($$"""
            public static class Functions
            {
                [Ankus.PgFunction]{{(table ? "[return: Ankus.PgColumnNames(\"state\")]" : "")}}
                public static System.Collections.Generic.IEnumerable<{{element}}> Rows() => System.Array.Empty<{{element}}>();
            }

            [Ankus.PgEnum] public enum Mood { First, Second }
            """);
        AssertSetCompilationSucceeds(compilation, diagnostics);
        Assert.StartsWith("CREATE TYPE \"mood\" AS ENUM (E'First', E'Second');\nCREATE FUNCTION \"rows\"()", ManifestValue(compilation, "Ankus.Sql"));
        string result = table ? "TABLE (\"state\" " + sqlType + ")" : "SETOF " + sqlType;
        Assert.StartsWith("CREATE FUNCTION \"rows\"() RETURNS " + result + " AS ", SetFunctionSql(compilation));
    }

    /// <summary>
    /// Return-only enum dependencies participate in cycle detection even when the function has no enum input parameters.
    /// </summary>
    /// <param name="element">The set or table element exposing the enum dependency.</param>
    [TestMethod]
    [DataRow("Mood")]
    [DataRow("Ankus.PgArray<Mood?>")]
    [DataRow("(int Id, Mood State)")]
    [DataRow("(int Id, Ankus.PgArray<Mood?> States)")]
    public void SetReturnEnumDependencyCyclesAreDiagnosed(string element)
    {
        (_, ImmutableArray<Diagnostic> diagnostics) = Generate($$"""
            [Ankus.PgEnum(Requires = new[] { "rows" })] public enum Mood { First }
            public static class Functions
            {
                [Ankus.PgFunction(Id = "rows")]
                public static System.Collections.Generic.IEnumerable<{{element}}> Rows() => System.Array.Empty<{{element}}>();
            }
            """);
        Diagnostic diagnostic = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual("ANKUS005", diagnostic.Id);
        Assert.Contains("cycle", diagnostic.GetMessage(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Reordered set/table declarations produce the same installation script, exports, and generated source.
    /// </summary>
    [TestMethod]
    public void SetOutputIsDeterministicAcrossDeclarationOrder()
    {
        const string first = "public static class A { [Ankus.PgFunction] public static System.Collections.Generic.IEnumerable<int> Numbers() => System.Array.Empty<int>(); }";
        const string second = "public static class B { [Ankus.PgFunction] public static System.Collections.Generic.IEnumerable<(int Id, string Name)> Named() => System.Array.Empty<(int, string)>(); }";
        (Compilation left, ImmutableArray<Diagnostic> leftDiagnostics) = Generate(first + second);
        (Compilation right, ImmutableArray<Diagnostic> rightDiagnostics) = Generate(second + first);
        AssertSetCompilationSucceeds(left, leftDiagnostics);
        AssertSetCompilationSucceeds(right, rightDiagnostics);
        Assert.Contains("RETURNS SETOF integer", ManifestValue(left, "Ankus.Sql"));
        Assert.Contains("RETURNS TABLE (\"id\" integer, \"name\" text)", ManifestValue(left, "Ankus.Sql"));
        Assert.AreEqual(ManifestValue(left, "Ankus.Sql"), ManifestValue(right, "Ankus.Sql"));
        Assert.AreEqual(ManifestValue(left, "Ankus.Exports"), ManifestValue(right, "Ankus.Exports"));
        Assert.AreSequenceEqual(left.SyntaxTrees.Skip(1).Select(static tree => tree.ToString()), right.SyntaxTrees.Skip(1).Select(static tree => tree.ToString()));
    }

    /// <summary>
    /// Requires successful generator execution and compilable generated callback code.
    /// </summary>
    /// <param name="compilation">The generated output compilation.</param>
    /// <param name="diagnostics">Generator-reported diagnostics.</param>
    private void AssertSetCompilationSucceeds(Compilation compilation, ImmutableArray<Diagnostic> diagnostics)
    {
        Assert.IsEmpty(diagnostics);
        Assert.IsEmpty(compilation.GetDiagnostics(context.CancellationToken).Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
    }

    /// <summary>
    /// Extracts one complete function declaration while preserving all quoted names and type spelling.
    /// </summary>
    /// <param name="compilation">The generated compilation containing installation metadata.</param>
    /// <param name="qualifiedName">The already quoted and optionally schema-qualified function name.</param>
    /// <returns>The function's SQL declaration with line breaks replaced by spaces.</returns>
    private static string SetFunctionSql(Compilation compilation, string qualifiedName = "\"rows\"")
    {
        string sql = ManifestValue(compilation, "Ankus.Sql");
        int start = sql.IndexOf("CREATE FUNCTION " + qualifiedName + "(", StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, start);
        int end = sql.IndexOf(";\n", start, StringComparison.Ordinal);
        Assert.IsGreaterThan(start, end);
        return sql.Substring(start, end - start + 1).Replace('\n', ' ');
    }
}
