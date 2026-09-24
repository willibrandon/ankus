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
    /// Gets whether this value has PostgreSQL's internal SQL type.
    /// </summary>
    internal bool IsInternal => Payload is not null || Datum?.IsSqlInternal == true;

    /// <summary>
    /// Gets whether the value uses the aggregate-specific managed root transport.
    /// </summary>
    internal bool IsManagedState => Payload is not null;

    /// <summary>
    /// Gets the PostgreSQL type spelling.
    /// </summary>
    internal string Sql => Datum?.Sql ?? "internal";

    /// <summary>
    /// Gets the nullable-aware managed value spelling.
    /// </summary>
    internal string Managed => (Datum?.Managed ?? "global::Ankus.PgAggregateState<" + Payload + ">") + (Nullable ? "?" : string.Empty);

    /// <summary>
    /// Resolves a support value while preserving aggregate-specific managed payload types.
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

        FunctionType? datum = FunctionType.Create(type, SqlTypeReference.Read(attributes));
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
        => IsManagedState ? "global::Ankus.NativeAggregate.Read<" + Payload + ">(" + slot + ")" + (Nullable ? string.Empty : "!") :
            ManagedConversion.Read(Datum!, slot, NumericConstraint.Rescale(attributes));
}
