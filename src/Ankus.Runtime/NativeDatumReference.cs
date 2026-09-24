using System.Runtime.InteropServices;

namespace Ankus;

/// <summary>
/// Matches the pointer-sized native raw-datum lifetime envelope.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct NativeDatumReference
{
    /// <summary>
    /// Carries the native Datum word.
    /// </summary>
    internal nuint _bits;
    /// <summary>
    /// Carries the native memory-context identity.
    /// </summary>
    internal nint _context;
    /// <summary>
    /// Carries the memory-context reset generation.
    /// </summary>
    internal nuint _generation;
}
