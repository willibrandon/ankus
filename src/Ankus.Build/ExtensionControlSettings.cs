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
        var values = new Dictionary<string, string>(ExtensionControlFile.Parse(generated), StringComparer.Ordinal);
        IReadOnlyDictionary<string, string> settings = ExtensionControlFile.Parse(authored);
        bool generatedRelocatable = values["relocatable"] == "true";
        foreach ((string name, string value) in settings)
        {
            switch (name)
            {
                case "default_version":
                case "module_pathname":
                case "encoding":
                    if (value != values[name])
                    {
                        throw new FormatException($"Control parameter '{name}' conflicts with the generated publication value '{values[name]}'.");
                    }

                    break;
                case "relocatable":
                case "superuser":
                case "trusted":
                    values[name] = ReadBoolean(name, value) ? "true" : "false";
                    break;
                case "no_relocate":
                    if (major < 16)
                    {
                        throw new FormatException("Control parameter 'no_relocate' requires PostgreSQL 16 or later.");
                    }

                    values[name] = value;
                    break;
                case "comment":
                case "schema":
                case "requires":
                    values[name] = value;
                    break;
                case "directory":
                    throw new FormatException("Control parameter 'directory' is not supported; Ankus publishes SQL beside the control file.");
                default:
                    throw new FormatException($"Unknown extension control parameter '{name}'.");
            }
        }

        if (values.ContainsKey("schema") && !settings.ContainsKey("relocatable"))
        {
            values["relocatable"] = "false";
        }

        bool relocatable = values["relocatable"] == "true";
        if (relocatable && (!generatedRelocatable || values.ContainsKey("schema")))
        {
            throw new FormatException("Control parameter 'relocatable' cannot be true with a fixed schema or non-relocatable generated SQL.");
        }

        return (ExtensionControlFile.Format(values), relocatable);
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
