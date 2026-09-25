namespace Ankus;

/// <summary>
/// Identifies a generated PostgreSQL node representation and its native downcast rules.
/// </summary>
/// <remarks>
/// A node starts with a native NodeTag, directly or through a struct or union prefix.
/// Cast acceptance does not validate pointer fields, their ownership, or externally supplied raw storage.
/// </remarks>
public interface IPgNativeNode : IPgNativeType
{
    /// <summary>
    /// Determines whether a native node with the supplied tag may be viewed as this target type.
    /// </summary>
    /// <param name="tag">The exact native NodeTag value.</param>
    /// <returns>Whether the generated target accepts the tag under PostgreSQL's node inheritance rules.</returns>
    static abstract bool AcceptsTag(uint tag);
}
