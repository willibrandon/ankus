using System.Text;
using Microsoft.CodeAnalysis.CSharp;

namespace Ankus.Generators;

/// <summary>
/// Retains the exact closed registration constants independently of semantic symbols and graph ownership.
/// </summary>
/// <param name="Managed">The globally qualified managed root preserving nested nullable arguments.</param>
/// <param name="IsValueType">Whether scalar registration uses the value-type contract.</param>
/// <param name="Name">The exact catalog leaf name.</param>
/// <param name="Schema">The fixed catalog schema or null for the extension schema.</param>
/// <param name="External">Whether the registered type exists outside this extension.</param>
/// <param name="Converter">The exact closed scalar converter spelling, or null for a range.</param>
/// <param name="CanRead">Whether the scalar converter reads input values.</param>
/// <param name="CanWrite">Whether the scalar converter writes output values.</param>
/// <param name="Bound">The closed managed scalar bound of a range, or null for a scalar.</param>
internal sealed record DatumRegistrationModel(string Managed, bool IsValueType, string Name, string? Schema,
    bool External, string? Converter, bool CanRead, bool CanWrite, string? Bound)
{
    /// <summary>
    /// Renders lazy registration without resolving backend catalogs or instantiating author code.
    /// </summary>
    /// <returns>The exact managed registration statement.</returns>
    internal string Emit()
    {
        var source = new StringBuilder();
        string identity = SymbolDisplay.FormatLiteral(Name, true) + ", " +
            (Schema is null ? "null" : SymbolDisplay.FormatLiteral(Schema, true)) +
            ", global::Ankus.PgTypeOrigin." + (External ? "External" : "ThisExtension");
        if (Bound is not null)
        {
            source.AppendLine("        global::Ankus.CompilerServices.PgDatumRegistry.RegisterRange<" + Bound + ">(" + identity + ");");
        }
        else
        {
            source.AppendLine("        global::Ankus.CompilerServices.PgDatumRegistry.Register" + (IsValueType ? "Value" : "Reference") + "<" + Managed + ">(" +
                identity + ", typeof(" + Converter + "), static () => new " + Converter + "(), " +
                (CanRead ? "true" : "false") + ", " + (CanWrite ? "true" : "false") + ");");
        }

        return source.ToString();
    }
}
