---
title: Custom types
description: Declare PostgreSQL base types with generated CBOR storage and JSON text, or explicit codecs.
---

Put `[PgType]` on a record, class, struct, or enum. Ankus generates a serializer,
the PostgreSQL type, its input/output functions, and its array type:

```csharp
[PgType]
public sealed record Reading(string Sensor, decimal Value, string? Unit);
```

```sql
CREATE TABLE readings(value reading);
INSERT INTO readings VALUES ('{"Sensor":"outside","Value":19.125,"Unit":"°C"}'), (NULL);
SELECT value FROM readings;
```

Default types store CBOR and use JSON for SQL text input/output, following pgrx's
`PostgresType` model. Ankus owns the contract generator and serialization rules;
Microsoft's `System.Formats.Cbor` and `System.Text.Json` provide token readers and
writers. Generated code calls constructors and accesses members directly. No
reflection-based serializer or separate JSON context is needed.

## Generated contracts

The default serializer supports:

- Public instance fields and properties of accessible classes, structs,
  and records, including inherited members, virtual overrides, and immutable
  constructor-bound members. Abstract classes require declared concrete variants.
- Booleans, signed and unsigned 8/16/32/64-bit integers, `float`, `double`,
  `decimal`, and Unicode strings. Decimal CBOR uses the decimal-fraction tag;
  decimal scale, including the scale of zero, is retained. JSON decimal input that would
  require rounding is rejected. A `decimal` with zero magnitude and its sign bit
  set is rejected because CBOR decimal fractions cannot preserve that sign bit.
- Enums as case-sensitive strings. Undefined numeric values and unnamed flag
  combinations are rejected.
- One-dimensional arrays, `List<T>`, and `Dictionary<string, T>`, nested with
  other supported contracts. `byte[]` is a sequence of integers in both formats,
  like a Rust `Vec<u8>`.
- Nullable values, nullable containers, and nullable elements at every level.
  SQL NULL bypasses the serializer; JSON or CBOR null cannot represent a present
  top-level value.

Member names keep their C# spelling. `[JsonPropertyName]` changes a member's key
in both formats. `[JsonIgnore]` excludes a member; `Condition = Never` includes
it. `[JsonStringEnumMemberName]` changes an enum's stored name.

An accessible `[JsonConstructor]` selects the constructor. Otherwise Ankus uses
an accessible parameterless constructor, or the sole accessible constructor.
Every parameter must match a serialized member's C# name (ignoring case) and
exact type, including nullability. Remaining members need accessible setters or
init accessors, or writable fields. Constructor-bound members keep the value
assigned by the constructor, including any normalization. A constructor that
binds or deliberately ignores C# `required` members must carry
`[SetsRequiredMembers]`; Ankus diagnoses missing annotations instead of overwriting
the constructor's values to satisfy C# initializer requirements.

Missing non-nullable members are errors. Missing nullable members become null;
`required` and `[JsonRequired]` require presence even when null is permitted.
Duplicate known members and duplicate dictionary keys are rejected. Unknown
members are skipped with input validation, allowing readers to tolerate added
fields. JSON names and dictionary keys are case-sensitive.

Unsupported shapes and serialization attributes produce `ANKUS017`. Hidden
inherited members, arbitrary framework types, multidimensional arrays, and
non-string dictionary keys currently require an explicit codec. Passing an
undeclared runtime subclass is rejected so additional state is not silently
discarded. Nesting is limited to 64 containers; cyclic graphs fail at
that limit. CBOR preserves non-finite floating-point values; JSON output rejects
them because JSON has no exact representation.

Malformed JSON input raises SQLSTATE `22P02`; malformed CBOR raises `22P03`.
Trailing data, numeric overflow or underflow, required null values, and invalid Unicode are
errors. Ankus deliberately reports invalid JSON instead of adopting pgrx's
current default input wrapper's conversion of a parse failure to SQL NULL.

## Tagged variants

Use standard `System.Text.Json.Serialization` attributes to declare a closed set
of variants, corresponding to tagged Rust enum variants:

```csharp
[PgType]
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(Measured), "measured")]
[JsonDerivedType(typeof(Unavailable), "unavailable")]
public abstract record Measurement(string Sensor);

public sealed record Measured(string Sensor, decimal Value, string? Unit)
    : Measurement(Sensor);
public sealed record Unavailable(string Sensor, string Reason) : Measurement(Sensor);
```

```sql
CREATE TABLE measurements(value measurement);
INSERT INTO measurements VALUES
    ('{"kind":"measured","Sensor":"outside","Value":19.125,"Unit":"°C"}'),
    ('{"Sensor":"outside","Reason":"offline","kind":"unavailable"}');
```

Each registered type needs a unique string or signed 32-bit integer discriminator.
Integer `7` and string `"7"` identify different variants. The property defaults to
`$type` when `[JsonPolymorphic]` is omitted. Writers put the discriminator first;
readers accept it anywhere in the object. Both JSON and CBOR store the same
discriminator and all inherited serialized members. Variants can appear in
nullable members, arrays, lists, dictionaries, and recursive contracts.

An abstract base requires a discriminator on input. A concrete base also accepts
an object without one and constructs the exact base type. Registering the base
itself gives its output an explicit discriminator. Unknown, duplicate, null,
incorrectly typed, and out-of-range discriminators are errors. Unregistered
runtime types, including subclasses of a registered variant, are errors.
Fallback options that discard concrete type identity are diagnosed at compile
time. A discriminator property cannot share a name with a serialized member.
Changing registrations or discriminator values changes the persisted contract.

## Explicit codecs

Provide a `PgTypeCodec<T>` to define the SQL text format and stored bytes yourself.

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

Use a custom type directly in `[PgFunction]` methods, aggregate callbacks, operators,
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

For default serialization the binary payload is CBOR, with object members encoded
as text-keyed maps and arrays as sequences. Renaming members or enum labels,
changing types or nullability, or removing required constructor parameters changes
the persisted contract. Plan a data migration before making incompatible changes.
CBOR use alone does not guarantee interchangeability with every pgrx/Serde contract;
match member names and value representations explicitly.
