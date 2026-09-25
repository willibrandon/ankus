using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Ankus.Generators;

/// <summary>
/// Validates a closed managed datum converter independently of generated storage codecs.
/// </summary>
internal sealed class DatumTypeDeclaration(INamedTypeSymbol type, INamedTypeSymbol converter, string name, string? schema,
    bool external, bool canRead, bool canWrite, bool inferred, DatumTypeDeclaration? rangeBound = null)
{
    private static readonly DiagnosticDescriptor s_invalid = new(
        "ANKUS019", "Invalid PostgreSQL datum mapping", "'{0}': {1}", "Ankus", DiagnosticSeverity.Error, isEnabledByDefault: true);

    /// <summary>
    /// Gets the exact managed root identity.
    /// </summary>
    internal INamedTypeSymbol Type { get; } = type;

    /// <summary>
    /// Gets the catalog leaf name.
    /// </summary>
    internal string Name { get; } = name;

    /// <summary>
    /// Gets the fixed schema or null for the owning extension schema.
    /// </summary>
    internal string? Schema { get; } = schema;

    /// <summary>
    /// Gets whether the type exists independently of this extension's SQL graph.
    /// </summary>
    internal bool External { get; } = external;

    /// <summary>
    /// Gets whether the converter implements the exact input contract.
    /// </summary>
    internal bool CanRead { get; } = canRead;

    /// <summary>
    /// Gets whether the converter implements the exact output contract.
    /// </summary>
    internal bool CanWrite { get; } = canWrite;

    /// <summary>
    /// Gets whether compiler constraint validation is required for an inferred converter construction.
    /// </summary>
    internal bool HasInferredConverter { get; } = inferred;

    /// <summary>
    /// Gets the scalar conversion shared by a synthetic mapped range contract.
    /// </summary>
    internal DatumTypeDeclaration? RangeBound { get; } = rangeBound;

    /// <summary>
    /// Gets the source format preserving nullable arguments inside a constructed managed identity.
    /// </summary>
    internal static SymbolDisplayFormat ManagedFormat { get; } = SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
        SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    /// <summary>
    /// Gets the globally qualified managed name.
    /// </summary>
    internal string Managed => Type.ToDisplayString(ManagedFormat);

    /// <summary>
    /// Gets the quoted SQL identity without inferring a schema from the function placement.
    /// </summary>
    internal string Sql => new SqlTypeReference(Name, Schema).Sql;

    /// <summary>
    /// Finds mapping metadata without constructing user code.
    /// </summary>
    internal static bool IsMapped(INamedTypeSymbol type) => type.GetAttributes().Any(static attribute =>
        attribute.AttributeClass?.ToDisplayString() == "Ankus.PgDatumTypeAttribute");

    /// <summary>
    /// Distinguishes a finite constructed root from an open annotated generic definition.
    /// </summary>
    internal static bool IsClosed(INamedTypeSymbol type) => !ContainsTypeParameter(type);

    /// <summary>
    /// Resolves an attributed type and optionally reports its invalid contract.
    /// </summary>
    internal static DatumTypeDeclaration? Create(INamedTypeSymbol type, IAssemblySymbol? assembly = null,
        SourceProductionContext? context = null)
    {
        type = (INamedTypeSymbol)type.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
        if (RangeTypeDeclaration.Bound(type) is { } bound)
        {
            DatumTypeDeclaration? scalar = Create(bound, assembly, context);
            return scalar is not null && RangeTypeDeclaration.TryCreate(scalar, out DatumTypeDeclaration? range, context) ? range : null;
        }

        AttributeData[]? attributes = Declarations(type, context);
        if (attributes is null || attributes.Length == 0)
        {
            return null;
        }

        assembly ??= type.ContainingAssembly;
        if (type.TypeKind is not (TypeKind.Class or TypeKind.Struct or TypeKind.Enum) || type.IsStatic || type.IsAbstract ||
            type.IsRefLikeType || !IsClosed(type) || !Accessible(type, assembly) ||
            type.GetAttributes().Any(static item => item.AttributeClass?.ToDisplayString() is "Ankus.PgTypeAttribute" or "Ankus.PgEnumAttribute"))
        {
            return Invalid("PgDatumType requires an accessible, closed, concrete class, struct, or enum without PgType or PgEnum.");
        }

        AttributeData? attribute = attributes.FirstOrDefault(item => item.ConstructorArguments.Length == 3 &&
            SymbolEqualityComparer.Default.Equals(item.ConstructorArguments[0].Value as ITypeSymbol, type)) ??
            attributes.FirstOrDefault(static item => item.ConstructorArguments.Length == 2);
        if (attribute is null)
        {
            return Invalid("No PgDatumType declaration selects this exact closed managed type.");
        }

        int offset = attribute.ConstructorArguments.Length == 3 ? 1 : 0;
        string? name = attribute.ConstructorArguments[offset].Value as string;
        string? schema = AttributeValues.Get<string?>(attribute, "Schema", null);
        if (!SqlText.IsIdentifier(name) || schema is not null && !SqlText.IsIdentifier(schema))
        {
            return Invalid("Type and schema names must be nonempty identifiers of at most 63 UTF-8 bytes, without zero characters or invalid Unicode.");
        }

        int origin = AttributeValues.Get(attribute, "Origin", 0);
        if (origin is not (0 or 1))
        {
            return Invalid("Origin must be ThisExtension or External.");
        }

        if (origin == 1 && schema is null)
        {
            return Invalid("External datum mappings require an explicit Schema.");
        }

        if (attribute.ConstructorArguments[offset + 1].Value is not INamedTypeSymbol converter)
        {
            return Invalid("The converter must be accessible, closed and concrete, with an accessible parameterless constructor.");
        }

        bool inferred = converter.IsUnboundGenericType;
        if (inferred)
        {
            INamedTypeSymbol? constructed = DatumConverterTemplate.Close(converter, type, out string? error);
            if (constructed is null)
            {
                return Invalid(error!);
            }

            converter = constructed;
        }

        if (!Accessible(converter, assembly) || converter.IsAbstract || converter.IsStatic || converter.IsRefLikeType)
        {
            return Invalid("The converter must be accessible, closed and concrete, with an accessible parameterless constructor.");
        }

        IMethodSymbol? constructor = converter.InstanceConstructors.FirstOrDefault(item => item.Parameters.Length == 0 &&
            IsAccessible(item, assembly));
        if (constructor is null)
        {
            return Invalid("The converter must be accessible, closed and concrete, with an accessible parameterless constructor.");
        }

        INamedTypeSymbol[] contracts = [.. converter.AllInterfaces.Where(static item => item.Arity == 1 &&
            item.ContainingNamespace.ToDisplayString() == "Ankus" && item.Name is "IPgDatumReader" or "IPgDatumWriter")
            .Where(item => SymbolEqualityComparer.Default.Equals(item.TypeArguments[0], type))];
        if (contracts.Length == 0 || type.IsReferenceType && contracts.Any(static item => item.TypeArguments[0].NullableAnnotation == NullableAnnotation.Annotated))
        {
            return Invalid("The converter must implement IPgDatumReader<T> or IPgDatumWriter<T> for this exact non-nullable managed type.");
        }

        bool required = false;
        for (INamedTypeSymbol? current = converter; current is not null; current = current.BaseType)
        {
            required |= current.GetMembers().Any(static member => member is IPropertySymbol { IsRequired: true } or IFieldSymbol { IsRequired: true });
        }

        if (required && !constructor.GetAttributes().Any(static item =>
            item.AttributeClass?.ToDisplayString() == "System.Diagnostics.CodeAnalysis.SetsRequiredMembersAttribute"))
        {
            return Invalid("A converter with C# required members needs a parameterless constructor carrying SetsRequiredMembers.");
        }

        return new(type, converter, name!, schema, origin == 1,
            contracts.Any(static item => item.Name == "IPgDatumReader"), contracts.Any(static item => item.Name == "IPgDatumWriter"), inferred);

        DatumTypeDeclaration? Invalid(string message)
        {
            if (context is { } output)
            {
                Error(type, message, output);
            }

            return null;
        }
    }

    /// <summary>
    /// Collects local, signature-referenced and explicitly provided closed mappings before emitting any artifacts.
    /// </summary>
    internal static List<DatumTypeDeclaration>? Discover(Compilation compilation, ImmutableArray<INamedTypeSymbol> local,
        ImmutableArray<INamedTypeSymbol> rangeTypes,
        ImmutableArray<IMethodSymbol> methods, ImmutableArray<INamedTypeSymbol> aggregates, ImmutableArray<AttributeData> attributes,
        SourceProductionContext context)
    {
        var candidates = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.IncludeNullability);
        var requestedRanges = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.IncludeNullability);
        foreach (INamedTypeSymbol type in rangeTypes.Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default))
        {
            AttributeData[]? declared = RangeTypeDeclaration.Declarations(type, context);
            if (declared is null)
            {
                return null;
            }

            if (IsClosed(type))
            {
                candidates.Add(type);
            }

            foreach (AttributeData attribute in declared.Where(static item => item.ConstructorArguments.Length == 2))
            {
                candidates.Add((INamedTypeSymbol)attribute.ConstructorArguments[0].Value!);
            }
        }

        foreach (INamedTypeSymbol type in local.Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default))
        {
            AttributeData[]? declared = Declarations(type, context);
            if (declared is null)
            {
                return null;
            }

            if (IsClosed(type))
            {
                candidates.Add(type);
            }

            foreach (AttributeData attribute in declared.Where(static item => item.ConstructorArguments.Length == 3))
            {
                candidates.Add((INamedTypeSymbol)attribute.ConstructorArguments[0].Value!);
            }
        }

        IMethodSymbol[] signatures = [.. methods.Concat(aggregates.SelectMany(AggregateDeclaration.SelectedMethods))
            .Distinct<IMethodSymbol>(SymbolEqualityComparer.Default)];
        foreach (IMethodSymbol method in signatures)
        {
            Collect(method.ReturnType);
            foreach (IParameterSymbol parameter in method.Parameters)
            {
                Collect(parameter.Type);
            }
        }

        foreach (AttributeData attribute in attributes)
        {
            if (attribute.AttributeClass?.ToDisplayString() == "Ankus.PgSqlTypeProviderAttribute" &&
                attribute.ConstructorArguments.Length == 2 && attribute.ConstructorArguments[1].Value is ITypeSymbol supplied)
            {
                Collect(supplied);
            }
        }

        bool valid = true;
        var declarations = new List<DatumTypeDeclaration>();
        foreach (INamedTypeSymbol type in candidates.OrderBy(static item => item.ToDisplayString(), StringComparer.Ordinal))
        {
            DatumTypeDeclaration? declaration = Create(type, compilation.Assembly, context);
            if (declaration is null)
            {
                valid = false;
            }
            else if (!Resolves(type) || !Resolves(declaration.Converter))
            {
                Error(type, "The mapped type and converter must resolve unambiguously through their global qualified names; extern-alias-only contracts are unsupported.", context);
                valid = false;
            }
            else
            {
                declarations.Add(declaration);
            }
        }

        valid &= DatumConverterTemplate.Validate(compilation, declarations, context);
        foreach (DatumTypeDeclaration scalar in declarations.ToArray())
        {
            if (!RangeTypeDeclaration.TryCreate(scalar, out DatumTypeDeclaration? range, context))
            {
                valid = false;
            }
            else if (range is not null)
            {
                if (!Resolves(range.Type))
                {
                    RangeTypeDeclaration.Error(scalar.Type, "The constructed range must resolve unambiguously through its global qualified name.", context);
                    valid = false;
                }
                else
                {
                    declarations.Add(range);
                }
            }
        }

        foreach (INamedTypeSymbol requested in requestedRanges)
        {
            if (!declarations.Any(item => SymbolEqualityComparer.Default.Equals(item.Type, requested)))
            {
                RangeTypeDeclaration.Error(RangeTypeDeclaration.Bound(requested)!,
                    "No valid PgRangeType declaration selects this exact closed scalar bound type.", context);
                valid = false;
            }
        }

        foreach (IMethodSymbol method in signatures)
        {
            foreach (IParameterSymbol parameter in method.Parameters)
            {
                ValidateSlot(parameter.Type, parameter.GetAttributes(), parameter, read: true);
            }

            ITypeSymbol result = method.ReturnType;
            if (SetResult.IsSequence(result))
            {
                result = ((INamedTypeSymbol)result).TypeArguments[0];
                if (result is INamedTypeSymbol { IsTupleType: true } tuple)
                {
                    AttributeData? namesAttribute = method.GetReturnTypeAttributes().FirstOrDefault(static attribute =>
                        attribute.AttributeClass?.ToDisplayString() == "Ankus.PgColumnNamesAttribute");
                    string[]? columnNames = namesAttribute is { ConstructorArguments.Length: 1 } &&
                        namesAttribute.ConstructorArguments[0] is { Kind: TypedConstantKind.Array, IsNull: false } names
                        ? [.. names.Values.Select(static item => item.Value as string ?? string.Empty)] : null;
                    for (int index = 0; index < tuple.TupleElements.Length; index++)
                    {
                        IFieldSymbol field = tuple.TupleElements[index];
                        string column = columnNames is not null && index < columnNames.Length ? columnNames[index] : SqlText.SnakeCase(field.Name);
                        ImmutableArray<AttributeData> bindings = [.. method.GetReturnTypeAttributes().Where(attribute =>
                            AttributeValues.Get<string?>(attribute, "Column", null) is { } target ? target == column :
                            !tuple.TupleElements.Any(element => attribute.AttributeClass?.ToDisplayString() switch
                            {
                                "Ankus.PgSqlTypeAttribute" => FunctionType.Create(element.Type)?.IsRaw == true,
                                "Ankus.PgCompositeTypeAttribute" => FunctionType.Create(element.Type)?.IsComposite == true,
                                _ => false,
                            }))];
                        ValidateSlot(field.Type, bindings, method, read: false);
                    }

                    continue;
                }

                if (result is INamedTypeSymbol { Name: "ValueTuple", Arity: 1 } single &&
                    single.ContainingNamespace.ToDisplayString() == "System")
                {
                    result = single.TypeArguments[0];
                }
            }

            ValidateSlot(result, method.GetReturnTypeAttributes(), method, read: false);
        }

        return valid ? [.. declarations.GroupBy(static declaration => declaration.Type, SymbolEqualityComparer.Default)
            .Select(static group => group.First())] : null;

        void Collect(ITypeSymbol type)
        {
            if (IsManagedState(type))
            {
                return;
            }

            if (type is IArrayTypeSymbol array)
            {
                Collect(array.ElementType);
            }
            else if (type is INamedTypeSymbol named)
            {
                if (RangeTypeDeclaration.Bound(named) is not null)
                {
                    requestedRanges.Add(named);
                }

                if (IsMapped(named))
                {
                    candidates.Add(named);
                    return;
                }

                foreach (ITypeSymbol argument in named.TypeArguments)
                {
                    Collect(argument);
                }
            }
        }

        bool Resolves(ITypeSymbol type)
        {
            if (type is IArrayTypeSymbol array)
            {
                return Resolves(array.ElementType);
            }

            if (type is not INamedTypeSymbol named)
            {
                return false;
            }

            string metadata = string.Join("+", Containers(named).Reverse().Select(static item => item.MetadataName));
            if (!named.ContainingNamespace.IsGlobalNamespace)
            {
                metadata = named.ContainingNamespace.ToDisplayString() + "." + metadata;
            }

            return SymbolEqualityComparer.Default.Equals(compilation.GetTypeByMetadataName(metadata), named.OriginalDefinition) &&
                named.TypeArguments.All(Resolves) && (named.ContainingType is null || Resolves(named.ContainingType));
        }

        void ValidateSlot(ITypeSymbol slot, ImmutableArray<AttributeData> bindings, ISymbol owner, bool read)
        {
            if (IsManagedState(slot))
            {
                return;
            }

            slot = slot switch
            {
                IArrayTypeSymbol { Rank: 1, IsSZArray: true } array => array.ElementType,
                INamedTypeSymbol { Name: "PgArray", Arity: 1 } array when array.ContainingNamespace.ToDisplayString() == "Ankus"
                    => array.TypeArguments[0],
                _ => slot,
            };
            if (slot is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } optional)
            {
                slot = optional.TypeArguments[0];
            }

            if (slot is INamedTypeSymbol named && (IsMapped(named) || RangeTypeDeclaration.Bound(named) is not null))
            {
                DatumTypeDeclaration? declaration = declarations.FirstOrDefault(item => SymbolEqualityComparer.Default.Equals(item.Type, named));
                if (bindings.Any(static item => item.AttributeClass?.ToDisplayString() is "Ankus.PgSqlTypeAttribute" or "Ankus.PgCompositeTypeAttribute"))
                {
                    Error(owner, "PgDatumType signatures cannot override their mapping with PgSqlType or PgCompositeType.", context);
                    valid = false;
                }

                if (declaration is not null && !(read ? declaration.CanRead : declaration.CanWrite))
                {
                    Error(owner, "The datum mapping for '" + declaration.Managed + "' does not support " +
                        (read ? "reading SQL arguments." : "writing SQL results."), context);
                    valid = false;
                }
            }
            else if (ContainsMapping(slot))
            {
                Error(owner, "Mapped datum types support scalar signatures and one array layer; nested arrays and other containers are unsupported.", context);
                valid = false;
            }
        }
    }

    /// <summary>
    /// Gets the statically constructible converter identity.
    /// </summary>
    internal INamedTypeSymbol Converter { get; } = converter;

    /// <summary>
    /// Validates deterministic exact and default declarations without instantiating open generic roots.
    /// </summary>
    private static AttributeData[]? Declarations(INamedTypeSymbol type, SourceProductionContext? context)
    {
        AttributeData[] attributes = [.. type.GetAttributes().Where(static item =>
            item.AttributeClass?.ToDisplayString() == "Ankus.PgDatumTypeAttribute")];
        var targets = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        bool hasDefault = false;
        foreach (AttributeData attribute in attributes)
        {
            if (attribute.ConstructorArguments.Length == 2)
            {
                if (hasDefault)
                {
                    return Invalid("A managed type may have only one default PgDatumType declaration.");
                }

                hasDefault = true;
            }
            else if (attribute.ConstructorArguments.Length != 3 ||
                attribute.ConstructorArguments[0].Value is not INamedTypeSymbol target || !IsClosed(target) ||
                !SymbolEqualityComparer.Default.Equals(target.OriginalDefinition, type.OriginalDefinition))
            {
                return Invalid("An explicit PgDatumType target must be a closed construction of the annotated managed type.");
            }
            else if (!targets.Add(target))
            {
                return Invalid("A closed managed type may have only one exact PgDatumType declaration.");
            }
        }

        return attributes;

        AttributeData[]? Invalid(string message)
        {
            if (context is { } output)
            {
                Error(type, message, output);
            }

            return null;
        }
    }

    /// <summary>
    /// Emits a lazy closed registration without resolving a backend catalog identity.
    /// </summary>
    internal void EmitRegistration(StringBuilder source)
    {
        if (RangeBound is { } bound)
        {
            source.AppendLine("        global::Ankus.PgDatumRegistry.RegisterRange<" + bound.Managed + ">(" +
                Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(Name, true) + ", " +
                (Schema is null ? "null" : Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(Schema, true)) +
                ", global::Ankus.PgTypeOrigin." + (External ? "External" : "ThisExtension") + ");");
            return;
        }

        source.AppendLine("        global::Ankus.PgDatumRegistry.Register" + (Type.IsValueType ? "Value" : "Reference") + "<" + Managed + ">(" +
            Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(Name, true) + ", " +
            (Schema is null ? "null" : Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(Schema, true)) +
            ", global::Ankus.PgTypeOrigin." + (External ? "External" : "ThisExtension") + ", typeof(" +
            Converter.ToDisplayString(ManagedFormat) + "), static () => new " +
            Converter.ToDisplayString(ManagedFormat) + "(), " +
            (CanRead ? "true" : "false") + ", " + (CanWrite ? "true" : "false") + ");");
    }

    /// <summary>
    /// Reports a source-located invalid mapping contract.
    /// </summary>
    internal static void Error(ISymbol symbol, string message, SourceProductionContext context)
        => context.ReportDiagnostic(Diagnostic.Create(s_invalid, symbol.Locations.FirstOrDefault(), symbol.Name, message));

    /// <summary>
    /// Finds a mapped leaf inside an unsupported container shape.
    /// </summary>
    private static bool ContainsMapping(ITypeSymbol type) => type switch
    {
        IArrayTypeSymbol array => ContainsMapping(array.ElementType),
        IPointerTypeSymbol pointer => ContainsMapping(pointer.PointedAtType),
        INamedTypeSymbol named => IsMapped(named) || named.TypeArguments.Any(ContainsMapping),
        _ => false,
    };

    /// <summary>
    /// Finds unbound parameters in a root, its arguments or any constructed containing type.
    /// </summary>
    private static bool ContainsTypeParameter(ITypeSymbol type) => type switch
    {
        ITypeParameterSymbol => true,
        IArrayTypeSymbol array => ContainsTypeParameter(array.ElementType),
        IPointerTypeSymbol pointer => ContainsTypeParameter(pointer.PointedAtType),
        INamedTypeSymbol named => named.IsUnboundGenericType || named.TypeArguments.Any(ContainsTypeParameter) ||
            named.ContainingType is not null && ContainsTypeParameter(named.ContainingType),
        _ => false,
    };

    /// <summary>
    /// Distinguishes an arbitrary managed aggregate payload from a SQL datum container.
    /// </summary>
    private static bool IsManagedState(ITypeSymbol type)
        => type is INamedTypeSymbol { Name: "PgAggregateState", Arity: 1 } state && state.ContainingNamespace.ToDisplayString() == "Ankus";

    /// <summary>
    /// Enumerates a declaration and its containing managed types.
    /// </summary>
    private static IEnumerable<INamedTypeSymbol> Containers(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.ContainingType)
        {
            yield return current;
        }
    }

    /// <summary>
    /// Checks closed type accessibility from generated code, including generic arguments and friend assemblies.
    /// </summary>
    private static bool Accessible(ITypeSymbol type, IAssemblySymbol assembly)
        => type is IArrayTypeSymbol array ? Accessible(array.ElementType, assembly) :
            type is INamedTypeSymbol named && Containers(named).All(current => !current.IsUnboundGenericType && !current.IsFileLocal &&
                IsAccessible(current, assembly) && current.TypeArguments.All(argument => Accessible(argument, assembly)));

    /// <summary>
    /// Accepts public declarations and internal access granted to the generated assembly.
    /// </summary>
    private static bool IsAccessible(ISymbol symbol, IAssemblySymbol assembly)
        => symbol.DeclaredAccessibility == Accessibility.Public ||
            symbol.DeclaredAccessibility is Accessibility.Internal or Accessibility.ProtectedOrInternal && symbol.ContainingAssembly.GivesAccessTo(assembly);
}
