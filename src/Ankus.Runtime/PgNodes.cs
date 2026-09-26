namespace Ankus;

/// <summary>
/// Creates checked node views over generated PostgreSQL representations and explicit native lifetimes.
/// </summary>
public static class PgNodes
{
    /// <summary>
    /// Borrows a node view while retaining the reference's original storage bounds and lifetime anchor.
    /// </summary>
    /// <typeparam name="T">The generated native node representation.</typeparam>
    /// <param name="reference">The initialized complete native value to view.</param>
    /// <returns>A node view without allocation or release rights.</returns>
    /// <remarks>
    /// The representation's complete size, alignment and selected-header ABI are checked. The caller
    /// remains responsible for the validity and lifetime of native pointer members. A tag does not
    /// prove that those members refer to valid objects.
    /// </remarks>
    public static PgNodeReference<T> Borrow<T>(PgNativeReference<T> reference) where T : unmanaged, IPgNativeNode
    {
        ArgumentNullException.ThrowIfNull(reference);
        var result = new PgNodeReference<T>(reference);
        result.Validate();
        return result;
    }
}
