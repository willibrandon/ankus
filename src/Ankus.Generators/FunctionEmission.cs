using System.Text;

namespace Ankus.Generators;

/// <summary>
/// Retains independently rendered dispatcher artifacts without graph state or compiler symbols.
/// </summary>
/// <param name="Managed">The complete managed dispatcher or iterator callback.</param>
/// <param name="Native">The native entry boundary with initialization left to composition.</param>
/// <param name="Exports">The native entry and information-function export declarations.</param>
/// <param name="NativeName">The native entry identity used by the final SQL graph.</param>
/// <param name="IsPlannerSupport">Whether the function has a scalar nonvariadic internal-to-internal SQL signature.</param>
internal sealed record FunctionEmission(string Managed, NativeFunctionEmission Native, string Exports, string NativeName, bool IsPlannerSupport)
{
    /// <summary>
    /// Adds cached artifacts after extension-wide validation selects the native initialization policy.
    /// </summary>
    /// <param name="managed">The complete extension managed source.</param>
    /// <param name="native">The complete extension native source.</param>
    /// <param name="exports">The complete extension export list.</param>
    /// <param name="ensureInitialized">Whether the entry must complete deferred initialization.</param>
    internal void AppendTo(StringBuilder managed, StringBuilder native, StringBuilder exports, bool ensureInitialized)
    {
        managed.Append(Managed);
        Native.AppendTo(native, ensureInitialized);
        exports.Append(Exports);
    }

    /// <summary>
    /// Retains independently cached conversion artifacts in the extension's rendering plans.
    /// </summary>
    /// <param name="managed">The managed dispatcher plan.</param>
    /// <param name="native">The native entry plan.</param>
    /// <param name="exports">The linker export plan.</param>
    /// <param name="ensureInitialized">Whether initialization must precede backend work.</param>
    internal void AppendTo(GeneratorSourceBuilder managed, GeneratorSourceBuilder native, GeneratorSourceBuilder exports, bool ensureInitialized)
    {
        managed.Append(Managed);
        Native.AppendTo(native, ensureInitialized);
        exports.Append(Exports);
    }
}
