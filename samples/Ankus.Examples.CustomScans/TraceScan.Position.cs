using Ankus.Postgres;

namespace Ankus.Examples.CustomScans;

/// <summary>
/// Delegates reversible scan positions to child access methods that support them.
/// </summary>
public static unsafe partial class TraceScan
{
    /// <summary>
    /// Saves the actual child's current position for PostgreSQL's merge executor.
    /// </summary>
    [PgNativeCallback(nameof(Mark))]
    private static partial CustomExecMethods_MarkPosCustomScanCallback Marker { get; }

    /// <summary>
    /// Restores the actual child so its next read follows the saved position.
    /// </summary>
    [PgNativeCallback(nameof(Restore))]
    private static partial CustomExecMethods_RestrPosCustomScanCallback Restorer { get; }

    /// <summary>
    /// Lets the child own its mark rather than saving a tuple or a borrowed slot address.
    /// </summary>
    private static void Mark(nint address)
    {
        var state = (State*)address;
        NativeMethods.ExecMarkPos(NativeMethods.list_nth(state->_scan.custom_ps, 0));
        state->_marks++;
    }

    /// <summary>
    /// Restores the native child through the error guard without assuming the old result slot is still valid.
    /// </summary>
    private static void Restore(nint address)
    {
        var state = (State*)address;
        NativeMethods.ExecRestrPos(NativeMethods.list_nth(state->_scan.custom_ps, 0));
        state->_restores++;
    }
}
