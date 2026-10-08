using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Ankus.Examples.Operators;

/// <summary>
/// Stores four fields as exactly 25 packed native bytes, with custom text and generated comparison operators.
/// </summary>
/// <remarks>
/// SQL text has the form <c>1;2;3;[1, 2, 3, 4, 5]</c>. Equality, ordering and hashing compare the fields in
/// declaration order, like Rust's derived traits on the corresponding pgrx struct.
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
[PgType(Name = "pgvarlenathing", NativeLayout = true, TextCodec = typeof(PgVarlenaThingTextCodec))]
[PgEquality]
[PgOrdering]
[PgHashing]
public unsafe struct PgVarlenaThing : IEquatable<PgVarlenaThing>, IComparable<PgVarlenaThing>, IPgHashable
{
    /// <summary>
    /// The number of bytes in <see cref="D"/>.
    /// </summary>
    public const int DLength = 5;

    /// <summary>
    /// The first unsigned field.
    /// </summary>
    public ulong A;

    /// <summary>
    /// The second unsigned field.
    /// </summary>
    public ulong B;

    /// <summary>
    /// The signed field.
    /// </summary>
    public int C;

    /// <summary>
    /// Five bytes stored inline.
    /// </summary>
    public fixed byte D[DLength];

    /// <summary>
    /// Creates a value from its four fields.
    /// </summary>
    /// <param name="a">The first unsigned field.</param>
    /// <param name="b">The second unsigned field.</param>
    /// <param name="c">The signed field.</param>
    /// <param name="d">Exactly five bytes.</param>
    public PgVarlenaThing(ulong a, ulong b, int c, ReadOnlySpan<byte> d)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(d.Length, DLength, nameof(d));
        A = a;
        B = b;
        C = c;
        for (int index = 0; index < DLength; index++)
        {
            D[index] = d[index];
        }
    }

    /// <summary>
    /// Compares every field.
    /// </summary>
    /// <param name="other">The value to compare.</param>
    /// <returns>Whether all four fields are equal.</returns>
    public readonly bool Equals(PgVarlenaThing other) => CompareTo(other) == 0;

    /// <inheritdoc />
    public override readonly bool Equals(object? obj) => obj is PgVarlenaThing other && Equals(other);

    /// <summary>
    /// Returns the same equality-compatible hash used by PostgreSQL hash indexes.
    /// </summary>
    /// <returns>The stable field hash.</returns>
    public override readonly int GetHashCode() => GetPostgresHashCode();

    /// <summary>
    /// Orders by <see cref="A"/>, <see cref="B"/>, <see cref="C"/> and then the bytes of <see cref="D"/>.
    /// </summary>
    /// <param name="other">The value to compare.</param>
    /// <returns>A negative, zero or positive result.</returns>
    public readonly int CompareTo(PgVarlenaThing other)
    {
        int order = A.CompareTo(other.A);
        if (order == 0)
        {
            order = B.CompareTo(other.B);
        }

        if (order == 0)
        {
            order = C.CompareTo(other.C);
        }

        for (int index = 0; order == 0 && index < DLength; index++)
        {
            order = D[index].CompareTo(other.D[index]);
        }

        return order;
    }

    /// <summary>
    /// Hashes the fields as fixed-width little-endian bytes, independently of the host byte order.
    /// </summary>
    /// <returns>The stable signed 32-bit hash.</returns>
    public readonly int GetPostgresHashCode()
    {
        Span<byte> bytes = stackalloc byte[sizeof(ulong) + sizeof(ulong) + sizeof(int) + DLength];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, A);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes[sizeof(ulong)..], B);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[(2 * sizeof(ulong))..], C);
        for (int index = 0; index < DLength; index++)
        {
            bytes[2 * sizeof(ulong) + sizeof(int) + index] = D[index];
        }

        return PgHash.Compute(bytes);
    }

    /// <summary>
    /// Compares two values for equality.
    /// </summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <returns>Whether all four fields are equal.</returns>
    public static bool operator ==(PgVarlenaThing left, PgVarlenaThing right) => left.Equals(right);

    /// <summary>
    /// Compares two values for inequality.
    /// </summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <returns>Whether any field differs.</returns>
    public static bool operator !=(PgVarlenaThing left, PgVarlenaThing right) => !left.Equals(right);

    /// <summary>
    /// Compares two values in field order.
    /// </summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <returns>Whether the left value sorts before the right value.</returns>
    public static bool operator <(PgVarlenaThing left, PgVarlenaThing right) => left.CompareTo(right) < 0;

    /// <summary>
    /// Compares two values in field order.
    /// </summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <returns>Whether the left value sorts after the right value.</returns>
    public static bool operator >(PgVarlenaThing left, PgVarlenaThing right) => left.CompareTo(right) > 0;

    /// <summary>
    /// Compares two values in field order.
    /// </summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <returns>Whether the left value sorts before or equals the right value.</returns>
    public static bool operator <=(PgVarlenaThing left, PgVarlenaThing right) => left.CompareTo(right) <= 0;

    /// <summary>
    /// Compares two values in field order.
    /// </summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <returns>Whether the left value sorts after or equals the right value.</returns>
    public static bool operator >=(PgVarlenaThing left, PgVarlenaThing right) => left.CompareTo(right) >= 0;
}
