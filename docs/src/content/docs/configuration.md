---
title: Configuration settings
description: Declare PostgreSQL settings with native storage and typed C# getters.
---

Declare a setting as a static partial getter in a public or internal partial class:

```csharp
public static partial class Settings
{
    [PgGucBool("my_extension.enabled", true, "Enable the extension")]
    public static partial bool Enabled { get; }

    [PgGucInt("my_extension.limit", 100, "Maximum items", Minimum = 1, Maximum = 10000)]
    public static partial int Limit { get; }

    [PgGucString("my_extension.label", null, "Optional label")]
    public static partial string? Label { get; }
}
```

The source generator creates native PostgreSQL storage and implements the getters.
PostgreSQL registers the settings when it loads the library, before calling an
optional `[PgInitialize]` method. Read a getter from an extension callback to obtain
the current native value. Reads outside a backend callback or on another thread
throw `InvalidOperationException`.

Use ordinary SQL configuration commands to change values:

```sql
SET "my_extension.limit" = '250';
SHOW "my_extension.limit";
BEGIN;
SET LOCAL "my_extension.limit" = '50';
ROLLBACK;
RESET "my_extension.limit";
```

PostgreSQL owns parsing, permissions, source priority, transaction and savepoint
restoration, function `SET` clauses, reset values, and configuration reloads.
Custom placeholders set before the library loads are adopted by the native
definition. Invalid placeholder values produce PostgreSQL's diagnostics.
Registration survives transaction rollback and `DROP EXTENSION`; dropping SQL
objects does not unload a library or unregister its settings.

## Types and metadata

| Attribute | Property type | Additional metadata |
| --- | --- | --- |
| `PgGucBool` | `bool` | Native Boolean spelling and synonyms |
| `PgGucInt` | `int` | Inclusive `Minimum` and `Maximum` |
| `PgGucReal` | `double` | Inclusive `Minimum` and `Maximum` |
| `PgGucString` | `string` or `string?` | Nullable boot value requires a nullable property |
| `PgGucEnum` | A C# enum | Optional `PgGucLabel` names and hidden labels |

Every declaration accepts a name, a constant default value, and a short
description. Optional properties include `LongDescription`, `Context`, `Flags`,
`Unit`, `Check`, `Assign`, and `Show`. Names must be qualified PostgreSQL parameter
names, such as `my_extension.limit`. Invalid declarations report `ANKUS014`.

Strings are owned managed copies. A null boot/reset string remains distinct from
an empty string in the typed getter, although ordinary `SHOW` displays both as
empty text. Nonnullable string properties require a nonnull default and cannot
accept a null result from a check hook.

GUC enums are independent of SQL catalog enums:

```csharp
public enum Mode : ulong
{
    [PgGucLabel("normal")]
    Normal = ulong.MaxValue,

    [PgGucLabel("experimental", Hidden = true)]
    Experimental = 1UL << 63,
}

public static partial class Settings
{
    [PgGucEnum("my_extension.mode", Mode.Normal, "Execution mode")]
    public static partial Mode Mode { get; }
}
```

Generated ordinal mappings preserve the managed enum's underlying values.
Aliases share an ordinal, and the first declared label is the canonical display
name. Native label matching is case insensitive. Hidden labels remain accepted
inputs but are omitted from available-value hints and `pg_settings.enumvals`.

`PgGucContext` expresses PostgreSQL's seven contexts: `Internal`, `Postmaster`,
`Sighup`, `SuperuserBackend`, `Backend`, `SuperuserSet`, and `UserSet`. Native
privilege checks remain authoritative, including parameter privileges where the
server supports them.

Combine `PgGucOptions` values through the attribute's `Flags` property. These
include listing/reset behavior, client reporting, restricted visibility,
identifier-sized strings, configuration-file restrictions, security restrictions,
and EXPLAIN settings. `RuntimeComputed` requires PostgreSQL 15 or later.
`PgGucUnit` separately selects one memory or time unit for numeric settings;
block conversions use the selected server's compiled block sizes.

