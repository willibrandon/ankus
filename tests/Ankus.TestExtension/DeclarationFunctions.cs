using System.Globalization;

namespace Ankus.TestExtension;

/// <summary>
/// Exercises function declaration metadata and backend dispatch in a fixed schema.
/// </summary>
[PgSchema("ankus_contract")]
public static class DeclarationFunctions
{
    /// <summary>
    /// Supplies a pure function with explicit planner, security, and strictness options.
    /// </summary>
    [PgFunction(Volatility = PgVolatility.Immutable, ParallelSafety = PgParallelSafety.Safe, Leakproof = true, Cost = 2.5)]
    public static int DeclarationIdentity(int inputValue) => inputValue;

    /// <summary>
    /// Reads the function-scoped path with stable, leader-only execution metadata.
    /// </summary>
    [PgFunction(Volatility = PgVolatility.Stable, ParallelSafety = PgParallelSafety.Restricted,
        SearchPath = ["pg_catalog", "ankus_contract", "pg_temp"])]
    public static string DeclarationPath() => Spi.ExecuteScalar<string>("SELECT current_setting('search_path')");

    /// <summary>
    /// Reads protected data using the installing role's privileges and a pinned schema path.
    /// </summary>
    [PgFunction(SecurityDefiner = true, SearchPath = ["pg_catalog", "ankus_contract", "pg_temp"])]
    public static string DeclarationSecret() => Spi.ExecuteScalar<string>("SELECT value FROM declaration_secret");

    /// <summary>
    /// Uses caller privileges for the same protected query.
    /// </summary>
    [PgFunction(SearchPath = ["pg_catalog", "ankus_contract", "pg_temp"])]
    public static string DeclarationInvoker() => Spi.ExecuteScalar<string>("SELECT value FROM declaration_secret");

    /// <summary>
    /// Proves STRICT suppresses managed dispatch even for nullable parameters.
    /// </summary>
    [PgFunction(NullInput = PgNullInput.Strict)]
    public static int DeclarationStrict(int? value) => value ?? 42;

    /// <summary>
    /// Receives NULL through an explicitly non-strict contract.
    /// </summary>
    [PgFunction(NullInput = PgNullInput.CalledOnNull)]
    public static int DeclarationCalled(int? value) => value ?? 42;

    /// <summary>
    /// Clears search_path for the call while retaining PostgreSQL's implicit system schema lookup.
    /// </summary>
    [PgFunction(SearchPath = [])]
    public static string DeclarationEmptyPath() => Spi.ExecuteScalar<string>("SELECT current_setting('search_path')");

    /// <summary>
    /// Preserves an IEEE NaN optional constant.
    /// </summary>
    [PgFunction]
    public static double DeclarationNaN(double value = double.NaN) => value;

    /// <summary>
    /// Preserves an IEEE negative-zero optional constant.
    /// </summary>
    [PgFunction]
    public static float DeclarationNegativeZero(float value = -0.0f) => value;

    /// <summary>
    /// Treats an optional NULL distinctly from zero.
    /// </summary>
    [PgFunction]
    public static int DeclarationNullable(int? value = null) => value ?? 99;

    /// <summary>
    /// Casts a signed-byte numeric default to PostgreSQL's internal char type.
    /// </summary>
    [PgFunction]
    public static sbyte DeclarationChar(sbyte value = -128) => value;

    /// <summary>
    /// Retains the unsigned OID maximum in an optional constant.
    /// </summary>
    [PgFunction]
    public static uint DeclarationOid(uint value = uint.MaxValue) => value;

    /// <summary>
    /// Preserves signed minimum defaults without applying a narrowing SQL cast before unary negation.
    /// </summary>
    [PgFunction]
    public static string DeclarationMinimums(short small = short.MinValue, int middle = int.MinValue, long big = long.MinValue)
        => $"{small}:{middle}:{big}";

    /// <summary>
    /// Exposes C# optional constants, including exact decimal scale and escaped text, as SQL defaults.
    /// </summary>
    [PgFunction]
    public static string DeclarationDefaults(int firstCount = -12, string text = "quote' slash\\ café", decimal amount = 1.2300m)
        => $"{firstCount}:{text}:{amount.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>
    /// Allows an explicit SQL argument name and a server-evaluated default.
    /// </summary>
    [PgFunction]
    public static PgDate DeclarationDefaultDate([PgParameter(Name = "when", Default = "current_date")] PgDate date) => date;

    /// <summary>
    /// Gives SQL callers an empty default variadic array while retaining normal C# params usage.
    /// </summary>
    [PgFunction]
    public static int DeclarationDefaultVariadic([PgParameter(Default = "ARRAY[]::integer[]")] params int[] values) => values.Sum();

    /// <summary>
    /// Preserves non-nullable value-type defaults instead of substituting SQL NULL.
    /// </summary>
    [PgFunction]
    public static string DeclarationStructDefaults(Guid uuid = default, PgDate date = default, DateOnly day = default,
        PgTimestamp timestamp = default, PgTimestampTz instant = default, PgTimeTz time = default,
        PgNumeric number = default, PgJson json = default, PgInterval interval = default)
        => $"{uuid}:{date.DaysSinceEpoch}:{day:yyyy-MM-dd}:{timestamp.MicrosecondsSinceEpoch}:" +
            $"{instant.MicrosecondsSinceEpoch}:{time.Time.Microseconds}:{time.OffsetSeconds}:{number.Text}:{json.Text}:{interval.Months},{interval.Days},{interval.Microseconds}";

    /// <summary>
    /// Installs an existing PostgreSQL planner support routine on an equivalent prefix function.
    /// </summary>
    [PgFunction(Volatility = PgVolatility.Immutable, ParallelSafety = PgParallelSafety.Safe, SupportFunction = "pg_catalog.text_starts_with_support")]
    public static bool DeclarationPrefix(string value, string prefix) => value.StartsWith(prefix, StringComparison.Ordinal);

    /// <summary>
    /// Overrides the containing schema with a quoted Unicode identifier.
    /// </summary>
    [PgFunction(Schema = "ankus café \"schema", CreateOrReplace = true)]
    public static int DeclarationOverride(int value) => value;

    /// <summary>
    /// Checks containing-schema inheritance for nested managed types.
    /// </summary>
    public static class Nested
    {
        /// <summary>
        /// Uses the enclosing schema without repeating its attribute.
        /// </summary>
        [PgFunction]
        public static int DeclarationNested() => 123;
    }
}
