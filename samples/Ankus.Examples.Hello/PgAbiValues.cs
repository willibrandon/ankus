using System.Runtime.InteropServices;

namespace Ankus.Examples.Hello;

/// <summary>
/// Represents PostgreSQL 18's <c>Pg_abi_values</c> structure exactly as declared
/// in <c>src/include/fmgr.h</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PgAbiValues
{
    private int _version;
    private int _functionMaxArguments;
    private int _indexMaxKeys;
    private int _nameDataLength;
    private int _float8ByValue;
    private PgAbiExtra _abiExtra;

    /// <summary>
    /// Creates the ABI values expected by a stock 64-bit PostgreSQL 18 server.
    /// These values are compared byte-for-byte when PostgreSQL loads the module.
    /// </summary>
    /// <returns>The PostgreSQL 18 ABI values.</returns>
    internal static PgAbiValues Create()
        => new()
        {
            _version = Postgres18Abi.VersionNumber,
            _functionMaxArguments = Postgres18Abi.FunctionMaxArguments,
            _indexMaxKeys = Postgres18Abi.IndexMaxKeys,
            _nameDataLength = Postgres18Abi.NameDataLength,
            _float8ByValue = Postgres18Abi.Float8ByValue,
            _abiExtra = PgAbiExtra.Create(),
        };
}
