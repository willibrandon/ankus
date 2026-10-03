using Ankus.Postgres;

namespace Ankus.TestExtension;

/// <summary>
/// Exercises exported and inline functions added by the expanded upstream header inventory.
/// </summary>
[PgSchema("native_inventory")]
public static class NativeInventoryFunctions
{
    /// <summary>
    /// Hashes the exact integer bits with PostgreSQL's native unsigned helper.
    /// </summary>
    /// <param name="value">The integer bit pattern.</param>
    /// <returns>The hash in PostgreSQL's signed SQL representation.</returns>
    [PgFunction]
    public static int Hash(int value)
    {
        unsafe
        {
            return unchecked((int)NativeMethods.hash_bytes_uint32((uint)value));
        }
    }

    /// <summary>
    /// Preserves all bits of the native seed and extended hash result.
    /// </summary>
    /// <param name="value">The integer bit pattern.</param>
    /// <param name="seed">The 64-bit seed.</param>
    /// <returns>The extended hash in PostgreSQL's signed SQL representation.</returns>
    [PgFunction]
    public static long ExtendedHash(int value, long seed)
    {
        unsafe
        {
            return unchecked((long)NativeMethods.hash_bytes_uint32_extended((uint)value, (ulong)seed));
        }
    }

    /// <summary>
    /// Calls the installed headers' inline overflow helper with real writable result storage.
    /// </summary>
    /// <param name="left">The first addend.</param>
    /// <param name="right">The second addend.</param>
    /// <returns>The exact sum, or SQL NULL when the native helper reports overflow.</returns>
    [PgFunction]
    public static unsafe int? Add(int left, int right)
    {
        int result;
        return NativeMethods.pg_add_s32_overflow(left, right, &result) ? null : result;
    }
}
