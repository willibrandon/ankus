---
title: Catalog name lookups
description: Resolve PostgreSQL type syntax and exact operator names through the native catalog rules.
---

`PgTypes` and `PgQualifiedNameBuilder` resolve current catalog identities inside
an extension callback. PostgreSQL performs the lookup, including search-path
selection, schema permissions and native error reporting. Results are copied
unsigned OIDs; lookups do not retain catalog pins or cache identities across DDL.

## Type syntax

```csharp
uint integer = PgTypes.GetOid("integer");
uint array = PgTypes.GetOid("integer[]");
uint domain = PgTypes.GetOid("\"My Schema\".\"My Type\"");
```

`GetOid` calls PostgreSQL's `regtypein`, corresponding to pgrx's `regtypein`
wrapper. It accepts the server's full type-name syntax: aliases such as
`double precision`, quoted identifiers, schema qualification, arrays and type
modifiers. Modifiers do not change the resolved OID: `numeric(12,3)` resolves
to `numeric`. Domains retain their own identity, and pseudotypes such as
`record` are valid lookup results.

A dash (`"-"`) or numeric zero returns OID zero. Numeric OIDs are accepted
without proving that a matching type exists. Resolving a name does not establish
that Ankus has a managed conversion for that type.

Missing or malformed names raise `PgException` with PostgreSQL's diagnostic.
Null input, embedded zero characters and invalid UTF-16 are rejected in managed
code. UTF-8 transport converts to the server encoding without replacing
unrepresentable characters.

`GetOidByManagedName<T>()` supplies `typeof(T).Name` to the same parser. This
corresponds to pgrx's `rust_regtypein<T>` convenience: it is useful when a SQL type
intentionally follows the managed type's short name. It omits namespaces and
declaring types, and preserves CLR array suffixes and generic arity markers.
PostgreSQL applies its normal unquoted-name rules.

This helper does not consult `[PgType]`, `[PgEnum]` or `[PgDatumType]` mappings,
infer declared schemas, or translate CLR primitive names. For example,
`GetOidByManagedName<int>()` looks up `Int32`, which PostgreSQL folds to `int32`;
use `GetOid("integer")` for PostgreSQL's built-in integer type. Use explicit SQL
syntax when the managed and database names differ.

## Qualified operator names

```csharp
PgQualifiedNameBuilder name = new() { "pg_catalog", "+" };
uint integer = PgTypes.GetOid("integer");
uint operation = name.GetOperatorOid(integer, integer);

uint prefixMinus = new PgQualifiedNameBuilder()
    .Add("pg_catalog").Add("-").GetOperatorOid(0, integer);
```

`Add` accepts one exact, unquoted catalog component and returns the same builder.
Dots, whitespace, case and quote characters remain part of that component;
Ankus does not parse SQL quoting or split a component at dots. For a schema named
`My.Schema`, add `"My.Schema"`, followed by the operator token. Do not add SQL
quote characters around the schema name.

The builder is a reusable managed `IReadOnlyList<string>`. Construction, indexing
and enumeration need no backend. Failed text validation leaves its components
unchanged. It is not thread-safe, and lookup requires the active backend thread.

`GetOperatorOid` calls native `OpernameGetOprid` with exact argument type OIDs:

| Components | Resolution |
| --- | --- |
| Operator | Current `search_path`, including native implicit `pg_catalog` rules |
| Schema, operator | Only the named schema |
| Database, schema, operator | Requires the current database, then resolves the schema |

The lookup performs no argument coercion. Pass zero as the left type for a prefix
operator. Zero as the right type permits legacy postfix lookup on servers that
support it. The raw name `!=` is not rewritten to `<>` by this API.

Missing operators and missing explicit schemas return zero. An invalid component
count, a foreign database, or missing schema `USAGE` permission raises
`PgException`. Unqualified operator lookup excludes the temporary namespace;
an explicit temporary schema follows PostgreSQL's native lookup behavior.

Every call reuses the builder's current components and observes the current
catalog and search path. Native list nodes and encoded strings live only in the
guarded operation's temporary context. Errors unwind below managed frames, so
ordinary `catch` and `finally` blocks can run and the same backend can recover.

For operator declarations, see [operators and casts](/operators-and-casts/).
To invoke functions, see [calling PostgreSQL functions](/calling-functions/).
