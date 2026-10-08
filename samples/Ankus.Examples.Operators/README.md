# Operators example

This sample ports pgrx's `operators` example and keeps an Ankus enum cast
example. It shows a manual operator, generated equality, B-tree and hash
operators for a JSON-text type, and the same generated operators for a type
with packed native storage and custom text:

```sql
CREATE EXTENSION ankus_operators;

-- A manual operator.
SELECT '{"value":1}'::mytype = '{"value":1}'::mytype;               -- true

-- Generated equality, ordering and hashing.
SELECT '{"Value":"a"}'::thing < '{"Value":"b"}'::thing;            -- true
SELECT thing_cmp('{"Value":"b"}', '{"Value":"a"}');                -- 1
CREATE TABLE things (value thing);
CREATE INDEX ON things USING btree (value);
CREATE INDEX ON things USING hash (value);

-- Packed native storage with custom text.
SELECT '1;2;3;[1,2,3,4,5]'::pgvarlenathing;                        -- 1;2;3;[1, 2, 3, 4, 5]
SELECT '1;2;3;[1,2,3,4,5]'::pgvarlenathing < '2;2;3;[1,2,3,4,5]'::pgvarlenathing;  -- true

-- The Ankus enum operator and cast example.
SELECT 'High'::priority === 'High'::priority;                      -- true
SELECT 'High'::priority::integer;                                  -- 30
```

## How pgrx maps to C#

| pgrx | Ankus |
| --- | --- |
| `#[pg_operator] #[opname(=)] fn my_eq` | `[PgOperator("=")]` on the static method `MyEq` |
| `#[derive(PostgresType, Serialize, Deserialize)]` | `[PgType]` with generated CBOR storage and JSON text |
| `#[derive(PostgresEq)]` | `[PgEquality]` with `IEquatable<T>`: `=`, `<>`, `<type>_eq` and `<type>_ne` |
| `#[derive(PostgresOrd)]` | `[PgOrdering]` with `IComparable<T>`: `<`, `>`, `<=`, `>=`, `<type>_cmp` and the default `<type>_btree_ops` class |
| `#[derive(PostgresHash)]` | `[PgHashing]` with `IPgHashable`: `<type>_hash` and the default `<type>_hash_ops` class |
| `#[repr(C)]` struct with `#[pgvarlena_inoutfuncs]` | `[PgType(NativeLayout = true, TextCodec = ...)]` on a packed struct |
| `PgVarlena<T>` | `PgVarlena<T>`: checked, copy-on-write access to the stored bytes, as in `pg_varlena_thing_with_c` |

pgrx's derived `=` operator declares a negator, `eqsel` and `eqjoinsel`
estimators, `HASHES` and `MERGES`. Ankus generates the same options. All
generated support functions are `IMMUTABLE`, `PARALLEL SAFE` and `STRICT`.
`MyEq` keeps pgrx's defaults: volatile, parallel unsafe, and no planner
options.

Rust's derived `Ord` compares `String` values by their UTF-8 bytes. .NET's
ordinal comparison uses UTF-16 code units, which put characters at U+10000
and above before U+E000 to U+FFFF. `Thing.CompareTo` compares by Unicode scalar
value, which is the same as UTF-8 byte order. For example,
`'{"Value":"｡"}'::thing < '{"Value":"😀"}'::thing` is true, as in pgrx.

`PgVarlenaThing` compares its fields in declaration order, like Rust's derived
traits: `a`, then `b`, `c`, and the bytes of `d`. Its text uses Rust's `Display`
form, `a;b;c;[d0, d1, d2, d3, d4]`.

Generated hashes use SeaHash with pgrx's seeds over an explicit byte encoding:
UTF-8 text for `Thing`, and the little-endian fields for `PgVarlenaThing`.

## Deliberate differences

- **Type text.** Serde writes pgrx's `Thing(String)` as the bare string `"a"`.
  Ankus's generated contract writes the record member by name, so the text is
  `{"Value":"a"}`. `mytype` keeps pgrx's `{"value":1}` through
  `[JsonPropertyName("value")]`.
- **Packed layout.** pgrx's `#[repr(C)]` struct has seven padding bytes after
  `d`, so its payload is 32 bytes. Ankus native layout requires `Pack = 1` and
  stores exactly 25 bytes. `pg_column_size` of a computed value is 29, including
  the four-byte header.
- **Text input.** pgrx's parser panics (`XX000`) on malformed input and ignores
  fields after the fourth and bytes after the fifth. This sample requires exactly
  four fields and five bytes and reports SQLSTATE `22P02`. It also accepts the
  spaces after the commas that its output contains, so values round-trip through
  text, `COPY` and `pg_dump`. pgrx's input rejects its own output.
- **Hash values.** pgrx hashes the Rust `Hash` encoding, which adds type-specific
  bytes. The generated hash values therefore differ from pgrx's, although both
  are stable. Rebuild hash indexes when moving data between the two.
- **Relocation.** pgrx's control file disables relocation. This extension has
  no fixed schemas or custom SQL, so Ankus marks it relocatable.
