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

Reading, formatting or copying an existing domain value does not rerun its
constraints. This preserves historical values after `ADD CHECK ... NOT VALID`
and domain-typed NULLs produced by outer joins, including for NOT NULL domains.
Explicit Ankus parameter and result assignments still check current domain
constraints. Formatting invokes the selected output function, and mapped readers
retain their own conversion behavior.

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

## Reusable scalar mappings

Use `[PgDatumType]` when several functions should share a managed representation
and converter. For the `u24` SQL type above:

```csharp
[assembly: PgSqlTypeProvider("u24-type", typeof(Unsigned24))]

[PgDatumType("u24", typeof(Unsigned24Converter))]
public readonly record struct Unsigned24(uint Value);

public sealed class Unsigned24Converter :
    IPgDatumReader<Unsigned24>, IPgDatumWriter<Unsigned24>
{
    public Unsigned24 Read(PgDatum value)
        => new(checked((uint)value.DangerousGetBits()));

    public PgDatum Write(Unsigned24 value, uint typeOid, PgMemoryContext destination)
    {
        if (value.Value > 0xFFFFFF)
        {
            throw new PgException("22003", "Value exceeds 24 bits.");
        }

        return PgDatum.DangerousCreate(value.Value, typeOid, destination);
    }
}

public static class MappedFunctions
{
    [PgFunction]
    public static Unsigned24? EchoMapped(Unsigned24? value) => value;
}
```

The mapping supplies conversion and SQL signature metadata. The existing SQL
block and input/output functions still define the type. A converter can read or
write by-value, fixed-size by-reference, or variable-length storage; it must
follow that SQL type's actual representation. It has an accessible parameterless
constructor and implements either interface or both for the exact managed type.
Ankus creates one converter lazily when a present value needs it. Registration
does not construct user code or access PostgreSQL catalogs.

A default generic declaration such as `Box<T>` can provide finite managed views
of one SQL type. The generator selects fully constructed roots found in supported
function or aggregate signatures, or in an exact managed `PgSqlTypeProvider`
declaration. For example, `Box<int>` and `Box<long>` can share the fixed name,
schema and origin on `Box<T>` while a closed converter
implements `IPgDatumReader<Box<int>>` and `IPgDatumReader<Box<long>>` separately.
Each requested CLR construction selects its own lazy registration; an unused
`Box<T>` declaration creates none. A constructed containing type, such as
`Outer<int>.Value`, also retains its exact managed identity. For an owned SQL
type, declare a provider for **each** closed construction that uses it, even if
the providers name the same completion block.

For independent SQL identities, use explicit closed declarations on the owning
managed type:

```csharp
[PgDatumType(typeof(NumberBox<int>), "int4", typeof(IntBoxConverter),
    Origin = PgTypeOrigin.External, Schema = "pg_catalog")]
[PgDatumType(typeof(NumberBox<long>), "int8", typeof(LongBoxConverter),
    Origin = PgTypeOrigin.External, Schema = "pg_catalog")]
public readonly record struct NumberBox<T>(T Value);
```

Here `IntBoxConverter` implements the reader and/or writer for
`NumberBox<int>`, while `LongBoxConverter` implements the corresponding
`NumberBox<long>` interfaces. Each converter follows its selected SQL type's
actual representation. The explicit type must be fully closed and have the
annotated declaration as its definition, including any constructed containing
types. Local explicit declarations register their roots even when used only
inside raw or SPI method bodies. They do not construct converters or look up
catalog OIDs during registration.

One exact declaration takes precedence over an optional default declaration,
regardless of attribute order. Duplicate exact targets or multiple defaults are
errors. Without a default, using an unlisted construction such as
`NumberBox<decimal>` fails during source generation. Every owned construction
still needs its exact managed provider, and distinct SQL types need their own
completed declarations.

The converter may also be an open generic definition such as
`typeof(NumberBoxConverter<>)`. Given
`NumberBoxConverter<T> : IPgDatumReader<NumberBox<T>>, IPgDatumWriter<NumberBox<T>>`,
Ankus infers `NumberBoxConverter<int>` for `NumberBox<int>` and
`NumberBoxConverter<long>` for `NumberBox<long>`. This works with either default
or explicit mappings. The converter implementation must still follow the SQL
representation selected for each construction.

Inference follows the exact reader and writer interface patterns, including
inherited interfaces and constructed containing types. It does not copy type
arguments by position: `Family<TOuter>.Converter<TInner>` implementing
`IPgDatumReader<Pair<TInner, TOuter>>` closes as `Family<long>.Converter<int>`
for `Pair<int, long>`. A parameter may represent the entire non-generic root,
and compatible reader and writer patterns may jointly determine parameters.

Every converter and containing-type parameter must be determined, with exactly
one complete construction. Missing or ambiguous assignments and violated C#
constraints produce `ANKUS019` before generation. Constraints validate the
unique result; they do not select between competing results. Supply an explicit
closed converter to resolve ambiguity. Ankus emits ordinary closed factories
for the selected finite roots, with separate lazy instances and no runtime
generic construction or reflection.

`IPgDatumReader<T>` converts SQL inputs into detached managed values. Copy native
data before returning; storing the input `PgDatum` in a field does not extend its
lifetime. `IPgDatumWriter<T>` receives the current target OID and an operation's
destination context. Allocate or copy pointer-based results into that context,
and keep their storage live until PostgreSQL consumes the returned datum. Do not
cache the OID or context in the converter. Ankus validates exact OIDs and native
owners before reading or writing, including typed NULL handles.

