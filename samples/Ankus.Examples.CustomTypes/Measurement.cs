using System.Text.Json.Serialization;

namespace Ankus.Examples.CustomTypes;

/// <summary>
/// Stores a tagged sensor result with inherited state and statically known variants.
/// </summary>
/// <param name="Sensor">The sensor identifier.</param>
[PgType]
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(Measured), "measured")]
[JsonDerivedType(typeof(Unavailable), "unavailable")]
public abstract record Measurement(string Sensor);

/// <summary>
/// Stores a successful measurement.
/// </summary>
/// <param name="Sensor">The sensor identifier.</param>
/// <param name="Value">The exact measured value.</param>
/// <param name="Unit">The optional unit.</param>
public sealed record Measured(string Sensor, decimal Value, string? Unit) : Measurement(Sensor);

/// <summary>
/// Stores a sensor's failure to provide a measurement.
/// </summary>
/// <param name="Sensor">The sensor identifier.</param>
/// <param name="Reason">The explanation for the missing value.</param>
public sealed record Unavailable(string Sensor, string Reason) : Measurement(Sensor);
