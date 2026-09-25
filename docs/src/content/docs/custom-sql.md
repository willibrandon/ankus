---
title: Custom SQL
description: Include installation SQL strings and files, replace generated declarations, and order dependencies.
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

## Declare supplied types

Use `PgSqlTypeProvider` to identify a type created by a `PgSql` or `PgSqlFile`
block. Functions using that exact `PgSqlType` or `PgCompositeType` binding then
depend on the block automatically:

```csharp
using Ankus;

[assembly: PgSql("pair-definition", """
    CREATE TYPE reporting.pair AS (number integer, label text);
    """)]
[assembly: PgSqlTypeProvider("pair-definition", "pair", Schema = "reporting")]

[PgSchema("reporting")]
public static class Pairs
{
    [PgFunction]
    [return: PgCompositeType("pair", Schema = "reporting")]
    public static PgHeapTuple Echo(
        [PgCompositeType("pair", Schema = "reporting")] PgHeapTuple value) => value;
}
```

The provider block also follows a matching declared schema. Repeat the attribute
when one block supplies several types. Its first argument must name a SQL block,
including a tracked SQL file, rather than a function or schema dependency alias.

Type and schema names are exact, unquoted catalog identifiers. A dot or quote
inside `Name` is part of that one identifier. Matching is case-sensitive and
does not normalize aliases such as `integer` and `int4`. Omitting `Schema`
matches only bindings that also omit it, using the installation search path;
it does not inherit the consuming function's schema. A fixed provider schema
prevents relocation. Unqualified providers still need the SQL block's
`Relocatable = true` assertion to permit relocation.

Scalar parameters and results, SETOF results, individual TABLE columns,
aggregate helpers, operators and casts receive these dependencies. Raw whole
arrays and composite arrays use the element type's provider. Existing external
types need no provider. Generated `PgType` and `PgEnum` declarations already
supply their catalog identities, even when their SQL is disabled or replaced;
claiming the same identity again is an error. To supply a disabled declaration,
keep its explicit `Requires` dependency on the supplying SQL block.

Provider metadata describes ordering. It does not parse the SQL, generate a
managed conversion, or verify the type's native storage layout. PostgreSQL
validates the objects when the extension installs. Keep explicit dependencies
for SQL routines, default expressions and other objects used by custom SQL.

### Shell types and completion

A manual base type often needs a shell declaration, native input/output
functions, then a completed type declaration. The completed block can be the
provider when it explicitly requires those functions, and the functions
explicitly require the shell. Ankus preserves that order and places ordinary
consumers after completion. See the [raw type example](../raw-values/).

An inferred type dependency is deferred only when an explicit `Requires` or
`Before` path already orders the consumer before the provider. The early consumer
must be valid with the objects created by those prerequisites. Bootstrap/final
positioning alone cannot authorize this deferral. Explicit dependency cycles
and unresolved cycles still produce `ANKUS005` with no installation manifest.

Alternatively, declare the shell block as the provider. Consumers needing the
completed type must then explicitly require the completion block. Declaring a
provider never means that Ankus can infer the contents of the SQL.

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

For pgrx users, these controls provide the behavior of boolean/string
`sql` options. C# string literals also supply the SQL that Rust can place in a
`pgrxsql` documentation fence. Generation reads constants without executing
extension code.

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

## Replace other declarations

`PgType`, `PgEnum`, `PgAggregate`, `PgOrdering` and `PgHashing` expose the same
`GenerateSql`, `Sql` and `SqlRelocatable` properties. Each controls its own SQL:

| Attribute | SQL replaced or disabled | Retained declarations and contracts |
|---|---|---|
| `PgType` | Shell type, all enabled I/O functions and completed base type | Codec, native I/O exports, managed type mapping and consuming declarations |
| `PgEnum` | `CREATE TYPE ... AS ENUM` | Managed labels, native conversions and consuming declarations |
| `PgAggregate` | `CREATE AGGREGATE` | Support functions with their independent `PgFunction` controls |
| `PgOrdering` | B-tree operator family and class | Comparison functions, relational operators and equality dependency |
| `PgHashing` | Hash operator family and class | Hash support function and equality dependency |

