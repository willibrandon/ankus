using System.Text.Json;
using System.Xml.Linq;

namespace Ankus.Build;

/// <summary>
/// Carries the consumer's resolved package settings without copying or interpreting NuGet credentials.
/// </summary>
/// <param name="Packages">The resolved global packages directory.</param>
/// <param name="Sources">The effective package sources, including command-line overrides.</param>
/// <param name="Configurations">The ordered configuration files NuGet selected for the consumer.</param>
/// <param name="Fallbacks">The effective package fallback directories.</param>
internal sealed record NativeBindingRestoreSettings(string Packages, string[] Sources, string[] Configurations, string[] Fallbacks)
{
    /// <summary>
    /// Reads the restore contract that the consuming SDK project already resolved.
    /// </summary>
    internal static async Task<NativeBindingRestoreSettings> ReadAsync(string assetsFile, CancellationToken cancellationToken)
    {
        await using FileStream stream = File.OpenRead(assetsFile);
        using JsonDocument assets = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        JsonElement restore = assets.RootElement.GetProperty("project").GetProperty("restore");
        string packages = AbsolutePath(restore.GetProperty("packagesPath").GetString());
        string[] sources = [.. restore.GetProperty("sources").EnumerateObject().Select(static source => source.Name)];
        string[] configurations = [.. restore.GetProperty("configFilePaths").EnumerateArray().Select(static file => AbsolutePath(file.GetString()))];
        string[] fallbacks = restore.TryGetProperty("fallbackFolders", out JsonElement folders)
            ? [.. folders.EnumerateArray().Select(static folder => AbsolutePath(folder.GetString()))] : [];
        return new(packages, sources, configurations, fallbacks);
    }

    /// <summary>
    /// Supplies NuGet's own restore graph with the consumer's effective configuration and sources.
    /// </summary>
    internal XElement CreateTarget()
        => new("Target", new XAttribute("Name", "_ApplyAnkusRestoreSettings"), new XAttribute("AfterTargets", "_GetRestoreSettings"),
            new XElement("PropertyGroup",
                List("_OutputSources", Sources),
                List("_OutputConfigFilePaths", Configurations),
                List("_OutputFallbackFolders", Fallbacks),
                List("_OutputPackagesPath", [Packages])));

    /// <summary>
    /// Rejects SDK changes that would silently substitute a different restore contract.
    /// </summary>
    internal void Verify(NativeBindingRestoreSettings actual)
    {
        if (Packages != actual.Packages || !Sources.SequenceEqual(actual.Sources) ||
            !Configurations.SequenceEqual(actual.Configurations) || !Fallbacks.SequenceEqual(actual.Fallbacks))
        {
            throw new InvalidOperationException("The selected SDK did not retain the consumer's NuGet restore settings.");
        }
    }

    private static XElement List(string name, IEnumerable<string> values)
        => new(name, string.Join(';', values.Select(NativeBindingSourceCommand.EscapeProperty)));

    private static string AbsolutePath(string? value)
        => value is not null && Path.IsPathFullyQualified(value) ? Path.GetFullPath(value)
            : throw new FormatException("The consumer restore settings contain a nonabsolute package or configuration path.");
}
