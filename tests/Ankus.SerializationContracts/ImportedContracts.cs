using System.Text.Json.Serialization;

namespace Ankus.SerializationContracts;

/// <summary>
/// Supplies a renamed virtual member from an actual referenced assembly.
/// </summary>
public class MemberBase
{
    /// <summary>
    /// Gets or sets a value whose persisted key contains a valid zero character.
    /// </summary>
    [JsonPropertyName("é\0")]
    public virtual int Value
    {
        get;
        set;
    }
}

/// <summary>
/// Inherits the attribute from its defining member while overriding its accessors.
/// </summary>
public sealed class Member : MemberBase
{
    /// <inheritdoc />
    public override int Value
    {
        get;
        set;
    }
}

/// <summary>
/// Supplies an exact zero-containing stored enum name independently of SQL enum declarations.
/// </summary>
public enum Mode
{
    /// <summary>
    /// Represents the ordinary zero-valued control.
    /// </summary>
    Stopped,

    /// <summary>
    /// Represents the named noncontiguous value.
    /// </summary>
    [JsonStringEnumMemberName("ready\0")]
    Ready = 7,
}

/// <summary>
/// Supplies exact zero-containing discriminator property and value strings.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind\0")]
[JsonDerivedType(typeof(Child), "child\0")]
public abstract class Variant;

/// <summary>
/// Carries a concrete variant's value through JSON and native binary storage.
/// </summary>
public sealed class Child : Variant
{
    /// <summary>
    /// Gets or sets the numeric payload.
    /// </summary>
    public int Value
    {
        get;
        set;
    }
}
