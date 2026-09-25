namespace Ankus;

public sealed unsafe partial class PgMemoryContext
{
    /// <summary>
    /// Allocates a checked native varlena header and zeroed payload in this live provider's context.
    /// </summary>
    internal (PgAllocation Allocation, nuint Offset) AllocateVarlena(nuint payloadLength)
    {
        EnsureAlive();
        NativeMemoryRequest request = new()
        {
            _operation = NativeMemoryOperation.AllocateVarlena,
            _context = _id,
            _length = payloadLength,
        };
        NativeMemoryContext.Invoke(ref request, out NativeMemoryResult result);
        return (new PgAllocation(_provider, result._pointer, result._length, PgAllocationOptions.Zeroed),
            checked((nuint)result._value));
    }

    /// <summary>
    /// Creates an initialized unmanaged value with individual native allocation ownership.
    /// </summary>
    /// <typeparam name="T">The unmanaged representation to copy; no C ABI or SQL type is inferred.</typeparam>
    /// <param name="value">The initial value copied into native storage.</param>
    /// <param name="options">The native initialization and size policies.</param>
    /// <param name="alignment">The requested explicit alignment, or zero for the server default.</param>
    /// <returns>The individually disposable owner.</returns>
    public PgNativeBox<T> CreateBox<T>(T value, PgAllocationOptions options = PgAllocationOptions.None, nuint alignment = 0)
        where T : unmanaged => new(InitializeNativeValue(Allocate<T>(options: options, alignment: alignment), value));

    /// <summary>
    /// Attempts initialized native value allocation using PostgreSQL's no-OOM policy.
    /// </summary>
    /// <typeparam name="T">The unmanaged representation to copy.</typeparam>
    /// <param name="value">The initial value.</param>
    /// <param name="options">The native initialization and size policies.</param>
    /// <param name="alignment">The requested explicit alignment, or zero for the server default.</param>
    /// <returns>The individually disposable owner, or null only for allocator exhaustion.</returns>
    public PgNativeBox<T>? TryCreateBox<T>(T value, PgAllocationOptions options = PgAllocationOptions.None, nuint alignment = 0)
        where T : unmanaged
    {
        PgAllocation? allocation = TryAllocate<T>(options: options, alignment: alignment);
        return allocation is null ? null : new PgNativeBox<T>(InitializeNativeValue(allocation, value));
    }

    /// <summary>
    /// Allocates an individually owned value whose entire representation is zeroed without running a constructor.
    /// </summary>
    /// <typeparam name="T">The unmanaged representation.</typeparam>
    /// <param name="options">Additional native size policies.</param>
    /// <param name="alignment">The requested explicit alignment, or zero for the server default.</param>
    /// <returns>The individually disposable zeroed owner.</returns>
    public PgNativeBox<T> AllocateZeroedBox<T>(PgAllocationOptions options = PgAllocationOptions.None, nuint alignment = 0)
        where T : unmanaged => new(AllocateZeroed<T>(options: options, alignment: alignment));

    /// <summary>
    /// Allocates individually owned native storage that must be fully initialized before its value is read.
    /// </summary>
    /// <typeparam name="T">The unmanaged representation.</typeparam>
    /// <param name="options">The native initialization and size policies.</param>
    /// <param name="alignment">The requested explicit alignment, or zero for the server default.</param>
    /// <returns>The individually disposable uninitialized owner.</returns>
    public PgNativeBox<T> DangerousAllocateUninitializedBox<T>(PgAllocationOptions options = PgAllocationOptions.None, nuint alignment = 0)
        where T : unmanaged => new(Allocate<T>(options: options, alignment: alignment));

    /// <summary>
    /// Creates an initialized unmanaged value reclaimed by its native context rather than individual disposal.
    /// </summary>
    /// <typeparam name="T">The unmanaged representation to copy.</typeparam>
    /// <param name="value">The initial value.</param>
    /// <param name="options">The native initialization and size policies.</param>
    /// <param name="alignment">The requested explicit alignment, or zero for the server default.</param>
    /// <returns>The context-owned value.</returns>
    public PgContextValue<T> CreateContextValue<T>(T value, PgAllocationOptions options = PgAllocationOptions.None, nuint alignment = 0)
        where T : unmanaged => new(InitializeNativeValue(Allocate<T>(options: options, alignment: alignment), value));

