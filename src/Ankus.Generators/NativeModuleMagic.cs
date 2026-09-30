using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Ankus.Generators;

/// <summary>
/// Emits header-selected module metadata with exact UTF-8 C strings.
/// </summary>
internal static class NativeModuleMagic
{
    private static readonly DiagnosticDescriptor s_invalidIdentity = new(
        "ANKUS025", "Invalid PostgreSQL module identity",
        "{0} must contain valid UTF-8 text without embedded zero characters",
        "Ankus", DiagnosticSeverity.Error, isEnabledByDefault: true,
        helpLinkUri: "https://willibrandon.github.io/ankus/reference/build-settings/#native-module-identity");

    /// <summary>
    /// Uses extended identity on PostgreSQL 18 and later while preserving earlier ABI declarations.
    /// </summary>
    /// <param name="compilation">The assembly identity and optional module attribute.</param>
    /// <param name="projectVersion">The evaluated project version, or null outside an SDK build.</param>
    /// <param name="context">Reports invalid project metadata before emitting native code.</param>
    /// <returns>The native declaration, or null after reporting invalid metadata.</returns>
    internal static string? Emit(Compilation compilation, string? projectVersion, SourceProductionContext context)
    {
        AttributeData? attribute = compilation.Assembly.GetAttributes().FirstOrDefault(static value =>
            value.AttributeClass?.ToDisplayString() == "Ankus.PgModuleAttribute");
        string? name = attribute?.NamedArguments.FirstOrDefault(static value => value.Key == "Name").Value.Value as string;
        string? version = attribute?.NamedArguments.FirstOrDefault(static value => value.Key == "Version").Value.Value as string;
        byte[]? nameBytes = Encode(name ?? compilation.AssemblyName!, "Name", attribute, context);
        byte[]? versionBytes = Encode(version ?? projectVersion ?? compilation.Assembly.Identity.Version.ToString(),
            "Version", attribute, context);
        if (nameBytes is null || versionBytes is null)
        {
            return null;
        }

        return "#if PG_VERSION_NUM >= 180000\nPG_MODULE_MAGIC_EXT(\n    .name = " + Literal(nameBytes) +
            ",\n    .version = " + Literal(versionBytes) + ");\n#else\nPG_MODULE_MAGIC;\n#endif\n\n";
    }

    /// <summary>
    /// Rejects truncation and replacement encoding in native metadata.
    /// </summary>
    /// <param name="value">The complete managed value.</param>
    /// <param name="property">The identity member named in the diagnostic.</param>
    /// <param name="attribute">The optional authored identity declaration.</param>
    /// <param name="context">The diagnostic destination.</param>
    /// <returns>The exact encoded bytes, or null when invalid.</returns>
    private static byte[]? Encode(string value, string property, AttributeData? attribute, SourceProductionContext context)
    {
        if (value.IndexOf('\0') < 0)
        {
            try
            {
                return new UTF8Encoding(false, true).GetBytes(value);
            }
            catch (EncoderFallbackException)
            {
                // A replacement character would change the authored module identity.
            }
        }

        AttributeSyntax? syntax = attribute?.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken) as AttributeSyntax;
        Location location = syntax?.ArgumentList?.Arguments.FirstOrDefault(argument => argument.NameEquals?.Name.Identifier.ValueText == property)
            ?.Expression.GetLocation() ?? Location.None;
        context.ReportDiagnostic(Diagnostic.Create(s_invalidIdentity, location, "PgModule." + property));
        return null;
    }

    /// <summary>
    /// Encodes every byte so C quoting, escapes and source-file encoding cannot alter identity.
    /// </summary>
    /// <param name="bytes">Validated UTF-8 bytes without a zero character.</param>
    /// <returns>A native string literal.</returns>
    private static string Literal(byte[] bytes)
        => "\"" + string.Concat(bytes.Select(static value => "\\x" + value.ToString("x2", CultureInfo.InvariantCulture))) + "\"";
}
