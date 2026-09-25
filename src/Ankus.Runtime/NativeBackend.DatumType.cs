namespace Ankus;

public static partial class NativeBackend
{
    /// <summary>
    /// Resolves a defined concrete mapped type in the fixed or current extension schema without caching its OID.
    /// </summary>
    internal static uint ResolveDatumType(string name, string? schema)
        => Scalar<uint>(SpiOperation.DatumType, 0, [SpiParameter.Create(name), SpiParameter.Create(schema)]);
}
