namespace Ankus;

public static unsafe partial class NativeBackend
{
    /// <summary>
    /// Invokes a managed callback beneath the native subtransaction recovery guard.
    /// </summary>
    /// <param name="callback">The exception-containing managed entry point.</param>
    /// <param name="state">The callback's synchronous managed handle.</param>
    internal static void RunSubtransaction(nint callback, nint state)
    {
        CheckAccess();
        var request = new NativeSpiRequest
        {
            _operation = SpiOperation.Subtransaction,
            _callback = callback,
            _callbackState = state,
        };
        NativeSpiResult result = default;
        Invoke(&request, &result);
    }
}
