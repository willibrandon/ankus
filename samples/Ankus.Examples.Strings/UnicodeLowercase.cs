using System.Buffers;
using System.Text;

namespace Ankus.Examples.Strings;

/// <summary>
/// Implements locale-independent full lowercase and final-sigma context using portable Unicode 17.0 data.
/// </summary>
internal static class UnicodeLowercase
{
    /// <summary>
    /// Rejects unpaired UTF-16 surrogates that cannot occur in Rust strings or PostgreSQL UTF-8 input.
    /// </summary>
    private static readonly UTF8Encoding s_utf8 = new(false, true);

    /// <summary>
    /// Preserves full Unicode lowercase independent of the platform's ICU or NLS version.
    /// </summary>
    /// <param name="input">The exact scalar string.</param>
    /// <returns>The complete lowercase string, including expansions and contextual final sigma.</returns>
    internal static string Convert(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        s_utf8.GetByteCount(input);
        var result = new StringBuilder(input.Length);
        Span<char> encoded = stackalloc char[2];
        for (int index = 0; index < input.Length;)
        {
            if (Rune.DecodeFromUtf16(input.AsSpan(index), out Rune scalar, out int consumed) != OperationStatus.Done)
            {
                throw new ArgumentException("The input contains invalid UTF-16.", nameof(input));
            }

            if (scalar.Value == 0x130)
            {
                result.Append("i\u0307");
            }
            else
            {
                int lower = scalar.Value == 0x3a3 && CasedBefore(input.AsSpan(0, index)) &&
                    !CasedAfter(input.AsSpan(index + consumed)) ? 0x3c2 : Simple(scalar.Value);
                int written = new Rune(lower).EncodeToUtf16(encoded);
                result.Append(encoded[..written]);
            }

            index += consumed;
        }

        return result.ToString();
    }

    /// <summary>
    /// Finds the last preceding non-ignorable scalar's Cased property.
    /// </summary>
    /// <param name="text">The validated UTF-16 prefix.</param>
    /// <returns>Whether the significant preceding scalar is cased.</returns>
    private static bool CasedBefore(ReadOnlySpan<char> text)
    {
        while (!text.IsEmpty)
        {
            Rune.DecodeLastFromUtf16(text, out Rune scalar, out int consumed);
            if (!Contains(UnicodeLowerData.s_ignorable, scalar.Value))
            {
                return Contains(UnicodeLowerData.s_cased, scalar.Value);
            }

            text = text[..^consumed];
        }

        return false;
    }

    /// <summary>
    /// Finds the first following non-ignorable scalar's Cased property.
    /// </summary>
    /// <param name="text">The validated UTF-16 suffix.</param>
    /// <returns>Whether the significant following scalar is cased.</returns>
    private static bool CasedAfter(ReadOnlySpan<char> text)
    {
        while (!text.IsEmpty)
        {
            Rune.DecodeFromUtf16(text, out Rune scalar, out int consumed);
            if (!Contains(UnicodeLowerData.s_ignorable, scalar.Value))
            {
                return Contains(UnicodeLowerData.s_cased, scalar.Value);
            }

            text = text[consumed..];
        }

        return false;
    }

    /// <summary>
    /// Applies the complete simple lowercase mapping without changing scalar gaps within alternating intervals.
    /// </summary>
    /// <param name="scalar">The valid Unicode scalar.</param>
    /// <returns>Its single-scalar lowercase value.</returns>
    private static int Simple(int scalar)
    {
        int[] rules = UnicodeLowerData.s_mappings;
        int first = 0;
        int last = rules.Length / 4 - 1;
        while (first <= last)
        {
            int middle = first + (last - first) / 2;
            int index = middle * 4;
            if (scalar < rules[index])
            {
                last = middle - 1;
            }
            else if (scalar > rules[index + 1])
            {
                first = middle + 1;
            }
            else
            {
                return (scalar - rules[index]) % rules[index + 2] == 0 ? scalar + rules[index + 3] : scalar;
            }
        }

        return scalar;
    }

    /// <summary>
    /// Tests a complete sorted Unicode property range catalog.
    /// </summary>
    /// <param name="ranges">The flattened inclusive range pairs.</param>
    /// <param name="scalar">The valid Unicode scalar.</param>
    /// <returns>Whether the scalar has the property.</returns>
    private static bool Contains(int[] ranges, int scalar)
    {
        int first = 0;
        int last = ranges.Length / 2 - 1;
        while (first <= last)
        {
            int middle = first + (last - first) / 2;
            int index = middle * 2;
            if (scalar < ranges[index])
            {
                last = middle - 1;
            }
            else if (scalar > ranges[index + 1])
            {
                first = middle + 1;
            }
            else
            {
                return true;
            }
        }

        return false;
    }
}
