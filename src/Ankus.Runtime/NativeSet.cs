using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Ankus;

/// <summary>
/// Roots generated function iterators across PostgreSQL calls and releases their managed ownership.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class NativeSet
{
    /// <summary>
    /// Acquires an iterator and roots it until native execution disposes the handle.
    /// </summary>
    /// <typeparam name="T">The managed row type.</typeparam>
    /// <param name="source">The sequence, or null for an empty set.</param>
    /// <returns>An owned iterator handle, or zero for a null sequence.</returns>
    public static nint Create<T>(IEnumerable<T>? source)
    {
        if (source is null)
        {
            return 0;
        }

        IEnumerator<T> iterator = source.GetEnumerator()
            ?? throw new InvalidOperationException("A set-returning sequence returned a null iterator.");
        try
        {
            return GCHandle.ToIntPtr(GCHandle.Alloc(iterator));
        }
        catch
        {
            iterator.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Advances a rooted iterator and reads its current row without changing ownership.
    /// </summary>
    /// <typeparam name="T">The managed row type.</typeparam>
    /// <param name="handle">The iterator handle.</param>
    /// <param name="value">The next row, or the default value after completion.</param>
    /// <returns>Whether a row was produced.</returns>
    public static bool MoveNext<T>(nint handle, out T value)
    {
        if (handle != 0 && GCHandle.FromIntPtr(handle).Target is IEnumerator<T> iterator && iterator.MoveNext())
        {
            value = iterator.Current;
            return true;
        }

        value = default!;
        return false;
    }

    /// <summary>
    /// Clears and frees a handle before disposing its iterator, including when disposal throws.
    /// </summary>
    /// <param name="handle">The owned handle, cleared before invoking user cleanup.</param>
    public static void Dispose(ref nint handle)
    {
        if (handle == 0)
        {
            return;
        }

        GCHandle owner = GCHandle.FromIntPtr(handle);
        handle = 0;
        IDisposable iterator = (IDisposable)owner.Target!;
        owner.Free();
        iterator.Dispose();
    }
}
