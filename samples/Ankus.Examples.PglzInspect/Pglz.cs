using Ankus.Postgres;

namespace Ankus.Examples.PglzInspect;

/// <summary>
/// Wraps PostgreSQL's PGLZ compressor in <c>common/pg_lzcompress.h</c> with checked spans.
/// </summary>
/// <remarks>
/// <c>pglz_compress</c> and <c>pglz_decompress</c> are pure: they neither allocate PostgreSQL memory nor raise errors.
/// They still run through Ankus's native guard, so they require an active PostgreSQL callback. Buffer sizes are checked
/// before any native call. Corrupt compressed input raises <see cref="InvalidDataException"/>.
/// </remarks>
public static class Pglz
{
    /// <summary>
    /// The extra bytes PGLZ may need beyond its input, from the C macro <c>PGLZ_MAX_OUTPUT(_dlen)</c>.
    /// </summary>
    private const int OutputOverhead = 4;

    /// <summary>
    /// Gets the largest compressed size PGLZ can produce for an input, saturating at <see cref="int.MaxValue"/>.
    /// </summary>
    /// <param name="inputLength">The uncompressed length.</param>
    /// <returns>The required destination capacity, <c>inputLength + 4</c> when it fits.</returns>
    public static int MaxOutput(int inputLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(inputLength);
        return inputLength > int.MaxValue - OutputOverhead ? int.MaxValue : inputLength + OutputOverhead;
    }

    /// <summary>
    /// Compresses <paramref name="source"/> into a caller-supplied buffer, which can be reused across calls.
    /// </summary>
    /// <param name="source">The uncompressed bytes.</param>
    /// <param name="destination">A buffer of at least <see cref="MaxOutput(int)"/> bytes; PGLZ only writes to it.</param>
    /// <param name="strategy">The acceptance strategy.</param>
    /// <param name="bytesWritten">The compressed length, or zero when PGLZ rejects the input.</param>
    /// <returns>Whether PGLZ accepted the input; false means its heuristics judged it not worth compressing.</returns>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is smaller than the maximum output.</exception>
    public static bool TryCompress(ReadOnlySpan<byte> source, Span<byte> destination, PglzStrategy strategy, out int bytesWritten)
    {
        if (destination.Length < (long)source.Length + OutputOverhead)
        {
            throw new ArgumentException("The destination buffer is smaller than PGLZ's maximum output.", nameof(destination));
        }

        int result;
        unsafe
        {
#if ANKUS_PG13
            // PostgreSQL 13 does not mark these globals PGDLLIMPORT, so a Windows extension cannot import them.
            // These are PostgreSQL 13's own definitions from pg_lzcompress.c.
            PGLZ_Strategy local = strategy switch
            {
                PglzStrategy.Default => new PGLZ_Strategy
                {
                    min_input_size = 32, max_input_size = int.MaxValue, min_comp_rate = 25, first_success_by = 1024,
                    match_size_good = 128, match_size_drop = 10,
                },
                PglzStrategy.Always => new PGLZ_Strategy
                {
                    min_input_size = 0, max_input_size = int.MaxValue, min_comp_rate = 0, first_success_by = int.MaxValue,
                    match_size_good = 128, match_size_drop = 6,
                },
                _ => throw new ArgumentOutOfRangeException(nameof(strategy), strategy, "Unknown PGLZ strategy."),
            };
            PGLZ_Strategy* native = &local;
#else
            PGLZ_Strategy* native = strategy switch
            {
                PglzStrategy.Default => NativeGlobals.PGLZ_strategy_default,
                PglzStrategy.Always => NativeGlobals.PGLZ_strategy_always,
                _ => throw new ArgumentOutOfRangeException(nameof(strategy), strategy, "Unknown PGLZ strategy."),
            };
#endif
            fixed (byte* input = source)
            fixed (byte* output = destination)
            {
                result = NativeMethods.pglz_compress((sbyte*)input, source.Length, (sbyte*)output, native);
            }
        }

        if (result < 0)
        {
            bytesWritten = 0;
            return false;
        }

        // Defense in depth: a larger result would expose bytes PGLZ never wrote.
        if (result > source.Length + OutputOverhead)
        {
            throw new InvalidOperationException($"pglz_compress returned {result} bytes for a {source.Length}-byte input.");
        }

        bytesWritten = result;
        return true;
    }

    /// <summary>
    /// Compresses <paramref name="source"/> into a new array.
    /// </summary>
    /// <param name="source">The uncompressed bytes.</param>
    /// <param name="strategy">The acceptance strategy.</param>
    /// <returns>The compressed bytes, or null when PGLZ rejects the input.</returns>
    public static byte[]? Compress(ReadOnlySpan<byte> source, PglzStrategy strategy)
    {
        byte[] buffer = GC.AllocateUninitializedArray<byte>(checked(source.Length + OutputOverhead));
        return TryCompress(source, buffer, strategy, out int written) ? buffer.AsSpan(0, written).ToArray() : null;
    }

    /// <summary>
    /// Decompresses into the first <paramref name="rawSize"/> bytes of a caller-supplied buffer.
    /// </summary>
    /// <param name="source">The PGLZ-compressed bytes.</param>
    /// <param name="destination">A buffer of at least <paramref name="rawSize"/> bytes; it may be larger.</param>
    /// <param name="rawSize">The expected uncompressed length.</param>
    /// <param name="checkComplete">Whether all of <paramref name="source"/> must be consumed.</param>
    /// <returns>The number of bytes written, never more than <paramref name="rawSize"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="rawSize"/> is negative.</exception>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is smaller than <paramref name="rawSize"/>.</exception>
    /// <exception cref="InvalidDataException">The input is corrupt or truncated.</exception>
    public static int Decompress(ReadOnlySpan<byte> source, Span<byte> destination, int rawSize, bool checkComplete)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rawSize);
        if (destination.Length < rawSize)
        {
            throw new ArgumentException("The destination buffer is smaller than the uncompressed size.", nameof(destination));
        }

        int result;
        unsafe
        {
            fixed (byte* input = source)
            fixed (byte* output = destination)
            {
                result = NativeMethods.pglz_decompress((sbyte*)input, source.Length, (sbyte*)output, rawSize, checkComplete);
            }
        }

        if (result < 0)
        {
            throw new InvalidDataException("pglz_decompress failed: corrupt or truncated input.");
        }

        if (result > rawSize)
        {
            throw new InvalidOperationException($"pglz_decompress returned {result} bytes for a {rawSize}-byte destination.");
        }

        return result;
    }

    /// <summary>
    /// Decompresses into a new array of the bytes actually produced.
    /// </summary>
    /// <param name="source">The PGLZ-compressed bytes.</param>
    /// <param name="rawSize">The expected uncompressed length from a trusted source, such as a TOAST header.</param>
    /// <param name="checkComplete">Whether all of <paramref name="source"/> must be consumed.</param>
    /// <returns>The uncompressed bytes.</returns>
    /// <remarks>
    /// <paramref name="rawSize"/> selects an allocation of up to 2 GiB; never take it from untrusted SQL input.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="rawSize"/> is negative.</exception>
    /// <exception cref="InvalidDataException">The input is corrupt or truncated.</exception>
    public static byte[] Decompress(ReadOnlySpan<byte> source, int rawSize, bool checkComplete)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rawSize);
        if (rawSize == 0)
        {
            return [];
        }

        byte[] buffer = GC.AllocateUninitializedArray<byte>(rawSize);
        int written = Decompress(source, buffer, rawSize, checkComplete);
        return written == rawSize ? buffer : buffer.AsSpan(0, written).ToArray();
    }
}
