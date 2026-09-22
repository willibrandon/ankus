using Microsoft.CodeAnalysis;

namespace Ankus.Generators;

/// <summary>
/// Represents one installation step and its explicit and automatically resolved prerequisites.
/// </summary>
internal sealed class SqlEntity
{
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
    /// Gets the stable key used for deterministic ordering of independent nodes.
    /// </summary>
    internal string Key { get; }

    /// <summary>
    /// Gets or sets the complete installation SQL for this node.
    /// </summary>
    internal string Sql { get; set; }

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
    /// Gets resolved prerequisite nodes, including generated schema dependencies.
    /// </summary>
    internal HashSet<SqlEntity> Dependencies { get; } = [];

    /// <summary>
    /// Gets or sets the normal, bootstrap, or final positioning discriminator.
    /// </summary>
    internal int Order { get; set; }

    /// <summary>
    /// Gets a human-readable name for diagnostics.
    /// </summary>
    internal string DisplayName => Names.OrderBy(static name => name, StringComparer.Ordinal).FirstOrDefault() ?? Key;
}
