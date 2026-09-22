using System.Runtime.CompilerServices;

namespace Ankus;

/// <summary>
/// Holds independently owned diagnostic buffers in the native transport layout.
/// </summary>
[InlineArray(Length)]
internal struct NativeErrorFields
{
    /// <summary>
    /// Contains the number of slots defined by NativeDiagnosticField.
    /// </summary>
    internal const int Length = 14;

    private NativeValue _element0;
}
