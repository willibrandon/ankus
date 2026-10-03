using System.ComponentModel;

namespace Ankus.CompilerServices;

/// <summary>
/// Requires an explicit unsafe context for generated raw native calls and global accesses.
/// </summary>
/// <remarks>
/// This generated-code contract also covers scalar signatures whose native implementation can
/// violate PostgreSQL ownership, synchronization or backend state. It does not remove the native error guard.
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property, Inherited = false)]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class NativeUnsafeAccessAttribute : Attribute;
