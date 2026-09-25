using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Ankus.Generators;

/// <summary>
/// Builds a closed serialization graph and emits direct member access and constructor calls.
/// </summary>
internal sealed class DefaultTypeSerializer(IAssemblySymbol assembly)
{
    private readonly Dictionary<string, SerializationNode> _nodes = new(StringComparer.Ordinal);
    private readonly List<SerializationNode> _ordered = [];

    /// <summary>
    /// Validates every reachable type before emitting a serializer.
    /// </summary>
    internal static DefaultTypeSerializer? Create(INamedTypeSymbol type, out string? error)
    {
        var serializer = new DefaultTypeSerializer(type.ContainingAssembly);
        try
        {
            serializer.Add(type.WithNullableAnnotation(NullableAnnotation.NotAnnotated));
            error = null;
            return serializer;
        }
        catch (InvalidOperationException exception)
        {
            error = exception.Message;
            return null;
        }
    }

    /// <summary>
    /// Creates a node before visiting its children so recursive contracts remain finite.
    /// </summary>
    private SerializationNode Add(ITypeSymbol type)
    {
        string managed = Display(type);
        if (_nodes.TryGetValue(managed, out SerializationNode? existing))
        {
            return existing;
        }

        if (_ordered.Count >= 256)
        {
            throw new InvalidOperationException("The default serializer supports at most 256 distinct reachable type contracts; provide an explicit codec.");
        }

        var node = new SerializationNode(type, _ordered.Count);
        _nodes.Add(managed, node);
        _ordered.Add(node);
        if (type is INamedTypeSymbol nullable && nullable.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        {
            node.Kind = "nullable";
            node.Element = Add(nullable.TypeArguments[0]);
            return node;
        }

        node.Primitive = type.SpecialType switch
        {
            SpecialType.System_Boolean => "Boolean",
            SpecialType.System_SByte or SpecialType.System_Int16 or SpecialType.System_Int32 or SpecialType.System_Int64 => "Int64",
            SpecialType.System_Byte or SpecialType.System_UInt16 or SpecialType.System_UInt32 or SpecialType.System_UInt64 => "UInt64",
            SpecialType.System_Single => "Single",
            SpecialType.System_Double => "Double",
            SpecialType.System_Decimal => "Decimal",
            SpecialType.System_String => "String",
            _ => null,
        };
        if (node.Primitive is not null)
        {
            node.Kind = "primitive";
            return node;
        }

        if (type is IArrayTypeSymbol array && array.Rank == 1 && array.IsSZArray)
        {
            node.Kind = "array";
            node.Element = Add(array.ElementType);
            return node;
        }

        if (type is not INamedTypeSymbol named || named.IsRefLikeType || named.IsAbstract || named.IsStatic || named.IsUnboundGenericType ||
            named.TypeKind is not (TypeKind.Class or TypeKind.Struct or TypeKind.Enum) || !Accessible(named, assembly))
        {
            throw Unsupported(type, "an accessible concrete value contract is required");
        }

        ValidateAttributes(named);
        string definition = named.OriginalDefinition.ToDisplayString();
        if (definition is "System.Collections.Generic.List<T>" or "System.Collections.Generic.Dictionary<TKey, TValue>")
        {
            node.Kind = definition == "System.Collections.Generic.List<T>" ? "list" : "dictionary";
            if (node.Kind == "dictionary" && (named.TypeArguments[0].SpecialType != SpecialType.System_String ||
                named.TypeArguments[0].NullableAnnotation == NullableAnnotation.Annotated))
            {
                throw Unsupported(type, "dictionary keys must be non-null strings");
            }

            node.Element = Add(named.TypeArguments[named.TypeArguments.Length - 1]);
            return node;
        }

        if (named.TypeKind == TypeKind.Enum)
        {
            node.Kind = "enum";
            var names = new HashSet<string>(StringComparer.Ordinal);
            var values = new HashSet<object>();
            foreach (IFieldSymbol field in named.GetMembers().OfType<IFieldSymbol>().Where(static item => item.HasConstantValue))
            {
                ValidateAttributes(field);
                string name = Attribute(field, "JsonStringEnumMemberNameAttribute")?.ConstructorArguments[0].Value as string ?? field.Name;
                if (!names.Add(name) || !values.Add(field.ConstantValue!))
                {
                    throw Unsupported(type, "enum names and values must be unique");
                }

                node.EnumMembers.Add((field.Name, name));
            }

            return node;
        }

        string namespaceName = named.ContainingNamespace.ToDisplayString();
        if (named.SpecialType != SpecialType.None || namespaceName == "System" || namespaceName.StartsWith("System.", StringComparison.Ordinal) ||
            named.BaseType is { SpecialType: not (SpecialType.System_Object or SpecialType.System_ValueType) })
        {
            throw Unsupported(type, "inheritance and framework-specific contracts require an explicit codec");
        }

        node.Kind = "object";
        var memberNames = new HashSet<string>(StringComparer.Ordinal);
        bool ignoredRequired = false;
        foreach (ISymbol member in named.GetMembers())
        {
            if (member.IsStatic)
            {
                continue;
            }

            AttributeData? ignore = Attribute(member, "JsonIgnoreAttribute");
            if (ignore is not null)
            {
                int condition = AttributeValues.Get(ignore, "Condition", 1);
                if (condition == 1)
                {
                    ignoredRequired |= member is IPropertySymbol { IsRequired: true } or IFieldSymbol { IsRequired: true };
                    continue;
                }

                if (condition != 0)
                {
                    throw Unsupported(type, "conditional JsonIgnore changes stored shape; use Always, Never, or an explicit codec");
                }
            }

            ValidateAttributes(member);
            if (member.DeclaredAccessibility != Accessibility.Public || member.IsImplicitlyDeclared && member is IFieldSymbol)
            {
                ignoredRequired |= member is IPropertySymbol { IsRequired: true } or IFieldSymbol { IsRequired: true };
                continue;
            }

            ITypeSymbol memberType;
            bool writable;
            bool required;
            if (member is IPropertySymbol property)
            {
                if (property.IsIndexer)
                {
                    throw Unsupported(type, "indexers require an explicit codec");
                }

                memberType = property.Type;
                writable = property.SetMethod is { } setter && IsVisible(setter, assembly);
                required = property.IsRequired;
                if (property.GetMethod?.DeclaredAccessibility != Accessibility.Public)
                {
                    throw Unsupported(type, "serialized properties need public getters");
                }
            }
            else if (member is IFieldSymbol field)
            {
                memberType = field.Type;
                writable = !field.IsReadOnly;
                required = field.IsRequired;
            }
            else
            {
                continue;
            }

            AttributeData? rename = Attribute(member, "JsonPropertyNameAttribute");
            string name = rename is null ? member.Name : rename.ConstructorArguments[0].Value as string ??
                throw Unsupported(type, "serialized member names cannot be null");
            if (!memberNames.Add(name))
            {
                throw Unsupported(type, "serialized member names must be unique");
            }

            node.Members.Add(new SerializationMember(member.Name, name, Add(memberType), writable,
                required || Attribute(member, "JsonRequiredAttribute") is not null, required));
        }

        IMethodSymbol[] constructors = [.. named.InstanceConstructors.Where(constructor => IsVisible(constructor, assembly))];
        IMethodSymbol[] marked = [.. named.InstanceConstructors.Where(static constructor => Attribute(constructor, "JsonConstructorAttribute") is not null)];
        node.Constructor = marked.Length switch
        {
            0 => constructors.FirstOrDefault(static constructor => constructor.Parameters.Length == 0) ??
                (constructors.Length == 1 ? constructors[0] : null),
            1 when IsVisible(marked[0], assembly) => marked[0],
            _ => null,
        };
        if (node.Constructor is null)
        {
            throw Unsupported(type, "select one accessible constructor with JsonConstructor, or supply an accessible parameterless constructor");
        }

        foreach (IParameterSymbol parameter in node.Constructor.Parameters)
        {
            SerializationMember[] matches = [.. node.Members.Where(member => string.Equals(member.Name, parameter.Name, StringComparison.OrdinalIgnoreCase) &&
                SymbolEqualityComparer.IncludeNullability.Equals(member.Value.Type, parameter.Type))];
            if (parameter.RefKind != RefKind.None || matches.Length != 1 || node.ConstructorMembers.Contains(matches[0]))
            {
                throw Unsupported(type, "each constructor parameter must match one serialized member by name and exact type, including nullability");
            }

            node.ConstructorMembers.Add(matches[0]);
        }

        if (node.Members.Any(member => !member.Writable && !node.ConstructorMembers.Contains(member)))
        {
            throw Unsupported(type, "read-only members must be bound to constructor parameters");
        }

        if ((ignoredRequired || node.ConstructorMembers.Any(static member => member.InitializerRequired)) &&
            !node.Constructor.GetAttributes().Any(static attribute =>
                attribute.AttributeClass?.ToDisplayString() == "System.Diagnostics.CodeAnalysis.SetsRequiredMembersAttribute"))
        {
            throw Unsupported(type, "constructors binding or ignoring C# required members must carry SetsRequiredMembers to preserve constructor results");
        }

        return node;
    }

    /// <summary>
    /// Emits a private codec inside the generated dispatcher.
    /// </summary>
    internal void Emit(string name, StringBuilder source)
    {
        string root = _ordered[0].Managed;
        source.AppendLine("    private sealed class " + name + " : global::Ankus.PgSerializedTypeCodec<" + root + ">");
        source.AppendLine("    {");
        source.AppendLine("        protected override " + root + " ReadValue(ref global::Ankus.PgTypeReader reader) => Read_0(ref reader);");
        source.AppendLine("        protected override void WriteValue(global::Ankus.PgTypeWriter writer, " + root + " value) => Write_0(writer, value);");
        foreach (SerializationNode node in _ordered)
        {
            EmitRead(node, source);
            EmitWrite(node, source);
        }

        source.AppendLine("    }");
    }

    /// <summary>
    /// Emits exact scalar conversions and owned container construction.
    /// </summary>
    private static void EmitRead(SerializationNode node, StringBuilder source)
    {
        source.AppendLine("        private static " + node.Managed + " Read_" + node.Index + "(ref global::Ankus.PgTypeReader reader)");
        source.AppendLine("        {");
        if (node.CanBeNull)
        {
            source.AppendLine("            if (reader.ReadNull()) return null;");
        }
        else
        {
            source.AppendLine("            if (reader.ReadNull()) throw new global::System.FormatException(\"A required value cannot be null.\");");
        }

        switch (node.Kind)
        {
            case "primitive":
                string conversion = node.Primitive is "Int64" or "UInt64" ? "checked((" + node.Managed + ")reader.Read" + node.Primitive + "())" :
                    "reader.Read" + node.Primitive + "()";
                source.AppendLine("            return " + conversion + ";");
                break;
            case "nullable":
                source.AppendLine("            return Read_" + node.Element!.Index + "(ref reader);");
                break;
            case "enum":
                source.AppendLine("            return reader.ReadString() switch");
                source.AppendLine("            {");
                foreach ((string member, string name) in node.EnumMembers)
                {
                    source.AppendLine("                " + Literal(name) + " => " + node.Managed + ".@" + member + ",");
                }

                source.AppendLine("                _ => throw new global::System.FormatException(\"Unknown enum name.\"),");
                source.AppendLine("            };");
                break;
            case "array":
            case "list":
                source.AppendLine("            reader.ReadStartArray();");
                source.AppendLine("            var values = new global::System.Collections.Generic.List<" + node.Element!.Managed + ">();");
                source.AppendLine("            while (!reader.ReadEndArray()) values.Add(Read_" + node.Element.Index + "(ref reader));");
                source.AppendLine("            return values" + (node.Kind == "array" ? ".ToArray()" : "") + ";");
                break;
            case "dictionary":
                source.AppendLine("            reader.ReadStartObject();");
                source.AppendLine("            var values = new global::System.Collections.Generic.Dictionary<string, " + node.Element!.Managed + ">(global::System.StringComparer.Ordinal);");
                source.AppendLine("            string? key;");
                source.AppendLine("            while ((key = reader.ReadPropertyName()) is not null)");
                source.AppendLine("            {");
                source.AppendLine("                if (!values.TryAdd(key, Read_" + node.Element.Index + "(ref reader))) throw new global::System.FormatException(\"Duplicate dictionary key.\");");
                source.AppendLine("            }");
                source.AppendLine("            return values;");
                break;
            default:
                EmitReadObject(node, source);
                break;
        }

        source.AppendLine("        }");
    }

    /// <summary>
    /// Collects fields before invoking a selected constructor exactly once.
    /// </summary>
    private static void EmitReadObject(SerializationNode node, StringBuilder source)
    {
        source.AppendLine("            reader.ReadStartObject();");
        for (int index = 0; index < node.Members.Count; index++)
        {
            source.AppendLine("            " + node.Members[index].Value.Managed + " value" + index + " = default!;");
            source.AppendLine("            bool seen" + index + " = false;");
        }

        source.AppendLine("            string? key;");
        source.AppendLine("            while ((key = reader.ReadPropertyName()) is not null)");
        source.AppendLine("            {");
        source.AppendLine("                switch (key)");
        source.AppendLine("                {");
        for (int index = 0; index < node.Members.Count; index++)
        {
            SerializationMember member = node.Members[index];
            source.AppendLine("                    case " + Literal(member.SerializedName) + ":");
            source.AppendLine("                        if (seen" + index + ") throw new global::System.FormatException(" + Literal("Duplicate member: " + member.SerializedName) + ");");
            source.AppendLine("                        seen" + index + " = true;");
            source.AppendLine("                        value" + index + " = Read_" + member.Value.Index + "(ref reader);");
            source.AppendLine("                        break;");
        }

        source.AppendLine("                    default: reader.Skip(); break;");
        source.AppendLine("                }");
        source.AppendLine("            }");
        for (int index = 0; index < node.Members.Count; index++)
        {
            SerializationMember member = node.Members[index];
            if (member.Required || !member.Value.CanBeNull)
            {
                source.AppendLine("            if (!seen" + index + ") throw new global::System.FormatException(" + Literal("Missing required member: " + member.SerializedName) + ");");
            }
        }

        source.AppendLine("            return new " + Display(node.Type.WithNullableAnnotation(NullableAnnotation.NotAnnotated)) + "(" +
            string.Join(", ", node.ConstructorMembers.Select(member => "value" + node.Members.IndexOf(member))) + ")");
        source.AppendLine("            {");
        foreach (SerializationMember member in node.Members.Where(member => member.Writable && !node.ConstructorMembers.Contains(member)))
        {
            source.AppendLine("                @" + member.Name + " = value" + node.Members.IndexOf(member) + ",");
        }

        source.AppendLine("            };");
    }

    /// <summary>
    /// Emits member reads without reflection, preserving the declared nullable graph.
    /// </summary>
    private static void EmitWrite(SerializationNode node, StringBuilder source)
    {
        source.AppendLine("        private static void Write_" + node.Index + "(global::Ankus.PgTypeWriter writer, " + node.Managed + " value)");
        source.AppendLine("        {");
        if (node.CanBeNull)
        {
            source.AppendLine("            if (value is null) { writer.WriteNull(); return; }");
        }
        else if (node.Type.IsReferenceType)
        {
            source.AppendLine("            if (value is null) throw new global::System.InvalidOperationException(\"A required value cannot be null.\");");
        }

        if (node.Type.IsReferenceType && node.Kind != "primitive")
        {
            source.AppendLine("            if (value.GetType() != typeof(" + Display(node.Type.WithNullableAnnotation(NullableAnnotation.NotAnnotated)) +
                ")) throw new global::System.InvalidOperationException(\"Runtime subtypes require an explicit codec.\");");
        }

        switch (node.Kind)
        {
            case "primitive":
                source.AppendLine("            writer.Write" + node.Primitive + "(value);");
                break;
            case "nullable":
                source.AppendLine("            Write_" + node.Element!.Index + "(writer, value.Value);");
                break;
            case "enum":
                source.AppendLine("            writer.WriteString(value switch");
                source.AppendLine("            {");
                foreach ((string member, string name) in node.EnumMembers)
                {
                    source.AppendLine("                " + node.Managed + ".@" + member + " => " + Literal(name) + ",");
                }

                source.AppendLine("                _ => throw new global::System.InvalidOperationException(\"Unnamed enum values cannot be serialized.\"),");
                source.AppendLine("            });");
                break;
            case "array":
            case "list":
                source.AppendLine("            writer.WriteStartArray(value." + (node.Kind == "array" ? "Length" : "Count") + ");");
                source.AppendLine("            foreach (" + node.Element!.Managed + " item in value) Write_" + node.Element.Index + "(writer, item);");
                source.AppendLine("            writer.WriteEndArray();");
                break;
            case "dictionary":
                source.AppendLine("            writer.WriteStartObject(value.Count);");
                source.AppendLine("            foreach (global::System.Collections.Generic.KeyValuePair<string, " + node.Element!.Managed + "> item in value)");
                source.AppendLine("            {");
                source.AppendLine("                writer.WritePropertyName(item.Key);");
                source.AppendLine("                Write_" + node.Element.Index + "(writer, item.Value);");
                source.AppendLine("            }");
                source.AppendLine("            writer.WriteEndObject();");
                break;
            default:
                source.AppendLine("            writer.WriteStartObject(" + node.Members.Count.ToString(CultureInfo.InvariantCulture) + ");");
                foreach (SerializationMember member in node.Members)
                {
                    source.AppendLine("            writer.WritePropertyName(" + Literal(member.SerializedName) + ");");
                    source.AppendLine("            Write_" + member.Value.Index + "(writer, value.@" + member.Name + ");");
                }

                source.AppendLine("            writer.WriteEndObject();");
                break;
        }

        source.AppendLine("        }");
    }

    /// <summary>
    /// Finds standard serialization metadata without instantiating attributes.
    /// </summary>
    private static AttributeData? Attribute(ISymbol symbol, string name) => symbol.GetAttributes().FirstOrDefault(attribute =>
        attribute.AttributeClass?.ToDisplayString() == "System.Text.Json.Serialization." + name);

    /// <summary>
    /// Prevents silently ignoring customization that would change persisted meaning.
    /// </summary>
    private static void ValidateAttributes(ISymbol symbol)
    {
        foreach (AttributeData attribute in symbol.GetAttributes())
        {
            for (INamedTypeSymbol? type = attribute.AttributeClass; type is not null; type = type.BaseType)
            {
                if (type.ContainingNamespace.ToDisplayString() == "System.Text.Json.Serialization")
                {
                    if (type.Name is "JsonPropertyNameAttribute" or "JsonIgnoreAttribute" or "JsonConstructorAttribute" or
                        "JsonRequiredAttribute" or "JsonStringEnumMemberNameAttribute")
                    {
                        break;
                    }

                    throw new InvalidOperationException("Default serialization does not support " + type.Name +
                        " on " + symbol.Name + "; provide an explicit codec.");
                }
            }
        }
    }

    /// <summary>
    /// Requires every containing declaration to be visible to generated code.
    /// </summary>
    private static bool Accessible(INamedTypeSymbol type, IAssemblySymbol assembly)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.ContainingType)
        {
            if (current.IsFileLocal || current.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal) ||
                current.DeclaredAccessibility == Accessibility.Internal && !SymbolEqualityComparer.Default.Equals(current.ContainingAssembly, assembly))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Accepts public and assembly-visible constructor and setter access.
    /// </summary>
    private static bool IsVisible(IMethodSymbol method, IAssemblySymbol assembly) => method.DeclaredAccessibility == Accessibility.Public ||
        method.DeclaredAccessibility is Accessibility.Internal or Accessibility.ProtectedOrInternal &&
        SymbolEqualityComparer.Default.Equals(method.ContainingAssembly, assembly);

    /// <summary>
    /// Reports a contract that needs an explicit codec rather than lossy inference.
    /// </summary>
    private static InvalidOperationException Unsupported(ITypeSymbol type, string reason) => new("Cannot serialize " + Display(type) + ": " + reason + ".");

    /// <summary>
    /// Preserves nullable annotations throughout nested generic and array types.
    /// </summary>
    internal static string Display(ITypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
        SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier));

    /// <summary>
    /// Escapes a generated string literal.
    /// </summary>
    private static string Literal(string value) => Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(value, true);
}
