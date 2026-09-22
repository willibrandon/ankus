using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Ankus.Generators;

/// <summary>
/// Validates enum contracts and emits their SQL and statically closed Native AOT registrations.
/// </summary>
internal sealed class EnumDeclaration
{
    private static readonly DiagnosticDescriptor s_invalid = new(
        "ANKUS006", "Invalid PostgreSQL enum", "'{0}': {1}", "Ankus", DiagnosticSeverity.Error, isEnabledByDefault: true);

    /// <summary>
    /// Gets the attributed managed enum.
    /// </summary>
    internal INamedTypeSymbol Type { get; private set; } = null!;

    /// <summary>
    /// Gets the enum attribute and its graph options.
    /// </summary>
    internal AttributeData Attribute { get; private set; } = null!;

    /// <summary>
    /// Gets the exact SQL type identifier.
    /// </summary>
    internal string Name { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the fixed or inherited schema, or null for the installation schema.
    /// </summary>
    internal string? Schema { get; private set; }

    /// <summary>
    /// Gets the qualified, quoted SQL type name.
    /// </summary>
    internal string Sql => (Schema is null ? string.Empty : SqlText.Identifier(Schema) + ".") + SqlText.Identifier(Name);

    /// <summary>
    /// Gets the fully qualified managed type identity.
    /// </summary>
    internal string Managed => Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    /// <summary>
    /// Gets the labels in source declaration order, independently of underlying enum values.
    /// </summary>
    internal List<(IFieldSymbol Field, string Label)> Labels { get; } = [];

    /// <summary>
    /// Reads and validates an attributed enum, optionally reporting diagnostics.
    /// </summary>
    internal static EnumDeclaration? Create(INamedTypeSymbol type, SourceProductionContext? context = null)
    {
        AttributeData? attribute = type.GetAttributes().FirstOrDefault(static item => item.AttributeClass?.ToDisplayString() == "Ankus.PgEnumAttribute");
        if (attribute is null)
        {
            return null;
        }

        if (type.TypeKind != TypeKind.Enum || type.GetAttributes().Any(static item => item.AttributeClass?.ToDisplayString() == "System.FlagsAttribute"))
        {
            return Invalid("PgEnum requires an enum without Flags; PostgreSQL enums are distinct labels, not bit masks.");
        }

        for (INamedTypeSymbol? container = type; container is not null; container = container.ContainingType)
        {
            if (container.IsGenericType || container.IsFileLocal || container.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal))
            {
                return Invalid("Enum declarations and their containing types must be accessible, non-generic, and not file-local.");
            }
        }

        var result = new EnumDeclaration
        {
            Type = type, Attribute = attribute,
            Name = AttributeValues.Get(attribute, "Name", SqlText.SnakeCase(type.Name)),
            Schema = AttributeValues.Get<string?>(attribute, "Schema", null),
        };
        for (INamedTypeSymbol? container = type.ContainingType; result.Schema is null && container is not null; container = container.ContainingType)
        {
            AttributeData? schema = container.GetAttributes().FirstOrDefault(static item => item.AttributeClass?.ToDisplayString() == "Ankus.PgSchemaAttribute");
            if (schema is not null)
            {
                result.Schema = schema.ConstructorArguments[0].Value as string;
                if (result.Schema is null)
                {
                    return Invalid("The inherited schema must have a non-null identifier.");
                }
            }
        }

        if (!SqlText.IsIdentifier(result.Name) || (result.Schema is not null && !SqlText.IsIdentifier(result.Schema)))
        {
            return Invalid("Enum type and schema names must be nonempty identifiers of at most 63 UTF-8 bytes.");
        }

        var values = new HashSet<object>();
        var labels = new HashSet<string>(StringComparer.Ordinal);
        foreach (IFieldSymbol field in type.GetMembers().OfType<IFieldSymbol>().Where(static field => field.HasConstantValue))
        {
            AttributeData? labelAttribute = field.GetAttributes().FirstOrDefault(static item => item.AttributeClass?.ToDisplayString() == "Ankus.PgEnumLabelAttribute");
            string? label = labelAttribute is null ? field.Name : labelAttribute.ConstructorArguments[0].Value as string;
            if (label is null || !SqlText.IsText(label) || Encoding.UTF8.GetByteCount(label) > 63 || !labels.Add(label))
            {
                return Invalid("Enum labels must be distinct, valid Unicode of at most 63 UTF-8 bytes without zero characters.");
            }

            if (!values.Add(field.ConstantValue!))
            {
                return Invalid("Enum members must have distinct numeric values; aliases cannot preserve distinct PostgreSQL labels.");
            }

            result.Labels.Add((field, label));
        }

        return result;

        EnumDeclaration? Invalid(string reason)
        {
            context?.ReportDiagnostic(Diagnostic.Create(s_invalid, type.Locations.FirstOrDefault(), type.Name, reason));
            return null;
        }
    }

    /// <summary>
    /// Emits a generated enum registration for module initialization, without backend access.
    /// </summary>
    internal void EmitRegistration(StringBuilder source)
    {
        source.AppendLine($"        global::Ankus.PgEnumRegistry.Register<{Managed}>({SymbolDisplay.FormatLiteral(Name, true)}, " +
            (Schema is null ? "null" : SymbolDisplay.FormatLiteral(Schema, true)) + ", new global::System.Collections.Generic.KeyValuePair<" + Managed + ", string>[]");
        source.AppendLine("        {");
        foreach ((IFieldSymbol field, string label) in Labels)
        {
            source.AppendLine($"            new({Managed}.@{field.Name}, {SymbolDisplay.FormatLiteral(label, true)}),");
        }

        source.AppendLine("        });");
    }

    /// <summary>
    /// Emits CREATE TYPE with label order preserved exactly.
    /// </summary>
    internal string CreateSql() => "CREATE TYPE " + Sql + " AS ENUM (" + string.Join(", ", Labels.Select(static item => SqlText.Literal(item.Label))) + ");\n";

    /// <summary>
    /// Emits a native catalog identity check so unsupported SPI enums fail before a query's subtransaction commits.
    /// </summary>
    internal void EmitNativeTypeCheck(StringBuilder source)
    {
        source.AppendLine("    {");
        source.AppendLine("        const AnkusValue name = { .data = (unsigned char *) " + Utf8Literal(Name) + ", .length = " +
            Encoding.UTF8.GetByteCount(Name).ToString(System.Globalization.CultureInfo.InvariantCulture) + " };");
        source.AppendLine(Schema is null ? "        const AnkusValue schema = { .is_null = 1 };" :
            "        const AnkusValue schema = { .data = (unsigned char *) " + Utf8Literal(Schema) + ", .length = " +
            Encoding.UTF8.GetByteCount(Schema).ToString(System.Globalization.CultureInfo.InvariantCulture) + " };");
        source.AppendLine("        if (ankus_resolve_enum(&name, &schema, true) == type) return true;");
        source.AppendLine("    }");
        source.AppendLine();
    }

    private static string Utf8Literal(string value) => "\"" + string.Concat(Encoding.UTF8.GetBytes(value).Select(static item =>
        "\\x" + item.ToString("x2", System.Globalization.CultureInfo.InvariantCulture))) + "\"";
}
