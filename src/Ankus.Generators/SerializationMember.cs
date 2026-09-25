namespace Ankus.Generators;

/// <summary>
/// Carries one directly accessible member and its persisted name.
/// </summary>
/// <param name="name">The managed identifier.</param>
/// <param name="serializedName">The map key.</param>
/// <param name="value">The member's exact contract.</param>
/// <param name="writable">Whether an object initializer can assign the member.</param>
/// <param name="required">Whether presence is explicitly required even when nullable.</param>
/// <param name="initializerRequired">Whether C# requires an initializer unless the constructor sets required members.</param>
internal sealed class SerializationMember(string name, string serializedName, SerializationNode value, bool writable, bool required, bool initializerRequired)
{
    /// <summary>
    /// Gets the managed identifier.
    /// </summary>
    internal string Name { get; } = name;

    /// <summary>
    /// Gets the map key.
    /// </summary>
    internal string SerializedName { get; } = serializedName;

    /// <summary>
    /// Gets the exact value contract.
    /// </summary>
    internal SerializationNode Value { get; } = value;

    /// <summary>
    /// Gets whether an initializer can assign this member.
    /// </summary>
    internal bool Writable { get; } = writable;

    /// <summary>
    /// Gets whether the member must be present even if its value can be null.
    /// </summary>
    internal bool Required { get; } = required;

    /// <summary>
    /// Gets whether the C# required modifier imposes a constructor or initializer obligation.
    /// </summary>
    internal bool InitializerRequired { get; } = initializerRequired;
}
