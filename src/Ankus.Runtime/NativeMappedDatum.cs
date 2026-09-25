namespace Ankus;

public partial struct NativeValue
{
    /// <summary>
    /// Reads a mapped scalar through its exact declared type and checked raw input lifetime.
    /// </summary>
    /// <typeparam name="T">The requested registered scalar type.</typeparam>
    /// <returns>The converted managed value or an allowed SQL NULL.</returns>
    public readonly T ReadMapped<T>()
    {
        DatumTypeMapping mapping = PgDatumRegistry.Require(typeof(T));
        mapping.RequireRead();
        PgDatum value = IsNull == 0 ? ReadPolymorphic() :
            PgDatum.DangerousCreate(0, unchecked((uint)_auxiliary2), PgMemoryContext.Current, isNull: true);
        return SpiRow.Convert<T>(mapping.Read(value));
    }

    /// <summary>
    /// Writes a declared mapped scalar through a live, exactly typed raw datum envelope.
    /// </summary>
    /// <typeparam name="T">The registered declared scalar type.</typeparam>
    /// <param name="value">The managed value or SQL NULL.</param>
    /// <returns>The native-owned transport envelope.</returns>
    public static NativeValue FromMapped<T>(T value) => PgDatumRegistry.Require(typeof(T)).Write(value);
}
