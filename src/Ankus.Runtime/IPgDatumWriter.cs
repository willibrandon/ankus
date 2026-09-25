namespace Ankus;

/// <summary>
/// Converts a present managed value to a live PostgreSQL datum with the resolved target identity.
/// </summary>
/// <typeparam name="T">The closed mapped managed type.</typeparam>
public interface IPgDatumWriter<T>
{
    /// <summary>
    /// Creates a checked result, optionally representing SQL NULL, without retaining the operation's context.
    /// </summary>
    /// <param name="value">The present managed value.</param>
    /// <param name="typeOid">The current exact PostgreSQL target type OID.</param>
    /// <param name="destination">The destination for native storage needed by this operation.</param>
    /// <returns>A live datum with exactly typeOid, including for SQL NULL; never a null handle.</returns>
    PgDatum Write(T value, uint typeOid, PgMemoryContext destination);
}
