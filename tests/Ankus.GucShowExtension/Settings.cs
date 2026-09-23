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
    {
        using PgMemoryContext memory = PgMemoryContext.Create("show memory");
        using PgAllocation value = memory.Allocate(sizeof(int));
        value.Write(current);
        if (current is 667 or 668 or 669)
        {
            try
            {
                PgLog.Write(current == 667 ? PgLogLevel.Error : PgLogLevel.Fatal,
                    new PgDiagnostic(current == 669 ? "Unrepresentable 🐘" : "Display requested termination.")
                    {
                        SqlState = "P0001",
                        Detail = "Managed frames unwind first.",
                        File = "Display.cs",
                        Routine = "Display",
                        Line = 41,
                    });
            }
            finally
            {
                PgLog.Write(PgLogLevel.Notice, $"Display finally {current}.");
            }
        }

        string sql;
        try
        {
            sql = Spi.ExecuteScalar<int>("SELECT 42").ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (InvalidOperationException)
        {
            sql = "unavailable";
        }

        PgLog.Write(PgLogLevel.Notice, $"show={current};sql={sql};café 100%");
        PgLog.Write(PgLogLevel.Debug1, "Display debug message.");
        return $"display={value.Read<int>()};native={Count};extra={(extra is null ? "none" : "present")}";
    }
}
