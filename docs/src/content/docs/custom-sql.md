---
title: Custom SQL
description: Include installation SQL strings and files, replace function SQL, and order declarations with dependencies.
---

## Include SQL text

Use an assembly-level `PgSql` attribute for installation SQL. Give each block a
unique dependency name:

```csharp
using Ankus;

[assembly: PgSql("report-table", """
    CREATE TABLE reporting.reports(id bigint PRIMARY KEY, title text NOT NULL);
    """, Requires = ["report-schema"])]

[PgSchema("reporting", Id = "report-schema")]
public static class Reports
{
    [PgFunction(Id = "report-count", Requires = ["report-table"])]
    public static long Count() => Spi.ExecuteScalar<long>("SELECT count(*) FROM reporting.reports");
}
```

Assembly attributes go after `using` directives and before namespace or type
declarations. SQL is trusted extension source: PostgreSQL executes it when
`CREATE EXTENSION` runs. Include the statement terminators yourself.

PostgreSQL records objects created by the script as extension members. A failed
installation rolls back its objects and data changes.

## Replace function SQL

Set `PgFunction.Sql` to replace a function's installation declaration with a
compile-time string. Native wrappers and managed argument/result conversion are
still generated:

```csharp
[PgFunction(Id = "increment", Sql = """
    CREATE FUNCTION increment(integer) RETURNS integer
    AS '@MODULE_PATHNAME@', '@FUNCTION_NAME@'
    LANGUAGE c IMMUTABLE STRICT PARALLEL SAFE;
    COMMENT ON FUNCTION increment(integer) IS 'Adds one with overflow checking';
    """, SqlRelocatable = true)]
public static int Increment(int value) => checked(value + 1);
```

The replacement supports two literal substitutions:

| Token | Replacement |
|---|---|
| `@FUNCTION_NAME@` | This method's generated native PostgreSQL entry point, including its unique overload identity |
| `@MODULE_PATHNAME@` | `MODULE_PATHNAME`, which PostgreSQL resolves from the extension control file during installation |

Include SQL quotes as shown above. Substitution applies to every occurrence,
including comments and quoted text; other tokens and braces remain literal.
The replacement supplies the complete SQL, including names, argument defaults,
planner options and statement terminators. Other function attributes do not
rewrite its text. Its declarations must match the managed wrapper's argument,
result and NULL contracts. Existing managed-signature validation still applies.

`Sql = null` preserves ordinary generation. Empty, whitespace-only and
comment-only strings are valid replacements and do not restore the default SQL.
NUL characters and invalid Unicode are rejected with `ANKUS005`; PostgreSQL
checks SQL syntax and database object definitions during installation.

These options also apply to SETOF/TABLE, `[PgTrigger]`, `[PgEventTrigger]`, and
aggregate support methods carrying `[PgFunction]`. A trigger replacement defines
the trigger **function**; use custom SQL to attach it to a table or database event.
An aggregate helper replacement affects that helper independently, while the
aggregate declaration still references its configured SQL name.

When a method also carries `[PgOperator]` or `[PgCast]`, its `Sql` replaces the
backing function and those attached declarations together. Supply every object
that downstream declarations need in the replacement or in ordered custom SQL.
The function, operator and cast dependency IDs remain available. Their external
prerequisites, including `Before` constraints targeting attached IDs, precede the
whole replacement; consumers follow it. A dependency that requires external SQL
between the backing function and its attached operator cannot be satisfied inside
one replacement and produces a cycle diagnostic. Ordinary generated declarations
retain their separate ordering.

Replacement SQL makes the extension non-relocatable unless every replacement
sets `SqlRelocatable = true`. This is an assertion that the SQL supports moving
the extension; it does not rewrite schema names. Fixed schemas and other custom
SQL blocks can still prevent relocation.

For pgrx users, these controls provide the function behavior of boolean/string
`sql` options. C# string literals also supply the SQL that Rust can place in a
`pgrxsql` documentation fence. Generation reads constants without executing
extension code. Type, aggregate-declaration and generated operator-class SQL
overrides are not yet exposed.

## Disable function SQL

Set `GenerateSql = false` to retain the managed method and native exports without
installing its function, attached operators or attached casts:

```csharp
[PgFunction(GenerateSql = false, Id = "optional-function")]
public static int OptionalFunction(int value) => checked(value * 2);
```

The declaration remains a dependency target and retains its prerequisites.
Dependent declarations are still generated, so provide any SQL objects they need
through explicitly ordered custom SQL. The generated `exports.txt` records native
entry points when a wrapper needs manual SQL registration.

`GenerateSql = false` cannot be combined with a non-null `Sql`, including empty
text. `SqlRelocatable` is consulted only for replacement strings; disabling SQL
does not by itself prevent relocation.

## Include a file

Add SQL files to the extension project's compiler inputs:

```xml
<ItemGroup>
  <AdditionalFiles Include="Sql/seed.sql" />
</ItemGroup>
```

Then reference the project-relative path:

```csharp
[assembly: PgSqlFile("seed-reports", "Sql/seed.sql", Requires = ["report-table"])]
```

Paths can also be absolute. The SDK supplies the project directory to the
generator; only files registered as `AdditionalFiles` can be included. Changes
to those files trigger generation and appear in the next published installation
script. The extension does not read them at runtime.

## Order declarations

`Requires` names declarations that must run first. `Before` names declarations
that must run afterward:

```csharp
[assembly: PgSql("report-view", """
    CREATE VIEW reporting.report_total AS SELECT reporting.count() AS total;
    """, Requires = ["report-count", "seed-reports"])]
```

SQL block names and generated declaration `Id` values share one case-sensitive
namespace. A function's `Id` is separate from its SQL name, so overloads can
have distinct dependency identifiers. `PgSchema.Id` identifies a schema node;
multiple classes declaring the same schema share that node.

Functions automatically depend on their declared schemas. Other relationships
need explicit dependencies: for example, a function default that calls a
SQL-created routine must require the block that creates that routine.

Independent declarations have deterministic output order. Declaration order in
C# files does not establish a SQL dependency.

For SQL that must run before or after everything else, use `Order`:

```csharp
[assembly: PgSql("initialize", """
    CREATE TABLE installation_log(message text);
    """, Order = PgSqlOrder.Bootstrap)]

[assembly: PgSql("finish", """
    INSERT INTO installation_log VALUES ('installed');
    """, Order = PgSqlOrder.Finalize)]
```

There can be one bootstrap block and one final block, including file-based
blocks. Bootstrap precedes generated schemas; final SQL follows all generated
and custom declarations.

`ANKUS005` reports duplicate or missing identifiers, dependency cycles, invalid
ordering options, and missing/unreadable file inputs. SQL syntax, object names,
and privileges are checked by PostgreSQL during installation.

## Relocation

Custom SQL makes the extension non-relocatable by default. Set
`Relocatable = true` on each block only when its objects and references support
moving to a different installation schema:

```csharp
[assembly: PgSql("values", "CREATE TABLE stored_values(value integer);", Relocatable = true)]
```

Every custom block must permit relocation, and fixed `PgSchema` or function
schema declarations still make the extension non-relocatable. Ankus writes
the resulting policy into the extension control file.

## Enum type dependencies

`[PgEnum(Id = "status-type")]` makes an enum available as a named dependency.
Use `Requires = new[] { "status-type" }` on SQL that creates tables or other
objects using it. Function signatures automatically depend on their enum types,
including array parameters and results. Enums can also declare `Requires` for
SQL or schema prerequisites. See [enumerated types](/enums/).
