using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Ankus.Generators;

/// <summary>
/// Describes an aggregate-only internal state or an ordinary supported SQL datum.
/// </summary>
internal sealed class AggregateType(FunctionType? datum, string? payload, bool nullable)
{
    /// <summary>
    /// Gets the ordinary datum conversion contract, absent for managed internal state.
    /// </summary>
    internal FunctionType? Datum { get; } = datum;

    /// <summary>
    /// Gets the managed payload type of an internal state wrapper.
    /// </summary>
    internal string? Payload { get; } = payload;

    /// <summary>
    /// Gets whether the SQL value can be null.
    /// </summary>
    internal bool Nullable { get; } = nullable;

    /// <summary>
    /// Gets whether this value uses aggregate-owned internal state transport.
    /// </summary>
    internal bool IsInternal => Payload is not null;

    /// <summary>
    /// Gets the PostgreSQL type spelling.
    /// </summary>
    internal string Sql => Datum?.Sql ?? "internal";

    /// <summary>
    /// Gets the nullable-aware managed value spelling.
    /// </summary>
    internal string Managed => (Datum?.Managed ?? "global::Ankus.PgAggregateState<" + Payload + ">") + (Nullable ? "?" : string.Empty);

    /// <summary>
    /// Resolves one aggregate support parameter or result without exposing internal state to ordinary functions.
    /// </summary>
    internal static AggregateType? Create(ITypeSymbol type, ImmutableArray<AttributeData> attributes)
    {
        if (type is INamedTypeSymbol { Name: "PgAggregateState", Arity: 1 } state && state.ContainingNamespace.ToDisplayString() == "Ankus")
        {
            ITypeSymbol payloadType = state.TypeArguments[0];
            if (payloadType.NullableAnnotation == NullableAnnotation.Annotated ||
                payloadType is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T })
            {
                return null;
            }

            return new(null, payloadType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
                SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier)),
                type.NullableAnnotation == NullableAnnotation.Annotated);
        }

        FunctionType? datum = FunctionType.Create(type, CompositeReference.Read(attributes));
        return datum is null || datum.Managed == "void" ? null : new(datum, null, datum.Nullable);
    }

    /// <summary>
    /// Compares SQL identity and the concrete managed payload identity for internal values.
    /// </summary>
    internal bool Matches(AggregateType other) => Sql == other.Sql && Payload == other.Payload;

    /// <summary>
    /// Determines whether PostgreSQL can seed this state directly from an input datum without a conversion call.
    /// </summary>
    internal bool AcceptsSeed(AggregateType input)
        => Matches(input) || (Sql, input.Sql) is ("oid", "integer") or ("inet", "cidr");

    /// <summary>
    /// Emits a nullable-aware managed read for a validated native argument slot.
    /// </summary>
    internal string Read(string slot, ImmutableArray<AttributeData> attributes)
        => IsInternal ? "global::Ankus.NativeAggregate.Read<" + Payload + ">(" + slot + ")" + (Nullable ? string.Empty : "!") :
            ManagedConversion.Read(Datum!, slot, NumericConstraint.Rescale(attributes));
}
