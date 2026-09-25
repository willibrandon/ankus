using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Ankus.Generators;

/// <summary>
/// Validates a generated base type and its statically constructed storage codec.
/// </summary>
internal sealed class CustomTypeDeclaration(INamedTypeSymbol type, INamedTypeSymbol? codec, DefaultTypeSerializer? serializer, AttributeData attribute, string name, string? schema)
{
    private static readonly DiagnosticDescriptor s_invalid = new(
        "ANKUS017", "Invalid PostgreSQL base type", "'{0}': {1}", "Ankus", DiagnosticSeverity.Error, isEnabledByDefault: true);

    /// <summary>
    /// Gets the attributed managed type.
    /// </summary>
    internal INamedTypeSymbol Type { get; } = type;

    /// <summary>
    /// Gets the attribute carrying installation dependencies.
    /// </summary>
    internal AttributeData Attribute { get; } = attribute;

    /// <summary>
    /// Gets the unquoted SQL type name.
    /// </summary>
    internal string Name { get; } = name;

    /// <summary>
    /// Gets the fixed schema or null for the installation schema.
    /// </summary>
    internal string? Schema { get; } = schema;

    /// <summary>
    /// Gets the qualified managed name.
    /// </summary>
    internal string Managed => Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    /// <summary>
    /// Gets the qualified, quoted SQL name.
    /// </summary>
    internal string Sql => Qualify(Name);

    /// <summary>
    /// Gets whether binary send and receive functions are generated.
    /// </summary>
    internal bool BinaryProtocol => AttributeValues.Get(Attribute, "BinaryProtocol", false);

    /// <summary>
    /// Gets a stable assembly-specific native symbol suffix.
    /// </summary>
    internal string Symbol
    {
        get
        {
            using SHA256 hash = SHA256.Create();
            byte[] bytes = hash.ComputeHash(Encoding.UTF8.GetBytes(Type.ContainingAssembly.Identity + ":" + Managed));
            return string.Concat(bytes.Take(16).Select(static value => value.ToString("x2", CultureInfo.InvariantCulture)));
        }
    }

    /// <summary>
    /// Gets a qualified I/O function name while keeping long SQL type identifiers intact.
    /// </summary>
    internal string Function(string role)
        => Qualify(Encoding.UTF8.GetByteCount(Name) + role.Length + 1 <= 63 ? Name + "_" + role : "ankus_" + Symbol + "_" + role);

    /// <summary>
    /// Quotes an identifier in the type's selected schema.
    /// </summary>
    private string Qualify(string identifier) => (Schema is null ? string.Empty : SqlText.Identifier(Schema) + ".") + SqlText.Identifier(identifier);

