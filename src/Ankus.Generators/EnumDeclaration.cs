using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Ankus.Generators;

/// <summary>
/// Holds a validated enum contract independently of compiler symbols and attribute objects.
/// </summary>
/// <param name="Name">The exact SQL type name.</param>
/// <param name="Schema">The fixed schema, or null for the installation schema.</param>
/// <param name="Managed">The fully qualified managed enum type.</param>
/// <param name="Labels">The members in source declaration order with exact constants and labels.</param>
internal sealed record EnumDeclaration(string Name, string? Schema, string Managed, EquatableArray<EnumLabel> Labels)
{
    /// <summary>
    /// Gets the qualified, quoted SQL type name.
    /// </summary>
    internal string Sql => (Schema is null ? string.Empty : SqlText.Identifier(Schema) + ".") + SqlText.Identifier(Name);

    /// <summary>
    /// Reads and validates an attributed enum, optionally reporting diagnostics.
    /// </summary>
    /// <param name="type">The attributed enum to analyze.</param>
    /// <param name="reportError">The optional destination for a contract validation failure.</param>
    /// <param name="cancellationToken">Cancels exact attribute analysis.</param>
    /// <param name="metadataFailure">Receives a structured unreadable attribute failure.</param>
    /// <returns>The detached immutable contract, or null when invalid or not attributed.</returns>
    internal static EnumDeclaration? Create(INamedTypeSymbol type, Action<string>? reportError = null,
        Action<AttributeMetadataFailure>? metadataFailure = null, CancellationToken cancellationToken = default)
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
            if (container.IsGenericType || container.IsFileLocal || container.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal))
            {
                return Invalid("Enum declarations and their containing types must be accessible, non-generic, and not file-local.");
            }
        }

        if (!ExactAttributeStrings.TryRead(type, attribute, cancellationToken, out AttributeStrings? options) || options is null)
        {
            metadataFailure?.Invoke(AttributeMetadataFailure.Create(type, attribute));
            return Invalid("The enum's exact attribute metadata cannot be read; rebuild its defining assembly.");
        }

        string name = options.Property("Name", SqlText.SnakeCase(type.Name))!;
        string? schemaName = options.Property("Schema", null);
        for (INamedTypeSymbol? container = type.ContainingType; schemaName is null && container is not null; container = container.ContainingType)
        {
            AttributeData? schema = container.GetAttributes().FirstOrDefault(static item => item.AttributeClass?.ToDisplayString() == "Ankus.PgSchemaAttribute");
            if (schema is not null)
            {
                if (!ExactAttributeStrings.TryRead(container, schema, cancellationToken, out AttributeStrings? inherited) || inherited is null)
                {
                    metadataFailure?.Invoke(AttributeMetadataFailure.Create(container, schema));
                    return Invalid("The enclosing schema's exact attribute metadata cannot be read; rebuild its defining assembly.");
                }

                schemaName = inherited.Arguments[0];
                if (schemaName is null)
                {
                    return Invalid("The inherited schema must have a non-null identifier.");
                }
            }
        }

        if (!SqlText.IsIdentifier(name) || (schemaName is not null && !SqlText.IsIdentifier(schemaName)))
        {
            return Invalid("Enum type and schema names must be nonempty identifiers of at most 63 UTF-8 bytes.");
        }

        var values = new HashSet<object>();
        var labels = new HashSet<string>(StringComparer.Ordinal);
        var members = new List<EnumLabel>();
        foreach (IFieldSymbol field in type.GetMembers().OfType<IFieldSymbol>().Where(static field => field.HasConstantValue))
        {
            AttributeData? labelAttribute = field.GetAttributes().FirstOrDefault(static item => item.AttributeClass?.ToDisplayString() == "Ankus.PgEnumLabelAttribute");
            string? label = field.Name;
            if (labelAttribute is not null)
            {
                if (!ExactAttributeStrings.TryRead(field, labelAttribute, cancellationToken, out AttributeStrings? item) || item is null)
                {
                    metadataFailure?.Invoke(AttributeMetadataFailure.Create(field, labelAttribute));
                    return Invalid("The enum label's exact attribute metadata cannot be read; rebuild its defining assembly.");
                }

                label = item.Arguments[0];
            }

            if (label is null || !SqlText.IsText(label) || Encoding.UTF8.GetByteCount(label) > 63 || !labels.Add(label))
            {
                return Invalid("Enum labels must be distinct, valid Unicode of at most 63 UTF-8 bytes without zero characters.");
            }

            if (!values.Add(field.ConstantValue!))
            {
                return Invalid("Enum members must have distinct numeric values; aliases cannot preserve distinct PostgreSQL labels.");
            }

            members.Add(new(field.Name, field.ConstantValue!, label));
        }

        return new(name, schemaName, type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), new(members));

        EnumDeclaration? Invalid(string reason)
        {
            reportError?.Invoke(reason);
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
        foreach (EnumLabel label in Labels)
        {
            source.AppendLine($"            new({Managed}.@{label.Member}, {SymbolDisplay.FormatLiteral(label.Label, true)}),");
        }

        source.AppendLine("        });");
    }

    /// <summary>
    /// Emits CREATE TYPE with label order preserved exactly.
    /// </summary>
    internal string CreateSql() => "CREATE TYPE " + (Schema is null ? "\0" : string.Empty) + Sql + " AS ENUM (" + string.Join(", ", Labels.Select(static item => SqlText.Literal(item.Label))) + ");\n";

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
