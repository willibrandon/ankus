using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Ankus.Generators;

/// <summary>
/// Represents one aggregate support method and its context-free SQL signature.
/// </summary>
internal sealed class AggregateHelper(IMethodSymbol method, string role, bool contextParameter,
    ImmutableArray<IParameterSymbol> parameters, AggregateType[] types, AggregateType result, FunctionDeclaration declaration)
{
    /// <summary>
    /// Gets the attributed or conventionally selected managed method.
    /// </summary>
    internal IMethodSymbol Method { get; } = method;

    /// <summary>
    /// Gets the aggregate support role used by default SQL naming and native behavior.
    /// </summary>
    internal string Role { get; } = role;

    /// <summary>
    /// Gets whether the method receives a leading managed aggregate context.
    /// </summary>
    internal bool ContextParameter { get; } = contextParameter;

    /// <summary>
    /// Gets managed SQL parameters after removing the optional context.
    /// </summary>
    internal ImmutableArray<IParameterSymbol> Parameters { get; } = parameters;

    /// <summary>
    /// Gets SQL/native type contracts, excluding a deserializer's synthetic dummy argument.
    /// </summary>
    internal AggregateType[] Types { get; } = types;

    /// <summary>
    /// Gets the support function's result contract.
    /// </summary>
    internal AggregateType Result { get; } = result;

    /// <summary>
    /// Gets the common planner, name and privilege options.
    /// </summary>
    internal FunctionDeclaration Declaration { get; } = declaration;

    /// <summary>
    /// Gets whether PostgreSQL supplies an extra SQL-nonnull internal dummy argument.
    /// </summary>
    internal bool Deserialize => Role == "Deserialize";

    /// <summary>
    /// Gets the complete SQL function identity for duplicate detection.
    /// </summary>
    internal string Signature => Declaration.QualifiedName + "(" + string.Join(",", Types.Select(static type => type.Sql)
        .Concat(Deserialize ? ["internal"] : [])) + ")";

    /// <summary>
    /// Gets named SQL parameter declarations, retaining variadic input and adding the native deserializer dummy.
    /// </summary>
    internal string Arguments => string.Join(", ", Parameters.Select((parameter, index) =>
        (parameter.IsParams ? "VARIADIC " : string.Empty) + SqlText.Identifier(ParameterName(parameter)) + " " + Types[index].Sql)
        .Concat(Deserialize ? ["internal"] : []));

    /// <summary>
    /// Resolves an optional SQL parameter name without copying CLR context into SQL.
    /// </summary>
    internal static string ParameterName(IParameterSymbol parameter)
    {
        AttributeData? attribute = parameter.GetAttributes().FirstOrDefault(static value => value.AttributeClass?.ToDisplayString() == "Ankus.PgParameterAttribute");
        return attribute is null ? SqlText.SnakeCase(parameter.Name) : AttributeValues.Get(attribute, "Name", SqlText.SnakeCase(parameter.Name));
    }
}
