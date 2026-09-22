using System.Text.Json.Serialization;

namespace Ankus.TestExtension;

/// <summary>
/// Supplies compile-time JSON contracts exercised inside the Native AOT extension.
/// </summary>
[JsonSerializable(typeof(JsonEnvelope))]
internal sealed partial class ExtensionJsonContext : JsonSerializerContext;
