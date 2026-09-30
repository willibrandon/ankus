using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;

namespace Ankus.Build;

/// <summary>
/// Selects node roots and discriminator values from successful observations of the installed headers.
/// </summary>
internal static class NativeBindingNodeTags
{
    /// <summary>
    /// Reads the complete native NodeTag enum before emitting any reference node declarations.
    /// </summary>
    /// <param name="root">The successful header-only Clang translation unit.</param>
    /// <returns>The exact names and unsigned values from the selected headers.</returns>
    internal static IReadOnlyDictionary<string, uint> Read(JsonElement root)
    {
        if (Text(root, "kind") != "TranslationUnitDecl" || !root.TryGetProperty("inner", out JsonElement children) ||
            children.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("Native node tags require a complete translation unit.");
        }

        JsonElement[] declarations = [.. children.EnumerateArray().Where(static child => Text(child, "kind") == "EnumDecl" &&
            Text(child, "name") == "NodeTag" && child.TryGetProperty("inner", out JsonElement members) && members.ValueKind == JsonValueKind.Array &&
            members.EnumerateArray().Any(static member => Text(member, "kind") == "EnumConstantDecl"))];
        if (declarations.Length != 1)
        {
            throw new FormatException("The selected headers must define exactly one NodeTag enum.");
        }

        var tags = new Dictionary<string, uint>(StringComparer.Ordinal);
        ulong next = 0;
        foreach (JsonElement member in declarations[0].GetProperty("inner").EnumerateArray())
        {
            if (Text(member, "kind") != "EnumConstantDecl")
            {
                continue;
            }

            string name = Text(member, "name");
            string? evaluated = ConstantValue(member);
            uint value;
            if (evaluated is not null)
            {
                if (!uint.TryParse(evaluated, NumberStyles.None, CultureInfo.InvariantCulture, out value))
                {
                    throw new FormatException("A native node tag does not fit an unsigned four-byte discriminator.");
                }
            }
            else
            {
                if (member.TryGetProperty("inner", out JsonElement expressions) && expressions.EnumerateArray().Any(static expression =>
                    !Text(expression, "kind").EndsWith("Attr", StringComparison.Ordinal) && !Text(expression, "kind").EndsWith("Comment", StringComparison.Ordinal)))
                {
                    throw new FormatException("A native node tag initializer has no compiler-evaluated value.");
                }

                if (next > uint.MaxValue)
                {
                    throw new FormatException("An implicit native node tag exceeds its discriminator storage.");
                }

                value = (uint)next;
            }

            if (!tags.TryAdd(name, value))
            {
                throw new FormatException("The native NodeTag enum repeats an identifier.");
            }

            next = (ulong)value + 1;
        }

        Validate(tags);
        return new ReadOnlyDictionary<string, uint>(tags);
    }

    /// <summary>
    /// Reconciles reference node identities with a separately collected, validated native record graph.
    /// </summary>
    internal static NativeBindingCatalog Select(NativeBindingCatalog catalog, NativeRecordGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        NativeBindingRecordValidation.Validate(graph, graph.Target, graph.Roots.Keys);
        if (!graph.Roots.TryGetValue("ankus_node_tag_contract", out int root))
        {
            throw new FormatException("The native node discriminator root is missing.");
        }

        NativeRecordType type = graph.Types[graph.Types[root].Canonical];
        if (type.Kind != "enum" || type.Declaration is not int declaration ||
            graph.Declarations[declaration].Name != "NodeTag" || graph.Declarations[declaration].Size != sizeof(uint))
        {
            throw new FormatException("The native node discriminator is not the four-byte NodeTag enum.");
        }

        var tags = new Dictionary<string, uint>(StringComparer.Ordinal);
        foreach (NativeRecordConstant constant in graph.Declarations[declaration].EnumValues)
        {
            if (!uint.TryParse(constant.Value, NumberStyles.None, CultureInfo.InvariantCulture, out uint value) || !tags.TryAdd(constant.Name, value))
            {
                throw new FormatException("Invalid native node discriminator value.");
            }
        }

        return Select(catalog, tags);
    }

    /// <summary>
    /// Preserves available node identities and cast families while replacing reference discriminator numbers.
    /// </summary>
    internal static NativeBindingCatalog Select(NativeBindingCatalog catalog, IReadOnlyDictionary<string, uint> tags)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(tags);
        Validate(tags);
        var types = new Dictionary<string, NativeBindingType>(StringComparer.Ordinal);
        foreach ((string name, NativeBindingType type) in catalog.Types)
        {
            if (!type.IsNode || type.CastTags.Count == 0 || type.CastTags.Any(tags.ContainsKey))
            {
                types.Add(name, type with { CastTags = Array.AsReadOnly(type.CastTags.Where(tags.ContainsKey).ToArray()) });
            }
        }

        return catalog with
        {
            Tags = new ReadOnlyDictionary<string, uint>(tags.ToDictionary(StringComparer.Ordinal)),
            Types = new ReadOnlyDictionary<string, NativeBindingType>(types),
        };
    }

    private static void Validate(IReadOnlyDictionary<string, uint> tags)
    {
        if (!tags.TryGetValue("T_Invalid", out uint invalid) || invalid != 0 || tags.Values.Distinct().Count() != tags.Count)
        {
            throw new FormatException("Native NodeTag requires distinct values and T_Invalid = 0.");
        }

        foreach (string name in tags.Keys)
        {
            NativeBindingCDeclaration.ValidateName(name);
            if (!name.StartsWith("T_", StringComparison.Ordinal))
            {
                throw new FormatException("Invalid native node tag identifier.");
            }
        }
    }

    private static string? ConstantValue(JsonElement element)
    {
        if (Text(element, "kind") == "ConstantExpr")
        {
            if (!element.TryGetProperty("value", out JsonElement value) || value.ValueKind != JsonValueKind.String)
            {
                throw new FormatException("A native node tag constant has no compiler-evaluated value.");
            }

            return value.GetString();
        }

        if (element.TryGetProperty("inner", out JsonElement children))
        {
            if (children.ValueKind != JsonValueKind.Array)
            {
                throw new FormatException("Invalid native node tag expression.");
            }

            foreach (JsonElement child in children.EnumerateArray())
            {
                if (ConstantValue(child) is string value)
                {
                    return value;
                }
            }
        }

        return null;
    }

    private static string Text(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()! : "";
}
