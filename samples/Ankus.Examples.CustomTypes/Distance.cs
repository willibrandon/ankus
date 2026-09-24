using System.Buffers;
using System.Buffers.Binary;
using System.Globalization;

namespace Ankus.Examples.CustomTypes;

/// <summary>
/// Stores an exact distance in millimeters as a PostgreSQL base type.
/// </summary>
/// <param name="Millimeters">The signed distance.</param>
[PgType(typeof(DistanceCodec))]
public readonly record struct Distance(long Millimeters);

/// <summary>
/// Uses millimeter text and a portable eight-byte storage representation.
/// </summary>
public sealed class DistanceCodec : PgTypeCodec<Distance>
{
    /// <inheritdoc />
    public override Distance Parse(string text)
    {
        if (!text.EndsWith("mm", StringComparison.Ordinal))
        {
            throw new PgException("22P02", "Distance must end with mm.");
        }

        return new(long.Parse(text.AsSpan(0, text.Length - 2), CultureInfo.InvariantCulture));
    }

    /// <inheritdoc />
    public override string Format(Distance value) => value.Millimeters.ToString(CultureInfo.InvariantCulture) + "mm";

    /// <inheritdoc />
    public override Distance Read(ReadOnlySpan<byte> payload)
    {
        if (payload.Length != sizeof(long))
        {
            throw new PgException("22P03", "Invalid distance payload.");
        }

        return new(BinaryPrimitives.ReadInt64BigEndian(payload));
    }

    /// <inheritdoc />
    public override void Write(Distance value, IBufferWriter<byte> destination)
    {
        BinaryPrimitives.WriteInt64BigEndian(destination.GetSpan(sizeof(long)), value.Millimeters);
        destination.Advance(sizeof(long));
    }
}
