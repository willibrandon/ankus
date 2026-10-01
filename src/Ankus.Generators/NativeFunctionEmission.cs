using System.Text;

namespace Ankus.Generators;

/// <summary>
/// Keeps a rendered entry boundary independent of extension-wide initialization selection.
/// </summary>
/// <param name="Header">The declarations and entry signature through its opening brace.</param>
/// <param name="Body">The conversion and invocation body through its closing brace.</param>
internal sealed record NativeFunctionEmission(string Header, string Body)
{
    /// <summary>
    /// Composes the cached boundary with the extension's validated initialization requirement.
    /// </summary>
    /// <param name="source">The complete extension native source.</param>
    /// <param name="ensureInitialized">Whether the entry must complete deferred initialization before backend work.</param>
    internal void AppendTo(StringBuilder source, bool ensureInitialized)
    {
        source.Append(Header);
        if (ensureInitialized)
        {
            source.AppendLine("    ankus_ensure_initialized();");
        }

        source.Append(Body);
    }

    /// <summary>
    /// Retains cached entry fragments before the independently cached final native render.
    /// </summary>
    /// <param name="source">The extension's native source plan.</param>
    /// <param name="ensureInitialized">Whether initialization must precede backend work.</param>
    internal void AppendTo(GeneratorSourceBuilder source, bool ensureInitialized)
    {
        source.Append(Header);
        if (ensureInitialized)
        {
            source.AppendLine("    ankus_ensure_initialized();");
        }

        source.Append(Body);
    }
}
