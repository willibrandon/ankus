using System.Runtime.CompilerServices;

namespace Ankus.Examples.Hello;

/// <summary>
/// Stores PostgreSQL's fixed-width <c>FMGR_ABI_EXTRA</c> value inline in the
/// module magic block without introducing a managed reference.
/// </summary>
[InlineArray(Postgres18Abi.AbiExtraLength)]
internal struct PgAbiExtra
{
    private byte _element0;

    /// <summary>
    /// Creates the ABI suffix expected by an official PostgreSQL server build,
    /// including its terminating null byte and zero-filled capacity.
    /// </summary>
    /// <returns>The fixed-width PostgreSQL ABI suffix.</returns>
    internal static PgAbiExtra Create()
    {
        PgAbiExtra value = default;
        Postgres18Abi.AbiExtra.CopyTo(value);
        return value;
    }
}
