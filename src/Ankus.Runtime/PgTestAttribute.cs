namespace Ankus;

/// <summary>
/// Declares a synchronous, zero-SQL-argument test that runs inside PostgreSQL.
/// Native entry points are emitted only when AnkusIncludeTests is enabled.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class PgTestAttribute : Attribute
{
    /// <summary>
    /// Gets or sets an ordered schema search path scoped to this test; null preserves the caller's path.
    /// Use <see cref="PgSearchPath.ExtensionSchema"/> for the schema selected during CREATE EXTENSION.
    /// </summary>
    public string[]? SearchPath
    {
        get;
        set;
    }

    /// <summary>
    /// Gets or sets the exact PostgreSQL primary error message expected from the test, or null for success.
    /// </summary>
    public string? ExpectedError
    {
        get;
        set;
    }

    /// <summary>
    /// Gets or sets a reason for the host test framework to report this test as ignored.
    /// </summary>
    public string? IgnoreReason
    {
        get;
        set;
    }
}
