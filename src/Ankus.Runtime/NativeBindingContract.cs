namespace Ankus;

/// <summary>
/// Checks a complete generated declaration identity against the current native capability.
/// </summary>
internal static unsafe class NativeBindingContract
{
    /// <summary>
    /// Borrows the exact identity bytes while the backend validates its selected headers and server major.
    /// </summary>
    /// <param name="identity">The generated UTF-8 binding identity.</param>
    /// <param name="postgresMajor">The generated PostgreSQL major version.</param>
    internal static void Validate(ReadOnlySpan<byte> identity, int postgresMajor)
    {
        fixed (byte* data = identity)
        {
            NativeMemoryRequest request = new()
            {
                _operation = NativeMemoryOperation.NativeBinding,
                _data = (nint)data,
                _length = (nuint)identity.Length,
                _value = postgresMajor,
            };
            NativeMemoryContext.Invoke(ref request, out _);
        }
    }
}
