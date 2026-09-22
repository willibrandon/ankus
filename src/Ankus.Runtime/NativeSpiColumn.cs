using System.Runtime.InteropServices;

namespace Ankus;

/// <summary>
/// Carries SPI column names and declared/base type OIDs in native-owned memory.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct NativeSpiColumn
{
    /// <summary>
    /// Contains the declared type OID.
    /// </summary>
    internal uint _typeOid;

    /// <summary>
    /// Contains the underlying base type OID used for datum conversion.
    /// </summary>
    internal uint _baseTypeOid;

    /// <summary>
    /// Contains a UTF-8 column name.
    /// </summary>
    internal NativeValue _name;
}
