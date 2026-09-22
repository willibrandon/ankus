namespace Ankus.Examples.Hello;

/// <summary>
/// Contains ABI constants copied from the PostgreSQL 18 headers used by the
/// official 64-bit Linux image.
/// </summary>
internal static class Postgres18Abi
{
    /// <summary>
    /// Gets <c>PG_VERSION_NUM / 100</c>, the version representation encoded in
    /// PostgreSQL's module magic block.
    /// </summary>
    internal const int VersionNumber = 1800;

    /// <summary>
    /// Gets PostgreSQL's configured maximum number of function arguments.
    /// </summary>
    internal const int FunctionMaxArguments = 100;

    /// <summary>
    /// Gets PostgreSQL's configured maximum number of index keys.
    /// </summary>
    internal const int IndexMaxKeys = 32;

    /// <summary>
    /// Gets PostgreSQL's configured length of a <c>NameData</c> value.
    /// </summary>
    internal const int NameDataLength = 64;

    /// <summary>
    /// Gets whether PostgreSQL passes eight-byte floating-point values by value.
    /// </summary>
    internal const int Float8ByValue = 1;

    /// <summary>
    /// Gets the fixed capacity of PostgreSQL's extra ABI identifier.
    /// </summary>
    internal const int AbiExtraLength = 32;

    /// <summary>
    /// Gets the null-terminated ABI identifier used by official PostgreSQL builds.
    /// </summary>
    internal static ReadOnlySpan<byte> AbiExtra => "PostgreSQL\0"u8;
}
