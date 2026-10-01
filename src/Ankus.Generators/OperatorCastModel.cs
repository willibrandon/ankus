namespace Ankus.Generators;

/// <summary>
/// Contains validated operator or cast catalog contracts independently of compiler and graph state.
/// </summary>
/// <param name="Operator">The prefix or binary operator, or null for a cast.</param>
/// <param name="Cast">The conversion contract, or null for an operator.</param>
internal sealed record OperatorCastModel(OperatorCastModel.OperatorDefinition? Operator, OperatorCastModel.CastDefinition? Cast)
{
    /// <summary>
    /// Retains normalized operator and backing-function identities alongside typed operand catalog fragments.
    /// </summary>
    /// <param name="Name">The normalized qualified operator spelling without a default-schema marker.</param>
    /// <param name="TemplateName">The normalized spelling with its extension-default schema marker.</param>
    /// <param name="Function">The escaped backing-function identity.</param>
    /// <param name="Left">The left operand, or null for a prefix operator.</param>
    /// <param name="Right">The right operand.</param>
    /// <param name="Options">The validated references and capability clauses in authored grammar order.</param>
    internal sealed record OperatorDefinition(string Name, string TemplateName, string Function, SqlTypeTemplate? Left,
        SqlTypeTemplate Right, EquatableArray<string> Options);

    /// <summary>
    /// Retains a cast's backing function, source/modifier operands and result catalog identity.
    /// </summary>
    /// <param name="Function">The escaped backing-function identity.</param>
    /// <param name="Arguments">The ordered source and optional type-modifier/explicitness catalog fragments.</param>
    /// <param name="Result">The result catalog identity.</param>
    /// <param name="Context">The validated explicit, assignment or implicit conversion policy.</param>
    internal sealed record CastDefinition(string Function, EquatableArray<SqlTypeTemplate> Arguments, SqlTypeTemplate Result, int Context);
}
