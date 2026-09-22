---
title: Execution and lifetime
description: Understand backend-thread access, owned values, and error recovery.
---

Your function runs synchronously inside the PostgreSQL backend that called it.
SPI, quoting, and logging APIs must run on that thread. Calling them from
`Task.Run` throws `InvalidOperationException`.

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

Cursors opened in an SPI environment containing trigger transition tables close
when that trigger callback ends, including detached cursors. Retained plans
resolve transition tables from the current invocation; they do not preserve
previous transition rows. Owned `PgTriggerContext` metadata and fetched rows
survive callback completion. See [triggers](/triggers/).

## Errors

An unhandled managed exception becomes PostgreSQL ERROR after `finally` blocks
and `using` scopes finish.

SPI calls use internal subtransactions. A failed call rolls back its work before
throwing `PgException`, allowing your function to catch it and continue. Successful
calls remain part of the caller's transaction.
