namespace Ankus.Generators;

/// <summary>
/// Retains an output plugin export's rendered managed dispatcher, native entry and linker export.
/// </summary>
/// <param name="Declaration">The immutable invocation contract.</param>
/// <param name="Managed">The managed dispatcher and ordered cleanup.</param>
/// <param name="Native">The native export entering the shared callback boundary.</param>
/// <param name="Exports">The export's linker declaration.</param>
internal sealed record OutputPluginEmission(OutputPluginDeclaration Declaration, string Managed, string Native, string Exports)
{
    /// <summary>
    /// Appends the selected export after the shared native callback boundary has been emitted.
    /// </summary>
    /// <param name="managed">The assembly's dispatcher source.</param>
    /// <param name="native">The assembly's native source.</param>
    /// <param name="exports">The assembly's linker exports.</param>
    internal void AppendTo(GeneratorSourceBuilder managed, GeneratorSourceBuilder native, GeneratorSourceBuilder exports)
    {
        managed.Append(Managed);
        native.Append(Native);
        exports.Append(Exports);
    }
}
