namespace Ankus.TestExtension;

/// <summary>
/// Supplies a source-generated JSON contract with nested PostgreSQL JSON values.
/// </summary>
/// <param name="Id">The envelope identity.</param>
/// <param name="Value">The nested JSON value.</param>
/// <param name="Metadata">The nested JSONB metadata.</param>
public sealed record JsonEnvelope(Guid Id, PgJson Value, PgJsonb Metadata);
