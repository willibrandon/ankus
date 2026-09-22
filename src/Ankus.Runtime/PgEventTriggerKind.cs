namespace Ankus;

/// <summary>
/// Identifies the PostgreSQL event that invoked an event trigger function.
/// </summary>
public enum PgEventTriggerKind
{
    /// <summary>
    /// Runs before a supported DDL command changes the catalog.
    /// </summary>
    DdlCommandStart,

    /// <summary>
    /// Runs after a supported DDL command has changed the catalog.
    /// </summary>
    DdlCommandEnd,

    /// <summary>
    /// Runs after objects have been removed by a supported DDL command.
    /// </summary>
    SqlDrop,

    /// <summary>
    /// Runs before an existing table is rewritten.
    /// </summary>
    TableRewrite,

    /// <summary>
    /// Runs when a database session logs in. PostgreSQL supports this event starting with version 17.
    /// </summary>
    Login,
}
