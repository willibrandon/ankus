namespace Ankus;

public partial struct NativeValue
{
    /// <summary>
    /// Reads a mapped scalar or array through its exact declared type and checked raw input lifetime.
    /// </summary>
    /// <typeparam name="T">The requested registered scalar or array type.</typeparam>
    /// <returns>The converted managed value or an allowed SQL NULL.</returns>
    public readonly T ReadMapped<T>()
    {
        DatumTypeMapping? mapping = PgDatumRegistry.Find(typeof(T));
        DatumArrayMapping? array = PgDatumRegistry.FindArray(typeof(T));
        if (mapping is not null)
        {
            mapping.RequireRead();
        }
        else if (array is not null)
        {
            array.RequireRead();
        }
        else
        {
            mapping = PgDatumRegistry.Require(typeof(T));
            mapping.RequireRead();
        }

        PgDatum value = IsNull == 0 ? ReadPolymorphic() :
            PgDatum.DangerousCreate(0, unchecked((uint)_auxiliary2), PgMemoryContext.Current, isNull: true);
        return mapping is not null ? SpiRow.Convert<T>(mapping.Read(value)) : (T)array!.Read(value, typeof(T))!;
    }

    /// <summary>
    /// Writes a declared mapped scalar or array through a live, exactly typed raw datum envelope.
    /// </summary>
    /// <typeparam name="T">The registered declared scalar or array type.</typeparam>
    /// <param name="value">The managed value or SQL NULL.</param>
    /// <returns>The native-owned transport envelope.</returns>
    public static NativeValue FromMapped<T>(T value) => PgDatumRegistry.FindArray(typeof(T)) is { } array
        ? array.Write(value) : PgDatumRegistry.Require(typeof(T)).Write(value);
}
