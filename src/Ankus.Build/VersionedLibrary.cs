using System.Text;

namespace Ankus.Build;

/// <summary>
/// Names one native library per extension version and resolves PostgreSQL's MODULE_PATHNAME marker for it.
/// </summary>
/// <remarks>
/// This follows cargo-pgrx's versioned shared-object mode. The library is <c>&lt;name&gt;-&lt;version&gt;</c> plus the
/// platform suffix, and SQL refers to it without the suffix, which PostgreSQL appends while searching
/// <c>dynamic_library_path</c>. The primary control file has no <c>module_pathname</c>, so the build performs the
/// substitution PostgreSQL would otherwise perform: every occurrence of the marker in a script is replaced.
/// </remarks>
internal static class VersionedLibrary
{
    /// <summary>
    /// Gets PostgreSQL's script marker for the control file's module path.
    /// </summary>
    internal const string Marker = "MODULE_PATHNAME";

    private static readonly UTF8Encoding s_utf8 = new(false, true);

    /// <summary>
    /// Reads the SDK's Boolean argument, treating an omitted value as an ordinary unversioned library.
    /// </summary>
    /// <param name="value">The MSBuild property text.</param>
    /// <returns>Whether the publication uses versioned libraries.</returns>
    /// <exception cref="ArgumentException">The value is neither empty, <c>true</c> nor <c>false</c>.</exception>
    internal static bool ParseMode(string value)
        => value switch
        {
            "true" => true,
            "false" or "" => false,
            _ => throw new ArgumentException("AnkusVersionedLibrary must be true or false.", nameof(value)),
        };

    /// <summary>
    /// Gets the library's base name, which precedes the version in each version's library filename.
    /// </summary>
    /// <param name="library">The published library filename, including the platform suffix.</param>
    /// <param name="version">The extension version that the library implements.</param>
    /// <returns>The filename stem without its trailing <c>-&lt;version&gt;</c>.</returns>
    /// <exception cref="ArgumentException">The filename does not end with the version before its suffix.</exception>
    internal static string GetBaseName(string library, string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(library);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        string stem = Path.GetFileNameWithoutExtension(library);
        string suffix = "-" + version;
        if (stem.Length <= suffix.Length || !stem.EndsWith(suffix, StringComparison.Ordinal))
        {
            throw new ArgumentException($"A versioned native library must be named '<name>{suffix}' before its platform suffix.", nameof(library));
        }

        return stem[..^suffix.Length];
    }

    /// <summary>
    /// Gets the SQL module path of another version's library with the same base name.
    /// </summary>
    /// <param name="baseName">The library base name.</param>
    /// <param name="version">The extension version whose library is referenced.</param>
    /// <returns>The suffix-free name that PostgreSQL resolves through <c>dynamic_library_path</c>.</returns>
    internal static string GetModulePath(string baseName, string version) => baseName + "-" + version;

    /// <summary>
    /// Replaces each marker occurrence exactly as PostgreSQL replaces it when a control file names the module.
    /// </summary>
    /// <param name="text">The script text.</param>
    /// <param name="modulePath">The versioned library's module path.</param>
    /// <returns>The script that references the versioned library directly.</returns>
    internal static string Substitute(string text, string modulePath) => text.Replace(Marker, modulePath, StringComparison.Ordinal);

    /// <summary>
    /// Applies the same substitution to an encoded compiler graph so its declarations still reproduce the script.
    /// </summary>
    /// <param name="encoded">The base64 graph emitted by the generator.</param>
    /// <param name="modulePath">The versioned library's module path.</param>
    /// <returns>The graph with substituted preamble and declaration SQL; other fields are copied unchanged.</returns>
    /// <exception cref="FormatException">The graph framing is invalid.</exception>
    internal static string SubstituteGraph(string encoded, string modulePath)
    {
        try
        {
            byte[] bytes = Convert.FromBase64String(encoded);
            if (bytes.Length < 12 || !bytes.AsSpan(0, 8).SequenceEqual("ANKUSG1\0"u8) && !bytes.AsSpan(0, 8).SequenceEqual("ANKUSG2\0"u8))
            {
                throw new FormatException("Invalid embedded SQL graph header.");
            }

            using var input = new MemoryStream(bytes, writable: false);
            using var reader = new BinaryReader(input, s_utf8);
            using var output = new MemoryStream(bytes.Length);
            using (var writer = new BinaryWriter(output, s_utf8, leaveOpen: true))
            {
                writer.Write(reader.ReadBytes(8));
                if (bytes[6] == (byte)'2')
                {
                    CopyText(substitute: true);
                }

                int count = CopyCount();
                for (int index = 0; index < count; index++)
                {
                    CopyText(substitute: false);
                    CopyText(substitute: false);
                    CopyText(substitute: true);
                    CopyText(substitute: false);
                    for (int list = 0; list < 3; list++)
                    {
                        int values = CopyCount();
                        for (int value = 0; value < values; value++)
                        {
                            CopyText(substitute: false);
                        }
                    }
                }

                if (input.Position != input.Length)
                {
                    throw new FormatException("Unexpected trailing embedded SQL graph data.");
                }

                int CopyCount()
                {
                    int value = reader.ReadInt32();
                    if (value < 0 || value > (input.Length - input.Position) / 4)
                    {
                        throw new FormatException("Invalid embedded SQL graph count.");
                    }

                    writer.Write(value);
                    return value;
                }

                void CopyText(bool substitute)
                {
                    int length = reader.ReadInt32();
                    if (length < 0 || length > input.Length - input.Position)
                    {
                        throw new FormatException("Invalid embedded SQL graph text length.");
                    }

                    byte[] text = reader.ReadBytes(length);
                    if (substitute)
                    {
                        text = s_utf8.GetBytes(Substitute(s_utf8.GetString(text), modulePath));
                    }

                    writer.Write(text.Length);
                    writer.Write(text);
                }
            }

            return Convert.ToBase64String(output.ToArray());
        }
        catch (Exception error) when (error is EndOfStreamException or DecoderFallbackException)
        {
            throw new FormatException("Invalid embedded SQL graph encoding or framing.", error);
        }
    }
}
