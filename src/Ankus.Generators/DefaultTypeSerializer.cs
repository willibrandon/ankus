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
    internal static SerializationModel? Create(INamedTypeSymbol type, out string? error)
    {
        var serializer = new DefaultTypeSerializer(type.ContainingAssembly);
        try
        {
            serializer.Add(type.WithNullableAnnotation(NullableAnnotation.NotAnnotated));
            foreach (SerializationNode contract in serializer._ordered.Where(static node => node.Kind == "polymorphic"))
            {
                if (contract.Variants.Select(static variant => variant.Shape).Concat(contract.BaseShape is { } baseShape ? [baseShape] : [])
                    .Any(shape => shape.Members.Any(member => member.SerializedName == contract.DiscriminatorName)))
                {
                    throw Unsupported(contract.Type, "the discriminator property must not collide with a serialized member");
                }
            }

            error = null;
            return serializer.Freeze();
        }
        catch (InvalidOperationException exception)
        {
            error = exception.Message;
            return null;
        }
    }

    /// <summary>
    /// Detaches the validated graph without retaining symbols, constructor objects or cyclic node references.
    /// </summary>
    /// <returns>The exact ordered contracts needed by codec rendering.</returns>
    private SerializationModel Freeze() => new(new(_ordered.Select(static node => new SerializationModel.Node(
        node.Managed, Display(node.Type.WithNullableAnnotation(NullableAnnotation.NotAnnotated)),
        node.Type.IsReferenceType, node.CanBeNull, node.Index, node.Kind, node.Primitive, node.Element?.Index,
        node.DiscriminatorName, new(node.Variants.Select(static variant => new SerializationModel.Variant(
            variant.Shape.Index, variant.Tag as string, variant.Tag is int number ? number : null))),
        node.BaseShape?.Index, new(node.Members.Select(static member => new SerializationModel.Member(
            member.Name, member.SerializedName, member.Value.Index, member.Writable, member.Required))),
        new(node.EnumMembers), new(node.ConstructorMembers.Select(member => node.Members.IndexOf(member)))))));

    /// <summary>
    /// Creates a node before visiting its children so recursive contracts remain finite.
    /// </summary>
    private SerializationNode Add(ITypeSymbol type, bool objectShape = false)
    {
        string managed = Display(type);
        string key = objectShape ? "object:" + managed : managed;
        if (_nodes.TryGetValue(key, out SerializationNode? existing))
        {
            return existing;
        }

        if (_ordered.Count >= 256)
        {
            throw new InvalidOperationException("The default serializer supports at most 256 distinct reachable type contracts; provide an explicit codec.");
        }

        var node = new SerializationNode(type, _ordered.Count);
        _nodes.Add(key, node);
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

        if (type is not INamedTypeSymbol named || named.IsRefLikeType || named.IsStatic || named.IsUnboundGenericType ||
            named.TypeKind is not (TypeKind.Class or TypeKind.Struct or TypeKind.Enum) || !Accessible(named, assembly))
        {
            throw Unsupported(type, "an accessible concrete value contract is required");
        }

        ValidateAttributes(named);
        if (!objectShape && (Attribute(named, "JsonPolymorphicAttribute") is not null ||
            Attribute(named, "JsonDerivedTypeAttribute") is not null))
        {
            AddPolymorphic(node, named);
            return node;
        }

        if (named.IsAbstract)
        {
            throw Unsupported(type, "abstract contracts must declare concrete variants with JsonDerivedType");
        }

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

        if (named.SpecialType != SpecialType.None)
        {
            throw Unsupported(type, "framework-specific contracts require an explicit codec");
        }

        node.Kind = "object";
        var memberNames = new HashSet<string>(StringComparer.Ordinal);
        bool ignoredRequired = false;
        foreach (ISymbol member in ObjectMembers(named))
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
    /// Resolves explicitly tagged concrete variants without permitting identity-losing fallback.
    /// </summary>
    private void AddPolymorphic(SerializationNode node, INamedTypeSymbol type)
    {
        if (type.TypeKind != TypeKind.Class)
        {
            throw Unsupported(type, "polymorphic contracts must be classes");
        }

        node.Kind = "polymorphic";
        AttributeData? configuration = Attribute(type, "JsonPolymorphicAttribute");
        if (configuration is not null)
        {
            node.DiscriminatorName = AttributeValues.Get(configuration, "TypeDiscriminatorPropertyName", "$type");
            if (AttributeValues.Get(configuration, "IgnoreUnrecognizedTypeDiscriminators", false) ||
                AttributeValues.Get(configuration, "UnknownDerivedTypeHandling", 0) != 0)
            {
                throw Unsupported(type, "polymorphic fallback loses concrete type identity; unknown discriminators and derived types must fail");
            }
        }

        var tags = new HashSet<object>();
        var types = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        foreach (AttributeData registration in type.GetAttributes().Where(static attribute =>
            attribute.AttributeClass?.ToDisplayString() == "System.Text.Json.Serialization.JsonDerivedTypeAttribute"))
        {
            if (registration.ConstructorArguments.Length != 2 ||
                registration.ConstructorArguments[0].Value is not INamedTypeSymbol derived ||
                registration.ConstructorArguments[1].Value is not (string or int))
            {
                throw Unsupported(type, "each JsonDerivedType must specify a concrete type and a string or Int32 discriminator");
            }

            object tag = registration.ConstructorArguments[1].Value!;
            bool related = false;
            for (INamedTypeSymbol? current = derived; current is not null; current = current.BaseType)
            {
                related |= SymbolEqualityComparer.Default.Equals(current, type);
            }

            if (!related || derived.IsAbstract || derived.IsStatic || derived.IsUnboundGenericType || !Accessible(derived, assembly))
            {
                throw Unsupported(type, "registered variants must be accessible, closed, concrete classes assignable to the declared base");
            }

            if (!types.Add(derived) || !tags.Add(tag))
            {
                throw Unsupported(type, "registered variant types and typed discriminators must be unique");
            }

            node.Variants.Add((Add(derived.WithNullableAnnotation(NullableAnnotation.NotAnnotated), true), tag));
        }

        if (node.Variants.Count == 0)
        {
            throw Unsupported(type, "JsonPolymorphic requires explicit JsonDerivedType registrations");
        }

        if (!type.IsAbstract)
        {
            node.BaseShape = Add(type.WithNullableAnnotation(NullableAnnotation.NotAnnotated), true);
        }
    }

    /// <summary>
    /// Includes inherited state once, honoring overrides and rejecting hidden serialized members.
    /// </summary>
    private static IEnumerable<ISymbol> ObjectMembers(INamedTypeSymbol type)
    {
        var hiddenNames = new HashSet<string>(StringComparer.Ordinal);
        var overridden = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        for (INamedTypeSymbol? current = type; current is not null &&
            current.SpecialType is not (SpecialType.System_Object or SpecialType.System_ValueType); current = current.BaseType)
        {
            string namespaceName = current.ContainingNamespace.ToDisplayString();
            if (current.SpecialType != SpecialType.None || namespaceName == "System" || namespaceName.StartsWith("System.", StringComparison.Ordinal))
            {
                throw Unsupported(type, "framework-specific contracts require an explicit codec");
            }

            ValidateAttributes(current);
            foreach (ISymbol member in current.GetMembers())
            {
                if (overridden.Contains(member))
                {
                    continue;
                }

                if (member is IPropertySymbol property)
                {
                    bool ignored = Attribute(property, "JsonIgnoreAttribute") is { } propertyIgnore &&
                        AttributeValues.Get(propertyIgnore, "Condition", 1) == 1;
                    for (IPropertySymbol? parent = property.OverriddenProperty; parent is not null; parent = parent.OverriddenProperty)
                    {
                        if (!ignored)
                        {
                            ValidateAttributes(parent);
                        }

                        overridden.Add(parent);
                    }
                }

                if (!member.IsStatic && member.DeclaredAccessibility == Accessibility.Public && member is IPropertySymbol or IFieldSymbol &&
                    hiddenNames.Contains(member.Name) && !(Attribute(member, "JsonIgnoreAttribute") is { } ignore &&
                    AttributeValues.Get(ignore, "Condition", 1) == 1))
                {
                    throw Unsupported(type, "hidden serialized members require an explicit codec");
                }

                yield return member;
            }

            hiddenNames.UnionWith(current.GetMembers().Select(static member => member.Name));
        }
    }

    /// <summary>
    /// Finds standard serialization metadata without instantiating attributes.
    /// </summary>
    private static AttributeData? Attribute(ISymbol symbol, string name) => symbol.GetAttributes().FirstOrDefault(attribute =>
        attribute.AttributeClass?.ToDisplayString() == "System.Text.Json.Serialization." + name) ??
        (symbol is IPropertySymbol { OverriddenProperty: { } parent } ? Attribute(parent, name) : null);

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
                        "JsonRequiredAttribute" or "JsonStringEnumMemberNameAttribute" or "JsonPolymorphicAttribute" or "JsonDerivedTypeAttribute")
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
}
