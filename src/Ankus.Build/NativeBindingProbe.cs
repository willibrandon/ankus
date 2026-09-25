using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

namespace Ankus.Build;

/// <summary>
/// Emits and validates native layout observations for every node and embedded value dependency.
/// </summary>
internal static class NativeBindingProbe
{
    /// <summary>
    /// Emits a standalone program that observes layouts through the selected PostgreSQL headers.
    /// </summary>
    /// <param name="catalog">The declarations whose physical representation is required.</param>
    /// <param name="headers">The pinned pgrx header include manifest.</param>
    /// <returns>C source that prints versioned, complete ABI observations.</returns>
    internal static string GenerateSource(NativeBindingCatalog catalog, string headers)
    {
        var source = new StringBuilder(headers);
        source.AppendLine();
        source.AppendLine("#include <stddef.h>");
        source.AppendLine("#include <limits.h>");
        source.AppendLine("#include <stdlib.h>");
        source.AppendLine("#undef printf");
        source.AppendLine("#undef malloc");
        source.AppendLine("#undef free");
        source.AppendLine("#if defined(_MSC_VER)");
        source.AppendLine("#define ANKUS_ALIGNOF(value) __alignof(__typeof__(value))");
        source.AppendLine("#else");
        source.AppendLine("#define ANKUS_ALIGNOF(value) __alignof__(value)");
        source.AppendLine("#endif");
        NativeBindingTarget.Write(source);
        source.AppendLine(CultureInfo.InvariantCulture, $"#if PG_VERSION_NUM / 10000 != {catalog.PostgresMajor}");
        source.AppendLine("#error PostgreSQL headers do not match the requested binding major");
        source.AppendLine("#endif");
        source.AppendLine("int main(void)");
        source.AppendLine("{");
        source.AppendLine("    unsigned int endian = 1;");
        source.AppendLine("    printf(\"header|2|%d|%zu|%zu|%d|%d|%s-%s\\n\", PG_VERSION_NUM, sizeof(void*), sizeof(long), CHAR_MIN < 0, *((unsigned char*)&endian) == 1, ANKUS_NATIVE_OS, ANKUS_NATIVE_ARCH);");
        foreach (string tag in catalog.Tags.Keys)
        {
            source.AppendLine(CultureInfo.InvariantCulture, $"    printf(\"tag|{tag}|%u\\n\", (unsigned){tag});");
        }

        NativeBindingEnums.Write(source, catalog);
        foreach (NativeBindingSelectionEntry entry in NativeBindingSelection.Create(catalog))
        {
            string value = entry.Path.Length == 0 ? "*value" : $"value->{entry.Path}";
            source.AppendLine("    {");
            source.AppendLine(CultureInfo.InvariantCulture,
                $"        {entry.Root}* value = malloc(sizeof({entry.Root}) + sizeof({entry.Expression}));");
            source.AppendLine("        if (value == NULL) return 1;");
            source.AppendLine(CultureInfo.InvariantCulture,
                $"        printf(\"type|{entry.Type.Name}|%zu|%zu\\n\", sizeof({value}), (size_t)ANKUS_ALIGNOF({value}));");
            foreach (NativeBindingField field in entry.Type.Fields)
            {
                string representation = NativeBindingSelection.ResolveAlias(catalog, field.Representation);
                bool flexible = representation.StartsWith("__IncompleteArrayField<", StringComparison.Ordinal);
                bool array = NativeBindingSelection.ArrayElement(representation) is not null;
                string expression = $"({value}).{field.NativeName}";
                string size = flexible ? "(size_t)0" : $"sizeof({expression})";
                string element = array ? expression + "[0]" : expression;
                source.AppendLine(CultureInfo.InvariantCulture,
                    $"        printf(\"field|{entry.Type.Name}|{field.Name}|%zu|%zu|%zu|%zu\\n\", (size_t)((char*)&({expression}) - (char*)&({value})), {size}, (size_t)ANKUS_ALIGNOF({expression}), sizeof({element}));");
            }

            source.AppendLine("        free(value);");
            source.AppendLine("    }");
        }

        source.AppendLine("    return 0;");
        source.AppendLine("}");
        return source.ToString();
    }

