using System.Text.Json.Serialization;

namespace Ankus.Examples.CustomSql;

/// <summary>
/// Creates the extension-owned home schema; its nested Dogs class creates a separate top-level dogs schema.
/// </summary>
[PgSchema("home")]
public static class Home
{
    /// <summary>
    /// Creates the extension-owned dogs schema. PostgreSQL schemas do not nest, so this is not home.dogs.
    /// </summary>
    [PgSchema("dogs")]
    public static class Dogs
    {
        /// <summary>
        /// Names the dogs that chomp the ball.
        /// </summary>
        [PgEnum]
        public enum Dog
        {
            /// <summary>
            /// The first dog.
            /// </summary>
            Brandy,

            /// <summary>
            /// The second dog.
            /// </summary>
            Nami,
        }
    }

    /// <summary>
    /// Stores the last dog to chomp the ball, using generated CBOR storage and JSON text.
    /// </summary>
    /// <param name="LastChomp">The dog serialized under pgrx's last_chomp field name.</param>
    [PgType]
    public sealed record Ball([property: JsonPropertyName("last_chomp")] Dogs.Dog LastChomp);
}
