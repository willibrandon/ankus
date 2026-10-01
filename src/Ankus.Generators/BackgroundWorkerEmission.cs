namespace Ankus.Generators;

/// <summary>
/// Retains exact worker dispatch, host initialization and owned error cleanup independently of compiler state.
/// </summary>
/// <param name="EntryPoint">The exact exported native worker symbol.</param>
/// <param name="Managed">The managed dispatcher and ordered cleanup.</param>
/// <param name="Native">The complete worker host entry and native error guard.</param>
/// <param name="Exports">The worker's linker export declaration.</param>
internal sealed record BackgroundWorkerEmission(string EntryPoint, string Managed, string Native, string Exports)
{
    /// <summary>
    /// Appends a selected worker only after current duplicate-export validation.
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
