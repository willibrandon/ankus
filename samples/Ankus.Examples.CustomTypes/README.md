# Custom types

`Distance` stores signed millimeters and accepts text such as `125mm`.

```sql
CREATE EXTENSION ankus_custom_types;
CREATE TABLE measurements(value distance);
INSERT INTO measurements VALUES ('125mm'), (NULL);
SELECT value FROM measurements;
```

`DistanceCodec` defines the SQL text format and the bytes stored in each row.
