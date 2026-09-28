namespace Ankus;

/// <summary>
/// Converts fixed SQL bytea and text values into checked borrowed views without changing type identity.
/// </summary>
internal static class PgBufferViews
{
    /// <summary>
    /// Determines whether the requested representation needs native buffer storage.
    /// </summary>
    /// <typeparam name="T">The declared managed result type.</typeparam>
    /// <returns>Whether the result is one of the two concrete buffer views.</returns>
    internal static bool Is<T>() => typeof(T) == typeof(PgByteaView) || typeof(T) == typeof(PgTextView);

    /// <summary>
    /// Reads a present buffer under its source lifetime, or returns null for SQL NULL.
    /// </summary>
    /// <typeparam name="T">The concrete buffer view type.</typeparam>
    /// <param name="value">The raw value with its original PostgreSQL type.</param>
    /// <returns>The checked view or null.</returns>
    internal static T Read<T>(PgDatum value)
    {
        value.Lifetime.Validate();
        if (value.IsNull)
        {
            return default!;
        }

        object view = typeof(T) == typeof(PgByteaView) ? new PgByteaView(value) : new PgTextView(value);
        return (T)view;
    }
}
