using Microsoft.CodeAnalysis;

namespace Ankus.Generators;

/// <summary>
/// Represents one installation step and its explicit and automatically resolved prerequisites.
/// </summary>
internal sealed class SqlEntity
{
    private string _sql = string.Empty;
    private string? _sqlTemplate = string.Empty;
    /// <summary>
    /// Creates an installation node with a stable internal key and diagnostic location.
    /// </summary>
    /// <param name="key">The unique internal sort key.</param>
    /// <param name="sql">The emitted SQL.</param>
    /// <param name="location">The source declaration location.</param>
    internal SqlEntity(string key, string sql, Location? location)
    {
        Key = key;
        Sql = sql;
        Location = location;
    }

    /// <summary>
    /// Creates a function node whose SQL is rendered after planner support references are resolved.
    /// </summary>
    /// <param name="key">The unique internal sort key.</param>
    /// <param name="function">The validated function definition.</param>
    /// <param name="location">The source declaration location.</param>
    internal SqlEntity(string key, SqlFunction function, Location? location) : this(key, string.Empty, location)
    {
        Function = function;
        _sqlTemplate = null;
    }

    /// <summary>
    /// Gets the original generated function contract, including when its SQL is disabled or replaced.
    /// </summary>
    internal SqlFunction? Function { get; }

    /// <summary>
    /// Gets the stable key used for deterministic ordering of independent nodes.
    /// </summary>
    internal string Key { get; }

    /// <summary>
    /// Gets or sets the complete installation SQL for this node.
    /// </summary>
    internal string Sql
    {
        get => _sqlTemplate is null ? Function!.Emit().Replace("\0", string.Empty) : _sql;
        set
        {
            _sqlTemplate = value;
            _sql = value.Replace("\0", string.Empty);
        }
    }

    /// <summary>
    /// Gets typed schema markers separately from ordinary installation SQL.
    /// </summary>
    internal string SqlTemplate => _sqlTemplate ?? Function!.Emit();

    /// <summary>
    /// Gets the source location used for graph diagnostics.
    /// </summary>
    internal Location? Location { get; }

    /// <summary>
    /// Gets dependency aliases exported by this node.
    /// </summary>
    internal HashSet<string> Names { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets explicit identifiers that must precede this node.
    /// </summary>
    internal HashSet<string> Requires { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets explicit identifiers that must follow this node.
    /// </summary>
    internal HashSet<string> Before { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets additional SQL and managed names for selecting this declaration without declaring dependency aliases.
    /// </summary>
    internal HashSet<string> SelectionNames { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets generated object identities for PostgreSQL extension attachment.
    /// </summary>
    internal HashSet<string> Attachments { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets or sets the declaration kind carried into native schema metadata.
    /// </summary>
    internal string Kind
    {
        get;
        set;
    } = "sql";

    /// <summary>
    /// Gets or sets the declaration family whose members must be selected together.
    /// </summary>
    internal SqlEntity? Owner
    {
        get;
        set;
    }

    /// <summary>
    /// Gets resolved prerequisite nodes, including generated schema dependencies.
    /// </summary>
    internal HashSet<SqlEntity> Dependencies { get; } = [];

    /// <summary>
    /// Gets compiler-resolved explicit prerequisites, which retain explicit shell-type ordering semantics.
    /// </summary>
    internal HashSet<SqlEntity> DeclaredDependencies { get; } = [];

    /// <summary>
    /// Gets typed Requires edges inherited by aggregate support functions independently of incoming Before edges.
    /// </summary>
    internal HashSet<SqlEntity> RequiredDeclarations { get; } = [];

    /// <summary>
    /// Gets inferred custom SQL type prerequisites that may follow an explicitly ordered shell-type consumer.
    /// </summary>
    internal HashSet<SqlEntity> TypeDependencies { get; } = [];

    /// <summary>
    /// Gets or sets the normal, bootstrap, or final positioning discriminator.
    /// </summary>
    internal int Order
    {
        get;
        set;
    }

    /// <summary>
    /// Gets a human-readable name for diagnostics.
    /// </summary>
    internal string DisplayName => Names.OrderBy(static name => name, StringComparer.Ordinal).FirstOrDefault() ?? Key;
}
