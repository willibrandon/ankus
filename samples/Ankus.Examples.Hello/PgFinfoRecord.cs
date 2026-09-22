using System.Runtime.InteropServices;

namespace Ankus.Examples.Hello;

/// <summary>
/// Represents PostgreSQL's <c>Pg_finfo_record</c>, returned by each
/// <c>pg_finfo_*</c> export to declare the version-1 function-call convention.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PgFinfoRecord
{
    private int _apiVersion;

    /// <summary>
    /// Creates a function-information record for PostgreSQL's only supported
    /// dynamically loaded function-call convention.
    /// </summary>
    /// <returns>A version-1 PostgreSQL function-information record.</returns>
    internal static PgFinfoRecord Create() => new() { _apiVersion = 1 };
}
