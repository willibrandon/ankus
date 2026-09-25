using System.Globalization;
using Ankus;
using Ankus.TestExtension;

[assembly: PgSql("mapped-shells", "CREATE TYPE datum_mappings.u24; CREATE TYPE datum_mappings.complex;", Requires = ["mapped-schema"])]
[assembly: PgSql("mapped-u24", """
    CREATE TYPE datum_mappings.u24 (INPUT=datum_mappings.u24_in, OUTPUT=datum_mappings.u24_out, LIKE=int4);
    """, Requires = ["mapped-u24-in", "mapped-u24-out"])]
[assembly: PgSqlTypeProvider("mapped-u24", typeof(MappedU24))]
[assembly: PgSqlTypeProvider("mapped-u24", typeof(MappedU24Alias))]
[assembly: PgSqlTypeProvider("mapped-u24", "u24", Schema = "datum_mappings")]
[assembly: PgSql("mapped-complex", """
    CREATE TYPE datum_mappings.complex (INPUT=datum_mappings.complex_in, OUTPUT=datum_mappings.complex_out,
        INTERNALLENGTH=16, ALIGNMENT=double, STORAGE=plain);
    """, Requires = ["mapped-complex-in", "mapped-complex-out"])]
[assembly: PgSqlTypeProvider("mapped-complex", typeof(MappedComplex))]
[assembly: PgSql("mapped-domains", """
    CREATE DOMAIN datum_mappings.positive AS integer CHECK(VALUE > 0);
    CREATE DOMAIN datum_mappings.other_positive AS integer CHECK(VALUE > 0);
    CREATE DOMAIN datum_mappings.required AS integer NOT NULL;
    """)]
[assembly: PgSqlTypeProvider("mapped-domains", typeof(MappedPositive))]
[assembly: PgSqlTypeProvider("mapped-domains", typeof(MappedRequired))]

namespace Ankus.TestExtension;

/// <summary>
/// Exercises statically selected converters over manual and external PostgreSQL types.
/// </summary>
[PgSchema("datum_mappings", Id = "mapped-schema")]
public static class DatumMappingFunctions
{
    private static int s_cleanup;
    private static SpiParameter s_savedParameter;

    /// <summary>
    /// Parses the manual type before its complete declaration using an inferred provider edge.
    /// </summary>
    /// <param name="text">The native input string.</param>
    /// <returns>The checked value written through the registered converter.</returns>
    [PgFunction(Name = "u24_in", Id = "mapped-u24-in", Requires = ["mapped-shells"])]
    public static MappedU24 U24Input([PgSqlType("cstring", Schema = "pg_catalog")] PgDatum text)
    {
        if (!uint.TryParse(text.ToPostgresString(), NumberStyles.None, CultureInfo.InvariantCulture, out uint value))
        {
            throw new PgException("22P02", "invalid mapped integer");
        }

        return new MappedU24(value);
    }

    /// <summary>
    /// Formats a mapped manual input through PostgreSQL's cstring ABI.
    /// </summary>
    /// <param name="value">The detached mapped input.</param>
    /// <returns>The native output string.</returns>
    [PgFunction(Name = "u24_out", Id = "mapped-u24-out", Requires = ["mapped-shells"])]
    [return: PgSqlType("cstring", Schema = "pg_catalog")]
    public static PgDatum U24Output(MappedU24 value)
        => PgFunctions.CallRaw("pg_catalog.int4out", PgMemoryContext.Current, PgFunctionArgument.Create(checked((int)value.Value)));

