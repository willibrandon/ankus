namespace Ankus.Build;

/// <summary>
/// Derives C compiler include directories from the Visual C++ and Windows SDK selected by Native AOT's toolchain discovery.
/// </summary>
internal static class WindowsToolchain
{
    /// <summary>
    /// Maps architecture-specific MSVC and Windows SDK library directories to their matching headers.
    /// </summary>
    /// <param name="libraryDirectories">The semicolon-separated directories discovered by the Native AOT SDK.</param>
    /// <returns>Existing include directories for that same toolchain version.</returns>
    internal static IEnumerable<string> GetIncludeDirectories(string libraryDirectories)
    {
        var includes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string library in libraryDirectories.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var architecture = new DirectoryInfo(library.Trim());
            DirectoryInfo? parent = architecture.Parent;
            if (parent?.Name.Equals("lib", StringComparison.OrdinalIgnoreCase) == true && parent.Parent is not null)
            {
                includes.Add(Path.Combine(parent.Parent.FullName, "include"));
            }
            else if (parent?.Parent?.Parent?.Name.Equals("lib", StringComparison.OrdinalIgnoreCase) == true)
            {
                DirectoryInfo version = parent.Parent;
                DirectoryInfo? sdk = version.Parent?.Parent;
                if (sdk is not null)
                {
                    foreach (string component in new[] { "ucrt", "shared", "um", "winrt" })
                    {
                        includes.Add(Path.Combine(sdk.FullName, "Include", version.Name, component));
                    }
                }
            }
        }

        return includes.Where(Directory.Exists);
    }
}
