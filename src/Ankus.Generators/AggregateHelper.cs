using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Ankus.Generators;

/// <summary>
/// Represents one aggregate support method and its context-free SQL signature.
/// </summary>
internal sealed class AggregateHelper(IMethodSymbol method, string role, bool contextParameter,
    ImmutableArray<AggregateParameter> parameters, AggregateType result, FunctionDeclaration declaration, AggregateInvocation? invocation = null)
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
    /// Gets validated SQL argument slots, excluding the managed context and native deserializer dummy.
    /// </summary>
    internal ImmutableArray<AggregateParameter> Parameters { get; } = parameters;

    /// <summary>
    /// Gets SQL/native type contracts, excluding a deserializer's synthetic dummy argument.
    /// </summary>
    internal AggregateType[] Types { get; } = [.. parameters.Select(static parameter => parameter.Type)];

    /// <summary>
    /// Gets the support function's result contract.
    /// </summary>
    internal AggregateType Result { get; } = result;

    /// <summary>
    /// Gets the common planner, name and privilege options.
    /// </summary>
    internal FunctionDeclaration Declaration { get; } = declaration;

    /// <summary>
    /// Gets the typed capability dispatch, or null for a conventional method declaration.
    /// </summary>
    internal AggregateInvocation? Invocation { get; } = invocation;

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
    internal string Arguments(SqlTypeProviders providers) => string.Join(", ", Parameters.Select(parameter =>
        (parameter.IsVariadic ? "VARIADIC " : string.Empty) + SqlText.Identifier(parameter.Name) + " " + (parameter.Type.Datum is { } datum ? SqlSchemaTemplate.Type(datum, providers) : "internal"))
        .Concat(Deserialize ? ["internal"] : []));
}
