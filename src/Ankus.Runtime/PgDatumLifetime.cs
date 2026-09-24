namespace Ankus;

/// <summary>
/// Checks a datum's extension provider, context identity, and reset generation before native access.
/// </summary>
internal sealed class PgDatumLifetime
{
    private readonly nint _provider;

    /// <summary>
    /// Captures the generation of a live context.
    /// </summary>
    /// <param name="context">The native storage lifetime anchor.</param>
    internal PgDatumLifetime(PgMemoryContext context)
    {
        ObjectDisposedException.ThrowIf(!context.IsAlive, context);
        _provider = NativeMemoryContext.Provider;
        ContextId = context.Id;
        NativeMemoryRequest request = new() { _operation = NativeMemoryOperation.CaptureGeneration, _context = ContextId };
        NativeMemoryContext.Invoke(ref request, out NativeMemoryResult result);
        Generation = unchecked((nuint)result._value);
    }

    /// <summary>
    /// Gets the registered context identity.
    /// </summary>
    internal nint ContextId { get; }

    /// <summary>
    /// Gets the reset generation captured for the datum's storage.
    /// </summary>
    internal nuint Generation { get; }

    /// <summary>
    /// Rejects stale context generations and access from another backend provider or thread.
    /// </summary>
    internal void Validate()
    {
        NativeMemoryContext.CheckProvider(_provider);
        NativeMemoryRequest request = new() { _operation = NativeMemoryOperation.CaptureGeneration, _context = ContextId };
        try
        {
            NativeMemoryContext.Invoke(ref request, out NativeMemoryResult result);
            if (unchecked((nuint)result._value) != Generation)
            {
                throw new ObjectDisposedException(nameof(PgDatum), "The datum's memory context has been reset.");
            }
        }
        catch (PgException exception) when (exception.SqlState == "55000")
        {
            throw new ObjectDisposedException(nameof(PgDatum), "The datum's memory context has been deleted.");
        }
    }
}
