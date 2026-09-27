namespace Ankus.Build;

/// <summary>
/// Resolves native global storage and access semantics from the complete selected-header contract.
/// </summary>
internal static class NativeBindingGlobalModel
{
    /// <summary>
    /// Selects actual object declarations without losing qualifiers through aliases or embedded native storage.
    /// </summary>
    /// <param name="records">The independently validated header declarations and native storage graph.</param>
    /// <param name="names">Unique global inventory names to expose.</param>
    /// <returns>Immutable, ordinally ordered global contracts.</returns>
    internal static IReadOnlyList<NativeBindingGlobalContract> Select(NativeHeaderRecords records, IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(names);
        NativeBindingSignatureValidation.Validate(records);
        var selected = new SortedDictionary<string, NativeBindingGlobalContract>(StringComparer.Ordinal);
        foreach (string name in names)
        {
            NativeBindingCDeclaration.ValidateName(name);
            if (!records.Headers.Symbols.TryGetValue(name, out NativeHeaderSymbol? symbol) || selected.ContainsKey(name))
            {
                throw new FormatException($"Unknown or duplicate native global selection '{name}'.");
            }

            int index = records.Graph.Roots[name];
            NativeRecordType storage = records.Graph.Types[index];
            NativeRecordType canonical = records.Graph.Types[storage.Canonical];
            if (symbol.IsFunction || canonical.Kind == "function" || canonical is { Kind: "scalar", Name: "void" })
            {
                throw new FormatException($"Native global {name} requires an object declaration.");
            }

            bool complete = storage.Size is >= 0 && storage.Alignment is > 0;
            (NativeHeaderQualifiers qualifiers, bool atomic) = ObjectQualifiers(records.Graph, index);
            selected.Add(name, new(name, symbol, index, complete,
                complete && !qualifiers.HasFlag(NativeHeaderQualifiers.Const),
                atomic || qualifiers.HasFlag(NativeHeaderQualifiers.Volatile)));
        }

        return Array.AsReadOnly(selected.Values.ToArray());
    }

    /// <summary>
    /// Follows by-value storage while keeping pointer targets outside the containing object's qualification.
    /// </summary>
    private static (NativeHeaderQualifiers Qualifiers, bool Atomic) ObjectQualifiers(NativeRecordGraph graph, int index)
    {
        NativeHeaderQualifiers qualifiers = NativeHeaderQualifiers.None;
        bool atomic = false;
        var pending = new Stack<int>();
        var visited = new HashSet<int>();
        pending.Push(index);
        while (pending.TryPop(out int current))
        {
            if (!visited.Add(current))
            {
                continue;
            }

            NativeRecordType declared = graph.Types[current];
            NativeRecordType type = graph.Types[declared.Canonical];
            qualifiers |= declared.Qualifiers | type.Qualifiers;
            atomic |= type.Kind == "atomic";
            if (type.Kind == "record")
            {
                foreach (NativeRecordField field in graph.Declarations[type.Declaration!.Value].Fields)
                {
                    pending.Push(field.Type);
                }
            }
            else if (type.Kind is "array" or "vector" or "complex" or "atomic")
            {
                pending.Push(type.Element!.Value);
            }
        }

        return (qualifiers, atomic);
    }
}

/// <summary>
/// Retains one native global's identity, complete storage and permitted C value accesses.
/// </summary>
/// <param name="Name">The public inventory name.</param>
/// <param name="Symbol">The selected header's actual declaration and linkage identity.</param>
/// <param name="StorageType">The graph node describing the declared object.</param>
/// <param name="IsComplete">Whether a whole value can be transported without inventing an extent.</param>
/// <param name="CanWrite">Whether complete storage has no const-qualified by-value subobject.</param>
/// <param name="RequiresTypedAccess">Whether value transport must use C assignments to retain declared qualifications.</param>
internal sealed record NativeBindingGlobalContract(string Name, NativeHeaderSymbol Symbol, int StorageType, bool IsComplete,
    bool CanWrite, bool RequiresTypedAccess);
