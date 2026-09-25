namespace Ankus.Examples.CustomTypes;

/// <summary>
/// Stores a measurement using generated CBOR storage and JSON text input/output.
/// </summary>
/// <param name="Sensor">The sensor that supplied the measurement.</param>
/// <param name="Value">The exact decimal measurement.</param>
/// <param name="Unit">The optional measurement unit.</param>
[PgType]
public sealed record Reading(string Sensor, decimal Value, string? Unit);
