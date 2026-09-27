namespace Ankus;

/// <summary>
/// Matches the native storage kind used for cross-extension layout validation.
/// </summary>
internal enum NativeSharedMemoryKind : uint
{
    /// <summary>
    /// Protects a copied value with a PostgreSQL lightweight lock.
    /// </summary>
    Locked,

    /// <summary>
    /// Stores a supported Interlocked scalar with bounded reader admission.
    /// </summary>
    Atomic,

    /// <summary>
    /// Stores an unmanaged aggregate with scoped reader admission.
    /// </summary>
    Shared,
}
