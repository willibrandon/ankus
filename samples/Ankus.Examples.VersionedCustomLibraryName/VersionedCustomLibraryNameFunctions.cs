namespace Ankus.Examples.VersionedCustomLibraryName;

/// <summary>
/// Ports pgrx's <c>versioned_custom_libname_so</c> example, which combines a custom library name with
/// versioned shared-object mode.
/// </summary>
/// <remarks>
/// <c>AnkusLibraryName</c> selects the library name <c>versioned_othername</c>, and <c>AnkusVersionedLibrary</c>
/// appends the extension version. Version 0.1.0 publishes <c>versioned_othername-0.1.0</c> plus the platform's
/// suffix, and the installation script references that file directly.
/// </remarks>
public static class VersionedCustomLibraryNameFunctions
{
    /// <summary>
    /// Returns the example's greeting from the versioned, custom-named library.
    /// </summary>
    /// <returns>The greeting.</returns>
    [PgFunction]
    public static string HelloVersionedCustomLibnameSo() => "Hello, versioned_custom_libname_so";
}
