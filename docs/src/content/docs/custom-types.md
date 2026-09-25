---
title: Custom types
description: Declare PostgreSQL base types with CBOR, packed native storage, or explicit codecs.
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

## Custom text with generated storage

Set `TextCodec` to a `PgTypeTextCodec<T>` when you want a custom SQL text format
while retaining the generated CBOR contract. The codec supplies only `Parse` and
`Format`; the generator still validates and serializes the type's members.

```csharp
using System.Globalization;
using Ankus;

[PgType(TextCodec = typeof(RgbColorTextCodec), BinaryProtocol = true)]
public readonly record struct RgbColor(byte Red, byte Green, byte Blue);

public sealed class RgbColorTextCodec : PgTypeTextCodec<RgbColor>
{
    public override RgbColor Parse(string text)
    {
        if (text.Length != 7 || text[0] != '#' ||
            !byte.TryParse(text.AsSpan(1, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out byte red) ||
            !byte.TryParse(text.AsSpan(3, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out byte green) ||
            !byte.TryParse(text.AsSpan(5, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out byte blue))
        {
            throw new PgException("22P02", "Color must use #RRGGBB hexadecimal notation.");
        }

        return new(red, green, blue);
    }

    public override string Format(RgbColor value) =>
        string.Create(CultureInfo.InvariantCulture, $"#{value.Red:X2}{value.Green:X2}{value.Blue:X2}");
}
```

```sql
SELECT '#12abef'::rgb_color::text; -- #12ABEF
SELECT NULL::rgb_color;          -- SQL NULL
```

CBOR stores the `Red`, `Green`, and `Blue` members. Binary send/receive, managed
function arguments, SPI, and arrays use that storage contract without calling
the text codec. Nested values retain their structural contracts; a nested type's
`TextCodec` does not replace its representation inside a containing object.
Text codecs also work with generated enum and tagged-variant contracts.

Ankus constructs one text codec lazily on its first text operation. Binary
operations neither construct nor invoke it, even if its constructor has failed.
Throw `PgException` to choose the SQLSTATE and diagnostics; other exceptions
become `38000` after managed frames unwind. Returning null for a present value
or returning null text is an error. Output must contain valid Unicode without
zero characters.

Both text and full storage codecs must be accessible, concrete types with an
accessible parameterless constructor and an exact, non-nullable `T`. Closed
generic codec types are supported. Constructors for codecs with C# `required`
members need `[SetsRequiredMembers]`. `TextCodec` cannot be combined with the
positional full storage codec option.

## Packed native storage

Use `NativeLayout = true` with `TextCodec` when the stored value should be a
densely packed unmanaged struct. Every struct in the layout must explicitly
declare `[StructLayout(LayoutKind.Sequential, Pack = 1)]`. For example, this
variant of the color above stores exactly three payload bytes:

```csharp
using System.Runtime.InteropServices;
using Ankus;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
[PgType(NativeLayout = true, TextCodec = typeof(PackedColorTextCodec), BinaryProtocol = true)]
public readonly record struct PackedColor(byte Red, byte Green, byte Blue);

public sealed class PackedColorTextCodec : PgTypeTextCodec<PackedColor>
{
    private readonly RgbColorTextCodec _text = new();

    public override PackedColor Parse(string text)
    {
        RgbColor value = _text.Parse(text);
        return new(value.Red, value.Green, value.Blue);
    }

    public override string Format(PackedColor value) =>
        _text.Format(new(value.Red, value.Green, value.Blue));
}
```

```sql
SELECT encode(packed_color_send('#12abef'::packed_color), 'hex'); -- 12abef
```

Supported fields are `byte`, `sbyte`, `short`, `ushort`, `int`, `uint`, `long`,
`ulong`, `float`, `double`, enums, nested packed structs declared in the same
assembly, and fixed buffers of these numeric primitives. All instance fields
participate, including private fields and auto-property backing fields; JSON
member attributes do not change the layout. Empty structs, reference fields,
booleans, characters, pointers, native-sized integers, generic structs, opaque
framework or external structs, explicit layouts, size overrides and inline
arrays are rejected. These constraints exclude padding and process-dependent
values from copied managed transport.

Storage uses field order and the host's native byte order. Floating-point bit
patterns and unnamed enum values are retained without text conversion. Binary
send/receive, when enabled, exposes the same bytes; this is not the generated
CBOR wire format. The payload excludes PostgreSQL's varlena header. Input must
have exactly the generated size or it raises SQLSTATE `22P03`. Text conversion
can enforce domain rules, but binary input validates size only; use an explicit
storage codec if you need additional binary validation.

Changing field order, field types, packing or byte order requires a data
migration. Ordinary function, SPI, array and set transports copy values into
managed structs. This storage option does not yet implement pgrx's borrowed
`PgVarlena<T>` views or copy-on-write ownership. See the compiled
[custom-type sample](https://github.com/willibrandon/ankus/tree/main/samples/Ankus.Examples.CustomTypes).

## NULL input policy

Generated input functions are `STRICT` by default. Set `NullInputErrorMessage`
to make a direct NULL call to the text input function raise SQLSTATE `22004`:

```csharp
[PgType(TextCodec = typeof(RgbColorTextCodec),
    NullInputErrorMessage = "Color input must not be NULL.")]
public readonly record struct RgbColor(byte Red, byte Green, byte Blue);
```

```sql
SELECT rgb_color_in(NULL); -- ERROR: Color input must not be NULL.
SELECT NULL::rgb_color;    -- The same error during literal coercion.
```

The error occurs before codec construction. PostgreSQL invokes non-strict input
functions when coercing untyped NULL literals, including inferred nullable
function arguments, and for NULL elements in a text array or fields in text COPY.
This option rejects all of those inputs. Already-typed SQL NULL values, such as
a nullable managed function result, a stored NULL or a binary COPY NULL, still
bypass the codec and can flow through nullable arguments and arrays.
This option also applies to default JSON types and full storage codecs. Output,
binary send, and binary receive functions remain strict.

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
