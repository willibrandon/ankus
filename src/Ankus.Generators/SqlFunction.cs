namespace Ankus.Generators;

/// <summary>
/// Retains a validated function declaration until cross-function identities and prerequisites have been resolved.
/// </summary>
/// <param name="declaration">The configured identity and execution options.</param>
/// <param name="definition">The rendered definition preceding graph-selected support and its statement terminator.</param>
/// <param name="isPlannerSupport">Whether the SQL signature is a nonvariadic scalar internal-to-internal function.</param>
/// <param name="requiresAggregateContext">Whether the native entry point requires an aggregate or window invocation.</param>
internal sealed class SqlFunction(FunctionDeclaration declaration, string definition,
    bool isPlannerSupport, bool requiresAggregateContext = false)
{
    private string? _plannerSupport;

    /// <summary>
    /// Gets the validated function identity and execution options.
    /// </summary>
    internal FunctionDeclaration Declaration { get; } = declaration;

    /// <summary>
    /// Gets whether PostgreSQL accepts this function's argument and result signature for planner support.
    /// </summary>
    internal bool IsPlannerSupport { get; } = isPlannerSupport;

    /// <summary>
    /// Gets whether the native entry point rejects calls outside an aggregate or window invocation.
    /// </summary>
    internal bool RequiresAggregateContext { get; } = requiresAggregateContext;

    /// <summary>
    /// Composes specialized callback families using the shared SQL definition grammar.
    /// </summary>
    /// <param name="declaration">The validated SQL identity and execution options.</param>
    /// <param name="arguments">The complete argument clauses.</param>
    /// <param name="result">The complete return clause.</param>
    /// <param name="nativeName">The generated native entry identity.</param>
    /// <param name="isPlannerSupport">Whether the signature is a scalar internal-to-internal function.</param>
    /// <param name="requiresAggregateContext">Whether calls require aggregate or window context.</param>
    /// <returns>A fresh graph-owned function with no retained compiler symbols.</returns>
    internal static SqlFunction Create(FunctionDeclaration declaration, string arguments, string result, string nativeName,
        bool isPlannerSupport, bool requiresAggregateContext = false)
        => new(declaration, FunctionSqlEmission.Format(declaration, arguments, result, nativeName), isPlannerSupport, requiresAggregateContext);

    /// <summary>
    /// Adds graph-resolved planner support without mutating the immutable declaration shared with emission.
    /// </summary>
    /// <param name="name">The quoted SQL function name with its schema selection marker.</param>
    internal void SetPlannerSupport(string name) => _plannerSupport = name;

    /// <summary>
    /// Renders SQL only after the graph has resolved cross-function configuration.
    /// </summary>
    /// <returns>The complete SQL definition with schema selection markers retained.</returns>
    internal string Emit()
        => definition + (_plannerSupport is null ? string.Empty : " SUPPORT " + _plannerSupport) + ";\n";
}
