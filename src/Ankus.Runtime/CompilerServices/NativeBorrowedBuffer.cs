namespace Ankus.CompilerServices;

/// <summary>
/// Owns optional detoast and transcoding allocations while checking the source's native lifetime.
/// </summary>
internal sealed class NativeBorrowedBuffer : IDisposable
{
    private readonly PgMemoryContext _context;
    private readonly PgDatum _datum;
    private readonly nint _data;
    private readonly NativeBufferKind _kind;

    /// <summary>
    /// Borrows a present bytea, text or C-string datum under a private child of its source owner.
    /// </summary>
    internal NativeBorrowedBuffer(PgDatum value, NativeBufferKind kind)
    {
        ArgumentNullException.ThrowIfNull(value);
        value.Lifetime.Validate();
        if (value.IsNull)
        {
            throw new ArgumentException("A borrowed buffer requires a non-NULL datum.", nameof(value));
        }

        PgMemoryContext parent = PgMemoryContext.FromId(NativeMemoryContext.Provider, value.Lifetime.ContextId);
        _context = PgMemoryContext.Create("Ankus borrowed buffer", parent);
        _kind = kind;
        try
        {
            var lifetime = new PgDatumLifetime(_context, source: value.Lifetime);
            (_datum, _data, Length) = NativeBackend.BorrowBuffer(value, lifetime, kind);
            lifetime.Scope?.Register(this);
        }
        catch (Exception primary)
        {
            PgResultCleanup.Dispose(_context, primary);
            throw;
        }
    }

    /// <summary>
    /// Gets the copied payload length in bytes.
    /// </summary>
    internal int Length { get; }

    /// <summary>
    /// Gets the original type identity, including a domain over the underlying buffer type.
    /// </summary>
    internal uint TypeOid => _datum.TypeOid;

    /// <summary>
    /// Gets the original server representation after validating the owner and callback lease.
    /// </summary>
    internal PgDatum Datum
    {
        get
        {
            Validate();
            return _datum;
        }
    }

    /// <summary>
    /// Rejects access after source reset, deletion, disposal, callback exit or backend changes.
    /// </summary>
    internal void Validate() => _datum.Lifetime.Validate();

    /// <summary>
    /// Validates the lifetime once; callers must not invoke backend operations while using the span.
    /// </summary>
    internal unsafe ReadOnlySpan<byte> GetSpan(bool includeTerminator = false)
    {
        Validate();
        if (includeTerminator && _kind != NativeBufferKind.CString)
        {
            throw new InvalidOperationException("Only a C-string view includes a terminating zero byte.");
        }

        return new ReadOnlySpan<byte>((void*)_data, includeTerminator ? checked(Length + 1) : Length);
    }

    /// <summary>
    /// Releases only private detoast or transcoding storage and invalidates this view.
    /// </summary>
    public void Dispose()
    {
        _datum.Lifetime.CheckThread();
        _context.Dispose();
        _datum.Lifetime.Scope?.Unregister(this);
    }
}
