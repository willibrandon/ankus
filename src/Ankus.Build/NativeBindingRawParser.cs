using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace Ankus.Build;

/// <summary>
/// Retains foreign declarations separately from Rust helper implementations and reference constants.
/// </summary>
internal static partial class NativeBindingRawParser
{
    /// <summary>
    /// Reads top-level bindgen items using masked offsets and preserves their original expressions.
    /// </summary>
    internal static NativeBindingRawCatalog Parse(string source, int postgresMajor)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfLessThan(postgresMajor, 13);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(postgresMajor, 19);
        string code = NativeBindingParser.MaskTrivia(source);
        var functions = new SortedDictionary<string, NativeBindingFunction>(StringComparer.Ordinal);
        var globals = new SortedDictionary<string, NativeBindingGlobal>(StringComparer.Ordinal);
        var constants = new SortedDictionary<string, NativeBindingConstant>(StringComparer.Ordinal);
        int scanned = 0;
        int depth = 0;
        foreach (Match declaration in TopLevelDeclaration().Matches(code))
        {
            for (; scanned < declaration.Index; scanned++)
            {
                if (code[scanned] == '{') { depth++; }
                else if (code[scanned] == '}') { depth--; }
            }

            if (depth != 0) { continue; }

            int start = declaration.Index + declaration.Length;
            if (declaration.Groups["name"].Success)
            {
                int valueStart = NativeBindingParser.ReadUntil(code, start, '=');
                int end = NativeBindingParser.ReadUntil(code, valueStart + 1, ';', trackAngles: false);
                string name = declaration.Groups["name"].Value;
                string representation = RequiredType(source, code, start, valueStart);
                string expression = source[(valueStart + 1)..end].Trim();
                if (expression.Length == 0 || !constants.TryAdd(name, new(representation, expression)))
                {
                    throw new FormatException($"Invalid or duplicate reference constant {name}.");
                }
            }
            else
            {
                int bodyStart = start - 1;
                Match header = ForeignHeader().Match(source[declaration.Index..(bodyStart + 1)].Trim());
                if (!header.Success) { throw new FormatException("Unsupported bindgen foreign ABI declaration."); }

                int bodyEnd = NativeBindingParser.ReadUntil(code, bodyStart + 1, '}', trackAngles: false);
                ReadForeignItems(source, code, bodyStart + 1, bodyEnd, header.Groups["abi"].Value, functions, globals);
            }
        }

        if (constants.Keys.Any(name => functions.ContainsKey(name) || globals.ContainsKey(name)))
        {
            throw new FormatException("A reference constant conflicts with a foreign declaration.");
        }

