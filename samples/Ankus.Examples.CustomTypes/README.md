# Custom types

`Distance` stores signed millimeters and accepts text such as `125mm`.

```sql
CREATE EXTENSION ankus_custom_types;
CREATE TABLE measurements(value distance);
INSERT INTO measurements VALUES ('125mm'), (NULL);
SELECT value FROM measurements;
```

`DistanceCodec` defines the SQL text format and the bytes stored in each row.

`Reading` uses `[PgType]` without a codec. Ankus generates CBOR storage and JSON
text I/O, including its immutable constructor and nullable unit:

```sql
CREATE TABLE readings(value reading);
INSERT INTO readings VALUES ('{"Sensor":"outside","Value":19.125,"Unit":"°C"}');
SELECT value FROM readings;
```

`Measurement` declares tagged variants with `JsonDerivedType`. Each variant
retains its inherited sensor identifier and its own fields in JSON and CBOR:

```sql
CREATE TABLE results(value measurement);
INSERT INTO results VALUES
    ('{"kind":"measured","Sensor":"outside","Value":19.125,"Unit":"°C"}'),
    ('{"Sensor":"outside","Reason":"offline","kind":"unavailable"}');
SELECT value FROM results;
```

`RgbColor` uses hexadecimal SQL text with generated CBOR storage. `PackedColor`
uses the same text convention and an explicit sequential, one-byte-packed layout
that stores three payload bytes. Its binary protocol exposes those exact bytes:

```sql
SELECT '#12abef'::rgb_color::text;
SELECT '#12abef'::packed_color::text;
SELECT encode(packed_color_send('#12abef'::packed_color), 'hex'); -- 12abef
```

Native layouts depend on field order and byte order. Changing a layout requires
a data migration. Ordinary managed transport copies the value; `PgVarlena<T>`
provides checked PostgreSQL borrowing and copy-on-write for native layouts.

`OrderedKey` demonstrates generated equality, comparison and default B-tree/hash
operator classes. It preserves the original spelling but folds ASCII letters to
uppercase for equality, ordering and stable hashes. The fixed normalization
does not depend on Unicode table or culture changes:

```sql
CREATE TABLE keys(value ordered_key);
CREATE UNIQUE INDEX keys_unique ON keys(value);
INSERT INTO keys VALUES ('{"Value":"hello"}');
SELECT value = '{"Value":"HELLO"}'::ordered_key FROM keys; -- true
```
