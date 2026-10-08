namespace Ankus.TestExtension;

/// <summary>
/// Reproduces a hook registration that PostgreSQL retries after the library's first load fails.
/// </summary>
public static partial class ModuleLoadFunctions
{
    private static int s_attempts;

    /// <summary>
    /// Gets the module registration mode; the default registers nothing.
    /// </summary>
    [PgGucString("ankus_test.module_load", "default", "Module registration test mode")]
    public static partial string Mode { get; }

    /// <summary>
    /// In <c>unguarded-hook</c> mode, installs the utility hook without an installation check and fails the first attempt.
    /// </summary>
    [PgModuleLoad]
    public static void Register()
    {
        if (Mode != "unguarded-hook")
        {
            return;
        }

        s_attempts++;
        NativeUtilityHookFunctions.InstallUnguarded();
        if (s_attempts == 1)
        {
            throw new PgException("P7870", "Module registration failed after installing its hook.");
        }
    }

    /// <summary>
    /// Reads the number of registration attempts made in this backend.
    /// </summary>
    /// <returns>The attempt count.</returns>
    [PgFunction]
    public static int ModuleLoadAttempts() => s_attempts;
}
