using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;

namespace Ankus.Generators;

/// <summary>
/// Resolves compiler-checked aggregate capabilities into SQL helpers and managed calls.
/// </summary>
internal static class AggregateContract
{
    private static readonly (string Name, int Arity)[] s_interfaces =
    [
        ("IPgAggregate", 2), ("IPgFinalizingAggregate", 3), ("IPgCombinableAggregate", 1),
        ("IPgSerializableAggregate", 1), ("IPgMovingAggregate", 2), ("IPgMovingFinalizingAggregate", 3),
    ];

    /// <summary>
    /// Finds supported closed capability interfaces on an aggregate container.
    /// </summary>
    internal static INamedTypeSymbol[] Interfaces(INamedTypeSymbol type)
        => [.. type.AllInterfaces.Where(static candidate => candidate.ContainingNamespace.ToDisplayString() == "Ankus" &&
            s_interfaces.Contains((candidate.Name, candidate.Arity)))];

    /// <summary>
    /// Reserves implementations from ordinary SQL function discovery, including explicit implementations.
    /// </summary>
    internal static IEnumerable<IMethodSymbol> SelectedMethods(INamedTypeSymbol type)
        => Interfaces(type).SelectMany(static capability => capability.GetMembers().OfType<IMethodSymbol>())
            .Select(type.FindImplementationForInterfaceMember).OfType<IMethodSymbol>();

