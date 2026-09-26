using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

namespace Ankus.Build;

/// <summary>
/// Connects the existing node contract to actual declarations in a complete selected-header graph.
/// </summary>
internal static class NativeBindingNodeRecords
{
    private const string Prefix = "ankus_node_record_";
    private const string TagRoot = "ankus_node_tag_contract";

    /// <summary>
    /// Names unevaluated native object types without allocating or evaluating a backend address.
    /// </summary>
    internal static NativeBindingNodeRoots CreateRoots(NativeBindingCatalog catalog, string headers)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(headers);
        var source = new StringBuilder(headers);
        source.AppendLine();
        var requests = new List<NativeHeaderRequest>();
        source.Append("extern NodeTag ").Append(TagRoot).AppendLine(";");
        requests.Add(new(TagRoot, TagRoot, false));
        foreach (NativeBindingSelectionEntry entry in NativeBindingSelection.Create(catalog))
        {
            NativeBindingCDeclaration.ValidateName(entry.Type.Name);
            string name = Prefix + entry.Type.Name;
            source.Append("extern __typeof__(").Append(entry.Expression).Append(") ").Append(name).AppendLine(";");
            requests.Add(new(name, name, false));
        }

        return new(source.ToString(), requests.AsReadOnly());
    }

    /// <summary>
    /// Requires every existing native value and tag to agree with independently measured layout observations.
    /// </summary>
    internal static NativeBindingNodeContract Create(NativeRecordGraph graph, NativeBindingCatalog catalog, NativeBindingLayout layout)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(layout);
        NativeBindingRecordValidation.Validate(graph, graph.Target, graph.Roots.Keys);
        NativeHeaderTarget target = graph.Target;
        if (catalog.PostgresMajor != target.PostgresVersion / 10000 || layout.PostgresVersion != target.PostgresVersion ||
            layout.RuntimeIdentifier != target.RuntimeIdentifier || layout.PointerSize != target.PointerSize ||
            layout.IsLittleEndian != target.IsLittleEndian || layout.CharIsSigned != target.Numeric.CharIsSigned)
        {
            throw new FormatException("Native node and record observations describe different targets.");
        }

        IReadOnlyList<NativeBindingSelectionEntry> selected = NativeBindingSelection.Create(catalog);
        var values = new Dictionary<int, NativeBindingType>();
        if (!layout.Types.Keys.Order(StringComparer.Ordinal).SequenceEqual(selected.Select(static entry => entry.Type.Name), StringComparer.Ordinal))
        {
            throw new FormatException("Native node observations do not contain the complete selected values.");
        }

        if (graph.Roots.Keys.Any(name => name.StartsWith(Prefix, StringComparison.Ordinal) && !layout.Types.ContainsKey(name[Prefix.Length..])))
        {
            throw new FormatException("The native graph contains an unknown selected node value.");
        }

        foreach (NativeBindingSelectionEntry entry in selected)
        {
            if (entry.Type.CastTags.Any(tag => !catalog.Tags.ContainsKey(tag))) { throw new FormatException("A native node cast references an unknown tag."); }

            if (!graph.Roots.TryGetValue(Prefix + entry.Type.Name, out int root)) { throw new FormatException("A selected native node value is missing from the type graph."); }

            NativeRecordType type = Canonical(root);
            if (type.Kind != "record" || type.Declaration is not int index || !values.TryAdd(index, entry.Type))
            {
                throw new FormatException("Selected node values require distinct native record identities.");
            }

            NativeRecordDeclaration declaration = graph.Declarations[index];
            NativeBindingTypeLayout observed = layout.Types[entry.Type.Name];
            if (declaration.Kind != (entry.Type.IsUnion ? "union" : "struct") || !declaration.IsComplete ||
                declaration.Size != observed.Size || declaration.Alignment != observed.Alignment || observed.Fields.Count != entry.Type.Fields.Count)
            {
                throw new FormatException($"Native node value '{entry.Type.Name}' disagrees with its measured storage.");
            }

            foreach (NativeBindingField field in entry.Type.Fields)
            {
                NativeRecordField[] matches = [.. declaration.Fields.Where(value => value.Name == field.NativeName)];
                if (matches.Length != 1 || matches[0].BitWidth is not null || !observed.Fields.TryGetValue(field.Name, out NativeBindingFieldLayout? physical))
                {
                    throw new FormatException("A native node field is missing, duplicated or no longer addressable.");
                }

                NativeRecordField member = matches[0];
                NativeRecordType memberType = graph.Types[member.Type];
                NativeRecordType canonical = Canonical(member.Type);
                long? stride = canonical.Kind == "array" ? graph.Types[canonical.Element!.Value].Size : memberType.Size;
                if (member.OffsetBits != (long)physical.Offset * 8 || (memberType.Size ?? 0) != physical.Size ||
                    memberType.Alignment != physical.Alignment || stride != physical.ElementSize)
                {
                    throw new FormatException($"Native node field '{entry.Type.Name}.{field.NativeName}' disagrees with its independently measured layout.");
                }
            }
        }

        if (!graph.Roots.TryGetValue(TagRoot, out int tagType) || Canonical(tagType).Kind != "enum" || Canonical(tagType).Declaration is not int tagIndex)
        {
            throw new FormatException("The native node graph requires its exact NodeTag declaration root.");
        }

        NativeRecordDeclaration enumeration = graph.Declarations[tagIndex];
        if (enumeration.Kind != "enum" || enumeration.Size != sizeof(uint) || enumeration.EnumValues.Count != catalog.Tags.Count ||
            enumeration.EnumValues.Any(value => !catalog.Tags.TryGetValue(value.Name, out uint expected) || value.Value != expected.ToString(CultureInfo.InvariantCulture)))
        {
            throw new FormatException("The selected headers have a different node tag contract.");
        }

        foreach (NativeRecordType type in graph.Types.Where(static type => type.Kind == "scalar" && type.Name is "long" or "unsigned long"))
        {
            if (type.Size != layout.LongSize) { throw new FormatException("Native node observations disagree on C long storage."); }
        }

        IReadOnlyDictionary<int, string> enums = ValidateEnums(graph, catalog, layout);
        return new(new ReadOnlyDictionary<int, NativeBindingType>(values), tagIndex, enums, NativeBindingNodeLayouts.Encode(catalog, layout),
            new ReadOnlyDictionary<string, uint>(catalog.Tags.ToDictionary(StringComparer.Ordinal)));

        NativeRecordType Canonical(int index) => graph.Types[graph.Types[index].Canonical];
    }

    private static ReadOnlyDictionary<int, string> ValidateEnums(NativeRecordGraph graph, NativeBindingCatalog catalog, NativeBindingLayout layout)
    {
        IReadOnlyDictionary<string, NativeBindingEnum> selected = NativeBindingEnums.Select(catalog);
        if (!selected.Keys.SequenceEqual(layout.Enums.Keys.Order(StringComparer.Ordinal), StringComparer.Ordinal))
        {
            throw new FormatException("Native node observations do not contain the complete selected enum representations.");
        }

        var names = new Dictionary<int, string>();
        foreach ((string name, NativeBindingEnum expected) in selected)
        {
            int[] identities = [.. graph.Types.Where(type => type.Kind == "alias" && type.Name == name)
                .Select(type => graph.Types[type.Canonical].Declaration).OfType<int>().Distinct()];
            if (identities.Length != 1 || !names.TryAdd(identities[0], name))
            {
                throw new FormatException($"Native enum '{name}' requires a distinct measured identity.");
            }

            NativeRecordDeclaration declaration = graph.Declarations[identities[0]];
            if (declaration.Kind != "enum" || declaration.EnumUnderlying is not int index)
            {
                throw new FormatException($"Native enum '{name}' has no measured integer representation.");
            }

            NativeRecordType underlying = graph.Types[graph.Types[index].Canonical];
            bool? signed = underlying.Name switch
            {
                "signed char" or "short" or "int" or "long" or "long long" => true,
                "unsigned char" or "unsigned short" or "unsigned int" or "unsigned long" or "unsigned long long" => false,
                _ => null,
            };
            NativeBindingEnumLayout measured = layout.Enums[name];
            if (underlying.Kind != "scalar" || declaration.Size != measured.Size || signed != measured.IsSigned ||
                expected.Values.Any(pair => !declaration.EnumValues.Any(value => value.Name == pair.Key &&
                    value.Value == NativeBindingEnums.NativeValue(pair.Value, measured).ToString(CultureInfo.InvariantCulture))))
            {
                throw new FormatException($"Native enum '{name}' disagrees with its independently measured representation or values.");
            }
        }

        return new(names);
    }
}

/// <summary>
/// Supplies synthetic variables that let the compiler report exact named and embedded native types.
/// </summary>
/// <param name="Source">The selected headers followed by unevaluated declarations.</param>
/// <param name="Requests">The exact symbols to collect through the existing frontend boundary.</param>
internal sealed record NativeBindingNodeRoots(string Source, IReadOnlyList<NativeHeaderRequest> Requests);

/// <summary>
/// Retains verified managed names, node semantics and concrete allocation metadata for one graph.
/// </summary>
/// <param name="Values">Existing managed value identities keyed by their actual native declaration index.</param>
/// <param name="NodeTag">The exact four-byte native node discriminator declaration.</param>
/// <param name="Enums">Existing managed enum names keyed by their independently verified native declaration.</param>
/// <param name="Layouts">Concrete tag:size:alignment observations for the backend boundary.</param>
/// <param name="Tags">The complete verified node tag values.</param>
internal sealed record NativeBindingNodeContract(IReadOnlyDictionary<int, NativeBindingType> Values, int NodeTag,
    IReadOnlyDictionary<int, string> Enums, string Layouts, IReadOnlyDictionary<string, uint> Tags);
