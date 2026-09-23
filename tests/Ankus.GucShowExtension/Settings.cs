namespace Ankus.GucShowExtension;

/// <summary>
/// Exercises a show-only library without managed initialization or SQL exports.
/// </summary>
public static partial class Settings
{
    /// <summary>
    /// Gets a setting with a display callback and no other managed hooks.
    /// </summary>
    [PgGucInt("ankus_guc_show.count", 7, "Display example", Show = nameof(Show))]
    public static partial int Count { get; }

    /// <summary>
    /// Observes current native storage from inside the display callback without invoking itself recursively.
    /// </summary>
    /// <param name="current">The current integer.</param>
    /// <param name="extra">The optional copied hook data.</param>
    /// <returns>The display value and independently read native value.</returns>
    internal static string Show(int current, PgGucExtra? extra)
        => $"display={current};native={Count};extra={(extra is null ? "none" : "present")}";
}
