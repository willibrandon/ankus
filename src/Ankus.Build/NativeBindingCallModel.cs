namespace Ankus.Build;

/// <summary>
/// Resolves one shared fixed-call contract for native bodies and managed argument storage.
/// </summary>
internal static class NativeBindingCallModel
{
    /// <summary>
    /// Validates the complete observed graph before selecting functions and resolving C parameter adjustment.
    /// </summary>
    /// <param name="records">The complete selected-header signature and storage observations.</param>
    /// <param name="names">Unique fixed function inventory names to lower.</param>
    /// <returns>Ordinally ordered calls retaining their written native storage requirements.</returns>
    internal static IReadOnlyList<NativeBindingCall> Select(NativeHeaderRecords records, IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(names);
        NativeBindingSignatureValidation.Validate(records);
        var selected = new SortedDictionary<string, NativeHeaderSymbol>(StringComparer.Ordinal);
        foreach (string name in names)
        {
            if (!records.Headers.Symbols.TryGetValue(name, out NativeHeaderSymbol? symbol) || !selected.TryAdd(name, symbol))
            {
                throw new FormatException($"Unknown or duplicate native call selection '{name}'.");
            }
        }

        var calls = new List<NativeBindingCall>(selected.Count);
        NativeRecordGraph graph = records.Graph;
        foreach ((string name, NativeHeaderSymbol symbol) in selected)
        {
            NativeBindingCDeclaration.ValidateName(name);
            NativeRecordType root = graph.Types[graph.Types[graph.Roots[name]].Canonical];
            if (!symbol.IsFunction || Canonical(symbol.Type) is not NativeHeaderFunction function || root.Function is not NativeRecordFunction shape)
            {
                throw new FormatException($"Native call {name} requires a function declaration.");
            }

            if (!function.HasPrototype || !shape.HasPrototype || function.IsVariadic || shape.IsVariadic)
            {
                throw new FormatException($"Native call {name} requires a fixed prototype; variadic and unprototyped calls need explicit call-site types.");
            }

            NativeRecordType declared = graph.Types[graph.Roots[name]];
            while (declared.Function is null && declared.Element is int element) { declared = graph.Types[element]; }

            if (declared.Function is not NativeRecordFunction written || function.Parameters.Count != shape.Parameters.Count ||
                function.Parameters.Count != written.Parameters.Count)
            {
                throw new FormatException($"Native call {name} has inconsistent parameter shapes.");
            }

            var parameters = new List<NativeBindingCallValue>(written.Parameters.Count);
            for (int index = 0; index < written.Parameters.Count; index++)
            {
                int storage = written.Parameters[index];
                if (graph.Types[graph.Types[storage].Canonical].Kind is "array" or "function") { storage = shape.Parameters[index]; }

                parameters.Add(Value(function.Parameters[index], storage, name));
            }

            bool hasResult = Canonical(function.Result) is not NativeHeaderScalar { Name: "void" };
            NativeRecordType canonicalResult = graph.Types[graph.Types[written.Result].Canonical];
            if (hasResult != (canonicalResult.Kind != "scalar" || canonicalResult.Name != "void"))
            {
                throw new FormatException($"Native call {name} has inconsistent result shapes.");
            }

            calls.Add(new(name, symbol, parameters, hasResult ? Value(function.Result, written.Result, name) : null));
        }

        return calls;

        NativeBindingCallValue Value(NativeHeaderType type, int index, string name)
        {
            NativeRecordType storage = graph.Types[index];
            if (storage.Size is not long size || size < 0 || storage.Alignment is not long alignment || alignment <= 0 ||
                storage.Kind is "array" or "function" || Canonical(type) is NativeHeaderArray or NativeHeaderFunction)
            {
                throw new FormatException($"Native call value {name} requires a complete object representation.");
            }

            return new(type, index);
        }
    }

    private static NativeHeaderType Canonical(NativeHeaderType type)
    {
        while (true)
        {
            switch (type)
            {
                case NativeHeaderAlias alias: type = alias.Underlying; break;
                case NativeHeaderQualified qualified: type = qualified.Underlying; break;
                case NativeHeaderAdjusted adjusted: type = adjusted.Adjusted; break;
                default: return type;
            }
        }
    }
}

/// <summary>
/// Couples one fixed function with the exact native objects transported across its guarded body.
/// </summary>
/// <param name="Name">The selected inventory name.</param>
/// <param name="Symbol">The selected-header native declaration and linkage metadata.</param>
/// <param name="Parameters">Ordered adjusted argument objects with their written qualifiers and alignment.</param>
/// <param name="Result">The result object, or null for an actual void return.</param>
internal sealed record NativeBindingCall(string Name, NativeHeaderSymbol Symbol, IReadOnlyList<NativeBindingCallValue> Parameters,
    NativeBindingCallValue? Result);

/// <summary>
/// Retains a C declaration separately from its measured object-storage node.
/// </summary>
/// <param name="Type">The exact native declarator, including adjusted parameters.</param>
/// <param name="StorageType">The graph index supplying byte size and required native alignment.</param>
internal sealed record NativeBindingCallValue(NativeHeaderType Type, int StorageType);
