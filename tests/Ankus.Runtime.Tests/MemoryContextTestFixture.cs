using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Ankus.Runtime.Tests;

/// <summary>
/// Supplies scripted native memory responses without modeling PostgreSQL's allocator behavior.
/// </summary>
internal sealed unsafe class MemoryContextTestFixture : IDisposable
{
    [ThreadStatic]
    private static MemoryContextTestFixture? s_current;

    private readonly MemoryContextTestFixture? _previous = s_current;
    private readonly nint _name = Marshal.StringToCoTaskMemUTF8("context café 🐘");

    /// <summary>
    /// Installs this test's thread-local callback state.
    /// </summary>
    internal MemoryContextTestFixture() => s_current = this;

    /// <summary>
    /// Gets the requests that reached the native boundary.
    /// </summary>
    internal List<NativeMemoryRequest> Requests { get; } = [];

    /// <summary>
    /// Gets or sets an optional response script invoked after recording each request.
    /// </summary>
    internal Func<NativeMemoryRequest, NativeMemoryResult>? Handler { get; set; }

    /// <summary>
    /// Gets or sets the current context returned by the scripted bridge.
    /// </summary>
    internal nint Current { get; set; } = 101;

    /// <summary>
    /// Gets the number of released owned diagnostic buffers.
    /// </summary>
    internal int ErrorReleases { get; private set; }

    /// <summary>
    /// Enters a distinct native callback envelope with the selected stable provider identity.
    /// </summary>
    /// <param name="provider">The stable provider identity.</param>
    /// <returns>The scope that restores the enclosing capability.</returns>
    internal static Scope Enter(nint provider = 17) => new(provider);

    /// <summary>
    /// Supplies deterministic baseline responses that individual cases can override.
    /// </summary>
    /// <param name="request">The observed native request.</param>
    /// <returns>The response for this request.</returns>
    internal NativeMemoryResult Respond(NativeMemoryRequest request)
    {
        switch (request._operation)
        {
            case NativeMemoryOperation.Current:
            case NativeMemoryOperation.Predefined:
                return new NativeMemoryResult { _context = Current };
            case NativeMemoryOperation.Create:
                return new NativeMemoryResult { _context = 202 };
            case NativeMemoryOperation.Parent:
            case NativeMemoryOperation.Owner:
                return new NativeMemoryResult { _context = 303 };
            case NativeMemoryOperation.Name:
                int byteLength = Encoding.UTF8.GetByteCount("context café 🐘");
                if (request._data != 0)
                {
                    new ReadOnlySpan<byte>((void*)_name, byteLength).CopyTo(new Span<byte>((void*)request._data, checked((int)request._length)));
                }

                return new NativeMemoryResult { _length = (nuint)byteLength };
            case NativeMemoryOperation.Allocate:
                return new NativeMemoryResult { _pointer = 501, _length = request._length };
            case NativeMemoryOperation.Reallocate:
                return new NativeMemoryResult { _context = 502, _length = request._length };
            case NativeMemoryOperation.Switch:
                nint previous = Current;
                Current = request._context;
                return new NativeMemoryResult { _context = previous };
            default:
                return default;
        }
    }

    /// <summary>
    /// Releases the fixture's text buffer and restores the enclosing callback state.
    /// </summary>
    public void Dispose()
    {
        Marshal.FreeCoTaskMem(_name);
        s_current = _previous;
    }

    /// <summary>
    /// Catches scripted errors before they can escape the unmanaged boundary.
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int Invoke(nint api, NativeMemoryRequest* request, NativeMemoryResult* result, NativeCallError* error)
    {
        try
        {
            MemoryContextTestFixture fixture = s_current!;
            fixture.Requests.Add(*request);
            *result = fixture.Handler is null ? fixture.Respond(*request) : fixture.Handler(*request);
            return 0;
        }
        catch (Exception exception)
        {
            NativeError.Write(exception, error);
            for (int index = 0; index < NativeErrorFields.Length; index++)
            {
                if (ReleaseCallback(ref error->_fields[index]) != null)
                {
                    ReleaseCallback(ref error->_fields[index]) = &ReleaseError;
                }
            }

            return 1;
        }
    }

    /// <summary>
    /// Counts diagnostic ownership release while retaining the originating allocator.
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void ReleaseError(void* pointer)
    {
        s_current!.ErrorReleases++;
        NativeMemory.Free(pointer);
    }

    /// <summary>
    /// Accesses diagnostic release callbacks for ownership assertions.
    /// </summary>
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_release")]
    private static extern ref delegate* unmanaged[Cdecl]<void*, void> ReleaseCallback(ref NativeValue value);

    /// <summary>
    /// Owns a distinct native envelope and balances its managed capability binding.
    /// </summary>
    internal sealed class Scope : IDisposable
    {
        private readonly NativeMemoryApi* _api;
        private readonly nint _previous;

        /// <summary>
        /// Creates and enters a callback envelope.
        /// </summary>
        /// <param name="provider">The stable provider identity.</param>
        internal Scope(nint provider)
        {
            _api = (NativeMemoryApi*)NativeMemory.AllocZeroed((nuint)sizeof(NativeMemoryApi));
            _api->_provider = provider;
            _api->_current = s_current!.Current;
            _api->_invoke = &Invoke;
            _previous = NativeMemoryContext.Enter((nint)_api);
        }

        /// <summary>
        /// Gets this callback's envelope address.
        /// </summary>
        internal nint Address => (nint)_api;

        /// <summary>
        /// Restores the enclosing binding before freeing this callback envelope.
        /// </summary>
        public void Dispose()
        {
            NativeMemoryContext.Exit(_previous);
            NativeMemory.Free(_api);
        }
    }
}
