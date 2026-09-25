# Native declaration catalogs

The generated `pg13.json` through `pg19.json` files retain native node tags,
structs/unions, fields, typedefs, enums and node cast sets from pgrx commit
`70383e884582d1bcc7cd681d10886b995a2830cb`. They preserve complete bindgen type
expressions and do not assert physical sizes or offsets. Physical layouts must
come from the selected PostgreSQL headers and target toolchain.

Regenerate with `eng/Ankus.Bindings.cs`; do not edit generated catalogs by hand.
These declarations are input to native binding generation. They are not evidence
that all raw PostgreSQL functions, globals, callbacks or node APIs are implemented.
