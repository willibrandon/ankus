namespace Ankus;

/// <summary>
/// Links stack-resident shared read admissions without allocating for ordinary aggregate reads.
/// </summary>
internal unsafe struct NativeSharedReadScope
{
    [ThreadStatic]
    private static NativeSharedReadScope* s_current;

    private NativeSharedReadScope* _previous;
    private nint _provider;
    private nuint _address;
    private nuint _length;

    /// <summary>
    /// Publishes a caller-owned stack frame until the matching synchronous read exits.
    /// </summary>
    internal static void Push(NativeSharedReadScope* frame, nint provider, nint address, nuint length)
    {
        frame->_previous = s_current;
        frame->_provider = provider;
        frame->_address = (nuint)address;
        frame->_length = length;
        s_current = frame;
    }

    /// <summary>
    /// Finds the innermost live admission covering a complete inline cell and its native provider.
    /// </summary>
    internal static nint Find(nint address, nuint length)
    {
        nint provider = NativeMemoryContext.Provider;
        for (NativeSharedReadScope* frame = s_current; frame != null; frame = frame->_previous)
        {
            if (frame->_provider == provider && (nuint)address >= frame->_address && length <= frame->_length &&
                (nuint)address - frame->_address <= frame->_length - length)
            {
                return (nint)frame;
            }
        }

        throw new InvalidOperationException("Access an inline PostgreSQL spinlock directly within its owning PgShared.Read callback.");
    }

    /// <summary>
    /// Releases borrowed locks before unlinking this frame and dropping its address admission.
    /// </summary>
    internal static void Pop(NativeSharedReadScope* frame)
    {
        try
        {
            NativeSpinLockLease.ReleaseFrame((nint)frame);
        }
        finally
        {
            s_current = frame->_previous;
        }
    }
}
