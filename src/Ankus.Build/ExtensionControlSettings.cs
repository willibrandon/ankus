using Ankus.PgConfig;

namespace Ankus.Build;

/// <summary>
/// Combines author control settings with the publication's generated identity and schema contract.
/// </summary>
internal static class ExtensionControlSettings
{
    /// <summary>
    /// Applies author settings without changing generated file identities or weakening relocation requirements.
    /// </summary>
    /// <param name="generated">The generated control assignments.</param>
    /// <param name="authored">The author control assignments.</param>
    /// <param name="major">The selected PostgreSQL major.</param>
    /// <returns>The complete control text and effective relocation flag.</returns>
    internal static (string Control, bool Relocatable) Merge(string generated, string authored, int major)
    {
        IReadOnlyDictionary<string, string> identity = ExtensionControlFile.Parse(generated);
        Dictionary<string, string> settings = ReadSettings(identity, authored, major, secondary: false, currentVersion: true);
        (Dictionary<string, string> values, bool relocatable) = Apply(identity, settings, identity["relocatable"] == "true");
        return (ExtensionControlFile.Format(values), relocatable);
    }

    /// <summary>
    /// Validates a version override and returns only its normalized assignments and effective relocation flag.
    /// </summary>
    /// <param name="primary">The complete primary control assignments.</param>
    /// <param name="authored">The version-specific author assignments.</param>
    /// <param name="major">The selected PostgreSQL major.</param>
    /// <param name="currentVersion">Whether this file describes the generated installation SQL and native library.</param>
    /// <param name="generatedRelocatable">Whether the current generated SQL permits relocation.</param>
    /// <returns>The secondary control text and effective relocation flag.</returns>
    internal static (string Control, bool Relocatable) MergeVersion(string primary, string authored, int major,
        bool currentVersion, bool generatedRelocatable)
    {
        IReadOnlyDictionary<string, string> identity = ExtensionControlFile.Parse(primary);
        Dictionary<string, string> settings = ReadSettings(identity, authored, major, secondary: true, currentVersion);
        (_, bool relocatable) = Apply(identity, settings, !currentVersion || generatedRelocatable);
        return (ExtensionControlFile.Format(settings), relocatable);
    }

    private static Dictionary<string, string> ReadSettings(IReadOnlyDictionary<string, string> identity,
        string authored, int major, bool secondary, bool currentVersion)
    {
        var settings = new Dictionary<string, string>(ExtensionControlFile.Parse(authored), StringComparer.Ordinal);
        foreach ((string name, string value) in settings)
        {
            switch (name)
            {
                case "default_version" when secondary:
                case "directory" when secondary:
                    throw new FormatException($"Control parameter '{name}' cannot be set in a secondary extension control file.");
                case "default_version":
                case "module_pathname" when currentVersion:
                case "encoding":
                    if (value != identity[name])
                    {
                        throw new FormatException($"Control parameter '{name}' conflicts with the generated publication value '{identity[name]}'.");
                    }

                    break;
                case "relocatable":
                case "superuser":
                case "trusted":
                    settings[name] = ReadBoolean(name, value) ? "true" : "false";
                    break;
                case "no_relocate":
                    if (major < 16)
                    {
                        throw new FormatException("Control parameter 'no_relocate' requires PostgreSQL 16 or later.");
                    }

                    break;
                case "module_pathname":
                case "comment":
                case "schema":
                case "requires":
                case "directory":
                    break;
                default:
                    throw new FormatException($"Unknown extension control parameter '{name}'.");
            }
        }

        return settings;
    }

    private static (Dictionary<string, string> Values, bool Relocatable) Apply(IReadOnlyDictionary<string, string> primary,
        Dictionary<string, string> settings, bool generatedRelocatable)
    {
        if ((primary.ContainsKey("schema") || settings.ContainsKey("schema")) && !settings.ContainsKey("relocatable"))
        {
            settings["relocatable"] = "false";
        }

        var values = new Dictionary<string, string>(primary, StringComparer.Ordinal);
        foreach ((string name, string value) in settings)
        {
            values[name] = value;
        }

        bool relocatable = values["relocatable"] == "true";
        if (relocatable && (!generatedRelocatable || values.ContainsKey("schema")))
        {
            throw new FormatException("Control parameter 'relocatable' cannot be true with a fixed schema or non-relocatable generated SQL.");
        }

        return (values, relocatable);
    }

    private static bool ReadBoolean(string name, string value)
    {
        if (value.Length != 0)
        {
            if ("true".StartsWith(value, StringComparison.OrdinalIgnoreCase) ||
                "yes".StartsWith(value, StringComparison.OrdinalIgnoreCase) || value == "1" ||
                value.Equals("on", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if ("false".StartsWith(value, StringComparison.OrdinalIgnoreCase) ||
                "no".StartsWith(value, StringComparison.OrdinalIgnoreCase) || value == "0" ||
                (value.Length >= 2 && "off".StartsWith(value, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }
        }

        throw new FormatException($"Control parameter '{name}' requires a PostgreSQL Boolean value.");
    }
}
