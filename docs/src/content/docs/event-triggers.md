---
title: Event triggers
description: Handle PostgreSQL DDL, dropped objects, table rewrites, and login events with owned metadata.
---

Use `[PgEventTrigger]` on a synchronous static method returning `void` with one
nonnull `PgEventTriggerContext` parameter. Ankus generates a zero-argument SQL
function returning `event_trigger`. Add `[PgFunction]` to configure its name,
schema, security, search path, and dependency identifiers.

```csharp
[PgEventTrigger]
[PgFunction(Id = "report-table-creation-function")]
public static void ReportTableCreation(PgEventTriggerContext context)
{
    foreach (PgDdlCommand command in context.GetDdlCommands())
    {
        if (command.ObjectType == "table")
        {
            PgLog.Write(PgLogLevel.Notice, $"{command.CommandTag}: {command.ObjectIdentity}");
        }
    }
}
```

Attach the callback with PostgreSQL SQL, optionally in a dependency-ordered
installation block:

```csharp
[assembly: PgSql("report-table-creation", """
    CREATE EVENT TRIGGER ankus_report_table_creation ON ddl_command_end
    WHEN TAG IN ('CREATE TABLE') EXECUTE FUNCTION report_table_creation();
    """, Requires = ["report-table-creation-function"], Relocatable = true)]
```

The `Ankus.Examples.EventTriggers` sample uses this attachment; see also
[custom SQL](/custom-sql/). Event triggers have database-wide names and scope;
PostgreSQL requires a superuser to create them. Their function bindings follow
extension relocation, and extension-owned event triggers are removed on uninstall.
PostgreSQL handles tag filters, alphabetical firing order, and enable/replica/always
settings. Event-trigger DDL itself does not invoke event triggers.

## Events and helpers

`context.Kind` is a `PgEventTriggerKind`. `Event` preserves PostgreSQL's event name,
and `CommandTag` preserves its command tag, such as `CREATE TABLE` or `ALTER TABLE`.

| Kind | Event | Timing and available helper |
|---|---|---|
| `DdlCommandStart` | `ddl_command_start` | Before DDL execution and object-existence checks |
| `DdlCommandEnd` | `ddl_command_end` | After successful DDL, before commit; `GetDdlCommands()` |
| `SqlDrop` | `sql_drop` | After catalog deletion, before command end; `GetDroppedObjects()` |
| `TableRewrite` | `table_rewrite` | Before an eligible table rewrite; `GetTableRewrite()` |
| `Login` | `login` | On a new connection after authentication; PostgreSQL 17+ |

Event triggers apply to the commands supported by PostgreSQL, including some
`ALTER` commands that drop objects. A command that fails does not run its end
handlers. A metadata-only alteration need not rewrite a table; `CLUSTER` and
`VACUUM` do not invoke `table_rewrite` handlers.

`Rows`, `SetMode`, SQL parameter/result binding attributes, and ordinary `SELECT`
invocation do not apply to event callbacks. Row triggers use the separate
[`PgTriggerContext` API](/triggers/).

## Owned command and object metadata

`GetDdlCommands()` returns immutable `PgDdlCommand` snapshots. One statement can
produce several base commands, and a successful no-op can produce an empty list.
Each snapshot includes the catalog class/object/subobject identity, command tag,
object type, optional schema and object identity, and `InExtension`. The latter
identifies commands executed inside an extension script. GRANT, REVOKE, and
default-privilege commands can have null catalog identities and names; zero and
an empty string are distinct values.

`GetDroppedObjects()` preserves every reported object, including implicit
dependencies. `Original` and `Normal` describe separate dependency properties.
The snapshots include `IsTemporary`, object identity, and ordered `AddressNames`
and `AddressArguments`. Optional values remain null, and empty argument lists
remain empty. Temporary names use PostgreSQL's canonical `pg_temp` representation.
Dropped metadata remains useful after the catalog entries have disappeared.

`GetTableRewrite()` returns a `PgTableRewrite` with `TableOid` and a `Reason`
bitmap. `PgTableRewriteReason` defines `AlterPersistence`, `DefaultValue`,
`ColumnRewrite`, and `AccessMethod`. Multiple reasons can be combined; the numeric
bitmap preserves additional bits supplied by the server.

These helpers project PostgreSQL's public metadata columns. They do not expose
the internal parse tree or opaque `pg_ddl_command` object. The native callback
does not supply the fired event trigger's name or OID.

## Lifetime and errors

Context properties and returned snapshots own their managed data. They remain
readable after SPI disconnects, nested DDL, callback return, or transaction end.
Address collections cannot be mutated through their public views.

Call each helper on the current invocation's context and only in its matching
event phase. Calling through an old context, a suspended outer event context,
or a worker thread throws `InvalidOperationException`. Nested event handlers
restore their enclosing context when they finish. An event callback entered
from a row trigger has its own SPI environment; the outer row trigger's
transition tables become available again when control returns to it.

Throw `PgException` to reject an operation with a PostgreSQL SQLSTATE and
diagnostics. Unhandled managed exceptions are reported through the same guarded
native boundary. An error in an end or drop handler rolls back the DDL and its
transactional side effects. Guarded SPI errors can be caught in the callback
when the operation should recover and continue.

Login handlers run for physical connections and can prevent connection startup
if they fail. Keep a working administrative connection while developing one.
They also run on standby servers, so account for read-only operation when writing
a login handler. The sample uses DDL events and does not install a login handler.
