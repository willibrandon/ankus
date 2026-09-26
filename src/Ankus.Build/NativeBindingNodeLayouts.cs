using System.Globalization;

namespace Ankus.Build;

/// <summary>
/// Retains the measured complete representation for each concrete native node tag.
/// </summary>
internal static class NativeBindingNodeLayouts
{
    /// <summary>
    /// Encodes concrete tag sizes and alignments independently of inheritance-based cast predicates.
    /// </summary>
    /// <param name="catalog">The selected PostgreSQL declaration graph.</param>
    /// <param name="layout">The matching selected-header measurements.</param>
    /// <returns>Ordered tag:size:alignment records, separated by semicolons.</returns>
    internal static string Encode(NativeBindingCatalog catalog, NativeBindingLayout layout)
    {
        var records = new List<string>();
        foreach ((string tag, uint value) in catalog.Tags.OrderBy(static entry => entry.Value))
        {
            string name = tag[2..];
            name = name switch
            {
                "IntList" or "OidList" or "XidList" => "List",
                "Integer" or "Float" or "String" or "BitString" or "Null" when catalog.PostgresMajor <= 14 => "Value",
                _ => NativeBindingSelection.ResolveAlias(catalog, name),
            };
            if (layout.Types.TryGetValue(name, out NativeBindingTypeLayout? native))
            {
                records.Add(string.Create(CultureInfo.InvariantCulture, $"{value}:{native.Size}:{native.Alignment}"));
            }
        }

        return string.Join(';', records);
    }
}
