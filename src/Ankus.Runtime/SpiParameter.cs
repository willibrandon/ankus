namespace Ankus;

/// <summary>
/// Describes a typed positional SQL parameter, including the PostgreSQL type of a NULL value.
/// </summary>
public readonly struct SpiParameter
{
    private SpiParameter(uint typeOid, object? value)
    {
        TypeOid = typeOid;
        Value = value;
    }

    /// <summary>
    /// Gets the PostgreSQL type OID used when binding the parameter.
    /// </summary>
    public uint TypeOid { get; }

    /// <summary>
    /// Gets the managed parameter value, or null for SQL NULL.
    /// </summary>
    public object? Value { get; }

    /// <summary>
    /// Creates a positional parameter using the declared CLR type, including nullable types.
    /// Parameters appear in SQL as $1, $2, and so on.
    /// </summary>
    /// <typeparam name="T">The managed parameter type.</typeparam>
    /// <param name="value">The parameter value.</param>
    /// <returns>A typed parameter.</returns>
    public static SpiParameter Create<T>(T value) => new(SpiType.GetOid<T>(), value);
}