Defaults, empty strings, invalid text, relocation opt-in and dependency rules
work as described for functions. Disabling a declaration keeps its graph ID and
prerequisites. It does not disable its consumers or siblings. Supply compatible
objects through ordered SQL when those declarations still need them. Existing
codec, enum, aggregate and managed comparison/hash validation remains active.

All replacement strings support `@MODULE_PATHNAME@`. Other substitutions apply
only in the contexts below; unknown or out-of-context tokens remain literal.
`PgEquality` has no SQL override options, matching pgrx's equality derive.

## Replace base-type SQL

A type replacement supplies the entire shell/I/O/completed-type sequence. Use
these tokens for the generated native entry points, adding SQL string quotes:

| Token in `PgType.Sql` | Native entry point |
|---|---|
| `@INPUT_FUNCTION_NAME@` | Text input |
| `@OUTPUT_FUNCTION_NAME@` | Text output |
| `@RECEIVE_FUNCTION_NAME@` | Binary receive; requires `BinaryProtocol = true` |
| `@SEND_FUNCTION_NAME@` | Binary send; requires `BinaryProtocol = true` |

For example, this type keeps generated JSON text and CBOR storage while choosing
its SQL I/O function names:

```csharp
[PgType(Name = "stored_value", BinaryProtocol = true, SqlRelocatable = true, Sql = """
    CREATE TYPE stored_value;
    CREATE FUNCTION stored_value_input(cstring) RETURNS stored_value
        AS '@MODULE_PATHNAME@', '@INPUT_FUNCTION_NAME@' LANGUAGE c IMMUTABLE STRICT;
    CREATE FUNCTION stored_value_output(stored_value) RETURNS cstring
        AS '@MODULE_PATHNAME@', '@OUTPUT_FUNCTION_NAME@' LANGUAGE c IMMUTABLE STRICT;
    CREATE FUNCTION stored_value_receive(internal) RETURNS stored_value
        AS '@MODULE_PATHNAME@', '@RECEIVE_FUNCTION_NAME@' LANGUAGE c IMMUTABLE STRICT;
    CREATE FUNCTION stored_value_send(stored_value) RETURNS bytea
        AS '@MODULE_PATHNAME@', '@SEND_FUNCTION_NAME@' LANGUAGE c IMMUTABLE STRICT;
    CREATE TYPE stored_value (
        INTERNALLENGTH = variable, INPUT = stored_value_input, OUTPUT = stored_value_output,
        RECEIVE = stored_value_receive, SEND = stored_value_send, ALIGNMENT = int4, STORAGE = extended);
    """)]
public readonly record struct StoredValue(int Number);
```

When binary protocol is disabled, omit the receive/send declarations and clauses.
Using either binary token then produces `ANKUS005`, including occurrences in
comments. Enabling binary callbacks still emits both native exports even if the
replacement does not register them in SQL.

Preserve the declared type name and schema so generated consumers can resolve
its identity. Preserve its by-reference, variable-length storage contract;
`NativeLayout` changes the payload, not the PostgreSQL datum representation.
This option does not map arbitrary fixed-length or by-value base types. A type
with `NullInputErrorMessage` needs `CALLED ON NULL INPUT` on its text input
declaration for that managed policy to execute. See [custom types](/custom-types/).

## Replace index families

Ordering and hashing replacements retain their generated support functions and
operators. Two tokens give their exact SQL helper identifiers, including the
hashed names used when a type name is too long for an ordinary suffix:

| Context | Token | Expansion |
|---|---|---|
| `PgOrdering.Sql` | `@COMPARISON_FUNCTION_SQL@` | Quoted comparison function name |
| `PgHashing.Sql` | `@HASH_FUNCTION_SQL@` | Quoted hash function name |

Names are schema-qualified when a fixed schema is declared. These are SQL
identifiers without argument lists. Use them directly, without
string quotes, for example `FUNCTION 1 @COMPARISON_FUNCTION_SQL@(stored_value,
stored_value)` in a B-tree class or `FUNCTION 1 @HASH_FUNCTION_SQL@(stored_value)`
in a hash class. Replacement SQL chooses the family/class names and whether a
class is `DEFAULT`. The original group ID still orders consumers after the
complete family/class replacement. See [operators and casts](/operators-and-casts/).

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