    /// <summary>
    /// Reads and validates an attributed base type, optionally reporting diagnostics.
    /// </summary>
    internal static CustomTypeDeclaration? Create(INamedTypeSymbol type, SourceProductionContext? context = null)
    {
        AttributeData? attribute = type.GetAttributes().FirstOrDefault(static item => item.AttributeClass?.ToDisplayString() == "Ankus.PgTypeAttribute");
        if (attribute is null)
        {
            return null;
        }

        if (type.IsRefLikeType || type.IsStatic || type.IsAbstract || type.IsUnboundGenericType || !Accessible(type) ||
            type.GetAttributes().Any(static item => item.AttributeClass?.ToDisplayString() == "Ankus.PgEnumAttribute"))
        {
            return Invalid("PgType requires an accessible, concrete, non-generic class, struct, or enum without PgEnum.");
        }

        INamedTypeSymbol? codec = attribute.ConstructorArguments.FirstOrDefault().Value as INamedTypeSymbol;
        DefaultTypeSerializer? serializer = null;
        if (codec is null)
        {
            serializer = DefaultTypeSerializer.Create(type, out string? error);
            if (serializer is null)
            {
                return Invalid(error!);
            }
        }
        else
        {
            if (!Accessible(codec) || codec.IsAbstract || codec.IsStatic ||
                !codec.InstanceConstructors.Any(static constructor => constructor.Parameters.Length == 0 &&
                    constructor.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal))
            {
                return Invalid("The codec must be accessible and concrete, with an accessible parameterless constructor.");
            }

            INamedTypeSymbol? contract = codec.BaseType;
            while (contract is not null && !(contract.Name == "PgTypeCodec" && contract.Arity == 1 && contract.ContainingNamespace.ToDisplayString() == "Ankus"))
            {
                contract = contract.BaseType;
            }

            if (contract is null || !SymbolEqualityComparer.Default.Equals(contract.TypeArguments[0], type))
            {
                return Invalid("The codec must derive from PgTypeCodec<T> for this exact managed type.");
            }
        }

        string name = AttributeValues.Get(attribute, "Name", SqlText.SnakeCase(type.Name));
        string? schema = AttributeValues.Get<string?>(attribute, "Schema", null);
        for (INamedTypeSymbol? container = type.ContainingType; schema is null && container is not null; container = container.ContainingType)
        {
            AttributeData? inherited = container.GetAttributes().FirstOrDefault(static item => item.AttributeClass?.ToDisplayString() == "Ankus.PgSchemaAttribute");
            if (inherited is not null)
            {
                schema = inherited.ConstructorArguments.FirstOrDefault().Value as string;
                if (schema is null)
                {
                    return Invalid("The inherited schema must have a non-null identifier.");
                }
            }
        }

        if (!SqlText.IsIdentifier(name) || schema is not null && !SqlText.IsIdentifier(schema))
        {
            return Invalid("Type and schema names must be valid identifiers of at most 63 UTF-8 bytes.");
        }

        return new(type, codec, serializer, attribute, name, schema);

        CustomTypeDeclaration? Invalid(string message)
        {
            context?.ReportDiagnostic(Diagnostic.Create(s_invalid, type.Locations.FirstOrDefault(), type.Name, message));
            return null;
        }
    }

    /// <summary>
    /// Requires every containing type to be visible to the generated dispatchers.
    /// </summary>
    private static bool Accessible(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.ContainingType)
        {
            if (current.IsGenericType || current.IsFileLocal || current.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Emits registration with all nullable and collection instantiations visible to Native AOT.
    /// </summary>
    internal void EmitRegistration(StringBuilder source)
    {
        source.AppendLine("        global::Ankus.PgTypeRegistry.Register" + (Type.IsValueType ? "Value" : "Reference") + "<" + Managed + ">(" +
            Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(Name, true) + ", " +
            (Schema is null ? "null" : Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(Schema, true)) + ", static () => new " +
            (codec?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "Codec_" + Symbol) + "());");
    }

    /// <summary>
    /// Emits an owned serializer for a type without an explicit codec.
    /// </summary>
    internal void EmitSerializer(StringBuilder source) => serializer?.Emit("Codec_" + Symbol, source);

    /// <summary>
    /// Emits a catalog check that restricts native binary decoding to generated custom types.
    /// </summary>
    internal void EmitNativeTypeCheck(StringBuilder source)
    {
        source.AppendLine("    {");
        source.AppendLine("        const AnkusValue name = { .data = (unsigned char *) " + Utf8Literal(Name) + ", .length = " + Encoding.UTF8.GetByteCount(Name).ToString(CultureInfo.InvariantCulture) + " };");
        source.AppendLine(Schema is null ? "        const AnkusValue schema = { .is_null = 1 };" :
            "        const AnkusValue schema = { .data = (unsigned char *) " + Utf8Literal(Schema) + ", .length = " + Encoding.UTF8.GetByteCount(Schema).ToString(CultureInfo.InvariantCulture) + " };");
        source.AppendLine("        if (ankus_resolve_named_type(&name, &schema, true, TYPTYPE_BASE) == type) return true;");
        source.AppendLine("    }");
    }

    /// <summary>
    /// Escapes exact UTF-8 bytes for a generated native string literal.
    /// </summary>
    private static string Utf8Literal(string value) => "\"" + string.Concat(Encoding.UTF8.GetBytes(value).Select(static item =>
        "\\x" + item.ToString("x2", CultureInfo.InvariantCulture))) + "\"";
}
