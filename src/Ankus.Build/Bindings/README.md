# Native declaration catalogs

The generated `pg13.json` through `pg19.json` files retain native node tags,
structs/unions, fields, typedefs, enums and node cast sets from pgrx commit
`fc91c63ebad11784647b50ee7e265c1fd9c9924f` (pgrx 0.19.3). They preserve complete bindgen type
expressions and do not assert physical sizes or offsets. Physical layouts must
come from the selected PostgreSQL headers and target toolchain.

The matching `pg13.raw.json` through `pg19.raw.json` files inventory foreign
functions, globals and top-level reference constants from that same commit.
Functions retain parameter order, complete callback and array expressions,
return types, variadic markers, ABI and declaration attributes. Globals retain
their declared type and mutability. `NativeSymbol` preserves `link_name`, including
pgrx's `__pgrx_cshim` symbols; those names are not PostgreSQL exports. Hook aliases
resolve through the corresponding type catalog. Both inventories preserve each
major's own signatures instead of applying the newest signature to older servers.

Selected-header helpers supplement declarations missing from a major's foreign
inventory. This includes `pg_add_s32_overflow` from `common/int.h` on all seven
majors. The compiler measures its actual signature and the native call uses the
installed header implementation; the pinned inventories remain unchanged.

`ReferenceConstants` retain unevaluated expressions from pgrx's reference build.
They include configuration values and platform-specific widths. Never use them
as measurements of the extension's selected target; use its headers and compiler.
The raw inventory records its source revision and is loaded independently of the
node layout graph.

| PostgreSQL input | Foreign functions | Foreign globals | Reference constants |
|---|---:|---:|---:|
| 13 | 8,666 | 552 | 5,664 |
| 14 | 9,058 | 580 | 6,353 |
| 15 | 9,294 | 595 | 6,473 |
| 16 | 9,622 | 615 | 6,564 |
| 17 | 9,814 | 636 | 6,691 |
| 18 | 10,204 | 686 | 6,861 |
| 19 | 10,424 | 750 | 6,939 |

The reference server versions are 13.23, 14.24, 15.19, 16.15, 17.11,
18.6 and 19 beta 4. The OID catalog uses the same pgrx revision; SQLSTATE
generation reads PostgreSQL's `REL_19_BETA4` tag. Selected-header measurements
remain authoritative for each consumer's installation.

Regenerate catalogs and attribution with `eng/Ankus.Bindings.cs`. Regenerate each
`pgXX.h` directly from that major's installed PostgreSQL server headers with
`eng/Ankus.Headers.cs`. The manifest generation follows pgrx 0.19.3's source
rules and does not copy pgrx's already-generated output. The binding app's check
mode compares those source-derived files with the pinned pgrx release bytes as a
separate drift guard. Do not edit generated inputs by hand. These declarations
are input to native binding generation. They are not evidence that all raw
PostgreSQL functions, globals, callbacks or node APIs are implemented.
