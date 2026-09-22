namespace Ankus;

/// <summary>
/// Owns a PostgreSQL enum catalog entry, including its label OID and server sort position.
/// SortOrder follows PostgreSQL declaration and ALTER TYPE ordering, independently of C# numeric values.
/// </summary>
/// <param name="Label">The exact label decoded from the server encoding.</param>
/// <param name="TypeOid">The PostgreSQL enum type OID.</param>
/// <param name="ValueOid">The pg_enum row OID used as the stored enum datum.</param>
/// <param name="SortOrder">The catalog's sort position, which may be fractional after adding labels.</param>
public sealed record PgEnumInfo(string Label, uint TypeOid, uint ValueOid, float SortOrder);
