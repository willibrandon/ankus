---
title: Execution and lifetime
description: Understand backend-thread access, owned values, and error recovery.
---

Your function runs synchronously inside the PostgreSQL backend that called it.
SPI, quoting, and logging APIs must run on that thread. Calling them from
`Task.Run` throws `InvalidOperationException`.

`[PgInitialize]` runs when the library first loads in a backend, before its SQL
functions. Successful initialization and managed static state belong to that
backend process. Failed initialization can be retried; PostgreSQL rolls back SQL
work with its transaction, while managed mutations remain. See
[extension initialization](/initialization/) for preload and transaction rules.

## Values

Strings, byte arrays, JSON values, and SPI result rows are managed copies. You
can keep them after the function returns. Editing a `SpiRow` changes that copy;
use SQL to update the database.

## Server resources

A `SpiSession` is valid only inside its `Spi.Connect` callback. Nested sessions
temporarily suspend access to the enclosing session.

Prepared statements can outlive a callback. A session-owned statement needs
`Keep()` before the session closes. Dispose it on the backend thread when it is
no longer needed.

Cursors normally end with their transaction. `Detach()` transfers responsibility
for closing the cursor; it does not extend the PostgreSQL portal's lifetime.

`PgMemoryContext` and `PgAllocation` follow PostgreSQL's native ownership tree.
A live handle can be reused by a later synchronous callback from the same
extension and backend. Reset, parent deletion, and transaction cleanup invalidate
affected handles; checked access then throws before dereferencing freed storage.
Dispose owned resources on the backend thread. `Run` temporarily selects a
context and restores the previous one even when the callback throws. See
[memory contexts](/memory-contexts/) for reset variants and allocation rules.

Cursors opened in an SPI environment containing trigger transition tables close
when that trigger callback ends, including detached cursors. Retained plans
resolve transition tables from the current invocation; they do not preserve
previous transition rows. Owned `PgTriggerContext` metadata and fetched rows
survive callback completion. See [triggers](/triggers/).

Event trigger metadata and DDL/drop/rewrite snapshots also remain owned after
callback completion. Query their helpers only through the current invocation's
context and in the matching event phase. Nested callbacks restore their parent
context before returning. See [event triggers](/event-triggers/).

## Errors

An unhandled managed exception becomes PostgreSQL ERROR after `finally` blocks
and `using` scopes finish.

SPI calls use internal subtransactions. A failed call rolls back its work before
throwing `PgException`, allowing your function to catch it and continue. Successful
calls remain part of the caller's transaction.
