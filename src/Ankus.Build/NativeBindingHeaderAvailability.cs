using System.Collections.ObjectModel;
using System.Text.Json;

namespace Ankus.Build;

/// <summary>
/// Separates declarations present in selected native headers from explicitly absent inventory entries.
/// </summary>
internal static class NativeBindingHeaderAvailability
{
    /// <summary>
    /// Matches only top-level native declarations and takes their kind from the selected target headers.
    /// </summary>
    /// <param name="root">A successful complete compiler observation of the selected headers.</param>
    /// <param name="inventory">The supported major's reference function and global inventory.</param>
    /// <returns>Deterministically ordered available and absent requests, retaining their native identities.</returns>
    internal static NativeBindingAvailability Read(JsonElement root, NativeBindingRawCatalog inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("kind", out JsonElement kind) || kind.ValueKind != JsonValueKind.String ||
            kind.GetString() != "TranslationUnitDecl")
        {
            throw new FormatException("Native availability requires a complete translation unit.");
        }

        var declarations = new Dictionary<string, string>(StringComparer.Ordinal);
        if (root.TryGetProperty("inner", out JsonElement children))
        {
            if (children.ValueKind != JsonValueKind.Array)
            {
                throw new FormatException("Invalid native availability declarations.");
            }

            foreach (JsonElement child in children.EnumerateArray())
            {
                if (child.ValueKind != JsonValueKind.Object || !child.TryGetProperty("kind", out JsonElement childKind) || childKind.ValueKind != JsonValueKind.String)
                {
                    throw new FormatException("Invalid native availability declaration.");
                }

                string? declarationKind = childKind.GetString();
                if (declarationKind is not ("FunctionDecl" or "VarDecl"))
                {
                    continue;
                }

                if (!child.TryGetProperty("name", out JsonElement identifier) || identifier.ValueKind != JsonValueKind.String)
                {
                    throw new FormatException("A native declaration has no identifier.");
                }

                string name = identifier.GetString()!;
                NativeBindingCDeclaration.ValidateName(name);
                if (declarations.TryGetValue(name, out string? existing) && existing != declarationKind)
                {
                    throw new FormatException($"Native declaration '{name}' has conflicting kinds.");
                }

                declarations[name] = declarationKind;
            }
        }

        var available = new List<NativeHeaderRequest>();
        var absent = new List<NativeHeaderRequest>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (string name in inventory.Functions.Keys.Concat(inventory.Globals.Keys).Order(StringComparer.Ordinal))
        {
            if (!names.Add(name))
            {
                throw new FormatException($"Duplicate native inventory entry '{name}'.");
            }

            NativeHeaderRequest request = Request(inventory, name);
            NativeBindingCDeclaration.ValidateName(request.Name);
            NativeBindingCDeclaration.ValidateName(request.NativeName);
            if (!declarations.TryGetValue(request.NativeName, out string? declarationKind))
            {
                absent.Add(request);
                continue;
            }

            // The reference inventory identifies names, not the selected platform's ABI. PostgreSQL can
            // declare one name as a function on ARM64 and a dispatch-pointer global on x64.
            available.Add(request with
            {
                IsFunction = declarationKind == "FunctionDecl"
            });
        }

        return new(available.AsReadOnly(), absent.AsReadOnly());
    }

    /// <summary>
    /// Resolves one inventory name, replacing pgrx-only shim suffixes with the original native declaration.
    /// </summary>
    internal static NativeHeaderRequest Request(NativeBindingRawCatalog raw, string name)
    {
        if (raw.Functions.TryGetValue(name, out NativeBindingFunction? function))
        {
            return new(name, function.NativeSymbol == name + "__pgrx_cshim" ? name : function.NativeSymbol, true);
        }

        if (raw.Globals.TryGetValue(name, out NativeBindingGlobal? global))
        {
            return new(name, global.NativeSymbol, false);
        }

        throw new FormatException($"Unknown native function or global '{name}'.");
    }
}

/// <summary>
/// Retains the complete inventory partition without equating absent declarations with collection failures.
/// </summary>
/// <param name="Available">Inventory requests actually declared by the selected headers.</param>
/// <param name="Absent">Inventory requests absent from the successful header observation.</param>
internal sealed record NativeBindingAvailability(ReadOnlyCollection<NativeHeaderRequest> Available, ReadOnlyCollection<NativeHeaderRequest> Absent);
