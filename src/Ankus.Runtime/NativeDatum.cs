using System.Runtime.InteropServices;

namespace Ankus;

/// <summary>
/// Carries raw datum parameters with a native-validated context lifetime.
/// </summary>
public partial struct NativeValue
{
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
