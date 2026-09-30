using Microsoft.CodeAnalysis;

namespace Ankus.Generators;

/// <summary>
/// Stores authored SQL parameter policies independently of compiler attributes.
/// </summary>
/// <param name="Name">The explicit SQL identifier, or null for the managed-name default.</param>
/// <param name="Default">The explicit SQL default expression, or null for C# inference.</param>
/// <param name="Element">The optional aggregate tuple element selector.</param>
/// <param name="Variadic">Whether the attribute requests aggregate variadic input.</param>
internal sealed record SqlParameterOptions(string? Name, string? Default, string? Element, bool Variadic)
{
    /// <summary>
    /// Reads the exact options and existing defaults from one parameter policy.
    /// </summary>
    /// <param name="attribute">The authored SQL parameter attribute.</param>
    /// <returns>The detached immutable policy.</returns>
    internal static SqlParameterOptions Read(AttributeData attribute)
        => new(AttributeValues.Get<string?>(attribute, "Name", null), AttributeValues.Get<string?>(attribute, "Default", null),
            AttributeValues.Get<string?>(attribute, "Element", null), AttributeValues.Get(attribute, "Variadic", false));
}
