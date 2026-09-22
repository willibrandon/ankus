using System.Runtime.InteropServices;

namespace Ankus;

/// <summary>
/// Carries a SPI operation and its borrowed arguments through the native guard.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NativeSpiRequest
{
    /// <summary>
    /// Points to null-terminated UTF-8 command text, when required by the operation.
    /// </summary>
    internal byte* _command;

    /// <summary>
    /// Points to typed parameters, or type-only entries when preparing a statement.
    /// </summary>
    internal NativeSpiParameter* _parameters;

    /// <summary>
    /// Contains the retained plan handle, updated when preparing or freeing a plan.
    /// </summary>
    internal nint _plan;

    /// <summary>
    /// Contains the UTF-8 command length, excluding its terminator.
    /// </summary>
    internal int _commandLength;

    /// <summary>
    /// Contains the positional parameter count.
    /// </summary>
    internal int _parameterCount;

    /// <summary>
    /// Contains the maximum returned rows, or zero for no limit.
    /// </summary>
    internal int _limit;

    /// <summary>
    /// Selects the guarded operation.
    /// </summary>
    internal SpiOperation _operation;

    /// <summary>
    /// Selects which result cells to copy.
    /// </summary>
    internal SpiResultMode _resultMode;

    /// <summary>
    /// Selects read-only execution using a one-byte C flag.
    /// </summary>
    internal byte _readOnly;
}
