namespace Ankus;

/// <summary>
/// Converts checked datums to polymorphic wrappers without assigning a fixed catalog type to them.
/// </summary>
internal static class PgPolymorphic
{
    /// <summary>
    /// Determines whether a managed result requests its resolved native type.
    /// </summary>
    /// <typeparam name="T">The requested result representation.</typeparam>
    /// <returns>Whether the type is a supported polymorphic wrapper.</returns>
    internal static bool Is<T>() => typeof(T) == typeof(PgAnyElement) || typeof(T) == typeof(PgAnyArray);

    /// <summary>
    /// Wraps a present value or returns a null wrapper while retaining the source lifetime.
    /// </summary>
    /// <typeparam name="T">The polymorphic wrapper type.</typeparam>
    /// <param name="value">The raw value with its actual PostgreSQL identity.</param>
    /// <returns>The nullable checked wrapper.</returns>
    internal static T Read<T>(PgDatum value)
    {
        value.Lifetime.Validate();
        if (value.IsNull)
        {
            return default!;
        }

        object wrapper = typeof(T) == typeof(PgAnyArray) ? new PgAnyArray(value) : new PgAnyElement(value);
        return (T)wrapper;
    }
}