Nullable CLR absence bypasses the converter. A present zero remains present.
A reader must return a non-null managed value for a present SQL input. A writer
may deliberately return SQL NULL using a live `PgDatum` with `isNull: true` and
the supplied OID; returning a null handle is an error. PostgreSQL still checks
domain constraints when consuming the result. Converter exceptions unwind
through the managed boundary before PostgreSQL raises ERROR.

### Ownership and external types

The default `Origin = PgTypeOrigin.ThisExtension` requires one
`PgSqlTypeProvider` declaration for the exact managed type. An unqualified owned
mapping resolves through the invoking function's owning extension schema,
including after extension relocation. Set `Schema` for a fixed schema; owned
fixed schemas prevent relocation. Type and schema names are exact, unquoted
catalog identifiers, with a maximum of 63 UTF-8 bytes.

For an existing SQL type, set `Origin = PgTypeOrigin.External` and an explicit
schema:

```csharp
[PgDatumType("int4", typeof(CountReader),
    Origin = PgTypeOrigin.External, Schema = "pg_catalog")]
public readonly record struct Count(int Value);

public sealed class CountReader : IPgDatumReader<Count>
{
    public Count Read(PgDatum value) => new(value.Read<int>());
}
```

This read-only mapping can appear in function inputs, `PgDatum.Read<Count>()`,
`Spi.ExecuteScalar<Count>()`, or `PgFunctions.Call<Count>()`.
Returning a value from a generated managed callback, or supplying a typed
parameter, needs a writer. `PgFunctionArgument.Default<Count>()`
only supplies a type for PostgreSQL's default expression and needs no writer.
`SpiParameter.Create<Count?>(null)` still requires a writer even though SQL NULL
skips its invocation.

External mappings have no provider dependency. Their fixed type references alone
do not prevent extension relocation; generated operator families add fixed-schema
objects and therefore do. Multiple CLR wrappers can share one SQL type;
the requested CLR type selects the converter. Domains keep their own identity:
a sibling domain or its base type cannot be read through a domain mapping.
Catalog lookups use current OIDs. A retained typed parameter cannot silently
switch to a replacement type after its original type is dropped.

### Supported conversion paths

Mapped scalars work in ordinary function arguments/results, nullable values,
SETOF/TABLE outputs, aggregate support methods, and manual operator/cast functions.
Readable mappings can also declare `PgEquality`, `PgOrdering`, and `PgHashing`
without a writer. See [generated type operators](/operators-and-casts/#manual-datum-mappings)
for value contracts, type-provider ordering, schemas and index-class rules.
They also work in `SpiParameter.Create<T>`, `PgFunctionArgument.Create<T>`, typed
SPI scalar-result helpers, named/OID `PgFunctions.Call<T>`, and direct raw reads:

```csharp
Unsigned24 value;
using (SpiRawResult result = Spi.QueryRaw("SELECT '42'::u24"))
{
    value = result[0][0].Read<Unsigned24>();
}
// value is detached and remains usable after result disposal.
```

Typed scalar-result helpers select the requested mapping and dispose their
temporary native storage after its reader returns. Sessions, prepared statements,
and two- or three-column results follow the same rule. A missing reader is an
error before execution, even when SQL would return NULL or no rows. A present
NULL cell still checks exact type identity; an empty result follows the ordinary
nullable/reference absence rules without invoking a converter.

Catalog calls check the declared result's exact mapped OID before invoking the
function or evaluating its defaults. SPI result identity is checked after SQL
executes. A caught managed reader error does not roll back SQL that already
completed. See [SPI queries](/spi/#scalar-values) and
[calling PostgreSQL functions](/calling-functions/).

The same scalar reader/writer contracts compose into `T[]` and `PgArray<T>`,
including nullable value elements. Each array retains the exact current mapped
element type and its corresponding array type, even when empty or all-NULL.
Raw array reads detach their elements under a temporary owner; the original raw
array keeps its existing owner. Writers construct the complete array before
releasing temporary element storage. See [mapped array elements](/arrays/#mapped-elements)
for shape, domain, NULL and ownership rules.

Value-type mappings can add [`PgRangeType`](/ranges/#mapped-bounds) to reuse
their converter for finite `PgRange<T>` bounds and arrays of those ranges.
The range has its own SQL identity and provider. NULL ranges, empty ranges and
infinite ends skip scalar conversion; a finite bound writer returning SQL NULL
is rejected. The backend verifies the declared range's exact scalar subtype.

Use raw result owners and `Read<T>()` for mapped row fields. `SpiRow.Get<T>` and
`PgHeapTuple.Get<T>` do not convert canonical cells through these converters.
For native addresses, `DangerousCall<T>` selects the registered reader; use
`DangerousCallRaw` for an explicit native owner or delayed mapped read. The caller
must supply an address whose actual result matches the mapping's SQL type and
representation; native-address calls cannot check a catalog return declaration.
Nested mapped SQL array containers are rejected, as they are in pgrx. Use one
`PgArray<T>` to retain multiple dimensions of mapped scalar elements. Different
argument and result SQL spellings remain unsupported.
A default generic declaration needs a selected closed root: an
unused open template or a `PgDatum.Read<T>()` call in an arbitrary method body
cannot create one by itself. Add an explicit closed declaration for a local
raw-only root. Accessible closed nested CLR declarations are supported.
The generator rejects unsupported mapped signatures with `ANKUS019`.

Local non-generic annotated types are registered even when only used by raw APIs.
Default generic local declarations and types from a referenced assembly must
occur in a supported generated signature or an exact managed-type provider
declaration to become registration roots. Ambiguous externally aliased names are
rejected. A type cannot combine
`PgDatumType` with `PgType` or `PgEnum`, and mapped slots do not use per-parameter
`PgSqlType` or `PgCompositeType` overrides.
