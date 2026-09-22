using System.Runtime.InteropServices;

namespace Ankus.Examples.Hello;

/// <summary>
/// Represents PostgreSQL's <c>NullableDatum</c>: an eight-byte datum followed by
/// a one-byte null flag and native alignment padding.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct NullableDatum
{
    private readonly ulong _value;
    private readonly byte _isNull;

    /// <summary>
    /// Gets the raw PostgreSQL datum. Pass-by-value SQL types store their value
    /// directly; pass-by-reference SQL types store a native pointer.
    /// </summary>
    internal readonly ulong Value => _value;
}
