using System.Runtime.InteropServices;

namespace Ankus.Examples.Hello;

/// <summary>
/// Represents PostgreSQL 18's <c>Pg_magic_struct</c> exactly as declared in
/// <c>src/include/fmgr.h</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct PgMagic
{
    private int _length;
    private PgAbiValues _abiFields;
    private byte* _name;
    private byte* _version;

    /// <summary>
    /// Creates the module magic block PostgreSQL uses to reject ABI-incompatible
    /// extension libraries before invoking any SQL-callable function.
    /// </summary>
    /// <returns>The PostgreSQL 18 module magic block.</returns>
    internal static PgMagic Create()
        => new()
        {
            _length = sizeof(PgMagic),
            _abiFields = PgAbiValues.Create(),
            _name = null,
            _version = null,
        };
}
