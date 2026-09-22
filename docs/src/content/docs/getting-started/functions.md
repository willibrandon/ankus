---
title: Write a function
description: Declare C# methods, map PostgreSQL types, and handle SQL NULL.
---

Mark a synchronous static method with `[PgFunction]`:

```csharp
using Ankus;

public static class Functions
{
    [PgFunction]
    public static string Greet(string name) => $"Hello, {name}!";
}
```

The method is exposed as `greet(text)`. Names use snake case by default.
Ankus generates the native entry point and SQL declaration during publishing.

## Types

| C# | PostgreSQL |
| --- | --- |
| `bool` | `boolean` |
| `sbyte` | `"char"` (the internal signed byte type) |
| `short`, `int`, `long` | `smallint`, `integer`, `bigint` |
| `uint` | `oid` |
| `float`, `double` | `real`, `double precision` |
| `string` | `text` |
| `byte[]` | `bytea` |
| `Guid` | `uuid` |
| `PgJson`, `PgJsonb` | `json`, `jsonb` |
| `void` result | `void` |

Text and binary inputs are managed copies. They remain valid after PostgreSQL
releases the original storage.

## SQL NULL

Use nullable C# types to receive SQL NULL:

```csharp
[PgFunction]
public static string GreetOrDefault(string? name) => $"Hello, {name ?? "world"}!";
```

If every parameter is required, the generated SQL function is `STRICT`:
PostgreSQL returns NULL without calling the method when any argument is NULL.

For mixed signatures, a NULL required argument still returns NULL without
entering the method. Nullable arguments reach your code. A null result becomes
SQL NULL.

## Errors

An unhandled managed exception becomes PostgreSQL ERROR. The managed stack
unwinds first, so `finally` blocks and `using` scopes run normally.

Use [`PgException`](/logging/#errors) when the error needs a particular SQLSTATE,
detail, or hint.
