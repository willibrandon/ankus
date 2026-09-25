using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Ankus.Generators;

/// <summary>
/// Validates a generated base type and its statically constructed storage codec.
/// </summary>
internal sealed class CustomTypeDeclaration(INamedTypeSymbol type, INamedTypeSymbol? codec, INamedTypeSymbol? textCodec,
    DefaultTypeSerializer? serializer, int nativeSize, AttributeData attribute, string name, string? schema)
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
    /// Gets whether this type has a statically proved native payload.
    /// </summary>
    internal bool NativeLayout => nativeSize != 0;

    /// <summary>
    /// Gets the optional error raised by a NULL call to the text input function.
    /// </summary>
    internal string? NullInputErrorMessage => AttributeValues.Get<string?>(Attribute, "NullInputErrorMessage", null);

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

        object? codecArgument = attribute.ConstructorArguments.FirstOrDefault().Value;
        TypedConstant textArgument = attribute.NamedArguments.FirstOrDefault(static argument => argument.Key == "TextCodec").Value;
        if (codecArgument is not null and not INamedTypeSymbol || textArgument.Kind == TypedConstantKind.Array ||
            textArgument.Value is not null and not INamedTypeSymbol)
        {
            return Invalid("Codec options require an accessible, closed named codec type.");
        }

        var codec = codecArgument as INamedTypeSymbol;
        var textCodec = textArgument.Value as INamedTypeSymbol;
        bool nativeLayout = AttributeValues.Get(attribute, "NativeLayout", false);
        if (codec is not null && textCodec is not null)
        {
            return Invalid("TextCodec selects generated storage and cannot be combined with an explicit storage codec.");
        }

        if (nativeLayout && (codec is not null || textCodec is null))
        {
            return Invalid("NativeLayout requires TextCodec and cannot be combined with an explicit storage codec.");
        }

        if (AttributeValues.Get<string?>(attribute, "NullInputErrorMessage", null) is { } nullMessage && !SqlText.IsText(nullMessage))
        {
            return Invalid("NullInputErrorMessage must contain valid Unicode without zero characters.");
        }

        if (type.IsRefLikeType || type.IsStatic || type.IsAbstract && (codec is not null ||
            !type.GetAttributes().Any(static item => item.AttributeClass?.ToDisplayString() == "System.Text.Json.Serialization.JsonDerivedTypeAttribute")) ||
            type.IsUnboundGenericType || !Accessible(type) ||
            type.GetAttributes().Any(static item => item.AttributeClass?.ToDisplayString() == "Ankus.PgEnumAttribute"))
        {
            return Invalid("PgType requires an accessible, non-generic class, struct, or enum without PgEnum. Abstract classes require generated serialization with declared concrete variants.");
        }

        DefaultTypeSerializer? serializer = null;
        if (textCodec is not null && ValidateCodec(textCodec, type, "PgTypeTextCodec") is { } textError)
        {
            return Invalid(textError);
        }

        int nativeSize = 0;
        if (nativeLayout)
        {
            nativeSize = NativeTypeLayout.Validate(type, out string? error);
            if (error is not null)
            {
                return Invalid(error);
            }
        }
        else if (codec is null)
        {
            serializer = DefaultTypeSerializer.Create(type, out string? error);
            if (serializer is null)
            {
                return Invalid(error!);
            }
        }
        else if (ValidateCodec(codec, type, "PgTypeCodec") is { } codecError)
        {
            return Invalid(codecError);
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

        return new(type, codec, textCodec, serializer, nativeSize, attribute, name, schema);

        CustomTypeDeclaration? Invalid(string message)
        {
            context?.ReportDiagnostic(Diagnostic.Create(s_invalid, type.Locations.FirstOrDefault(), type.Name, message));
            return null;
        }
    }

    /// <summary>
    /// Validates direct codec construction and its exact non-null managed contract.
    /// </summary>
    private static string? ValidateCodec(INamedTypeSymbol codec, INamedTypeSymbol type, string baseName)
    {
        IMethodSymbol? constructor = codec.InstanceConstructors.FirstOrDefault(constructor => constructor.Parameters.Length == 0 &&
            (constructor.DeclaredAccessibility == Accessibility.Public ||
             constructor.DeclaredAccessibility is Accessibility.Internal or Accessibility.ProtectedOrInternal &&
             codec.ContainingAssembly.GivesAccessTo(type.ContainingAssembly)));
        if (!AccessibleCodec(codec, type.ContainingAssembly) || codec.IsAbstract || codec.IsStatic || constructor is null)
        {
            return "The codec must be accessible, closed and concrete, with an accessible parameterless constructor.";
        }

        bool required = false;
        INamedTypeSymbol? contract = null;
        for (INamedTypeSymbol? current = codec; current is not null; current = current.BaseType)
        {
            required |= current.GetMembers().Any(static member => member is IPropertySymbol { IsRequired: true } or IFieldSymbol { IsRequired: true });
            if (current.Name == baseName && current.Arity == 1 && current.ContainingNamespace.ToDisplayString() == "Ankus")
            {
                contract = current;
            }
        }

        if (contract is null || !SymbolEqualityComparer.Default.Equals(contract.TypeArguments[0], type) ||
            type.IsReferenceType && contract.TypeArguments[0].NullableAnnotation == NullableAnnotation.Annotated)
        {
            return "The codec must derive from " + baseName + "<T> for this exact non-nullable managed type.";
        }

        if (required && !constructor.GetAttributes().Any(static attribute =>
            attribute.AttributeClass?.ToDisplayString() == "System.Diagnostics.CodeAnalysis.SetsRequiredMembersAttribute"))
        {
            return "A codec with C# required members needs a parameterless constructor carrying SetsRequiredMembers.";
        }

        return null;
    }

    /// <summary>
    /// Accepts statically closed codec types and verifies containing declarations and generic arguments.
    /// </summary>
    private static bool AccessibleCodec(ITypeSymbol type, IAssemblySymbol assembly)
    {
        if (type is IArrayTypeSymbol array)
        {
            return AccessibleCodec(array.ElementType, assembly);
        }

        if (type is not INamedTypeSymbol named || named.IsUnboundGenericType)
        {
            return false;
        }

        for (INamedTypeSymbol? current = named; current is not null; current = current.ContainingType)
        {
            if (current.IsFileLocal || !(current.DeclaredAccessibility == Accessibility.Public ||
                current.DeclaredAccessibility is Accessibility.Internal or Accessibility.ProtectedOrInternal &&
                current.ContainingAssembly.GivesAccessTo(assembly)) ||
                current.TypeArguments.Any(argument => !AccessibleCodec(argument, assembly)))
            {
                return false;
            }
        }

        return true;
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
        source.AppendLine("        global::Ankus.PgTypeRegistry.Register" + (NativeLayout ? "Native" : Type.IsValueType ? "Value" : "Reference") + "<" + Managed + ">(" +
            Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(Name, true) + ", " +
            (Schema is null ? "null" : Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(Schema, true)) + ", " +
            (NativeLayout ? nativeSize.ToString(CultureInfo.InvariantCulture) + ", " : string.Empty) + "static () => new " +
            (codec?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "Codec_" + Symbol) + "());");
    }

    /// <summary>
    /// Emits an owned serializer for a type without an explicit codec.
    /// </summary>
    internal void EmitSerializer(StringBuilder source)
    {
        if (nativeSize != 0)
        {
            source.AppendLine("    private sealed class Codec_" + Symbol + " : global::Ankus.PgTypeCodec<" + Managed + ">");
            source.AppendLine("    {");
            source.AppendLine("        private readonly global::Ankus.PgNativeTypeCodec<" + Managed + "> _codec = new(" +
                nativeSize.ToString(CultureInfo.InvariantCulture) + ", static () => new " + textCodec!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "());");
            source.AppendLine("        public override " + Managed + " Parse(string text) => _codec.Parse(text);");
            source.AppendLine("        public override string Format(" + Managed + " value) => _codec.Format(value);");
            source.AppendLine("        public override " + Managed + " Read(global::System.ReadOnlySpan<byte> payload) => _codec.Read(payload);");
            source.AppendLine("        public override void Write(" + Managed + " value, global::System.Buffers.IBufferWriter<byte> destination) => _codec.Write(value, destination);");
            source.AppendLine("    }");
        }
        else
        {
            serializer?.Emit("Codec_" + Symbol, source, textCodec?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        }
    }

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
