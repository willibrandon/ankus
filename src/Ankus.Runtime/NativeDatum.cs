using System.Runtime.InteropServices;

namespace Ankus;

/// <summary>
/// Carries raw datum parameters with a native-validated context lifetime.
/// </summary>
public partial struct NativeValue
{
    /// <summary>
    /// Copies a generated polymorphic input into the active scalar or iterator memory owner.
    /// </summary>
    /// <returns>The present datum with its resolved type.</returns>
    public readonly PgDatum ReadPolymorphic()
    {
        PgMemoryContext context = PgMemoryContext.Current;
        PgDatum borrowed = PgDatum.DangerousCreate(unchecked((nuint)Integral), unchecked((uint)_auxiliary2), context);
        return borrowed.CopyTo(context);
    }

    /// <summary>
    /// Writes a checked polymorphic result with its exact type for native return validation.
    /// </summary>
    /// <param name="datum">The live native value.</param>
    /// <returns>The owned transport envelope.</returns>
    public static NativeValue FromPolymorphic(PgDatum datum)
    {
        ArgumentNullException.ThrowIfNull(datum);
        NativeValue value = datum.ToNative();
        value._auxiliary1 = -6;
        value.Integral = datum.TypeOid;
        return value;
    }

    /// <summary>
    /// Copies a raw word and its lifetime into an owned parameter envelope.
    /// </summary>
    /// <param name="bits">The native Datum word.</param>
    /// <param name="context">The context identity.</param>
    /// <param name="generation">The captured context generation.</param>
    /// <param name="isNull">The SQL NULL flag.</param>
    /// <returns>The parameter transport.</returns>
    internal static NativeValue FromDatum(nuint bits, nint context, nuint generation, bool isNull)
    {
        NativeDatumReference reference = new() { _bits = bits, _context = context, _generation = generation };
        NativeValue value = FromBytes(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in reference, 1)));
        value._auxiliary1 = -5;
        value.IsNull = isNull ? (byte)1 : (byte)0;
        return value;
    }
}
