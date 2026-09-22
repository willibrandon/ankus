namespace Ankus;

/// <summary>
/// Defines the ordered UTF-8 diagnostic slots shared with the generated native error bridge.
/// </summary>
internal enum NativeDiagnosticField
{
    /// <summary>
    /// Contains the primary message.
    /// </summary>
    Message,

    /// <summary>
    /// Contains additional client-visible detail.
    /// </summary>
    Detail,

    /// <summary>
    /// Contains a corrective hint.
    /// </summary>
    Hint,

    /// <summary>
    /// Contains the execution context.
    /// </summary>
    Context,

    /// <summary>
    /// Contains the schema name.
    /// </summary>
    Schema,

    /// <summary>
    /// Contains the table name.
    /// </summary>
    Table,

    /// <summary>
    /// Contains the column name.
    /// </summary>
    Column,

    /// <summary>
    /// Contains the data type name.
    /// </summary>
    DataType,

    /// <summary>
    /// Contains the constraint name.
    /// </summary>
    Constraint,

    /// <summary>
    /// Contains the internally executed query.
    /// </summary>
    InternalQuery,

    /// <summary>
    /// Contains the source filename.
    /// </summary>
    File,

    /// <summary>
    /// Contains the source routine name.
    /// </summary>
    Routine,

    /// <summary>
    /// Contains server-only detail.
    /// </summary>
    DetailLog,

    /// <summary>
    /// Contains the native backtrace.
    /// </summary>
    Backtrace,
}
