using Microsoft.CodeAnalysis;

namespace Ankus.Generators;

/// <summary>
/// Reconstructs one managed argument from its flattened SQL slots.
/// </summary>
/// <param name="type">The closed interface argument type.</param>
/// <param name="start">The first SQL slot consumed by this argument.</param>
/// <param name="count">The number of SQL slots.</param>
/// <param name="tuple">Whether the argument is a tuple rather than one scalar.</param>
internal sealed class AggregateArgument(ITypeSymbol type, int start, int count, bool tuple)
{
    /// <summary>
    /// Gets the exact managed type, retaining nested nullable annotations.
    /// </summary>
    internal string Managed { get; } = Format(type);

    /// <summary>
    /// Reconstructs the argument from expressions that already own any borrowed values.
    /// </summary>
    /// <param name="values">Expressions ordered by native SQL slot.</param>
    /// <returns>The managed scalar, tuple or empty argument group.</returns>
    internal string Read(IReadOnlyList<string> values)
        => count == 0 ? "default(" + Managed + ")" : !tuple ? values[start] :
            count == 1 ? "new " + Managed + "(" + values[start] + ")" : "(" + string.Join(", ", values.Skip(start).Take(count)) + ")";

    /// <summary>
    /// Formats a type without erasing reference nullability.
    /// </summary>
    internal static string Format(ITypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
        SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier));
}
