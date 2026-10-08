# Composite types example

This sample ports pgrx's `composite_type` example. Custom SQL creates three
composite types, and C# functions, an operator and an aggregate use them as
arguments and results:

```sql
CREATE EXTENSION ankus_composites;

SELECT create_dog('Nami', 0);                                   -- (Nami,0)
SELECT scritch_dog(ROW('Nami', 1)::Dog);                         -- (Nami,1)
SELECT make_friendship(ROW('Nami', 0)::Dog, ROW('Sally', 0)::Cat);
-- ("(Sally,0)","(Nami,0)")
SELECT (ROW('Nami', 0)::Dog + 1).scritches;                      -- 1
SELECT sum_scritches(d)
FROM (VALUES (ROW('Nami', 0)::Dog), (ROW('Brandy', 42)::Dog)) AS dogs(d);  -- 42
```

The sample also shows composite arrays, sets and anonymous records:

```sql
SELECT * FROM scritch_repeatedly(ROW('Nami', 3)::Dog, 3);   -- Nami 4, Nami 5, Nami 6
SELECT echo_dogs(ARRAY[NULL, ROW('Bo', 4)::Dog]);         -- {NULL,"(Bo,4)"}
SELECT * FROM make_record('Nami', 7) AS r(name text, scritches integer);
```

## How pgrx maps to C#

pgrx's `extension_sql!(..., bootstrap)` becomes an assembly-level `[PgSql]`
with `Order = PgSqlOrder.Bootstrap`. It creates `Dog` and `Cat` before every
generated declaration. The friendship type is in an ordinary block named
`create_cat_and_dog_friendship`. As in pgrx, `make_friendship` requires that
block by name, so its type exists before the function is created.

pgrx's `composite_type!("Dog")` becomes a `PgHeapTuple` parameter or result
bound with `[PgCompositeType("dog")]`. pgrx's `DOG_COMPOSITE_TYPE` constant
becomes `CompositeTypes.Dog`. PostgreSQL folds the unquoted names in
`CREATE TYPE Dog` to lowercase, and the attribute names the exact catalog
type, so the constants are lowercase. `get_by_name` and `set_by_name` become
`Get<T>` and `Set<T>`. A nested composite field is read and written as
another `PgHeapTuple`.

pgrx's `PgHeapTuple::new_composite_type("Dog")` looks the type up by name
through `search_path`. The C# functions that create tuples use the type
PostgreSQL resolved for their own result, `PgFunctionContext.ResultTypeOid`.
They therefore work when the extension is installed in a schema that is not
on the caller's `search_path`, and after `ALTER EXTENSION ... SET SCHEMA`.
The other functions copy their input tuple with `Clone()`.

pgrx's `#[pg_operator] #[opname(+)]` becomes `[PgOperator("+")]` on the
backing method `add_scritches_to_dog`.

## Deliberate differences

- **`scritch_dog`.** pgrx's comment describes adding one scritch, but its code
  reads the count and writes the same count back, and its test expects the
  input count. The port keeps that observable behavior. Use the `+` operator
  to add scritches.
- **`sum_scritches`.** pgrx describes this aggregate but leaves its
  implementation commented out as not working. Ankus supports composite
  aggregate inputs, so the sample implements the described aggregate: integer
  state, initial condition `0`, and a strict transition that skips NULL dogs. Like
  pgrx's `+` operator, a NULL count inside a present dog adds zero.
- **Overflow.** Additions use checked arithmetic. Overflow raises SQLSTATE
  `38000` with .NET's overflow message. pgrx's `i32` addition panics with
  `XX000` in the debug builds that `cargo pgrx test` uses and wraps in release
  builds.
- **Relocation.** pgrx's control file disables relocation. The custom SQL uses
  unqualified names and the functions resolve their types through PostgreSQL,
  so this extension remains relocatable.
- **Additional functions.** `scritch_repeatedly`, `echo_dogs` and `make_record`
  are Ankus additions that cover composite sets, arrays and anonymous records.
