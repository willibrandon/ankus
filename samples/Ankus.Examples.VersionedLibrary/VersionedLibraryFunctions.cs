namespace Ankus.Examples.VersionedLibrary;

/// <summary>
/// Ports pgrx's <c>versioned_so</c> example, which runs in versioned shared-object mode.
/// </summary>
/// <remarks>
/// <c>AnkusVersionedLibrary</c> names the published library <c>Ankus.Examples.VersionedLibrary-0.1.0</c> plus the
/// platform's suffix. The control file has no <c>module_pathname</c>; the installation script references that
/// versioned library directly, so another version's library can be installed beside it.
/// </remarks>
public static class VersionedLibraryFunctions
{
    /// <summary>
    /// Returns the example's greeting from the versioned library.
    /// </summary>
    /// <returns>The greeting.</returns>
    [PgFunction]
    public static string HelloVersionedSo() => "Hello, versioned_so";
}