Options map to the native flags rather than adding an independent policy. In
PostgreSQL 18, `DisallowInFile` blocks `ALTER SYSTEM` and affects configuration
help, but does not itself reject a custom `UserSet` parameter supplied manually
in a configuration file. Choose the appropriate context and check hook for
additional restrictions.

## Hooks

Name an accessible synchronous static hook in the property's containing class:

```csharp
public static partial class Settings
{
    [PgGucInt("my_extension.batch", 10, "Batch size", Check = nameof(CheckBatch))]
    public static partial int Batch { get; }

    internal static PgGucCheckResult<int> CheckBatch(int proposed, PgGucSource source)
    {
        if (proposed <= 0)
        {
            return new(new PgGucCheckError("Batch size must be positive."));
        }

        return new(proposed);
    }
}
```

For a property of type `T`, hook signatures are:

```csharp
PgGucCheckResult<T> Check(T proposed, PgGucSource source)
void Assign(T accepted, PgGucExtra? extra)
string Show(T current, PgGucExtra? extra)
```

A check result can accept a normalized value with optional `PgGucExtra`, or reject
it with `PgGucCheckError`. Native numeric bounds validate the proposed input;
PostgreSQL does not revalidate a numeric replacement accepted by the hook.
Extra data is an immutable copied byte sequence; null
and empty data are distinct. PostgreSQL retains a flat native allocation for the
accepted value and its reset/transaction history. Assign and show receive owned
managed copies which can outlive that native allocation.

Checks can run for boot defaults, file reloads, startup options, ordinary `SET`,
and validation-only operations such as `ALTER ROLE` or function `SET` clauses.
Validation does not guarantee assignment. A rejected result preserves native
severity selection: an interactive change can raise ERROR while placeholder
adoption can warn. A null diagnostic message keeps PostgreSQL's default message.
Thrown managed exceptions are contained and converted to native check diagnostics.

Assign runs before PostgreSQL changes the backing value. Reading the property in
assign returns the old value. Restoration calls assign with previously accepted
extra data without running check again. Assign hooks must not fail: an unexpected
exception terminates that backend with FATAL, since continuing could leave
partially restored state.

Show changes display text without changing typed storage or boot/reset metadata.
It must return a nonnull string. A show failure raises ERROR during a transaction;
outside a transaction, such as client parameter reporting, it terminates the
backend with FATAL.

Typed setting reads are available in every hook. Check hooks can use guarded SQL
when PostgreSQL has a valid transaction. Assign and show cannot use SQL, including
during ordinary `SET`, because PostgreSQL also calls them during restoration and
outside transactions. The current callback binding also makes `PgLog` unavailable
in those phases.

## Preloading

Native declarations without managed initialization or hooks can be registered
through `shared_preload_libraries`. PostgreSQL's postmaster executes only native
registration, and managed code starts separately when a backend first calls it.
`Postmaster` settings require this loading mode. A late load is rejected before
PostgreSQL's fatal late-registration path.

See the [configuration sample](https://github.com/willibrandon/ankus/tree/main/samples/Ankus.Examples.Configuration)
for contexts, units, and typed getters. Its startup-only declaration requires:

```ini
shared_preload_libraries = 'Ankus.Examples.Configuration'
ankus_configuration.startup = 11
```

Shared-preload names, descriptions, labels, and string defaults currently must be
ASCII: the postmaster has no database encoding to use for inherited metadata.
Backend registration supports server-encoded text. Libraries containing managed
hooks or `[PgInitialize]` are rejected before managed entry in a forking postmaster;
use backend loading or `session_preload_libraries` for those libraries. Managed
postmaster hooks, non-ASCII shared-preload metadata, prefix reservation, and the
raw placeholder flag remain tracked full-port work in `PROGRESS.md`.
