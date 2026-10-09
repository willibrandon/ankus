using System.Runtime.InteropServices;

namespace Ankus.Examples.WalDecoder;

/// <summary>
/// Holds state shared by one decoding context's callbacks, stored in PostgreSQL memory.
/// </summary>
/// <remarks>
/// The startup callback allocates this value in the decoding context's memory context and stores its address in
/// <c>output_plugin_private</c>. PostgreSQL frees that context with the decoding context, including after an error.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct DecodingState
{
    /// <summary>
    /// The number of row changes decoded in the current transaction.
    /// </summary>
    public long TransactionChangeCount;
}
