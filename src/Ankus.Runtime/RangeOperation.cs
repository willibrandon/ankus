namespace Ankus;

/// <summary>
/// Identifies allowlisted PostgreSQL range operations; order matches the native dispatch table.
/// </summary>
internal enum RangeOperation
{
    /// <summary>
    /// Parses range text.
    /// </summary>
    Parse,
    /// <summary>
    /// Validates and canonicalizes a range.
    /// </summary>
    Canonicalize,
    /// <summary>
    /// Formats a range with PostgreSQL output routines.
    /// </summary>
    Format,
    /// <summary>
    /// Tests element containment.
    /// </summary>
    ContainsValue,
    /// <summary>
    /// Tests range containment.
    /// </summary>
    ContainsRange,
    /// <summary>
    /// Tests overlap.
    /// </summary>
    Overlaps,
    /// <summary>
    /// Tests adjacency.
    /// </summary>
    Adjacent,
    /// <summary>
    /// Computes a single-range union.
    /// </summary>
    Union,
    /// <summary>
    /// Computes an intersection.
    /// </summary>
    Intersect,
    /// <summary>
    /// Computes a single-range difference.
    /// </summary>
    Difference,
    /// <summary>
    /// Computes a spanning range.
    /// </summary>
    Merge,
    /// <summary>
    /// Constructs a mapped range from exact raw finite bounds and inclusion flags.
    /// </summary>
    BuildMapped,
    /// <summary>
    /// Resolves the current scalar subtype of an exact catalog range identity.
    /// </summary>
    Subtype,
}
