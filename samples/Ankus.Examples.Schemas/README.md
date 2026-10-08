# Schemas example

This sample ports pgrx's `schemas` example. It places functions and types in
four different schemas:

| Object | Schema |
| --- | --- |
| `hello_default_schema()`, `return_vec_of_customtype()`, `mytype` | The schema selected by `CREATE EXTENSION` |
| `hello_some_schema()`, `mysomeschematype` | `some_schema`, created and owned by the extension |
| `mypgcatalogtype` | PostgreSQL's existing `pg_catalog` |
| `hello_public()` | PostgreSQL's existing `public` |

```sql
CREATE EXTENSION ankus_schemas;

SELECT hello_default_schema();
-- Hello from the schema where you installed this extension
SELECT '{"Value":"test"}'::MyType;
SELECT some_schema.hello_some_schema();          -- Hello from some_schema
SELECT '{"Value":"test"}'::MyPgCatalogType;      -- found through any search_path
SELECT hello_public();                           -- Hello from the public schema

SET search_path TO some_schema, public;
SELECT '{"Value":"test"}'::MySomeSchemaType;
SELECT hello_some_schema();
```

## How pgrx maps to C#

Without a schema declaration, Ankus creates functions and types in the schema
selected by `CREATE EXTENSION`: the first existing schema in `search_path`, or
the schema named by `WITH SCHEMA`.

pgrx's `#[pg_schema] mod some_schema { ... }` becomes a static class marked
`[PgSchema("some_schema")]`. Functions and nested types inherit that schema.
The extension creates the schema and `DROP EXTENSION` removes it. PostgreSQL
schemas do not nest, so a nested `[PgSchema]` class creates another top-level
schema, just as pgrx hoists nested modules.

pgrx's `mod pg_catalog` and `mod public` blocks refer to schemas that already
exist. Use `[PgSchema("pg_catalog", Create = false)]` and
`[PgSchema("public", Create = false)]`: the extension adds members to these
schemas without owning them. Creating objects in `pg_catalog` requires a
superuser, so the control file keeps pgrx's `superuser = true`. Ankus rejects
creating any `pg_`-prefixed schema.

A fixed schema makes the extension non-relocatable. `ALTER EXTENSION ... SET
SCHEMA` fails. Drop the extension and create it again with `WITH SCHEMA` to
choose a different installation schema.

pgrx's README function `#[search_path(@extschema@)]` becomes
`SearchPath = [PgSearchPath.ExtensionSchema]`. PostgreSQL replaces the token
with the actual installation schema when the extension is created, so
`return_vec_of_customtype()` has `search_path=<installation schema>` in
`pg_proc.proconfig`. pgrx needs this path to find the type when returning a
`Vec<T>` of a custom type. Ankus resolves generated type identities from the
extension's catalog schema instead, so this function would work without it. Use
the option when a function's own SQL uses unqualified names from the
extension's schema.

## Deliberate differences

- **Type text.** Serde writes pgrx's single-field tuple structs, such as
  `MyType(String)`, as the bare string `"test"`. Ankus's generated contract
  writes a C# record's members by name, so the text is `{"Value":"test"}`.
  Use a `PgTypeTextCodec<T>` if a type must keep a different text form.
- **Type names.** pgrx uses the Rust identifier unquoted, so PostgreSQL folds
  `MyType` to `mytype`. Ankus defaults to snake case (`my_type`). The sample
  sets `PgType.Name` to keep pgrx's catalog names, so pgrx's SQL works unchanged.
- **Tests.** pgrx places its `#[pg_test]` functions in a `tests` schema inside
  the extension. Ankus's integration tests run the same queries from a client
  against the published extension instead.
