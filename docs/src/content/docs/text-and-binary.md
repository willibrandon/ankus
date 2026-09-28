---
title: Text and binary values
description: Choose managed copies or checked native views of PostgreSQL text and bytea.
---

Use `string` and `byte[]` for independent managed values. Use `PgTextView` and
`PgByteaView` to inspect PostgreSQL storage under a checked native lifetime.

| C# representation | SQL type | Storage |
| --- | --- | --- |
| `string` | `text` | Independent managed Unicode string |
| `byte[]` | `bytea` | Independent managed bytes, including embedded zero |
| `PgTextView` | `text` | Checked UTF-8 view with the original server datum retained |
| `PgByteaView` | `bytea` | Checked native byte view |

## Function arguments and results

```csharp
using Ankus;

public static class BufferFunctions
{
    [PgFunction]
    public static int BinaryLength(PgByteaView value) => value.Count;

    [PgFunction]
    public static int Utf8Length(PgTextView value) => value.Utf8Length;

    [PgFunction]
    public static PgByteaView? Echo(PgByteaView? value) => value;
}
```

Nullable views represent SQL NULL. Empty text and empty binary values remain
present views with zero length. `PgByteaView` indexing and enumeration preserve
every byte; `PgTextView.GetUtf8Byte` addresses bytes, including positions inside
a multibyte character. `Utf8Length` is not a character count.

Generated scalar arguments expire when their managed callback returns. Returning
an input view transfers its value before that cleanup. SETOF/TABLE and aggregate
inputs use independent native snapshots when they must survive input callbacks.
Dispose retained views when your iterator or aggregate no longer needs them.

## Copies and encoding

`PgByteaView.ToArray()` makes an independent managed copy; `CopyTo` writes to a
caller-supplied span. `PgTextView.ToString()` returns an independent managed
string; `CopyUtf8To` copies its UTF-8 bytes. A destination that is too short is
rejected without a partial copy.

Flat and short native values keep their original storage. PostgreSQL detoasts
compressed and external values into a private child memory context. Text in a
UTF-8 database keeps its original payload when detoasting is unnecessary. Other
server encodings require conversion into the private context. Invalid encoding
is rejected without replacing bytes or characters.

`PgTextView.Datum` retains the original server-encoded representation. Its
`TypeOid` preserves domains and the `varchar`/`bpchar` identities accepted by
raw reads; padded `bpchar` text retains trailing spaces. Generated view signatures
use `text` or `bytea`, and returned raw datums must match that declared SQL type.
Use an explicit conversion or a [bound raw return](/raw-values/) when choosing
another SQL identity.

## Raw values and SPI

```csharp
using PgTextView text = Spi.ExecuteScalar<PgTextView>("SELECT 'café'::text");
string copy = text.ToString();

using SpiRawResult rows = Spi.QueryRaw("SELECT '\\x0000ff'::bytea");
using PgByteaView bytes = rows[0][0].Read<PgByteaView>();
byte first = bytes[0];
```

Typed scalar SPI results and PostgreSQL function calls preserve view storage
after temporary result cleanup. Views bind as parameters with their original
type identity. A view read from an existing `PgDatum` borrows that datum's
lifetime. Reading SQL NULL produces a null view; directly constructing a view
from a NULL datum is rejected.

Dispose explicitly created views while their backend remains active. Disposing
a view, deleting or resetting its source context, or ending its input callback
invalidates native access. `ResetOnly()` invalidates aliases even when private
child contexts survive. A view created from another view retains the original
source dependency. Copied length and type metadata remain readable after expiry.
Copy a datum into an independent memory context before its source expires if it
must survive that source.

## Direct spans

`DangerousGetSpan()` and `DangerousGetUtf8Span()` validate once and expose a
readonly span without copying. The span cannot check later native lifetime
changes. Keep it on the originating backend thread, and do not retain it across
backend calls, source resets, view disposal or callback exit. Checked indexing
and copy methods validate native access each time they are called.
