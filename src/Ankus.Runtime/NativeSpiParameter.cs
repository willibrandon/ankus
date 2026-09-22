using System.Runtime.InteropServices;

namespace Ankus;

/// <summary>
/// Carries a positional SPI parameter and its declared PostgreSQL type across the native boundary.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct NativeSpiParameter
{
    /// <summary>
    /// Contains the parameter value and any owned buffer.
    /// </summary>
    internal NativeValue _value;

    /// <summary>
    /// Contains the declared PostgreSQL type OID.
    /// </summary>
    internal uint _typeOid;
}
