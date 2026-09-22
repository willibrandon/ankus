---
title: Custom SQL
description: Include installation SQL strings and files, with dependencies on generated declarations.
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
