using System.Text.Json.Serialization;

namespace Ankus.Examples.TypeVariants;

/// <summary>
/// Variant 1: a point stored with generated CBOR and written as JSON text, like pgrx's default <c>PostgresType</c>.
/// </summary>
/// <remarks>
/// <c>[PgType]</c> without a codec generates the serializer, the <c>coord</c> type and its input and output functions.
/// The JSON member names match pgrx's Serde field names. As in pgrx, the type is not named <c>point</c>, which
/// would collide with PostgreSQL's built-in geometric type.
/// </remarks>
/// <param name="X">The horizontal coordinate.</param>
/// <param name="Y">The vertical coordinate.</param>
[PgType]
public sealed record Coord([property: JsonPropertyName("x")] double X, [property: JsonPropertyName("y")] double Y);

/// <summary>
/// Functions over the JSON-default <see cref="Coord"/> type.
/// </summary>
public static class JsonDefaultFunctions
{
    /// <summary>
    /// Returns the origin.
    /// </summary>
    /// <returns>The point (0, 0).</returns>
    [PgFunction]
    public static Coord CoordOrigin() => new(0.0, 0.0);

    /// <summary>
    /// Moves a point by the given offsets.
    /// </summary>
    /// <param name="p">The point to move.</param>
    /// <param name="dx">The horizontal offset.</param>
    /// <param name="dy">The vertical offset.</param>
    /// <returns>The translated point.</returns>
    [PgFunction]
    public static Coord CoordTranslate(Coord p, double dx, double dy) => new(p.X + dx, p.Y + dy);
}
