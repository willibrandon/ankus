using System.Collections.ObjectModel;

namespace Ankus.Build;

/// <summary>
/// Selects available reference nodes and fields while preserving native tag values and verified prefix contracts.
/// </summary>
internal static class NativeBindingNodeAvailability
{
    /// <summary>
    /// Retains present reference fields and records explicit absences before generating native member expressions.
    /// </summary>
    internal static NativeBindingSelectedNodes Read(NativeBindingCatalog catalog, NativeRecordGraph graph)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(graph);
        NativeBindingCatalog reference = catalog;
        catalog = NativeBindingNodeTags.Select(catalog, graph);
        if (graph.Target.PostgresVersion / 10000 != catalog.PostgresMajor)
        {
            throw new FormatException("Native node availability describes a different PostgreSQL major.");
        }

        Dictionary<string, NativeBindingType> types = catalog.Types.ToDictionary(StringComparer.Ordinal);
        var absent = new List<NativeBindingAbsentNodeField>();
        var declarations = new Dictionary<string, int>(StringComparer.Ordinal);
        var pending = new Queue<(NativeBindingType Type, int Native)>();
        foreach (NativeBindingType type in catalog.Types.Values.Where(static type => type.IsNode).OrderBy(static type => type.Name, StringComparer.Ordinal))
        {
            pending.Enqueue((type, Root(type)));
        }

        while (pending.TryDequeue(out (NativeBindingType Type, int Native) entry))
        {
            NativeRecordType native = Canonical(entry.Native);
            if (native.Kind != "record" || native.Declaration is not int index || !graph.Declarations[index].IsComplete ||
                graph.Declarations[index].Kind != (entry.Type.IsUnion ? "union" : "struct"))
            {
                throw new FormatException("A reference node value has a different native declaration kind.");
            }

            if (declarations.TryGetValue(entry.Type.Name, out int previous))
            {
                if (previous != index)
                {
                    throw new FormatException("One reference value names distinct native declaration identities.");
                }

                continue;
            }

            declarations.Add(entry.Type.Name, index);
            NativeRecordDeclaration declaration = graph.Declarations[index];
            if (entry.Type.IsNode && !entry.Type.IsUnion)
            {
                ValidatePrefix(entry.Type, declaration);
            }

            var fields = new List<NativeBindingField>();
            foreach (NativeBindingField field in entry.Type.Fields)
            {
                NativeRecordField? member = declaration.Fields.SingleOrDefault(value => value.Name == field.NativeName);
                string representation = NativeBindingSelection.ResolveAlias(catalog, field.Representation);
                if (member is null)
                {
                    if (entry.Type.IsNode && entry.Type.IsUnion && catalog.Types.TryGetValue(representation, out NativeBindingType? node) && node.IsNode)
                    {
                        throw new FormatException("A native node union lost a member required by its cast contract.");
                    }

                    absent.Add(new(entry.Type.Name, field.NativeName));
                    continue;
                }

                fields.Add(field);
                NativeRecordType value = Canonical(member.Type);
                while (NativeBindingSelection.ArrayElement(representation) is string element)
                {
                    if (value.Kind != "array" || value.Element is not int elementType)
                    {
                        throw new FormatException("A reference array field has a different native element contract.");
                    }

                    representation = NativeBindingSelection.ResolveAlias(catalog, element);
                    value = Canonical(elementType);
                }

                if (catalog.Types.TryGetValue(representation, out NativeBindingType? embedded))
                {
                    pending.Enqueue((embedded, value.Canonical));
                }
            }

            types[entry.Type.Name] = entry.Type with { Fields = fields.AsReadOnly() };
        }

