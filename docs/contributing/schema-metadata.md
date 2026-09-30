# Native schema metadata

Source generation retains the resolved SQL graph in the `Ankus.SqlGraph`
assembly metadata value. The build helper reads assembly metadata without
loading the assembly, checks that the graph reconstructs `Ankus.Sql`, and
embeds both in the native library. `ExtensionSchema.Read` reads the retained
ELF, PE or Mach-O section without executing extension code.

The outer framing is eight bytes `ANKUSSC\0`, a little-endian unsigned 32-bit
JSON byte length, then strict UTF-8 JSON. Native alignment may add zero bytes
after the document. Nonzero trailing data is invalid. The section is limited
to 64 MiB.

JSON format 1 carries the publication identity, relocation flag and complete
SQL. Format 2 adds a required base64 `graph` and an optional effective fixed
control `schema`. The graph's complete SQL must equal the outer `sql` exactly.
Duplicate JSON properties, mismatched target identities and invalid fixed
schemas are rejected. Format 1 remains readable for complete extraction; it
cannot satisfy graph or item-selection requests.

The decoded graph is limited to 32 MiB. Its independent binary contract is:

1. Eight bytes `ANKUSG1\0`.
2. A signed little-endian 32-bit declaration count, from zero through 100,000.
3. For each declaration: ID, kind, SQL template, family-owner ID, selection-name
   array, prerequisite-ID array, and attachment-template array.

Strings use a signed little-endian 32-bit UTF-8 byte length followed by strict
UTF-8 bytes. Arrays use a signed little-endian 32-bit count followed by strings;
each array is limited to 100,000 values. Negative lengths/counts, truncation,
duplicate IDs or array values, and trailing bytes are invalid. Dependencies
must name preceding declarations. A nonempty owner must name an existing,
different root without its own owner. Owners may follow their members because
some generated operator groups depend on their support functions.

Kinds are `schema`, `function`, `type`, `enum`, `operator`, `cast`, `aggregate`,
`sql`, `equality`, `ordering`, and `hashing`. IDs and aliases are case-sensitive.
Aliases may appear on different declarations, making a selection ambiguous;
explicit graph dependency IDs retain their compiler uniqueness requirement.

Only SQL and attachment templates may contain NUL. At a typed identifier
boundary, that compiler marker means "insert the effective default schema
prefix here." Authored SQL rejects NUL before substitution. Ordinary installation
SQL removes markers. Selected SQL substitutes a quoted fixed control schema
and period, or removes markers when no fixed schema was declared. Parameter
names, literals, comments and arbitrary authored SQL are never inferred as
qualification targets. Public `ExtensionSchemaItem.Sql` and `Attachments`
expose marker-free text.

Graph encoding uses the validated installation order. Names, prerequisites and
attachments are distinct and ordinally sorted for stable output. Selection
walks prerequisites and complete declaration families, then emits the original
installation order. Attachment identities come from typed declarations or
explicit custom providers; readers do not recover identities by parsing SQL.

Keep protocol fixtures independent of the producer. Verification includes
malformed/truncated inputs, exact SQL agreement, retained sections in real
linked libraries, compiler dependency facts, and published selected scripts
executed against PostgreSQL with catalog ownership and rollback checks.
