namespace Ankus.Build;

/// <summary>
/// Identifies complete native function-pointer signatures throughout the measured type graph.
/// </summary>
internal static class NativeBindingIndirectModel
{
    /// <summary>
    /// Shares pointer values by their canonical function identity, independently of aliases and object qualifiers.
    /// </summary>
    /// <param name="graph">The complete validated native graph.</param>
    /// <returns>Deterministically ordered pointer signatures, including address-only unsupported call shapes.</returns>
    internal static IReadOnlyList<NativeBindingIndirectCall> Describe(NativeRecordGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        NativeBindingRecordValidation.Validate(graph, graph.Target, graph.Roots.Keys);
        var pointers = new SortedDictionary<int, int>();
        var aliases = new Dictionary<int, string>();
        for (int index = 0; index < graph.Types.Count; index++)
        {
            NativeRecordType type = graph.Types[graph.Types[index].Canonical];
            if (type.Kind == "pointer" && FunctionType(graph, type.Element!.Value) is int signature)
            {
                pointers.TryAdd(signature, graph.Types[index].Canonical);
                if (graph.Types[index].Kind == "alias")
                {
                    AddAlias(signature, graph.Types[index].Name);
                }
            }
            else if (type.Kind == "function" && graph.Types[index].Kind == "alias")
            {
                AddAlias(graph.Types[index].Canonical, graph.Types[index].Name);
            }
        }

        var result = new List<NativeBindingIndirectCall>(pointers.Count);
        foreach ((int signature, int storage) in pointers)
        {
            NativeRecordFunction function = graph.Types[signature].Function!;
            NativeRecordType returned = graph.Types[graph.Types[function.Result].Canonical];
            int? returnType = returned is { Kind: "scalar", Name: "void" } ? null : function.Result;
            bool callable = function.HasPrototype && !function.IsVariadic &&
                function.CallingConvention is 1 or 2 or 3 or 4 or 10 or 11 or 12 &&
                function.Parameters.All(index => Complete(graph.Types[index])) &&
                (returnType is null || Complete(graph.Types[returnType.Value]));
            result.Add(new(signature, storage, aliases.GetValueOrDefault(signature),
                Array.AsReadOnly(function.Parameters.ToArray()), returnType, callable));
        }

        return result.AsReadOnly();

        void AddAlias(int signature, string name)
        {
            if (!aliases.TryGetValue(signature, out string? previous) || string.CompareOrdinal(name, previous) < 0)
            {
                aliases[signature] = name;
            }
        }
    }

    /// <summary>
    /// Validates every collected root before selecting unique canonical signatures with complete fixed call storage.
    /// </summary>
    /// <param name="records">The original header signatures and complete graph.</param>
    /// <param name="signatures">Canonical function identities selected by actual native imports.</param>
    /// <returns>The selected callable signatures in canonical order.</returns>
    internal static IReadOnlyList<NativeBindingIndirectCall> Select(NativeHeaderRecords records, IReadOnlyList<int> signatures)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(signatures);
        NativeBindingSignatureValidation.Validate(records);
        Dictionary<int, NativeBindingIndirectCall> available = Describe(records.Graph).ToDictionary(static call => call.FunctionType);
        var seen = new HashSet<int>();
        var selected = new List<NativeBindingIndirectCall>(signatures.Count);
        foreach (int signature in signatures.Order())
        {
            if (!seen.Add(signature) || !available.TryGetValue(signature, out NativeBindingIndirectCall? call) || !call.CanInvoke)
            {
                throw new FormatException("An indirect native call requires a unique canonical function-pointer signature with complete fixed storage.");
            }

            selected.Add(call);
        }

        return selected.AsReadOnly();
    }

    /// <summary>
    /// Resolves only canonical function types, leaving object pointers outside the callback domain.
    /// </summary>
    internal static int? FunctionType(NativeRecordGraph graph, int index)
        => graph.Types[graph.Types[index].Canonical].Kind == "function" ? graph.Types[index].Canonical : null;

    private static bool Complete(NativeRecordType type)
        => type is { Size: >= 0, Alignment: > 0 } && type.Kind is not ("array" or "function");
}

/// <summary>
/// Describes one native callable identity separately from any particular stored target address.
/// </summary>
/// <param name="FunctionType">The canonical function signature index.</param>
/// <param name="PointerType">An actual measured pointer value carrying that signature.</param>
/// <param name="Name">The ordinal first native typedef name, when present.</param>
/// <param name="Parameters">Adjusted native argument types.</param>
/// <param name="Result">The native result type, or null for void.</param>
/// <param name="CanInvoke">Whether a fixed prototype and complete values permit a generated guarded call.</param>
internal sealed record NativeBindingIndirectCall(int FunctionType, int PointerType, string? Name,
    IReadOnlyList<int> Parameters, int? Result, bool CanInvoke);
