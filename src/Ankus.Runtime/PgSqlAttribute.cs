namespace Ankus;

/// <summary>
/// Includes trusted SQL text in the extension installation script with explicit dependency ordering.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class PgSqlAttribute : Attribute
{
    /// <summary>
    /// Declares a named SQL block.
    /// </summary>
    /// <param name="name">The unique, case-sensitive dependency identifier.</param>
    /// <param name="sql">The complete SQL statements, including their terminators.</param>
    public PgSqlAttribute(string name, string sql)
    {
        Name = name;
        Sql = sql;
    }

    /// <summary>
    /// Gets the block's dependency identifier.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the SQL text, which PostgreSQL validates and executes during installation.
    /// </summary>
    public string Sql { get; }

    /// <summary>
    /// Gets or sets identifiers of declarations that must run before this block.
    /// </summary>
    public string[] Requires { get; set; } = [];

    /// <summary>
    /// Gets or sets identifiers of declarations that must run after this block.
    /// </summary>
    public string[] Before { get; set; } = [];

    /// <summary>
    /// Gets or sets whether this block runs first, last, or according to its explicit dependencies.
    /// </summary>
    public PgSqlOrder Order { get; set; }

    /// <summary>
    /// Gets or sets whether every object and reference in this block permits extension schema relocation.
    /// The default is false because arbitrary SQL may refer to fixed schemas.
    /// </summary>
    public bool Relocatable { get; set; }
}
