namespace Ankus.Generators;

/// <summary>
/// Retains a validated SQL input independently of current catalog provider qualification.
/// </summary>
/// <param name="Name">The exact unquoted SQL argument name.</param>
/// <param name="Type">The immutable datum conversion and catalog contract.</param>
/// <param name="Variadic">Whether PostgreSQL accepts an expanded final vector argument.</param>
/// <param name="Default">The exact authored or converted SQL default expression, or null for a required input.</param>
internal sealed record SqlFunctionParameter(string Name, FunctionType Type, bool Variadic, string? Default)
{
    /// <summary>
    /// Applies current extension-provider qualification without reading compiler symbols.
    /// </summary>
    /// <param name="providers">The validated graph catalog identities, or null when unavailable.</param>
    /// <returns>The complete named argument declaration.</returns>
    internal string Emit(SqlTypeProviders? providers)
        => (Variadic ? "VARIADIC " : string.Empty) + SqlText.Identifier(Name) + " " + SqlSchemaTemplate.Type(Type, providers) +
            (Default is null ? string.Empty : " DEFAULT (" + Default + ")");
}
