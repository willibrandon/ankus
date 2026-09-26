using System.Text;

namespace Ankus.Build;

/// <summary>
/// Emits guarded native bodies only for the generated accessors referenced by one consuming native object.
/// </summary>
internal static class NativeBindingCallImports
{
    /// <summary>
    /// Names pure address accessors without exposing an extension-specific dynamic library name to managed consumers.
    /// </summary>
    internal const string Prefix = "ankus_native_body_";

    /// <summary>
    /// Validates the object target and the complete shared declaration graph, then lowers its referenced fixed functions.
    /// </summary>
    /// <param name="records">The full selected-header signature and storage contract.</param>
    /// <param name="headers">The original C headers that must independently satisfy that contract.</param>
    /// <param name="image">The consuming compiler's relocatable native object.</param>
    /// <returns>Selected C bodies and pure address accessors, with no registry rooting unused imports.</returns>
    internal static string Generate(NativeHeaderRecords records, string headers, ReadOnlySpan<byte> image)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(headers);
        NativeObjectImports imports = NativeObjectSymbols.Read(image, Prefix);
        NativeHeaderTarget target = records.Headers.Target;
        int separator = target.RuntimeIdentifier.LastIndexOf('-');
        string format = separator < 0 ? "" : target.RuntimeIdentifier[..separator] switch
        {
            "win" => "coff", "osx" => "mach-o", "linux" or "linux-musl" => "elf", _ => "",
        };
        if (format != imports.Format || target.RuntimeIdentifier[(separator + 1)..] != imports.Architecture ||
            target.IsLittleEndian != imports.IsLittleEndian)
        {
            throw new FormatException("Native call imports do not match the selected header target.");
        }

        string[] names = [.. imports.Symbols.Select(static name => name[Prefix.Length..])];
        var source = new StringBuilder(NativeBindingCallSource.Generate(records, headers, names));
        source.AppendLine("typedef int (*AnkusNativeCallBody)(const AnkusNativeCallArgument *, size_t, void *, size_t);");
        foreach (string name in names)
        {
            // Accessors never enter PostgreSQL. The returned body still requires NativeRawCall's native guard.
            source.AppendLine("#if !defined(_WIN32)\n__attribute__((visibility(\"hidden\")))\n#endif");
            source.Append("AnkusNativeCallBody ").Append(Prefix).Append(name).AppendLine("(void)");
            source.Append("{ return ankus_native_call_").Append(name).AppendLine("; }");
        }

        return source.ToString().ReplaceLineEndings("\n");
    }
}
