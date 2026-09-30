namespace Ankus.Generators;

/// <summary>
/// Preserves an enum member's exact integral constant and authored PostgreSQL label without a field symbol.
/// </summary>
/// <param name="Member">The managed member name without identifier escaping.</param>
/// <param name="Value">The exact boxed integral constant, retaining its underlying width and signedness.</param>
/// <param name="Label">The exact PostgreSQL label.</param>
internal sealed record EnumLabel(string Member, object Value, string Label);
