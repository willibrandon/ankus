namespace Ankus.CompilerServices;

public static unsafe partial class NativeBackend
{
    /// <summary>
    /// Invokes a managed callback beneath the native subtransaction recovery guard.
    /// </summary>
    /// <param name="callback">The exception-containing managed entry point.</param>
    /// <param name="state">The callback's synchronous managed handle.</param>
    /// <param name="atomic">Whether statements in the scope share its subtransaction instead of recovering individually.</param>
    internal static void RunSubtransaction(nint callback, nint state, bool atomic)
    {
        CheckAccess();
        var request = new NativeSpiRequest
        {
            _operation = SpiOperation.Subtransaction,
            _callback = callback,
            _callbackState = state,
            _scalarOperation = atomic ? 1 : 0,
        };
        NativeSpiResult result = default;
        Invoke(&request, &result);
    }
}
