namespace Ankus;

/// <summary>
/// Exposes a static .NET method as a PostgreSQL function through generated native entry points and SQL.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class PgFunctionAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the SQL function name. The default is the method name converted to snake_case.
    /// </summary>
    public string? Name { get; set; }
}
