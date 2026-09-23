namespace Ankus.GucOnlyExtension;

/// <summary>
/// Proves a library with no managed native exports can register native settings.
/// </summary>
public static partial class Settings
{
    /// <summary>
    /// Gets an ordinary native Boolean.
    /// </summary>
    [PgGucBool("ankus_guc_only.enabled", true, "Enable the native-only café example")]
    public static partial bool Enabled { get; }
}
