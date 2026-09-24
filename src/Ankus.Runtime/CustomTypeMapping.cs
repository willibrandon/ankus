namespace Ankus;

/// <summary>
/// Defines a generated base type's codec and statically closed collection conversions.
/// </summary>
internal abstract class CustomTypeMapping(string name, string? schema)
{
    /// <summary>
    /// Resolves the current variable-length base-type identity.
    /// </summary>
    internal uint GetOid(bool missingOk = false) => NativeBackend.ResolveCustomType(name, schema, missingOk);

    /// <summary>
    /// Resolves the current array identity through the guarded catalog lookup.
    /// </summary>
    internal uint GetArrayOid() => NativeBackend.EnumArrayOid(GetOid());

    /// <summary>
    /// Parses a present text input.
    /// </summary>
    internal abstract object Parse(string text);

    /// <summary>
    /// Formats a present managed value.
    /// </summary>
    internal abstract string Format(object value);

    /// <summary>
    /// Reads a present stored value into independent managed storage.
    /// </summary>
    internal abstract object Read(NativeValue value);

    /// <summary>
    /// Serializes a present managed value into owned native transport.
    /// </summary>
    internal abstract NativeValue Write(object value);

    /// <summary>
    /// Wraps a statically supported vector while preserving element identity.
    /// </summary>
    internal abstract IPgArray Wrap(Array value);

    /// <summary>
    /// Converts owned arrays to the requested statically generated representation.
    /// </summary>
    internal abstract object Convert(IPgArray value, Type type);

    /// <summary>
    /// Reads a nullable array from validated native transport.
    /// </summary>
    internal abstract IPgArray ReadArray(NativeValue value, uint oid);
}
