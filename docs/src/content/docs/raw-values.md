---
title: Raw values and custom types
description: Bind raw PostgreSQL values and write custom type input and output functions.
---

Use `PgDatum` with `[PgSqlType]` for a type without a built-in C# mapping:

```csharp
[PgFunction]
[return: PgSqlType("pg_lsn", Schema = "pg_catalog")]
public static PgDatum? Echo(
    [PgSqlType("pg_lsn", Schema = "pg_catalog")] PgDatum? value) => value;
```

```sql
SELECT echo('0/1234'::pg_lsn); -- 0/1234
SELECT echo(NULL::pg_lsn);    -- NULL
```

Every raw parameter and result needs a binding. `Name` is the exact catalog
identifier, such as `int4`, without quotes or a schema prefix. `Schema` selects
a fixed schema; omitting it uses the installation search path. `IsArray = true`
binds an entire array of the named type, preserving shape and NULL cells.

`TypeOid` retains type identity, including domains. `Read<T>()` copies a supported
C# value; `ToPostgresString()` calls the type's output function. A nullable wrapper
accepts SQL NULL. A zero datum is a present value, not NULL.

Inputs belong to the current function call or iterator. Use `CopyTo(context)` to
keep one longer. Returned values must have the declared type and a live owner;
Ankus checks both before using their native storage. These bindings also work
in aggregate support methods, operators, casts, and `IEnumerable<PgDatum?>`
sets. For TABLE results, set `Column` to each raw column's SQL name.

When your extension creates the bound type, declare its SQL block with
`PgSqlTypeProvider` to order these signatures automatically. Type names and
optional schemas match exactly; a provider does not change datum conversion or
ownership. See [custom SQL](../custom-sql/#declare-supplied-types).

## Custom type representation

Create the SQL type and supply its input/output functions. This example stores
an unsigned 24-bit integer directly in PostgreSQL's datum word:

```csharp
using Ankus;
using System.Globalization;

[assembly: PgSql("u24-shell", "CREATE TYPE u24;")]
[assembly: PgSql("u24-type",
    "CREATE TYPE u24 (INPUT=u24_in, OUTPUT=u24_out, LIKE=int4);",
    Requires = ["u24-in", "u24-out"])]
[assembly: PgSqlTypeProvider("u24-type", "u24")]

public static class U24
{
    [PgFunction(Name = "u24_in", Id = "u24-in", Requires = ["u24-shell"])]
    [return: PgSqlType("u24")]
    public static PgDatum Input(
        [PgSqlType("cstring", Schema = "pg_catalog")] PgDatum text,
        PgFunctionContext call)
    {
        if (!uint.TryParse(text.ToPostgresString(), NumberStyles.None,
            CultureInfo.InvariantCulture, out uint value) || value > 0xFFFFFF)
        {
            throw new PgException("22003", "Value must be between 0 and 16777215.");
        }

        return PgDatum.DangerousCreate(value, call.ResultTypeOid, PgMemoryContext.Current);
    }

    [PgFunction(Name = "u24_out", Id = "u24-out", Requires = ["u24-shell"])]
    [return: PgSqlType("cstring", Schema = "pg_catalog")]
    public static PgDatum Output([PgSqlType("u24")] PgDatum value)
        => PgFunctions.CallRaw("pg_catalog.int4out", PgMemoryContext.Current,
            PgFunctionArgument.Create(checked((int)value.DangerousGetBits())));
}
```

```sql
SELECT '16777215'::u24; -- 16777215
```

The provider places other `u24` signatures after the completed type. Its explicit
requirements preserve shell → input/output → completion ordering for the two
functions needed to define the type. Additional consumers do not need to repeat
`Requires = ["u24-type"]`.

`DangerousCreate` requires a representation that matches the SQL type. For a
pointer-based value, its native storage must remain valid for the chosen owner.
Creating a handle does not copy that storage or take ownership of it.
