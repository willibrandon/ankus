namespace Ankus.Examples.CustomLibraryName;

/// <summary>
/// Ports pgrx's <c>custom_libname</c> example, whose shared library is named <c>other_name</c>.
/// </summary>
/// <remarks>
/// The project sets <c>AnkusLibraryName</c> to <c>other_name</c>, the counterpart of Cargo's <c>[lib] name</c>.
/// The published library is therefore <c>other_name</c> with the platform's suffix, and the generated control
/// file's <c>module_pathname</c> names it. The assembly keeps its project name and the SQL extension keeps its own
/// name, <c>ankus_custom_libname</c>.
/// </remarks>
public static class CustomLibraryNameFunctions
{
    /// <summary>
    /// Returns the example's greeting from the separately named library.
    /// </summary>
    /// <returns>The greeting.</returns>
    [PgFunction]
    public static string HelloCustomLibname() => "Hello, custom_libname";
}