        return new(postgresMajor, new ReadOnlyDictionary<string, NativeBindingFunction>(functions),
            new ReadOnlyDictionary<string, NativeBindingGlobal>(globals),
            new ReadOnlyDictionary<string, NativeBindingConstant>(constants));
    }

    /// <summary>
    /// Consumes every item in a foreign block so unsupported or malformed declarations cannot disappear silently.
    /// </summary>
    private static void ReadForeignItems(string source, string code, int position, int end, string abi,
        SortedDictionary<string, NativeBindingFunction> functions, SortedDictionary<string, NativeBindingGlobal> globals)
    {
        while (position < end)
        {
            SkipWhitespace(code, ref position);
            if (position == end) { break; }

            var attributes = new List<string>();
            string? linkName = null;
            while (position + 1 < end && code[position] == '#' && code[position + 1] == '[')
            {
                int attributeEnd = NativeBindingParser.ReadUntil(code, position + 2, ']', trackAngles: false);
                string attribute = source[position..(attributeEnd + 1)];
                attributes.Add(attribute);
                if (code[(position + 2)..attributeEnd].TrimStart().StartsWith("link_name", StringComparison.Ordinal))
                {
                    Match link = LinkName().Match(attribute);
                    if (!link.Success || linkName is not null) { throw new FormatException("Invalid or duplicate foreign link_name."); }

                    linkName = link.Groups["name"].Value;
                }

                position = attributeEnd + 1;
                SkipWhitespace(code, ref position);
            }

            Match item = ForeignItem().Match(code, position);
            if (!item.Success || item.Index != position || position >= end)
            {
                throw new FormatException("Unsupported or incomplete bindgen foreign item.");
            }

            string name = item.Groups["name"].Value;
            if (functions.ContainsKey(name) || globals.ContainsKey(name)) { throw new FormatException($"Duplicate foreign declaration {name}."); }

            position += item.Length;
            int itemEnd;
            if (item.Groups["kind"].Value == "fn")
            {
                int parametersEnd = NativeBindingParser.ReadUntil(code, position, ')');
                itemEnd = NativeBindingParser.ReadUntil(code, parametersEnd + 1, ';');
                if (itemEnd >= end) { throw new FormatException($"Unterminated foreign declaration {name}."); }

                (IReadOnlyList<NativeBindingParameter> parameters, bool variadic) = ReadParameters(
                    source[position..parametersEnd], code[position..parametersEnd]);
                int resultStart = parametersEnd + 1;
                SkipWhitespace(code, ref resultStart);
                string result = "()";
                if (resultStart < itemEnd)
                {
                    if (!code.AsSpan(resultStart).StartsWith("->", StringComparison.Ordinal))
                    {
                        throw new FormatException($"Invalid return declaration for {name}.");
                    }

                    result = RequiredType(source, code, resultStart + 2, itemEnd);
                }

                functions.Add(name, new(linkName ?? name, abi, parameters, result, variadic, attributes.AsReadOnly()));
            }
            else
            {
                itemEnd = NativeBindingParser.ReadUntil(code, position, ';');
                if (itemEnd >= end) { throw new FormatException($"Unterminated foreign declaration {name}."); }

                string representation = RequiredType(source, code, position, itemEnd);
                globals.Add(name, new(linkName ?? name, abi, representation, item.Groups["mutable"].Success, attributes.AsReadOnly()));
            }

            position = itemEnd + 1;
        }
    }

    /// <summary>
    /// Splits only outer parameter commas, preserving nested callbacks, generic types and arrays.
    /// </summary>
    private static (IReadOnlyList<NativeBindingParameter> Parameters, bool Variadic) ReadParameters(string source, string code)
    {
        var parameters = new List<NativeBindingParameter>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        bool variadic = false;
        int position = 0;
        string terminated = code + ",";
        while (position < code.Length)
        {
            SkipWhitespace(code, ref position);
            if (position == code.Length) { break; }

            if (variadic) { throw new FormatException("Variadic marker must be the final foreign parameter."); }

            int end = NativeBindingParser.ReadUntil(terminated, position, ',');
            string parameter = code[position..end].Trim();
            if (parameter == "...") { variadic = true; }
            else
            {
                Match name = ParameterName().Match(code, position);
                if (!name.Success || name.Index != position || name.Index + name.Length > end ||
                    !names.Add(name.Groups["name"].Value))
                {
                    throw new FormatException("Invalid or duplicate foreign parameter.");
                }

                parameters.Add(new(name.Groups["name"].Value, RequiredType(source, code, position + name.Length, end)));
            }

            position = end + 1;
        }

        return (parameters.AsReadOnly(), variadic);
    }

    private static string RequiredType(string source, string code, int start, int end)
    {
        if (code.AsSpan(start, end - start).Trim().IsEmpty) { throw new FormatException("Missing bindgen type representation."); }

        return source[start..end].Trim();
    }

    private static void SkipWhitespace(string code, ref int position)
    {
        while (position < code.Length && char.IsWhiteSpace(code[position])) { position++; }
    }

    [GeneratedRegex(@"(?m)^[ \t]*(?:(?:unsafe\s+)?extern\s+\{|pub[ \t]+const[ \t]+(?<name>[A-Za-z_]\w*)\s*:)")]
    private static partial Regex TopLevelDeclaration();

    [GeneratedRegex("^(?:unsafe\\s+)?extern\\s+\"(?<abi>C(?:-unwind)?)\"\\s*\\{$")]
    private static partial Regex ForeignHeader();

    [GeneratedRegex(@"pub\s+(?:(?<kind>fn)\s+(?<name>[A-Za-z_]\w*)\s*\(|(?<kind>static)\s+(?<mutable>mut\s+)?(?<name>[A-Za-z_]\w*)\s*:)")]
    private static partial Regex ForeignItem();

    [GeneratedRegex(@"(?<name>[A-Za-z_]\w*)\s*:")]
    private static partial Regex ParameterName();

    [GeneratedRegex("^#\\[\\s*link_name\\s*=\\s*\"(?<name>[^\"\\\\]+)\"\\s*\\]$")]
    private static partial Regex LinkName();
}

/// <summary>
/// Groups the foreign and reference-value inventory independently of measured native layouts.
/// </summary>
/// <param name="PostgresMajor">The PostgreSQL major represented by the declarations.</param>
/// <param name="Functions">Foreign functions indexed by bindgen name.</param>
/// <param name="Globals">Foreign globals indexed by bindgen name.</param>
/// <param name="ReferenceConstants">Unevaluated constants indexed by bindgen name.</param>
internal sealed record NativeBindingRawCatalog(int PostgresMajor, IReadOnlyDictionary<string, NativeBindingFunction> Functions,
    IReadOnlyDictionary<string, NativeBindingGlobal> Globals,
    IReadOnlyDictionary<string, NativeBindingConstant> ReferenceConstants)
{
    /// <summary>
    /// Gets the pgrx commit read by catalog generation, or null for standalone parsed input.
    /// </summary>
    public string? SourceRevision { get; init; }
}
