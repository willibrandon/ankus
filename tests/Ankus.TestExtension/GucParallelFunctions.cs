using System.Globalization;
using System.Text;

namespace Ankus.TestExtension;

/// <summary>
/// Observes native configuration values and regenerated hook data in actual parallel workers.
/// </summary>
public static partial class GucParallelFunctions
{
    private static readonly int s_initialProcess = Environment.ProcessId;
    private static PgGucExtra? s_booleanExtra;
    private static PgGucExtra? s_integerExtra;
    private static PgGucExtra? s_realExtra;
    private static PgGucExtra? s_textExtra;
    private static PgGucExtra? s_modeExtra;

    /// <summary>
    /// Gets a Boolean whose extra identifies the backend that checked it.
    /// </summary>
    [PgGucBool("ankus_parallel.boolean", true, "Parallel Boolean", Check = nameof(CheckBoolean), Assign = nameof(AssignBoolean))]
    public static partial bool Enabled { get; }

    /// <summary>
    /// Gets an integer whose extra follows function-local setting restoration.
    /// </summary>
    [PgGucInt("ankus_parallel.integer", 10, "Parallel integer", Check = nameof(CheckInteger), Assign = nameof(AssignInteger))]
    public static partial int Count { get; }

    /// <summary>
    /// Gets an exactly represented fractional setting.
    /// </summary>
    [PgGucReal("ankus_parallel.real", 0, "Parallel real", Check = nameof(CheckReal), Assign = nameof(AssignReal))]
    public static partial double Ratio { get; }

    /// <summary>
    /// Gets a nullable string, including a hook-normalized nondefault null value.
    /// </summary>
    [PgGucString("ankus_parallel.text", null, "Parallel text", Check = nameof(CheckText), Assign = nameof(AssignText))]
    public static partial string? Text { get; }

    /// <summary>
    /// Gets a wide enumeration with canonical, alias, and hidden labels.
    /// </summary>
    [PgGucEnum("ankus_parallel.mode", GucMode.Fast, "Parallel mode", Check = nameof(CheckMode), Assign = nameof(AssignMode))]
    public static partial GucMode Mode { get; }

    /// <summary>
    /// Gets an optional process restriction used to reject restoration only in foreign workers.
    /// </summary>
    [PgGucInt("ankus_parallel.owner", 0, "Parallel owner", Check = nameof(CheckOwner))]
    public static partial int Owner { get; }

    /// <summary>
    /// Reads five native values, regenerated extras, and backend-local managed initialization.
    /// </summary>
    /// <param name="discriminator">A table-derived discriminator that prevents constant folding.</param>
    /// <returns>The discriminator, process identities, five values, five extras, and initializer state.</returns>
    [PgFunction(ParallelSafety = PgParallelSafety.Safe, Volatility = PgVolatility.Stable)]
    public static string?[] GucParallelSnapshot(int discriminator) =>
    [
        discriminator.ToString(CultureInfo.InvariantCulture),
        Environment.ProcessId.ToString(CultureInfo.InvariantCulture),
        s_initialProcess.ToString(CultureInfo.InvariantCulture),
        Enabled.ToString(),
        Count.ToString(CultureInfo.InvariantCulture),
        BitConverter.DoubleToInt64Bits(Ratio).ToString(CultureInfo.InvariantCulture),
        Text,
        ((ulong)Mode).ToString(CultureInfo.InvariantCulture),
        ReadExtra(s_booleanExtra),
        ReadExtra(s_integerExtra),
        ReadExtra(s_realExtra),
        ReadExtra(s_textExtra),
        ReadExtra(s_modeExtra),
        InitializationFunctions.InitializationState(),
    ];

    /// <summary>
    /// Reaches PostgreSQL's native configuration mutation restriction from a worker.
    /// </summary>
    /// <param name="value">The attempted new integer value.</param>
    /// <param name="local">Whether the change is local to the current transaction.</param>
    /// <returns>The native integer if PostgreSQL unexpectedly accepts the setting.</returns>
    [PgFunction(ParallelSafety = PgParallelSafety.Safe)]
    public static int GucParallelSet(int value, bool local)
    {
        Spi.Query("SELECT set_config('ankus_parallel.integer', $1, $2)", readOnly: true, limit: 1,
            SpiParameter.Create(value.ToString(CultureInfo.InvariantCulture)), SpiParameter.Create(local));
        return Count;
    }

    /// <summary>
    /// Reads a function-scoped value through its actual table input.
    /// </summary>
    /// <param name="value">The table-derived value added to the current integer.</param>
    /// <returns>The current configured integer plus its argument.</returns>
    [PgFunction(ParallelSafety = PgParallelSafety.Safe, Volatility = PgVolatility.Stable)]
    public static int GucParallelInteger(int value) => checked(Count + value);

    /// <summary>
    /// Calls a function with native proconfig and observes restoration in the same worker.
    /// </summary>
    /// <param name="value">The input forwarded through the function-local setting.</param>
    /// <returns>The process, before/during/after values, and before/after owned extras.</returns>
    [PgFunction(ParallelSafety = PgParallelSafety.Safe, Volatility = PgVolatility.Stable)]
    public static string?[] GucParallelScope(int value)
    {
        int before = Count;
        string? extraBefore = ReadExtra(s_integerExtra);
        SpiResult result = Spi.Query("SELECT datatype.guc_parallel_scoped($1)", readOnly: true, limit: 1, SpiParameter.Create(value));
        return
        [
            Environment.ProcessId.ToString(CultureInfo.InvariantCulture),
            before.ToString(CultureInfo.InvariantCulture),
            checked(result[0].Get<int>(0) - value).ToString(CultureInfo.InvariantCulture),
            Count.ToString(CultureInfo.InvariantCulture),
            extraBefore,
            ReadExtra(s_integerExtra),
        ];
    }