    /// <summary>
    /// Parses two invariant doubles into a fixed-size native representation.
    /// </summary>
    /// <param name="text">The comma-separated cstring.</param>
    /// <returns>The detached pair.</returns>
    [PgFunction(Name = "complex_in", Id = "mapped-complex-in", Requires = ["mapped-shells"])]
    public static MappedComplex ComplexInput([PgSqlType("cstring", Schema = "pg_catalog")] PgDatum text)
    {
        string[] parts = text.ToPostgresString()!.Split(',');
        if (parts.Length != 2 || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double real) ||
            !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double imaginary))
        {
            throw new PgException("22P02", "invalid mapped complex");
        }

        return new MappedComplex(real, imaginary);
    }

    /// <summary>
    /// Formats both copied components without retaining the original native pointer.
    /// </summary>
    /// <param name="value">The mapped pair.</param>
    /// <returns>The cstring representation.</returns>
    [PgFunction(Name = "complex_out", Id = "mapped-complex-out", Requires = ["mapped-shells"])]
    [return: PgSqlType("cstring", Schema = "pg_catalog")]
    public static PgDatum ComplexOutput(MappedComplex value)
        => PgFunctions.CallRaw("pg_catalog.textout", PgMemoryContext.Current,
            PgFunctionArgument.Create(string.Create(CultureInfo.InvariantCulture, $"{value.Real:R},{value.Imaginary:R}")));

    /// <summary>
    /// Reports construction and conversion counts without touching a mapping.
    /// </summary>
    /// <returns>The construction, read and write counters.</returns>
    [PgFunction(Name = "counts")]
    public static string Counts() => $"{U24DatumConverter.Constructions}|{U24DatumConverter.Reads}|{U24DatumConverter.Writes}";

    /// <summary>
    /// Retains a nullable mapped value with framework-managed SQL NULL handling.
    /// </summary>
    /// <param name="value">The mapped value or SQL NULL.</param>
    /// <returns>The same managed value.</returns>
    [PgFunction(Name = "echo")]
    public static MappedU24? Echo(MappedU24? value) => value;

    /// <summary>
    /// Supplies a mapped return independently of SQL input conversion.
    /// </summary>
    /// <param name="value">The integer to write.</param>
    /// <returns>The manual type.</returns>
    [PgFunction(Name = "make")]
    public static MappedU24 Make(uint value) => new(value);

    /// <summary>
    /// Exposes one wrapper's managed interpretation as an independent integer oracle.
    /// </summary>
    /// <param name="value">The stored manual value.</param>
    /// <returns>The exact unsigned word.</returns>
    [PgFunction(Name = "as_integer"), PgCast]
    public static int AsInteger(MappedU24 value) => checked((int)value.Value);

    /// <summary>
    /// Exposes the second wrapper's different reader for the same SQL type.
    /// </summary>
    /// <param name="value">The alternative mapped value.</param>
    /// <returns>The stored value increased by one.</returns>
    [PgFunction(Name = "alias_integer")]
    public static int AliasInteger(MappedU24Alias value) => checked((int)value.Value);

    /// <summary>
    /// Uses the second wrapper's writer independently of its reader.
    /// </summary>
    /// <param name="value">The alternative managed value.</param>
    /// <returns>The SQL value decreased by one.</returns>
    [PgFunction(Name = "alias_make")]
    public static MappedU24Alias AliasMake(uint value) => new(value);

    /// <summary>
    /// Installs a manual operator through ordinary mapped signatures.
    /// </summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns>Whether the values are equal.</returns>
    [PgFunction(Name = "equal"), PgOperator("@=")]
    public static bool Equal(MappedU24 left, MappedU24 right) => left.Value == right.Value;

    /// <summary>
    /// Retains all bits of two fixed-size by-reference doubles.
    /// </summary>
    /// <param name="value">The detached pair.</param>
    /// <returns>The same pair under fresh output storage.</returns>
    [PgFunction(Name = "complex_echo")]
    public static MappedComplex? ComplexEcho(MappedComplex? value) => value;

    /// <summary>
    /// Exposes both components' raw bits independently of text formatting.
    /// </summary>
    /// <param name="value">The pair to inspect.</param>
    /// <returns>The signed IEEE words in invariant decimal form.</returns>
    [PgFunction(Name = "complex_bits")]
    public static string ComplexBits(MappedComplex value)
        => $"{BitConverter.DoubleToInt64Bits(value.Real)}|{BitConverter.DoubleToInt64Bits(value.Imaginary)}";

    /// <summary>
    /// Reads fixed-size storage before disposing its SPI owner, then writes from detached fields.
    /// </summary>
    /// <returns>The independent pair.</returns>
    [PgFunction(Name = "complex_from_spi")]
    public static MappedComplex ComplexFromSpi()
    {
        using SpiRawResult result = Spi.QueryRaw("SELECT '1.25,-2.5'::datum_mappings.complex");
        return result[0][0].Read<MappedComplex>();
    }

    /// <summary>
    /// Returns domain values and lets native PostgreSQL enforce their constraint.
    /// </summary>
    /// <param name="value">The unchecked managed integer.</param>
    /// <returns>The exactly typed domain.</returns>
    [PgFunction(Name = "positive_make")]
    public static MappedPositive PositiveMake(int value) => new(value);

    /// <summary>
    /// Reads an arbitrary raw result as the exact declared domain, including typed NULLs.
    /// </summary>
    /// <param name="sql">The test-controlled scalar query.</param>
    /// <returns>The domain's integer or SQL NULL.</returns>
    [PgFunction(Name = "positive_read")]
    public static int? PositiveRead(string sql)
    {
        using SpiRawResult result = Spi.QueryRaw(sql);
        return result[0][0].Read<MappedPositive?>()?.Value;
    }

    /// <summary>
    /// Demonstrates a read-only callback contract without a writer.
    /// </summary>
    /// <param name="value">The converter-adjusted integer.</param>
    /// <returns>The managed interpretation.</returns>
    [PgFunction(Name = "read_only")]
    public static int ReadOnly(ReadMappedInt value) => value.Value;

    /// <summary>
    /// Demonstrates a write-only callback contract without a reader.
    /// </summary>
    /// <param name="value">The integer to negate.</param>
    /// <returns>The output-only mapped value.</returns>
    [PgFunction(Name = "write_only")]
    public static WriteMappedInt WriteOnly(int value) => new(value);

    /// <summary>
    /// Keeps CLR enum values in a built-in SQL integer representation.
    /// </summary>
    /// <param name="value">The enum or SQL NULL.</param>
    /// <returns>The same enum value.</returns>
    [PgFunction(Name = "sign_echo")]
    public static MappedSign? SignEcho(MappedSign? value) => value;

    /// <summary>
    /// Returns a deliberately selected writer result for exact identity and lifetime checks.
    /// </summary>
    /// <param name="mode">The invalid or valid output mode.</param>
    /// <returns>The present CLR value whose writer may produce SQL NULL.</returns>
    [PgFunction(Name = "invalid_result")]
    public static AdversarialMappedInt InvalidResult(int mode) => new(mode);

    /// <summary>
    /// Copies reference values through the mapped scalar contract.
    /// </summary>
    /// <param name="value">The detached text wrapper.</param>
    /// <returns>The same managed reference or SQL NULL.</returns>
    [PgFunction(Name = "text_echo")]
    public static MappedText? TextEcho(MappedText? value) => value;

    /// <summary>
    /// Retains text after disposing SPI storage and dropping its toasted source.
    /// </summary>
    /// <returns>The independent large text.</returns>
    [PgFunction(Name = "text_from_store")]
    public static MappedText TextFromStore()
    {
        MappedText value;
        using (SpiRawResult result = Spi.QueryRaw("SELECT value FROM mapped_toast"))
        {
            value = result[0][0].Read<MappedText>();
        }

        Spi.Execute("DROP TABLE mapped_toast");
        return value;
    }

    /// <summary>
    /// Binds a declared mapped base class despite a different runtime reference type.
    /// </summary>
    /// <param name="value">The text to pass through typed SPI.</param>
    /// <returns>The independently decoded built-in text.</returns>
    [PgFunction(Name = "declared_parameter")]
    public static string? DeclaredParameter(string? value)
    {
        MappedText? mapped = value is null ? null : new DerivedMappedText(value);
        return Spi.ExecuteScalar<string?>("SELECT $1", SpiParameter.Create<MappedText?>(mapped));
    }

    /// <summary>
    /// Passes a write-only mapped argument to an ordinary PostgreSQL function.
    /// </summary>
    /// <param name="value">The integer whose argument conversion negates it.</param>
    /// <returns>The built-in function's sum.</returns>
    [PgFunction(Name = "call_argument")]
    public static int CallArgument(int value)
        => PgFunctions.Call<int>("pg_catalog.int4pl", PgFunctionArgument.Create(new WriteMappedInt(value)), PgFunctionArgument.Create(3));

    /// <summary>
    /// Requests a server default through a read-only mapping without a writer.
    /// </summary>
    /// <returns>The server's default followed by the read-only callback's adjustment.</returns>
    [PgFunction(Name = "call_default", Requires = ["mapped-default"])]
    public static int CallDefault()
        => PgFunctions.Call<int>("datum_mappings.default_target", PgFunctionArgument.Default<ReadMappedInt>());

    /// <summary>
    /// Supplies the server-evaluated default used by the type-only request.
    /// </summary>
    /// <param name="value">The ordinary defaulted argument.</param>
    /// <returns>The default value.</returns>
    [PgFunction(Name = "default_target", Id = "mapped-default")]
    public static int DefaultTarget(int value = 17) => value;

    /// <summary>
    /// Streams detached mapped values and records cleanup at every exit.
    /// </summary>
    /// <param name="value">The detached text input.</param>
    /// <param name="fail">Whether the second advance fails.</param>
    /// <returns>Two equal mapped values and SQL NULL.</returns>
    [PgFunction(Name = "text_rows")]
    public static IEnumerable<MappedText?> TextRows(MappedText value, bool fail)
    {
        try
        {
            yield return value;
            if (fail)
            {
                throw new PgException("P8503", "mapped iterator failed");
            }

            yield return value;
            yield return null;
        }
        finally
        {
            s_cleanup++;
        }
    }

    /// <summary>
    /// Materializes mapped values under the alternate executor path.
    /// </summary>
    /// <param name="value">The mapped input.</param>
    /// <param name="fail">Whether enumeration fails.</param>
    /// <returns>The copied values.</returns>
    [PgFunction(Name = "text_materialized", SetMode = PgSetMode.Materialize)]
    public static IEnumerable<MappedText?> TextMaterialized(MappedText value, bool fail) => TextRows(value, fail);

    /// <summary>
    /// Uses two independent mapped TABLE output columns and per-column NULLs.
    /// </summary>
    /// <param name="sourceNumber">The first mapped input.</param>
    /// <param name="sourceComplex">The second mapped input.</param>
    /// <returns>A present row followed by two SQL NULL columns.</returns>
    [PgFunction(Name = "mapped_table")]
    public static IEnumerable<(MappedU24? Number, MappedComplex? Complex)> Table(MappedU24 sourceNumber, MappedComplex sourceComplex)
    {
        yield return (sourceNumber, sourceComplex);
        yield return (null, null);
    }

    /// <summary>
    /// Reports completed iterator cleanup without user converter calls.
    /// </summary>
    /// <returns>The finally count.</returns>
    [PgFunction(Name = "cleanup_count")]
    public static int CleanupCount() => s_cleanup;

    /// <summary>
    /// Resolves a mapped external domain before and after catalog replacement.
    /// </summary>
    /// <param name="value">The integer to bind.</param>
    /// <param name="save">Whether to retain this exact parameter for a later invocation.</param>
    /// <returns>The independently read value and current exact type OID.</returns>
    [PgFunction(Name = "live_parameter")]
    public static string LiveParameter(int value, bool save)
    {
        SpiParameter parameter = SpiParameter.Create(new LiveMappedInt(value));
        if (save)
        {
            s_savedParameter = parameter;
        }

        using SpiRawResult result = Spi.QueryRaw("SELECT $1", parameter);
        return $"{result[0][0].Read<LiveMappedInt>().Value}|{parameter.TypeOid}";
    }

    /// <summary>
    /// Reuses an old parameter to prove DDL cannot silently rebind its exact type identity.
    /// </summary>
    /// <returns>The previously bound value, if the original identity is still valid.</returns>
    [PgFunction(Name = "saved_parameter")]
    public static int SavedParameter()
    {
        using SpiRawResult result = Spi.QueryRaw("SELECT $1", s_savedParameter);
        return result[0][0].Read<LiveMappedInt>().Value;
    }

    /// <summary>
    /// Requests a pseudotype identity before the deliberately unusable writer can run.
    /// </summary>
    /// <returns>The type identity only if validation incorrectly accepts the pseudotype.</returns>
    [PgFunction(Name = "pseudo_parameter")]
    public static uint PseudoParameter() => SpiParameter.Create(new PseudoMappedValue()).TypeOid;

    /// <summary>
    /// Writes a live typed domain NULL so native NOT NULL checking cannot be skipped.
    /// </summary>
    /// <param name="mode">Zero selects SQL NULL.</param>
    /// <returns>The present managed wrapper.</returns>
    [PgFunction(Name = "required_make")]
    public static MappedRequired RequiredMake(int mode) => new(mode);

    /// <summary>
    /// Supplies a strict domain input with a valid server-evaluated default.
    /// </summary>
    /// <param name="value">The non-null domain value.</param>
    /// <returns>The stored integer.</returns>
    [PgFunction(Name = "required_target", Id = "mapped-required-target")]
    public static int RequiredTarget([PgSqlType("required", Schema = "datum_mappings"),
        PgParameter(Default = "42::datum_mappings.required")] PgDatum value) => unchecked((int)value.DangerousGetBits());

    /// <summary>
    /// Binds a mapped writer-produced domain NULL or a type-only default to a strict target.
    /// </summary>
    /// <param name="mode">Zero produces SQL NULL, other values produce forty-two.</param>
    /// <param name="useDefault">Whether PostgreSQL should evaluate the default instead of converting a value.</param>
    /// <param name="byOid">Whether to invoke the resolved function OID.</param>
    /// <returns>The strict target's integer result.</returns>
    [PgFunction(Name = "required_call", Requires = ["mapped-required-target"])]
    public static int RequiredCall(int mode, bool useDefault, bool byOid)
    {
        PgFunctionArgument argument = useDefault ? PgFunctionArgument.Default<MappedRequired>()
            : PgFunctionArgument.Create(new MappedRequired(mode));
        return byOid ? PgFunctions.Call<int>(Spi.ExecuteScalar<uint>("SELECT 'datum_mappings.required_target(datum_mappings.required)'::regprocedure::oid"), argument)
            : PgFunctions.Call<int>("datum_mappings.required_target", argument);
    }

    /// <summary>
    /// Streams a typed NULL in a domain-valued SETOF column.
    /// </summary>
    /// <returns>The present wrapper whose writer returns SQL NULL.</returns>
    [PgFunction(Name = "required_rows")]
    public static IEnumerable<MappedRequired> RequiredRows()
    {
        yield return new MappedRequired(0);
    }

    /// <summary>
    /// Materializes the typed NOT NULL-domain result.
    /// </summary>
    /// <returns>The same domain-valued row.</returns>
    [PgFunction(Name = "required_materialized", SetMode = PgSetMode.Materialize)]
    public static IEnumerable<MappedRequired> RequiredMaterialized() => RequiredRows();

    /// <summary>
    /// Writes a typed domain NULL into a present TABLE row.
    /// </summary>
    /// <returns>The domain column plus an ordinary present column.</returns>
    [PgFunction(Name = "required_table")]
    public static IEnumerable<(MappedRequired Value, int Position)> RequiredTable()
    {
        yield return (new MappedRequired(0), 1);
    }

    /// <summary>
    /// Reads a mapped enum array through its selected converter, including unnamed values and SQL NULL.
    /// </summary>
    /// <param name="absent">Whether to read SQL NULL.</param>
    /// <param name="shaped">Whether to request a shaped array.</param>
    /// <returns>Every underlying enum value, or the whole-array NULL marker.</returns>
    [PgFunction(Name = "read_mapped_array")]
    public static string ReadMappedArray(bool absent, bool shaped)
    {
        using SpiRawResult result = Spi.QueryRaw(absent ? "SELECT NULL::integer[]" : "SELECT ARRAY[-1,0,1,42]");
        IEnumerable<MappedSign>? values = shaped ? result[0][0].Read<PgArray<MappedSign>?>() : result[0][0].Read<MappedSign[]?>();
        return values is null ? "NULL" : string.Join('|', values.Select(static value => ((int)value).ToString(CultureInfo.InvariantCulture)));
    }

    /// <summary>
    /// Streams writer-produced NULL, a present value and nullable CLR absence through the same mapped column.
    /// </summary>
    /// <param name="mode">The first writer result mode.</param>
    /// <returns>The selected result, forty-two and SQL NULL.</returns>
    [PgFunction(Name = "writer_rows")]
    public static IEnumerable<AdversarialMappedInt?> WriterRows(int mode)
    {
        yield return new AdversarialMappedInt(mode);
        yield return new AdversarialMappedInt(5);
        yield return null;
    }

    /// <summary>
    /// Materializes the same writer-produced typed NULL envelope.
    /// </summary>
    /// <param name="mode">The first writer result mode.</param>
    /// <returns>The materialized mapped cells.</returns>
    [PgFunction(Name = "writer_materialized", SetMode = PgSetMode.Materialize)]
    public static IEnumerable<AdversarialMappedInt?> WriterMaterialized(int mode) => WriterRows(mode);

    /// <summary>
    /// Places writer-produced typed NULL into a TABLE column with an independent present ordinal.
    /// </summary>
    /// <param name="mode">The first writer result mode.</param>
    /// <returns>Three present rows with independently nullable mapped cells.</returns>
    [PgFunction(Name = "writer_table")]
    public static IEnumerable<(AdversarialMappedInt? Value, int Position)> WriterTable(int mode)
    {
        yield return (new AdversarialMappedInt(mode), 1);
        yield return (new AdversarialMappedInt(5), 2);
        yield return (null, 3);
    }

    /// <summary>
    /// Attempts unsupported typed result paths before a nontransactional sequence side effect.
    /// </summary>
    /// <param name="mode">The ordinary scalar, mixed, session, prepared or function result path.</param>
    /// <returns>A value only if the unsupported operation incorrectly succeeds.</returns>
    [PgFunction(Name = "unsupported_result")]
    public static int UnsupportedResult(int mode)
    {
        const string scalar = "SELECT nextval('mapped_effect')::text";
        switch (mode)
        {
            case 0:
                Spi.ExecuteScalar<WriteMappedInt>(scalar);
                break;
            case 1:
                Spi.ExecuteScalars<PgAnyElement, WriteMappedInt>("SELECT nextval('mapped_effect'), 'text'::text");
                break;
            case 2:
                Spi.Connect(session => session.ExecuteScalar<WriteMappedInt>(scalar));
                break;
            case 3:
                using (SpiPreparedStatement plan = Spi.Prepare(scalar))
                {
                    plan.ExecuteScalar<WriteMappedInt>();
                }

                break;
            case 5:
                Spi.ExecuteScalar<WriteMappedInt[]>(scalar);
                break;
            case 6:
                Spi.ExecuteScalar<PgArray<WriteMappedInt>>(scalar);
                break;
            default:
                PgFunctions.Call<WriteMappedInt>("pg_temp.mapped_effect");
                break;
        }

        return -1;
    }
}

