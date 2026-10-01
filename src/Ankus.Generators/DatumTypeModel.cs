namespace Ankus.Generators;

/// <summary>
/// Carries validated closed mapping, registration and range-provider contracts without retaining compiler symbols.
/// </summary>
/// <param name="Reference">The exact managed/catalog identity and conversion capabilities.</param>
/// <param name="Registration">The independently renderable lazy registration.</param>
/// <param name="RangeBound">The scalar mapping of a range, or null for a scalar.</param>
/// <param name="Location">The current source coordinates used for provider diagnostics.</param>
internal sealed record DatumTypeModel(DatumTypeReference Reference, DatumRegistrationModel Registration,
    DatumTypeReference? RangeBound, GeneratorLocation? Location);
