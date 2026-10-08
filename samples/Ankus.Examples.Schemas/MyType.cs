namespace Ankus.Examples.Schemas;

/// <summary>
/// Stores text in the schema selected by CREATE EXTENSION, using generated CBOR storage and JSON text.
/// </summary>
/// <param name="Value">The stored text.</param>
[PgType(Name = "mytype")]
public sealed record MyType(string Value);
