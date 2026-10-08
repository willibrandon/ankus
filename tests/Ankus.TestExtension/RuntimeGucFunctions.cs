using System.Globalization;

namespace Ankus.TestExtension;

/// <summary>
/// Defines configuration parameters with names computed at run time, as pgrx extensions do through GucRegistry.
/// </summary>
public static class RuntimeGucFunctions
{
    private const string Prefix = "ankus_runtime";

    private static PgGucSetting<bool>? s_enabled;
    private static PgGucSetting<int>? s_limit;
    private static PgGucSetting<double>? s_ratio;
    private static PgGucSetting<string?>? s_label;
    private static PgGucSetting<int>? s_mode;

    /// <summary>
    /// Defines one parameter of each kind; module load calls this on every load attempt.
    /// </summary>
    internal static void DefineAtLoad()
    {
        string Name(string suffix) => Prefix + "." + suffix;
        s_enabled = PgGucRegistry.DefineBool(Name("enabled"), true, "Run-time Boolean", "Defined after the name is computed.");
        s_limit = PgGucRegistry.DefineInt(Name("limit"), 10, "Run-time integer", minimum: 1, maximum: 4096, unit: PgGucUnit.Kilobytes);
        s_ratio = PgGucRegistry.DefineReal(Name("ratio"), 0.25, "Run-time real", minimum: 0, maximum: 1);
        s_label = PgGucRegistry.DefineString(Name("label"), null, "Run-time string", context: PgGucContext.SuperuserSet);
        s_mode = PgGucRegistry.DefineEnum(Name("mode"), 1, "Run-time enumeration",
            [new PgGucEnumOption("off", 0), new PgGucEnumOption("on", 1), new PgGucEnumOption("true", 1, Hidden: true), new PgGucEnumOption("auto", 2)]);
    }

    /// <summary>
    /// Reads every load-time parameter through its managed reader.
    /// </summary>
    /// <returns>The values separated by vertical bars.</returns>
    [PgFunction]
    public static string RuntimeGucValues()
        => string.Join('|', s_enabled!.Value, s_limit!.Value, s_ratio!.Value.ToString(CultureInfo.InvariantCulture),
            s_label!.Value ?? "<null>", s_mode!.Value);

    /// <summary>
    /// Defines an integer parameter from SQL to test definition after load, repeated definitions and rejected names.
    /// </summary>
    /// <param name="name">The qualified name.</param>
    /// <param name="defaultValue">The default value.</param>
    /// <param name="maximum">The inclusive maximum.</param>
    /// <param name="context">The <see cref="PgGucContext"/> value.</param>
    /// <returns>The parameter's current value.</returns>
    [PgFunction]
    public static int RuntimeGucDefineInt(string name, int defaultValue, int maximum, int context)
        => PgGucRegistry.DefineInt(name, defaultValue, "SQL-defined integer", 0, maximum, context: (PgGucContext)context).Value;

    /// <summary>
    /// Attempts to redefine an attribute-declared parameter at run time.
    /// </summary>
    /// <returns>Never returns normally.</returns>
    [PgFunction]
    public static bool RuntimeGucRedefineAttribute() => PgGucRegistry.DefineBool("ankus_test.module_load", true, "Conflicting definition").Value;
}
