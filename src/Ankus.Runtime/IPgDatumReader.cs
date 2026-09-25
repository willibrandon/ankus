namespace Ankus;

/// <summary>
/// Converts a present, exactly typed PostgreSQL datum into an independently owned managed value.
/// </summary>
/// <typeparam name="T">The closed mapped managed type.</typeparam>
public interface IPgDatumReader<T>
{
    /// <summary>
    /// Reads a present value whose exact type and native lifetime have already been validated.
    /// </summary>
    /// <param name="value">The checked input. Copy data that must outlive its native owner.</param>
    /// <returns>A non-null managed value independent of the borrowed input storage.</returns>
    T Read(PgDatum value);
}
