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
