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
    /// Contains the cursor identity, which is never a dereferenceable PostgreSQL pointer.
    /// </summary>
    internal long _cursorId;

    /// <summary>
    /// Contains the scoped SPI connection identity, or zero for an independent operation.
    /// </summary>
    internal long _sessionId;

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

    /// <summary>
    /// Selects forward cursor movement using a one-byte C flag.
    /// </summary>
    internal byte _forward;

    /// <summary>
    /// Points to borrowed managed diagnostic transport during a reporting operation.
    /// </summary>
    internal NativeCallError* _diagnostic;

    /// <summary>
    /// Contains the version-independent reporting level.
    /// </summary>
    internal PgLogLevel _logLevel;

    /// <summary>
    /// Selects a function within the requested scalar operation family.
    /// </summary>
    internal int _scalarOperation;

    /// <summary>
    /// Contains the expected scalar result type, validated by the native dispatcher.
    /// </summary>
    internal uint _scalarResultOid;
}