    /// <summary>
    /// Accepts a Boolean with worker-owned evidence of its source and value.
    /// </summary>
    /// <param name="value">The proposed Boolean.</param>
    /// <param name="source">The native source retained during propagation.</param>
    /// <returns>The accepted value and fresh extra.</returns>
    internal static PgGucCheckResult<bool> CheckBoolean(bool value, PgGucSource source)
        => new(value, MakeExtra(value.ToString(), source));

    /// <summary>
    /// Accepts an integer with worker-owned evidence of its source and value.
    /// </summary>
    /// <param name="value">The proposed integer.</param>
    /// <param name="source">The native source retained during propagation.</param>
    /// <returns>The accepted value and fresh extra.</returns>
    internal static PgGucCheckResult<int> CheckInteger(int value, PgGucSource source)
        => new(value, MakeExtra(value.ToString(CultureInfo.InvariantCulture), source));

    /// <summary>
    /// Accepts a real with worker-owned evidence of its exact bits.
    /// </summary>
    /// <param name="value">The proposed real.</param>
    /// <param name="source">The native source retained during propagation.</param>
    /// <returns>The accepted value and fresh extra.</returns>
    internal static PgGucCheckResult<double> CheckReal(double value, PgGucSource source)
        => new(value, MakeExtra(BitConverter.DoubleToInt64Bits(value).ToString(CultureInfo.InvariantCulture), source));

    /// <summary>
    /// Allows the test to distinguish default null from a nondefault null serialized by PostgreSQL.
    /// </summary>
    /// <param name="value">The proposed text or null.</param>
    /// <param name="source">The native source retained during propagation.</param>
    /// <returns>The accepted text and fresh extra.</returns>
    internal static PgGucCheckResult<string?> CheckText(string? value, PgGucSource source)
    {
        string? accepted = value == "make-null" ? null : value;
        return new(accepted, MakeExtra(accepted ?? "<null>", source));
    }

    /// <summary>
    /// Accepts an enum with worker-owned evidence of its full unsigned value.
    /// </summary>
    /// <param name="value">The proposed enumeration value.</param>
    /// <param name="source">The native source retained during propagation.</param>
    /// <returns>The accepted value and fresh extra.</returns>
    internal static PgGucCheckResult<GucMode> CheckMode(GucMode value, PgGucSource source)
        => new(value, MakeExtra(((ulong)value).ToString(CultureInfo.InvariantCulture), source));

    /// <summary>
    /// Rejects a value accepted by a different backend during worker restoration.
    /// </summary>
    /// <param name="value">Zero or the required process ID.</param>
    /// <param name="source">The source reported in native rejection detail.</param>
    /// <returns>The accepted owner or a deterministic worker rejection.</returns>
    internal static PgGucCheckResult<int> CheckOwner(int value, PgGucSource source)
        => value == 0 || value == Environment.ProcessId ? new(value) :
            new(new PgGucCheckError("Configuration belongs to another backend.",
                $"source={source};owner={value};worker={Environment.ProcessId}", sqlState: "P7821"));

    /// <summary>
    /// Retains the Boolean's copied extra after native assignment.
    /// </summary>
    /// <param name="value">The accepted Boolean whose storage is still being updated.</param>
    /// <param name="extra">The copied extra generated by check.</param>
    internal static void AssignBoolean(bool value, PgGucExtra? extra) => s_booleanExtra = ValidateExtra(value.ToString(), extra);

    /// <summary>
    /// Retains the integer's copied extra after assignment or restoration.
    /// </summary>
    /// <param name="value">The accepted integer.</param>
    /// <param name="extra">The copied extra associated with that accepted value.</param>
    internal static void AssignInteger(int value, PgGucExtra? extra)
        => s_integerExtra = ValidateExtra(value.ToString(CultureInfo.InvariantCulture), extra);

    /// <summary>
    /// Retains the real's copied extra after native assignment.
    /// </summary>
    /// <param name="value">The accepted real.</param>
    /// <param name="extra">The copied extra associated with that accepted value.</param>
    internal static void AssignReal(double value, PgGucExtra? extra)
        => s_realExtra = ValidateExtra(BitConverter.DoubleToInt64Bits(value).ToString(CultureInfo.InvariantCulture), extra);

    /// <summary>
    /// Retains nullable text extra after native assignment.
    /// </summary>
    /// <param name="value">The accepted text.</param>
    /// <param name="extra">The copied extra associated with that accepted value.</param>
    internal static void AssignText(string? value, PgGucExtra? extra) => s_textExtra = ValidateExtra(value ?? "<null>", extra);

    /// <summary>
    /// Retains enum extra after native assignment.
    /// </summary>
    /// <param name="value">The accepted wide enum value.</param>
    /// <param name="extra">The copied extra associated with that accepted value.</param>
    internal static void AssignMode(GucMode value, PgGucExtra? extra)
        => s_modeExtra = ValidateExtra(((ulong)value).ToString(CultureInfo.InvariantCulture), extra);

    private static PgGucExtra MakeExtra(string value, PgGucSource source)
        => new(Encoding.UTF8.GetBytes($"{Environment.ProcessId}|{source}|{value}"));

    private static string? ReadExtra(PgGucExtra? extra) => extra is null ? null : Encoding.UTF8.GetString(extra.AsSpan());

    private static PgGucExtra ValidateExtra(string value, PgGucExtra? extra)
    {
        if (extra is null || !Encoding.UTF8.GetString(extra.AsSpan()).EndsWith("|" + value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Assignment extra does not match its accepted value.");
        }

        return extra;
    }
}
