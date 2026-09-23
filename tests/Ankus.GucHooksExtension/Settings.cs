namespace Ankus.GucHooksExtension;

/// <summary>
/// Exercises a check-only native library without a managed initializer or SQL exports.
/// </summary>
public static partial class Settings
{
    /// <summary>
    /// Gets a setting whose boot check demonstrates managed entry in the backend.
    /// </summary>
    [PgGucBool("ankus_guc_hooks.enabled", true, "Enable the hooks-only example", Check = nameof(Check))]
    public static partial bool Enabled { get; }

    /// <summary>
    /// Reports entry into the check callback and accepts the proposed value.
    /// </summary>
    /// <param name="proposed">The parsed Boolean value.</param>
    /// <param name="source">The native setting source.</param>
    /// <returns>The accepted value.</returns>
    internal static PgGucCheckResult<bool> Check(bool proposed, PgGucSource source)
    {
        string sql;
        try
        {
            sql = Spi.ExecuteScalar<int>("SELECT 42").ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (InvalidOperationException)
        {
            sql = "unavailable";
        }

        PgLog.Write(PgLogLevel.Notice, new PgDiagnostic("Ankus hooks-only check entered.")
        {
            Detail = $"source={source};sql={sql};pid={Environment.ProcessId}",
        });
        return new(proposed);
    }
}
