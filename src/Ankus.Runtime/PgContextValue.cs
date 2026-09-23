using System.ComponentModel;

namespace Ankus;

/// <summary>
/// Holds one checked unmanaged value whose storage is reclaimed by its PostgreSQL context.
/// </summary>
/// <typeparam name="T">The unmanaged representation copied to and from native storage.</typeparam>
/// <remarks>
/// This wrapper has no individual disposal or finalizer. Collection of the wrapper never frees
/// native bytes or invokes a value destructor. Unmanaged storage does not establish a C ABI or SQL type.
/// </remarks>
public sealed unsafe class PgContextValue<T> where T : unmanaged
{
    private readonly PgAllocation _allocation;

    /// <summary>
    /// Retains checked storage without exposing individual release rights.
    /// </summary>
    /// <param name="allocation">The allocation containing the complete value.</param>
    internal PgContextValue(PgAllocation allocation) => _allocation = allocation;

    /// <summary>
    /// Gets or sets a copied value while its native context and allocation remain live.
    /// </summary>
    public T Value
    {
        get => _allocation.Read<T>();
        set => _allocation.Write(value);
    }

    /// <summary>
    /// Gets the allocation's actual native owner while the allocation remains live.
    /// </summary>
    public PgMemoryContext Context => _allocation.Context;

    /// <summary>
    /// Gets the original initialization and native size policies.
    /// </summary>
    public PgAllocationOptions Options => _allocation.Options;

    /// <summary>
    /// Gets the requested explicit alignment, or zero for the server default.
    /// </summary>
    public nuint Alignment => _allocation.Alignment;

    /// <summary>
    /// Borrows a typed view sharing this value's checked allocation lifetime.
    /// </summary>
    /// <returns>A non-owning view.</returns>
    public PgNativeReference<T> Borrow() => _allocation.Borrow<T>();

    /// <summary>
    /// Copies all representation bytes into independent context-owned storage.
    /// </summary>
    /// <param name="context">The destination context, or null for the current context.</param>
    /// <returns>A shallow context-owned copy with default allocation policies.</returns>
    public PgContextValue<T> CloneInto(PgMemoryContext? context = null) => Borrow().CloneInto(context);

    /// <summary>
    /// Copies all representation bytes into independent individually owned storage.
    /// </summary>
    /// <param name="context">The destination context, or null for the current context.</param>
    /// <returns>A shallow individually owned copy with default allocation policies.</returns>
    public PgNativeBox<T> CloneOwnedInto(PgMemoryContext? context = null) => Borrow().CloneOwnedInto(context);

    /// <summary>
    /// Returns the current native address for explicitly unsafe interop.
    /// </summary>
    /// <returns>The borrowed address, invalid after native reclamation or raw transfer.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public void* DangerousGetPointer() => _allocation.DangerousGetPointer();

    /// <summary>
    /// Transfers native ownership without freeing bytes and invalidates every shared checked view.
    /// </summary>
    /// <returns>The live native pointer whose lifetime is now the recipient's responsibility.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public void* DangerousDetach() => _allocation.DangerousDetach();
}