    /// <summary>
    /// Validates complete probe output before any measured representation can generate managed bindings.
    /// </summary>
    /// <param name="catalog">The expected native declarations and node tags.</param>
    /// <param name="output">The successful probe's standard output.</param>
    /// <returns>The validated physical layout.</returns>
    internal static NativeBindingLayout Read(NativeBindingCatalog catalog, string output)
    {
        string[] lines = output.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        string[] header = lines.Length == 0 ? [] : lines[0].Split('|');
        if (header.Length != 8 || header[0] != "header" || header[1] != "2")
        {
            throw new FormatException("Missing or unsupported native layout header.");
        }

        int version = Number(header[2]);
        int pointer = Number(header[3]);
        int nativeLong = Number(header[4]);
        if (version / 10000 != catalog.PostgresMajor || pointer is not (4 or 8) || nativeLong is not (4 or 8) ||
            header[5] is not ("0" or "1") || header[6] is not ("0" or "1") ||
            !NativeBindingTarget.IsValid(header[7], pointer, header[6] == "1"))
        {
            throw new FormatException("Native layout version or primitive representation does not match the supported contract.");
        }

        IReadOnlyDictionary<string, NativeBindingEnumLayout> enums = NativeBindingEnums.Read(catalog, lines);
        Dictionary<string, NativeBindingType> expected = NativeBindingSelection.Create(catalog)
            .ToDictionary(static entry => entry.Type.Name, static entry => entry.Type, StringComparer.Ordinal);
        var tags = new HashSet<string>(StringComparer.Ordinal);
        var types = new SortedDictionary<string, (int Size, int Alignment, Dictionary<string, NativeBindingFieldLayout> Fields)>(StringComparer.Ordinal);
        foreach (string line in lines.Skip(1))
        {
            string[] parts = line.Split('|');
            switch (parts[0])
            {
                case "enum":
                case "constant":
                    break;
                case "tag" when parts.Length == 3:
                    if (!catalog.Tags.TryGetValue(parts[1], out uint expectedTag) || !tags.Add(parts[1]) ||
                        !uint.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out uint tag) || tag != expectedTag)
                    {
                        throw new FormatException($"Mismatched or duplicate native tag: {line}");
                    }

                    break;
                case "type" when parts.Length == 4:
                    int size = Number(parts[2]);
                    int alignment = Number(parts[3]);
                    if (!expected.ContainsKey(parts[1]) || size == 0 || !IsAlignment(alignment) || size % alignment != 0 ||
                        !types.TryAdd(parts[1], (size, alignment, new(StringComparer.Ordinal))))
                    {
                        throw new FormatException($"Invalid or duplicate native value: {line}");
                    }

                    break;
                case "field" when parts.Length == 7:
                    if (!types.TryGetValue(parts[1], out (int Size, int Alignment, Dictionary<string, NativeBindingFieldLayout> Fields) type))
                    {
                        throw new FormatException($"Missing enclosing native value: {line}");
                    }

                    NativeBindingField? declaration = expected[parts[1]].Fields.SingleOrDefault(field => field.Name == parts[2]);
                    int offset = Number(parts[3]);
                    int length = Number(parts[4]);
                    int fieldAlignment = Number(parts[5]);
                    int elementSize = Number(parts[6]);
                    if (declaration is null || offset > type.Size || length > type.Size - offset || !IsAlignment(fieldAlignment) || elementSize == 0)
                    {
                        throw new FormatException($"Invalid native field extent: {line}");
                    }

                    string representation = NativeBindingSelection.ResolveAlias(catalog, declaration.Representation);
                    bool flexible = representation.StartsWith("__IncompleteArrayField<", StringComparison.Ordinal);
                    bool array = NativeBindingSelection.ArrayElement(representation) is not null;
                    if (flexible ? length != 0 : length == 0 || length % elementSize != 0 || !array && length != elementSize)
                    {
                        throw new FormatException($"Invalid native field element size: {line}");
                    }

                    if (!type.Fields.TryAdd(parts[2], new(offset, length, fieldAlignment, elementSize)))
                    {
                        throw new FormatException($"Duplicate native field: {line}");
                    }

                    break;
                default:
                    throw new FormatException($"Unknown native layout record: {line}");
            }
        }

        if (tags.Count != catalog.Tags.Count || types.Count != expected.Count ||
            types.Any(type => type.Value.Fields.Count != expected[type.Key].Fields.Count))
        {
            throw new FormatException("Native layout output is incomplete.");
        }

        var layouts = new SortedDictionary<string, NativeBindingTypeLayout>(StringComparer.Ordinal);
        foreach ((string name, (int size, int alignment, Dictionary<string, NativeBindingFieldLayout> fields)) in types)
        {
            layouts.Add(name, new(size, alignment, new ReadOnlyDictionary<string, NativeBindingFieldLayout>(fields)));
        }

        return new(version, pointer, nativeLong, header[5] == "1", header[6] == "1", header[7],
            new ReadOnlyDictionary<string, NativeBindingTypeLayout>(layouts), enums);
    }

    private static bool IsAlignment(int value) => value > 0 && (value & (value - 1)) == 0;

    private static int Number(string value)
        => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int number)
            ? number : throw new FormatException($"Invalid native layout number: {value}");
}