/// <summary>
/// Retains a detached mapped reference across aggregate transitions.
/// </summary>
[PgAggregate(Name = "first_text", Schema = "datum_mappings")]
public static class MappedTextAggregate
{
    /// <summary>
    /// Keeps the first present text without retaining its input datum owner.
    /// </summary>
    /// <param name="state">The prior state.</param>
    /// <param name="value">The new input.</param>
    /// <returns>The retained first value.</returns>
    public static MappedText? Transition(MappedText? state, MappedText? value) => state ?? value;
}

/// <summary>
/// Writes typed SQL NULL from a present CLR final result after ordinary aggregate transitions.
/// </summary>
[PgAggregate(Name = "writer_final", Schema = "datum_mappings", InitialCondition = "0")]
public static class MappedWriterAggregate
{
    /// <summary>
    /// Accumulates the requested output mode.
    /// </summary>
    /// <param name="state">The prior mode sum.</param>
    /// <param name="value">The current increment.</param>
    /// <returns>The next sum.</returns>
    public static int Transition(int state, int value) => checked(state + value);

    /// <summary>
    /// Returns a present mapped object whose writer controls the native NULL flag.
    /// </summary>
    /// <param name="state">The requested writer mode.</param>
    /// <returns>The present managed final result.</returns>
    public static AdversarialMappedInt Final(int state) => new(state);
}

/// <summary>
/// Applies native domain constraints to a writer-produced final NULL.
/// </summary>
[PgAggregate(Name = "required_final", Schema = "datum_mappings", InitialCondition = "0")]
public static class MappedRequiredAggregate
{
    /// <summary>
    /// Keeps a simple integer state.
    /// </summary>
    /// <param name="state">The prior state.</param>
    /// <param name="value">The next integer.</param>
    /// <returns>The next state.</returns>
    public static int Transition(int state, int value) => checked(state + value);

    /// <summary>
    /// Produces a present object whose writer returns a domain-typed NULL.
    /// </summary>
    /// <param name="state">The accumulated output mode.</param>
    /// <returns>The domain wrapper.</returns>
    public static MappedRequired Final(int state) => new(state);
}