    /// <summary>
    /// Builds optional capability helpers without using conventional method-name lookup.
    /// </summary>
    internal static bool Create(AggregateDeclaration aggregate, SourceProductionContext context)
    {
        INamedTypeSymbol type = aggregate.Type;
        INamedTypeSymbol[] interfaces = Interfaces(type);
        if (interfaces.Count(static capability => capability.Name == "IPgAggregate") != 1 ||
            interfaces.GroupBy(static capability => capability.Name, StringComparer.Ordinal).Any(static group => group.Count() != 1))
        {
            return Invalid(type, "Typed aggregates require exactly one IPgAggregate state/input contract and at most one of each optional capability.");
        }

        var roles = new Dictionary<string, IMethodSymbol>(StringComparer.Ordinal);
        foreach (IMethodSymbol contract in interfaces.SelectMany(static capability => capability.GetMembers().OfType<IMethodSymbol>()))
        {
            roles.Add(contract.Name, contract);
        }

        foreach (string role in AggregateDeclaration.Roles)
        {
            if (aggregate.Attribute.NamedArguments.Any(argument => argument.Key == role))
            {
                return Invalid(type, "Typed aggregates select callbacks through their interfaces; remove callback name overrides from PgAggregate.");
            }

            if (!roles.TryGetValue(role, out IMethodSymbol? contract))
            {
                if (type.GetMembers(role).OfType<IMethodSymbol>().Any())
                {
                    return Invalid(type, $"The {role} method requires its aggregate capability interface on a typed aggregate.");
                }

                continue;
            }

            if (type.FindImplementationForInterfaceMember(contract) is not IMethodSymbol method || !method.IsStatic || method.IsAbstract ||
                method.IsAsync || method.IsGenericMethod)
            {
                return Invalid(type, $"Implement the static {role} member of {contract.ContainingType.ToDisplayString()} with a synchronous method.");
            }

            if (method.Parameters.Any(static parameter => parameter.RefKind != RefKind.None || parameter.IsOptional) ||
                method.Parameters[0].GetAttributes().Length != 0 || method.Parameters[0].IsParams ||
                method.GetAttributes().Any(static attribute => attribute.AttributeClass?.ToDisplayString() is
                    "Ankus.PgTriggerAttribute" or "Ankus.PgEventTriggerAttribute" or "Ankus.PgOperatorAttribute" or "Ankus.PgCastAttribute") ||
                method.GetReturnTypeAttributes().Any(static attribute => attribute.AttributeClass?.ToDisplayString() == "Ankus.PgColumnNamesAttribute") ||
                SetResult.IsSequence(contract.ReturnType))
            {
                return Invalid(method, "Aggregate capabilities require scalar results, required by-value inputs and an unannotated invocation context.");
            }

            if (!AggregateContractNullability.Validate(contract, method, context) ||
                !SqlTypeReference.ValidateValue(contract.ReturnType, method.GetReturnTypeAttributes(), method, "return", context) ||
                !NumericConstraint.ValidateValue(contract.ReturnType, method.GetReturnTypeAttributes(), context) ||
                !SqlNullability.ValidateValue(contract.ReturnType, method, method.Name + " result", context))
            {
                return false;
            }

            AggregateType? result = AggregateType.Create(contract.ReturnType, method.GetReturnTypeAttributes());
            if (result is null)
            {
                return Invalid(method, "The aggregate capability result must be a supported SQL value or a concrete PgAggregateState<T>.");
            }

            var slots = new List<AggregateParameter>();
            var arguments = new List<AggregateArgument>();
            for (int index = 1; index < contract.Parameters.Length; index++)
            {
                ITypeSymbol parameterType = contract.Parameters[index].Type;
                IParameterSymbol parameter = method.Parameters[index];
                bool group = index == 2 && role is "Transition" or "MovingTransition" or "MovingInverse" or "Final" or "MovingFinal";
                bool tuple = group && parameterType is INamedTypeSymbol { IsTupleType: true };
                bool empty = group && parameterType is INamedTypeSymbol { Name: "ValueTuple", Arity: 0, ContainingNamespace: { } ns } && ns.ToDisplayString() == "System";
                int start = slots.Count;
                if (tuple)
                {
                    INamedTypeSymbol tupleType = (INamedTypeSymbol)parameterType;
                    ImmutableArray<AttributeData> metadata = [.. parameter.GetAttributes().Where(AggregateParameter.IsSqlMetadata)];
                    if (parameter.IsParams || metadata.Any(attribute => AttributeValues.Get<string?>(attribute, "Element", null) is not { } element ||
                        !tupleType.TupleElements.Any(field => field.Name == element)))
                    {
                        return Invalid(parameter, "Tuple input metadata must select an existing C# tuple element by its exact Element name.");
                    }

                    foreach (IFieldSymbol element in tupleType.TupleElements)
                    {
                        ImmutableArray<AttributeData> attributes = [.. metadata.Where(attribute => AttributeValues.Get<string?>(attribute, "Element", null) == element.Name)];
                        if (!AddSlot(element.Type, AggregateParameter.ReadName(attributes, element.Name), AggregateParameter.ReadVariadic(attributes), attributes, parameter, grouped: true))
                        {
                            return false;
                        }
                    }
                }
                else if (empty)
                {
                    if (parameter.GetAttributes().Length != 0 || parameter.IsParams)
                    {
                        return Invalid(parameter, "An empty argument group has no SQL values to annotate.");
                    }
                }
                else if (!AddSlot(parameterType, AggregateParameter.ReadName(parameter), parameter.IsParams || AggregateParameter.ReadVariadic(parameter.GetAttributes()),
                    parameter.GetAttributes(), parameter))
                {
                    return false;
                }

                arguments.Add(new(parameterType, start, slots.Count - start, tuple));
            }

            bool extra = role == "Final" && aggregate.FinalExtra || role == "MovingFinal" && aggregate.MovingFinalExtra;
            if (extra)
            {
                var names = new HashSet<string>(slots.Select(static slot => slot.Name), StringComparer.Ordinal);
                int suffix = 0;
                foreach (AggregateParameter input in aggregate.Helpers["Transition"].Parameters.Skip(1))
                {
                    string extraName;
                    do
                    {
                        extraName = "__ankus_extra_" + (++suffix).ToString(CultureInfo.InvariantCulture);
                    }
                    while (!names.Add(extraName));

                    slots.Add(new(input.Type.AsNullable(), extraName, false, input.Attributes));
                }
            }

            if (slots.Count + (role == "Deserialize" ? 1 : 0) > 100 || slots.Select(static slot => slot.Name).Distinct(StringComparer.Ordinal).Count() != slots.Count)
            {
                return Invalid(method, "Aggregate support functions require at most 100 SQL arguments with distinct names.");
            }

            if (result.Datum?.IsSqlPolymorphic == true && !slots.Any(static slot => slot.Type.Datum?.IsSqlPolymorphic == true))
            {
                return Invalid(method, "A polymorphic aggregate result requires a polymorphic input; use FinalExtra for an internal-state final callback.");
            }

            AttributeData? function = method.GetAttributes().FirstOrDefault(static value => value.AttributeClass?.ToDisplayString() == "Ankus.PgFunctionAttribute");
            string helperName = SqlText.SnakeCase(type.Name) + "_" + SqlText.SnakeCase(role);
            if (function is not null)
            {
                helperName = AttributeValues.Get(function, "Name", helperName);
            }

            if (!SqlText.IsIdentifier(helperName))
            {
                return Invalid(method, "Aggregate support function names must be valid identifiers of at most 63 UTF-8 bytes.");
            }

            FunctionDeclaration? declaration = FunctionDeclaration.Create(method, helperName, context, contextParameter: true,
                sqlNullability: [.. slots.Select(static slot => slot.Type.Nullable)], schemaFallback: aggregate.Schema);
            if (declaration is null)
            {
                return false;
            }

            aggregate.Helpers.Add(role, new(method, role, true, [.. slots], result, declaration, new(type, contract, [.. arguments])));

            bool AddSlot(ITypeSymbol valueType, string name, bool variadic, ImmutableArray<AttributeData> attributes, IParameterSymbol source, bool grouped = false)
            {
                if (!SqlNullability.ValidateValue(valueType, source, source.Name, context) ||
                    !SqlTypeReference.ValidateValue(valueType, attributes, method, name, context, grouped) ||
                    !NumericConstraint.ValidateValue(valueType, attributes, context, grouped))
                {
                    return false;
                }

                AggregateType? value = AggregateType.Create(valueType, attributes);
                AttributeData[] naming = [.. attributes.Where(static attribute => attribute.AttributeClass?.ToDisplayString() == "Ankus.PgParameterAttribute")];
                if (value is null || !SqlText.IsIdentifier(name) || naming.Length > 1 || naming.Any(attribute =>
                    attribute.NamedArguments.Any(static argument => argument.Key == "Default") ||
                    !grouped && AttributeValues.Get<string?>(attribute, "Element", null) is not null))
                {
                    return Invalid(source, "Aggregate inputs require supported SQL types, valid names and no SQL defaults.");
                }

                slots.Add(new(value, name, variadic, attributes));
                return true;
            }
        }

        return true;

        bool Invalid(ISymbol source, string message)
        {
            AggregateDeclaration.ReportInvalid(source, type.Name, message, context);
            return false;
        }
    }
}
