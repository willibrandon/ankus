using System.Globalization;

namespace Ankus.Generators;

/// <summary>
/// Holds declarative rescaling values independently of a compiler attribute or source tree.
/// </summary>
/// <param name="Precision">The authored decimal precision.</param>
/// <param name="Scale">The authored decimal scale.</param>
internal sealed record NumericPrecision(int Precision, int Scale)
{
    /// <summary>
    /// Gets the existing invariant managed rescaling expression for validated values.
    /// </summary>
    internal string Suffix => ".Rescale(" + Precision.ToString(CultureInfo.InvariantCulture) + ", " +
        Scale.ToString(CultureInfo.InvariantCulture) + ")";
}
