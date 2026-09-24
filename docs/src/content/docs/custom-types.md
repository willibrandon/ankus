---
title: Custom types
description: Declare PostgreSQL base types with C# values and explicit storage codecs.
---

Put `[PgType]` on a class, struct, or enum and provide a `PgTypeCodec<T>`.
The codec defines its SQL text format and stored bytes. Ankus creates the type,
its input/output functions, and its array type.

This example stores distances as eight-byte integers and displays them as `125mm`:

```csharp
using System.Buffers;
using System.Buffers.Binary;
using System.Globalization;
using Ankus;

[PgType(typeof(DistanceCodec))]
public readonly record struct Distance(long Millimeters);

public sealed class DistanceCodec : PgTypeCodec<Distance>
{
    public override Distance Parse(string text)
    {
        if (!text.EndsWith("mm", StringComparison.Ordinal))
            throw new PgException("22P02", "Distance must end with mm.");

        return new(long.Parse(text.AsSpan(0, text.Length - 2), CultureInfo.InvariantCulture));
    }

    public override string Format(Distance value) =>
        value.Millimeters.ToString(CultureInfo.InvariantCulture) + "mm";

    public override Distance Read(ReadOnlySpan<byte> payload)
    {
        if (payload.Length != sizeof(long))
            throw new PgException("22P03", "Invalid distance payload.");

        return new(BinaryPrimitives.ReadInt64BigEndian(payload));
    }

    public override void Write(Distance value, IBufferWriter<byte> destination)
    {
        BinaryPrimitives.WriteInt64BigEndian(destination.GetSpan(sizeof(long)), value.Millimeters);
        destination.Advance(sizeof(long));
    }
}
```

After installing the extension:

```sql
CREATE TABLE measurements(value distance);
INSERT INTO measurements VALUES ('125mm'), (NULL);
SELECT value FROM measurements;
```

Use `Distance` directly in `[PgFunction]` methods, aggregate callbacks, operators,
casts, and SPI parameters and results. `Distance?` represents SQL NULL.
`Distance?[]` supports nullable elements; `PgArray<Distance?>` also preserves
dimensions and lower bounds. Sets and TABLE results use the same conversions.

## Names and installation

The SQL name defaults to snake case. Use `Name` to change it and `Schema` to
select a fixed schema. Types nested in a `[PgSchema]` class inherit that schema.
Without a fixed schema, the type follows the extension's installation schema,
including after relocation. Ankus creates types before functions that use them.
`Id` and `Requires` order custom installation SQL around the type.

## Storage and errors

`Read` receives the complete payload without PostgreSQL's storage header.
`Write` supplies that payload through the buffer writer. PostgreSQL handles
large-value compression and external storage. Keep the byte format compatible
across upgrades or migrate existing rows when changing it.

Return independent values from `Read`; its input span is borrowed. Ankus creates
one codec instance on first use per extension runtime. Do not keep borrowed buffers or
backend call state in that instance. SQL NULL bypasses the codec; returning null
for a present input is an error. Exceptions become PostgreSQL errors after the
managed callback unwinds.

Set `BinaryProtocol = true` on `[PgType]` to enable binary send/receive, including
binary COPY. The wire payload uses the codec's storage format. `Read` must reject
invalid lengths, trailing bytes, and invalid field values.