    /// <summary>
    /// Attempts initialized context-owned storage using PostgreSQL's no-OOM policy.
    /// </summary>
    /// <typeparam name="T">The unmanaged representation to copy.</typeparam>
    /// <param name="value">The initial value.</param>
    /// <param name="options">The native initialization and size policies.</param>
    /// <param name="alignment">The requested explicit alignment, or zero for the server default.</param>
    /// <returns>The context-owned value, or null only for allocator exhaustion.</returns>
    public PgContextValue<T>? TryCreateContextValue<T>(T value, PgAllocationOptions options = PgAllocationOptions.None, nuint alignment = 0)
        where T : unmanaged
    {
        PgAllocation? allocation = TryAllocate<T>(options: options, alignment: alignment);
        return allocation is null ? null : new PgContextValue<T>(InitializeNativeValue(allocation, value));
    }

    /// <summary>
    /// Adopts exclusive individual ownership of an initialized palloc-compatible value, mapping a null address to null.
    /// </summary>
    /// <typeparam name="T">The complete unmanaged representation at the supplied address.</typeparam>
    /// <param name="address">The live palloc-family address, or null.</param>
    /// <param name="huge">Whether the allocation uses PostgreSQL's huge-size policy.</param>
    /// <param name="alignment">The allocation's original explicit alignment, or zero for the server default.</param>
    /// <returns>The individually disposable owner, or null without backend access for a null address.</returns>
    /// <remarks>
    /// The caller guarantees accessible initialized bytes, native allocator provenance, and exclusive ownership.
    /// </remarks>
    public PgNativeBox<T>? DangerousAdoptBox<T>(void* address, bool huge = false, nuint alignment = 0) where T : unmanaged
        => address is null ? null : new PgNativeBox<T>(DangerousAdopt(address, (nuint)sizeof(T), huge, alignment));

    /// <summary>
    /// Adopts a non-null initialized palloc-compatible value for reclamation by its native context.
    /// </summary>
    /// <typeparam name="T">The complete unmanaged representation at the supplied address.</typeparam>
    /// <param name="address">The non-null live palloc-family address.</param>
    /// <param name="huge">Whether the allocation uses PostgreSQL's huge-size policy.</param>
    /// <param name="alignment">The allocation's original explicit alignment, or zero for the server default.</param>
    /// <returns>The non-null context-owned value.</returns>
    /// <remarks>
    /// The caller transfers exclusive ownership and guarantees accessible initialized bytes and native allocator provenance.
    /// </remarks>
    public PgContextValue<T> DangerousAdoptContextValue<T>(void* address, bool huge = false, nuint alignment = 0) where T : unmanaged
        => new(DangerousAdopt(address, (nuint)sizeof(T), huge, alignment));

    /// <summary>
    /// Borrows an external initialized address using this context as a reset-sensitive lifetime anchor.
    /// </summary>
    /// <typeparam name="T">The complete unmanaged representation available at the address.</typeparam>
    /// <param name="address">The accessible initialized raw address, or null.</param>
    /// <returns>A borrowed view, or null without backend access for a null address.</returns>
    /// <remarks>
    /// The address need not be a palloc chunk start. No native allocator ownership is inferred or acquired.
    /// The caller guarantees at least sizeof(T) valid bytes and honors any shorter external lifetime,
    /// including stack return, resource closure, external free, or external resize.
    /// </remarks>
    public PgNativeReference<T>? DangerousBorrow<T>(void* address) where T : unmanaged
    {
        if (address is null)
        {
            return null;
        }

        EnsureAlive();
        NativeMemoryRequest request = new() { _operation = NativeMemoryOperation.CaptureGeneration, _context = _id };
        NativeMemoryContext.Invoke(ref request, out NativeMemoryResult result);
        return new PgNativeReference<T>(_provider, _id, result._value, (nint)address);
    }

    private static PgAllocation InitializeNativeValue<T>(PgAllocation allocation, T value) where T : unmanaged
    {
        try
        {
            allocation.Write(value);
            return allocation;
        }
        catch (Exception primary)
        {
            try
            {
                allocation.Dispose();
            }
            catch (Exception cleanupError)
            {
                throw new AggregateException("Initializing a PostgreSQL native value and releasing its allocation failed.", primary, cleanupError);
            }

            throw;
        }
    }
}
