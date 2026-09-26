using System.Text.Json;

namespace Ankus.Build;

/// <summary>
/// Loads the pinned declarations and header manifests shipped inside the build tool.
/// </summary>
internal static class NativeBindingResources
{
    /// <summary>
    /// Reads a supported major's declaration catalog without requiring a reference checkout.
    /// </summary>
    /// <param name="major">The selected PostgreSQL major.</param>
    /// <returns>The pinned declarations for that major.</returns>
    internal static NativeBindingCatalog ReadCatalog(int major)
    {
        using Stream source = Open(major, "json");
        NativeBindingCatalog catalog = JsonSerializer.Deserialize<NativeBindingCatalog>(source)
            ?? throw new FormatException("The embedded native binding catalog is empty.");
        if (catalog.PostgresMajor != major) { throw new FormatException("The embedded native binding catalog has the wrong major."); }

        return catalog;
    }

    /// <summary>
    /// Loads a major's raw declaration inventory separately from the node layout dependency graph.
    /// </summary>
    /// <param name="major">The selected PostgreSQL major.</param>
    /// <returns>Reference declarations, without selected-target ABI or runtime call guarantees.</returns>
    internal static NativeBindingRawCatalog ReadRawCatalog(int major)
    {
        using Stream source = Open(major, "raw.json");
        NativeBindingRawCatalog catalog = JsonSerializer.Deserialize<NativeBindingRawCatalog>(source)
            ?? throw new FormatException("The embedded raw declaration catalog is empty.");
        if (catalog.PostgresMajor != major) { throw new FormatException("The embedded raw declaration catalog has the wrong major."); }

        return catalog;
    }

    /// <summary>
    /// Reads the matching header manifest to compile against the user's actual installation.
    /// </summary>
    /// <param name="major">The selected PostgreSQL major.</param>
    /// <returns>The pinned header include directives and their source license notice.</returns>
    internal static string ReadHeaders(int major)
    {
        using Stream source = Open(major, "h");
        using var reader = new StreamReader(source);
        return reader.ReadToEnd();
    }

    private static Stream Open(int major, string extension)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(major, 13);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(major, 19);
        return typeof(NativeBindingResources).Assembly.GetManifestResourceStream($"Ankus.Build.Bindings.pg{major}.{extension}")
            ?? throw new InvalidOperationException($"Missing embedded PostgreSQL {major} binding input.");
    }
}
