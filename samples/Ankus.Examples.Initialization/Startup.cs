namespace Ankus.Examples.Initialization;

/// <summary>
/// Initializes the extension once per successful backend library load.
/// </summary>
public static class Startup
{
    /// <summary>
    /// Reports initialization through PostgreSQL's guarded logging boundary.
    /// </summary>
    [PgInitialize]
    public static void Initialize() => PgLog.Write(PgLogLevel.Notice, "Ankus initialization sample loaded.");
}
