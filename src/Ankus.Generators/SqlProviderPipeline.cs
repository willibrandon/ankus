using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Ankus.Generators;

/// <summary>
/// Freezes provider declarations before current type and function inventory composition.
/// </summary>
internal static class SqlProviderPipeline
{
    /// <summary>
    /// Registers comparable assembly provider metadata while leaving catalog and dependency validation to the graph.
    /// </summary>
    /// <param name="context">The generator registration context.</param>
    /// <param name="attributes">The transient tracked assembly metadata.</param>
    /// <returns>The original ordered provider inventory and current diagnostic coordinates.</returns>
    internal static IncrementalValueProvider<EquatableArray<SqlProviderModel>> Register(IncrementalGeneratorInitializationContext context,
        IncrementalValueProvider<ImmutableArray<AttributeData>> attributes)
        => context.CompilationProvider.Combine(attributes).Select(static (value, token) =>
            new EquatableArray<SqlProviderModel>(value.Right.Where(static attribute => attribute.AttributeClass?.ToDisplayString() is
                "Ankus.PgSqlTypeProviderAttribute" or "Ankus.PgSqlFunctionProviderAttribute").Select(attribute =>
                    SqlProviderModel.Create(attribute, value.Left, token))))
            .WithTrackingName("SqlProviderAnalysis");
}
