namespace Ankus;

/// <summary>
/// Updates an exclusively protected unmanaged value through a reference limited to the synchronous callback.
/// </summary>
/// <typeparam name="T">The unmanaged protected value.</typeparam>
/// <typeparam name="TResult">The owned result returned by the callback.</typeparam>
/// <param name="value">The original protected value, valid only during this callback.</param>
/// <returns>The callback's owned result.</returns>
/// <remarks>
/// Writes take effect immediately, including when the callback throws. Nested spinlocks cannot be
/// acquired or queried while this mutable reference is active. Use a separate guard Read callback
/// for those operations while retaining the same parent guard.
/// </remarks>
public delegate TResult PgSharedMutator<T, out TResult>(scoped ref T value) where T : unmanaged;
