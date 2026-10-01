using System.Text;

namespace Ankus.Generators;

/// <summary>
/// Defines only the catalog and scalar-conversion policy consumed by custom-type I/O rendering.
/// </summary>
/// <param name="Type">The validated managed and catalog identity.</param>
/// <param name="Symbol">The assembly-specific native symbol suffix.</param>
/// <param name="IsValueType">Whether the managed payload is a value type.</param>
/// <param name="BinaryProtocol">Whether binary send and receive functions are generated.</param>
/// <param name="Alignment">The validated storage alignment.</param>
/// <param name="NullInputErrorMessage">The optional error for a null text input.</param>
internal sealed record CustomTypeIoModel(CustomTypeReference Type, string Symbol, bool IsValueType, bool BinaryProtocol,
    string Alignment, string? NullInputErrorMessage)
{
    /// <summary>
    /// Gets the globally qualified managed payload.
    /// </summary>
    internal string Managed => Type.Managed;

    /// <summary>
    /// Gets the optional fixed schema.
    /// </summary>
    internal string? Schema => Type.Schema;

    /// <summary>
    /// Gets the qualified SQL type name.
    /// </summary>
    internal string Sql => Qualify(Type.Name);

    /// <summary>
    /// Qualifies an I/O name while preserving the bounded fallback for long identifiers.
    /// </summary>
    /// <param name="role">The input, output, receive or send suffix.</param>
    /// <returns>The qualified function name.</returns>
    internal string Function(string role)
        => Qualify(Encoding.UTF8.GetByteCount(Type.Name) + role.Length + 1 <= 63 ? Type.Name + "_" + role : "ankus_" + Symbol + "_" + role);

    /// <summary>
    /// Gets the native entry point for an I/O role.
    /// </summary>
    /// <param name="role">The input, output, receive or send suffix.</param>
    /// <returns>The exact exported symbol.</returns>
    internal string NativeFunction(string role) => "ankus_fn_" + Symbol + "_type_" + role;

    /// <summary>
    /// Quotes an identifier in the selected schema.
    /// </summary>
    private string Qualify(string identifier) => (Schema is null ? string.Empty : SqlText.Identifier(Schema) + ".") + SqlText.Identifier(identifier);
}
