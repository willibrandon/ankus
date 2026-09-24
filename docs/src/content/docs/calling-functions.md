---
title: Calling PostgreSQL functions
description: Call built-in and extension functions by name, catalog OID, or native entry point.
---

Use `PgFunctions.Call<T>` inside an extension function to call a PostgreSQL
function without writing a SQL query:

```csharp
int positive = PgFunctions.Call<int>(
    "pg_catalog.abs", PgFunctionArgument.Create(-42));
```

This works with built-ins and functions written in C#, SQL, PL/pgSQL, or other
PostgreSQL languages. Names follow PostgreSQL's quoting, overload, and search-path
rules. You can also pass a function's catalog OID instead of its name.

`T` must match the result type. Use a nullable type when the result can be NULL:

```csharp
int? result = PgFunctions.Call<int?>(
    "pg_catalog.abs", PgFunctionArgument.Create<int?>(null));
```

Strict functions are not invoked when an argument is NULL. For a function
returning `void`, use `PgFunctions.Call` without a type argument. These methods
return one value; use `Spi.Query` to consume a set of rows.

## Default arguments

Omit trailing defaults or request one explicitly:

```csharp
int price = PgFunctions.Call<int>("discount_price",
    PgFunctionArgument.Create(100),
    PgFunctionArgument.Default<int>());
```

The default's type helps select the correct overload. PostgreSQL evaluates the
declared default expression during the call, including expressions such as
`nextval(...)`. A NULL value and a request for the default are distinct.

## Collation and variadic arguments

Pass `PgFunctionCallOptions` when a call needs a particular collation:

```csharp
[PgFunction]
public static string Lower(string value, PgFunctionContext call)
    => PgFunctions.Call<string>("pg_catalog.lower",
        new PgFunctionCallOptions { CollationOid = call.CollationOid },
        PgFunctionArgument.Create(value));
```

Without an override, PostgreSQL determines collation from argument types. Zero
explicitly selects no collation.

A named variadic call accepts individual arguments. To pass the final array
directly, set `Variadic = true`, matching SQL's `VARIADIC` keyword. Calls by OID
always use the declared parameter list, including any variadic array.

## Raw values

Use `CallRaw` for exact type OIDs, domains, or types without a managed mapping:

```csharp
using PgMemoryContext owner = PgMemoryContext.Create("function result");
PgDatum value = PgFunctions.CallRaw("current_status", owner);
string? text = value.ToPostgresString();
```

The result belongs to `owner` and expires when it resets or is deleted. A managed
read such as `Read<int>()` or `ToPostgresString()` returns an independent value.
Pass a raw input with `PgFunctionArgument.Create(datum)`. To reuse a parameter
with an explicit composite descriptor, use `PgFunctionArgument.Create(parameter)`.

## Errors and permissions

Calls use the current role and honor `EXECUTE` permissions, security-definer
functions, and function-local settings. A PostgreSQL error becomes a `PgException`;
the failed call's database changes roll back, and the caller can catch the error
and continue. Managed results are copied before native execution storage is freed.

## Native entry points

`DangerousCall<T>` and `DangerousCallRaw` call a native PostgreSQL version-1
function address with `PgDatum` arguments and an explicit collation. SQL NULL
remains separate from a zero datum. PostgreSQL errors still become `PgException`.

These APIs require a valid address and exact argument/result representations.
They supply null `flinfo`, `context`, and `resultinfo` fields to the native
function. Use name or OID calls when the function needs catalog metadata.
Copying a pointer-bearing value such as `internal` preserves its pointer; it
does not copy the pointed-to object or extend that object's lifetime.
