# Native declaration catalogs

The generated `pg13.json` through `pg19.json` files retain native node tags,
structs/unions, fields, typedefs, enums and node cast sets from pgrx commit
`70383e884582d1bcc7cd681d10886b995a2830cb`. They preserve complete bindgen type
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

`ReferenceConstants` retain unevaluated expressions from pgrx's reference build.
They include configuration values and platform-specific widths. Never use them
as measurements of the extension's selected target; use its headers and compiler.
The raw inventory records its source revision and is loaded independently of the
node layout graph.

| PostgreSQL input | Foreign functions | Foreign globals | Reference constants |
|---|---:|---:|---:|
| 13 | 7,349 | 475 | 4,968 |
| 14 | 7,701 | 508 | 5,592 |
| 15 | 7,890 | 515 | 5,683 |
| 16 | 8,193 | 529 | 5,765 |
| 17 | 8,339 | 546 | 5,882 |
| 18 | 8,645 | 579 | 6,112 |
| 19 | 8,799 | 591 | 6,159 |

Regenerate with `eng/Ankus.Bindings.cs`; do not edit generated catalogs by hand.
These declarations are input to native binding generation. They are not evidence
that all raw PostgreSQL functions, globals, callbacks or node APIs are implemented.