        catalog = NativeBindingEnums.SelectNative(catalog with { Types = new ReadOnlyDictionary<string, NativeBindingType>(types) }, graph);
        NativeBindingEnumChanges[] enums = [.. NativeBindingEnums.Select(catalog).OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new NativeBindingEnumChanges(pair.Key,
                Array.AsReadOnly(reference.Enums[pair.Key].Values.Keys.Except(pair.Value.Values.Keys).Order(StringComparer.Ordinal).ToArray()),
                Array.AsReadOnly(pair.Value.Values.Keys.Except(reference.Enums[pair.Key].Values.Keys).Order(StringComparer.Ordinal).ToArray()),
                Array.AsReadOnly(pair.Value.Values.Where(value => reference.Enums[pair.Key].Values.TryGetValue(value.Key, out string? previous) && previous != value.Value)
                    .OrderBy(static value => value.Key, StringComparer.Ordinal)
                    .Select(value => new NativeBindingChangedEnumValue(value.Key, reference.Enums[pair.Key].Values[value.Key], value.Value)).ToArray())))
            .Where(static value => value.AbsentValues.Count != 0 || value.AdditionalValues.Count != 0 || value.ChangedValues.Count != 0)];
        return new(catalog,
            Array.AsReadOnly(absent.OrderBy(static field => field.Type, StringComparer.Ordinal).ThenBy(static field => field.Field, StringComparer.Ordinal).ToArray()),
            new(Array.AsReadOnly(reference.Types.Keys.Except(catalog.Types.Keys).Order(StringComparer.Ordinal).ToArray()),
                Array.AsReadOnly(reference.Tags.Keys.Except(catalog.Tags.Keys).Order(StringComparer.Ordinal).ToArray()),
                Array.AsReadOnly(catalog.Tags.Keys.Except(reference.Tags.Keys).Order(StringComparer.Ordinal).ToArray()),
                Array.AsReadOnly(catalog.Tags.Where(pair => reference.Tags.TryGetValue(pair.Key, out uint value) && value != pair.Value)
                    .OrderBy(static pair => pair.Key, StringComparer.Ordinal).Select(pair => new NativeBindingChangedNodeTag(pair.Key, reference.Tags[pair.Key], pair.Value)).ToArray()),
                Array.AsReadOnly(enums)));

        NativeRecordType Canonical(int index) => graph.Types[graph.Types[index].Canonical];

        int Root(NativeBindingType type)
            => NativeBindingNodeRecords.ResolveType(graph, new(type, type.IsUnion ? "union " + type.Name : type.Name, ""));

        void ValidatePrefix(NativeBindingType type, NativeRecordDeclaration declaration)
        {
            NativeBindingField? first = type.Fields.Count == 0 ? null : type.Fields[0];
            NativeRecordField? field = first is null ? null : declaration.Fields.SingleOrDefault(value => value.Name == first.NativeName);
            if (first is null || field is null || field.OffsetBits != 0 || field.BitWidth is not null)
            {
                throw new FormatException("A native node lost its original zero-offset discriminator or parent prefix.");
            }

            string prefix = NativeBindingSelection.ResolveAlias(catalog, first.Representation);
            int expected;
            if (prefix == "NodeTag")
            {
                if (!graph.Roots.TryGetValue("ankus_node_tag_contract", out expected))
                {
                    throw new FormatException("The native node discriminator root is missing.");
                }
            }
            else if (catalog.Types.TryGetValue(prefix, out NativeBindingType? parent) && parent.IsNode)
            {
                expected = Root(parent);
            }
            else
            {
                throw new FormatException("A reference node has no discriminator or parent prefix.");
            }

            NativeRecordType actual = Canonical(field.Type);
            NativeRecordType required = Canonical(expected);
            if (actual.Kind != required.Kind || actual.Declaration is null || actual.Declaration != required.Declaration)
            {
                throw new FormatException("A native node prefix has a different declaration identity.");
            }
        }
    }
}

/// <summary>
/// Retains selected reference fields and the exact fields absent from the installed headers.
/// </summary>
internal sealed record NativeBindingSelectedNodes(NativeBindingCatalog Catalog, IReadOnlyList<NativeBindingAbsentNodeField> AbsentFields,
    NativeBindingNodeChanges Declarations);

/// <summary>
/// Records selected-header node differences without treating absent declarations as compiler failures.
/// </summary>
internal sealed record NativeBindingNodeChanges(IReadOnlyList<string> AbsentTypes, IReadOnlyList<string> AbsentTags,
    IReadOnlyList<string> AdditionalTags, IReadOnlyList<NativeBindingChangedNodeTag> ChangedTags, IReadOnlyList<NativeBindingEnumChanges> Enums);

/// <summary>
/// Records removed, added and changed members of an enum embedded in a selected node.
/// </summary>
internal sealed record NativeBindingEnumChanges(string Name, IReadOnlyList<string> AbsentValues,
    IReadOnlyList<string> AdditionalValues, IReadOnlyList<NativeBindingChangedEnumValue> ChangedValues);

/// <summary>
/// Preserves an enum member's reference expression and exact native value for review.
/// </summary>
internal sealed record NativeBindingChangedEnumValue(string Name, string Reference, string Native);

/// <summary>
/// Retains a reference discriminator and its independently observed native replacement.
/// </summary>
internal sealed record NativeBindingChangedNodeTag(string Name, uint Reference, uint Native);

/// <summary>
/// Identifies a reference field that the installed native declaration no longer contains.
/// </summary>
internal sealed record NativeBindingAbsentNodeField(string Type, string Field);
