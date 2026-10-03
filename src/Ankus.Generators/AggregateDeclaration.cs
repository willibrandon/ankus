using Microsoft.CodeAnalysis;

namespace Ankus.Generators;

/// <summary>
/// Resolves a declarative aggregate and validates PostgreSQL support-function relationships.
/// </summary>
internal sealed class AggregateDeclaration(INamedTypeSymbol type, AttributeData attribute)
{
    private static readonly DiagnosticDescriptor s_invalid = new(
        "ANKUS012", "Invalid PostgreSQL aggregate declaration", "'{0}': {1}", "Ankus", DiagnosticSeverity.Error, isEnabledByDefault: true);
    private static readonly DiagnosticDescriptor s_missingContract = new(
        "ANKUS029", "Missing PostgreSQL aggregate contract",
        "'{0}' must implement IPgAggregate<TState, TArgs>; declare optional callbacks through their aggregate capability interfaces",
        "Ankus", DiagnosticSeverity.Error, isEnabledByDefault: true,
        helpLinkUri: "https://willibrandon.github.io/ankus/aggregates/#compiler-checked-aggregate-contracts");
    private static readonly string[] s_roles = ["Transition", "Final", "Combine", "Serialize", "Deserialize", "MovingTransition", "MovingInverse", "MovingFinal"];

    /// <summary>
    /// Gets support roles in transition-before-final order.
    /// </summary>
    internal static IEnumerable<string> Roles => s_roles;

    /// <summary>
    /// Reports the common aggregate diagnostic at the offending declaration.
    /// </summary>
    internal static void ReportInvalid(ISymbol source, string name, string message, GeneratorDiagnostics context)
        => context.Report(s_invalid, source.Locations.FirstOrDefault(), name, message);

    /// <summary>
    /// Gets the aggregate's managed declaration container.
    /// </summary>
    internal INamedTypeSymbol Type { get; } = type;

    /// <summary>
    /// Gets the aggregate options and graph metadata.
    /// </summary>
    internal AttributeData Attribute { get; } = attribute;

    /// <summary>
    /// Gets support functions keyed by conventional role.
    /// </summary>
    internal Dictionary<string, AggregateHelper> Helpers { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets the aggregate's unquoted SQL name.
    /// </summary>
    internal string Name
    {
        get;
        private set;
    } = string.Empty;

    /// <summary>
    /// Gets the fixed schema, or null for the extension's installation namespace.
    /// </summary>
    internal string? Schema
    {
        get;
        private set;
    }

    /// <summary>
    /// Gets the qualified SQL identifier.
    /// </summary>
    internal string QualifiedName => (Schema is null ? string.Empty : SqlText.Identifier(Schema) + ".") + SqlText.Identifier(Name);

    /// <summary>
    /// Gets the normal, ordered-set, or hypothetical-set kind.
    /// </summary>
    internal int Kind
    {
        get;
        private set;
    }

    /// <summary>
    /// Gets the aggregate-level parallel-safety setting.
    /// </summary>
    internal int Parallel
    {
        get;
        private set;
    }

    /// <summary>
    /// Gets the validated textual initial condition, preserving null separately from empty text.
    /// </summary>
    internal string? Initial
    {
        get;
        private set;
    }

    /// <summary>
    /// Gets the moving implementation's textual initial condition.
    /// </summary>
    internal string? MovingInitial
    {
        get;
        private set;
    }

    /// <summary>
    /// Gets whether final receives typed SQL NULL input placeholders.
    /// </summary>
    internal bool FinalExtra
    {
        get;
        private set;
    }

    /// <summary>
    /// Gets whether moving final receives typed SQL NULL input placeholders.
    /// </summary>
    internal bool MovingFinalExtra
    {
        get;
        private set;
    }

    /// <summary>
    /// Gets the resolved final mutation policy, including PostgreSQL's kind-specific default.
    /// </summary>
    internal int FinalModify
    {
        get;
        private set;
    }

    /// <summary>
    /// Gets the resolved moving final mutation policy.
    /// </summary>
    internal int MovingFinalModify
    {
        get;
        private set;
    }

    /// <summary>
    /// Gets an optional structured sort operator name for SQL emission.
    /// </summary>
    internal string? SortOperator
    {
        get;
        private set;
    }

    /// <summary>
    /// Gets aggregated input contracts after the transition state.
    /// </summary>
    internal AggregateType[] Inputs => [.. Helpers["Transition"].Types.Skip(1)];

    /// <summary>
    /// Gets ordered direct arguments, which reach final functions but not transitions.
    /// </summary>
    internal AggregateType[] Direct
    {
        get;
        private set;
    } = [];

    /// <summary>
    /// Gets the aggregate signature in PostgreSQL's shared function namespace.
    /// </summary>
    internal string Signature => QualifiedName + "(" + string.Join(",", Direct.Concat(Inputs).Select(static value => value.Sql)) + ")";

    /// <summary>
    /// Freezes complete validated role and catalog semantics without retaining compiler state.
    /// </summary>
    /// <returns>The immutable aggregate and support contract.</returns>
    internal AggregateModel Freeze() => new(Name, Schema, Kind, Parallel, Initial, MovingInitial, FinalExtra, MovingFinalExtra,
        FinalModify, MovingFinalModify, AttributeValues.Get(Attribute, "StateSize", 0), AttributeValues.Get(Attribute, "MovingStateSize", 0),
        Attribute.NamedArguments.Any(static argument => argument.Key == "MovingFinalModify"), SortOperator, new(Direct),
        new(Helpers.Values.Select(static helper => helper.Freeze())));

    /// <summary>
    /// Reserves selected support methods from ordinary function discovery, including invalid aggregate declarations.
    /// </summary>
    internal static IEnumerable<IMethodSymbol> SelectedMethods(INamedTypeSymbol type)
        => AggregateContract.SelectedMethods(type);

    /// <summary>
    /// Creates a complete aggregate contract or reports declaration diagnostics.
    /// </summary>
    /// <param name="type">The attributed aggregate container.</param>
    /// <param name="compilation">The current compiler context for inherited-member accessibility.</param>
    /// <param name="context">The diagnostic receiver and cancellation context.</param>
    /// <returns>The validated declaration, or null after a reported error.</returns>
    internal static AggregateDeclaration? Create(INamedTypeSymbol type, Compilation compilation, GeneratorDiagnostics context)
    {
        AttributeData attribute = type.GetAttributes().First(static item => item.AttributeClass?.ToDisplayString() == "Ankus.PgAggregateAttribute");
        var aggregate = new AggregateDeclaration(type, attribute)
        {
            Name = AttributeValues.Get(attribute, "Name", SqlText.SnakeCase(type.Name)),
            Schema = AttributeValues.Get<string?>(attribute, "Schema", null),
            Kind = AttributeValues.Get(attribute, "Kind", 0),
            Parallel = AttributeValues.Get(attribute, "ParallelSafety", 0),
            Initial = AttributeValues.Get<string?>(attribute, "InitialCondition", null),
            MovingInitial = AttributeValues.Get<string?>(attribute, "MovingInitialCondition", null),
            FinalExtra = AttributeValues.Get(attribute, "FinalExtra", false),
            MovingFinalExtra = AttributeValues.Get(attribute, "MovingFinalExtra", false),
            FinalModify = AttributeValues.Get(attribute, "FinalModify", 0),
            MovingFinalModify = AttributeValues.Get(attribute, "MovingFinalModify", 0),
        };
        for (INamedTypeSymbol? container = type; container is not null; container = container.ContainingType)
        {
            if (container.IsGenericType || container.IsFileLocal || container.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal))
            {
                return Invalid("Aggregate containers must be accessible, non-generic, non-file-local classes or structs.");
            }

            AttributeData? schema = container.GetAttributes().FirstOrDefault(static item => item.AttributeClass?.ToDisplayString() == "Ankus.PgSchemaAttribute");
            if (aggregate.Schema is null && schema is not null)
            {
                aggregate.Schema = schema.ConstructorArguments[0].Value as string;
            }
        }

        if (type.TypeKind is not (TypeKind.Class or TypeKind.Struct) || !SqlText.IsIdentifier(aggregate.Name) ||
            aggregate.Schema is not null && !SqlText.IsIdentifier(aggregate.Schema))
        {
            return Invalid("Aggregate names and schemas must be valid identifiers of at most 63 UTF-8 bytes.");
        }

        if (aggregate.Kind is < 0 or > 2 || aggregate.Parallel is < 0 or > 2 ||
            aggregate.FinalModify is < 0 or > 3 || aggregate.MovingFinalModify is < 0 or > 3)
        {
            return Invalid("Aggregate kind, parallel safety, and final mutation policies must be defined enum values.");
        }

        aggregate.FinalModify = aggregate.FinalModify == 0 ? (aggregate.Kind == 0 ? 1 : 3) : aggregate.FinalModify;
        aggregate.MovingFinalModify = aggregate.MovingFinalModify == 0 ? (aggregate.Kind == 0 ? 1 : 3) : aggregate.MovingFinalModify;
        if (aggregate.Initial is not null && !SqlText.IsText(aggregate.Initial) ||
            aggregate.MovingInitial is not null && !SqlText.IsText(aggregate.MovingInitial))
        {
            return Invalid("Initial conditions must contain valid Unicode without zero characters.");
        }

        if (!AggregateContract.Interfaces(type).Any(static capability => capability.Name == "IPgAggregate"))
        {
            context.Report(s_missingContract, type.Locations.FirstOrDefault(), type.Name);
            return null;
        }

        if (!AggregateContract.Create(aggregate, compilation, context))
        {
            return null;
        }

        AggregateHelper transition = aggregate.Helpers["Transition"];
        if (transition.Types.Length == 0 || !transition.Result.Matches(transition.Types[0]) || transition.Result.Sql == "record")
        {
            return Invalid("Transition must accept a state followed by aggregate inputs and return the same state type; composite state requires a named type.");
        }

        AggregateType state = transition.Result;
        AggregateType[] inputs = aggregate.Inputs;
        AggregateHelper? final = aggregate.Helpers.TryGetValue("Final", out AggregateHelper? finalHelper) ? finalHelper : null;
        if (final is not null)
        {
            int directCount = final.Types.Length - 1 - (aggregate.FinalExtra ? inputs.Length : 0);
            if (directCount < 0 || !final.Types[0].Matches(state))
            {
                return Invalid("Final must accept the ordinary state, direct arguments, and any requested extra aggregate inputs.");
            }

            aggregate.Direct = [.. final.Types.Skip(1).Take(directCount)];
        }

        if (aggregate.Kind == 0 && aggregate.Direct.Length != 0 || aggregate.Kind != 0 && inputs.Length == 0 ||
            aggregate.Direct.Length + inputs.Length > 99 || inputs.Any(static value => value.IsInternal) || aggregate.Direct.Any(static value => value.IsInternal))
        {
            return Invalid("Normal aggregates have no direct arguments; ordered aggregates require aggregated inputs; at most 99 direct and aggregated SQL arguments are supported, without internal inputs.");
        }

        string[] argumentNames = [.. (final?.Parameters.Skip(1).Take(aggregate.Direct.Length) ?? []).Concat(transition.Parameters.Skip(1))
            .Select(static parameter => parameter.Name)];
        if (argumentNames.Distinct(StringComparer.Ordinal).Count() != argumentNames.Length)
        {
            return Invalid("Direct and aggregated SQL argument names must be distinct across the aggregate signature.");
        }

        if (state.IsInternal && (final is null || final.Result.IsInternal) || final?.Result.IsInternal == true)
        {
            return Invalid("An internal-state aggregate requires a final callback returning a supported SQL result.");
        }

        bool hasPolymorphicInput = aggregate.Direct.Concat(inputs).Any(static input => input.Datum?.IsSqlPolymorphic == true);
        if (!hasPolymorphicInput && (state.Datum?.IsSqlPolymorphic == true || final?.Result.Datum?.IsSqlPolymorphic == true))
        {
            return Invalid("A polymorphic aggregate state or result requires a polymorphic aggregate input to resolve its type.");
        }

        if (aggregate.Kind == 2 && (aggregate.Direct.Length < inputs.Length ||
            !aggregate.Direct.Skip(aggregate.Direct.Length - inputs.Length).Zip(inputs, static (left, right) => left.Matches(right)).All(static same => same)))
        {
            return Invalid("Hypothetical aggregate direct arguments must end with the same types as all aggregated inputs.");
        }

        if (!ValidateTransition(transition, aggregate.Initial) || !ValidateFinal(final, state, aggregate.FinalExtra))
        {
            return null;
        }

        bool hasMoving = aggregate.Helpers.TryGetValue("MovingTransition", out AggregateHelper? moving);
        bool hasInverse = aggregate.Helpers.TryGetValue("MovingInverse", out AggregateHelper? inverse);
        if (hasMoving != hasInverse || !hasMoving && (aggregate.Helpers.ContainsKey("MovingFinal") || aggregate.MovingInitial is not null ||
            AttributeValues.Get(attribute, "MovingStateSize", 0) != 0))
        {
            return Invalid("Moving aggregates require both MovingTransition and MovingInverse before moving options or MovingFinal can be used.");
        }

        if (hasMoving)
        {
            if (!hasPolymorphicInput && moving!.Result.Datum?.IsSqlPolymorphic == true)
            {
                return Invalid("A polymorphic moving state requires a polymorphic aggregate input to resolve its type.");
            }

            if (moving!.Types.Length != transition.Types.Length || !moving.Result.Matches(moving.Types[0]) || moving.Result.Sql == "record" ||
                !moving.Types.Skip(1).Zip(inputs, static (left, right) => left.Matches(right)).All(static same => same) ||
                inverse!.Types.Length != moving.Types.Length || !inverse.Result.Matches(moving.Result) ||
                !inverse.Types.Zip(moving.Types, static (left, right) => left.Matches(right)).All(static same => same) ||
                inverse.Declaration.Strict != moving.Declaration.Strict)
            {
                return Invalid("Moving transition and inverse must have matching input/state types and strictness, and the same aggregated inputs as Transition.");
            }

            AggregateHelper? movingFinal = aggregate.Helpers.TryGetValue("MovingFinal", out AggregateHelper? selected) ? selected : null;
            if (!(movingFinal?.Result ?? moving.Result).Matches(final?.Result ?? state))
            {
                return Invalid("The moving implementation must produce the same SQL result as the ordinary aggregate.");
            }

            if (!ValidateTransition(moving, aggregate.MovingInitial) || !ValidateFinal(movingFinal, moving.Result, aggregate.MovingFinalExtra))
            {
                return null;
            }
        }

        if (aggregate.Helpers.TryGetValue("Combine", out AggregateHelper? combine) &&
            (combine.Types.Length != 2 || combine.Types.Any(value => !value.Matches(state)) || !combine.Result.Matches(state) ||
                state.IsInternal && (combine.Declaration.Strict || combine.Types.Any(static value => !value.Nullable))))
        {
            return Invalid("Combine must accept two matching states and return that state; internal combine must accept nullable states and cannot be STRICT.");
        }

        bool serialize = aggregate.Helpers.TryGetValue("Serialize", out AggregateHelper? serializer);
        bool deserialize = aggregate.Helpers.TryGetValue("Deserialize", out AggregateHelper? deserializer);
        if (serialize != deserialize || serialize && (!state.IsInternal || serializer!.Types.Length != 1 ||
            !serializer.Types[0].Matches(state) || serializer.Result.Sql != "bytea" ||
            deserializer!.Types.Length != 1 || deserializer.Types[0].Sql != "bytea" || !deserializer.Result.Matches(state)))
        {
            return Invalid("Internal state serialization requires both Serialize(state) returning byte[] and Deserialize(byte[]) returning the same state.");
        }

        foreach (AggregateHelper helper in aggregate.Helpers.Values)
        {
            if (helper.Parameters.Any(static parameter => parameter.IsVariadic) &&
                (aggregate.Kind != 0 || helper.Role is not ("Transition" or "MovingTransition" or "MovingInverse" or "Final" or "MovingFinal") ||
                    helper.Types.Length < 2 || helper.Types[helper.Types.Length - 1].Datum?.IsVector != true || helper.Parameters.Take(helper.Parameters.Length - 1).Any(static parameter => parameter.IsVariadic)))
            {
                return Invalid("Variadic aggregate inputs require one trailing params vector on a normal aggregate; ordered-set VARIADIC ANY is not a concrete array.");
            }
        }

        string? sort = AttributeValues.Get<string?>(attribute, "SortOperator", null);
        if (sort is not null)
        {
            string[] parts = sort.Split('.');
            string operation = parts[parts.Length - 1];
            if (aggregate.Direct.Length + inputs.Length != 1 || parts.Length > 2 ||
                parts.Length == 2 && !SqlText.IsIdentifier(parts[0]) || !ValidOperator(operation))
            {
                return Invalid("SortOperator requires one aggregate argument and a valid operator, optionally qualified by one schema.");
            }

            aggregate.SortOperator = parts.Length == 2 ? SqlText.Identifier(parts[0]) + "." + SqlText.Identifier(operation) : operation;
        }

        return aggregate;

        bool ValidateTransition(AggregateHelper helper, string? initial)
        {
            if (helper.Result.IsInternal && initial is not null ||
                helper.Declaration.Strict && initial is null && (aggregate.Direct.Concat(inputs).FirstOrDefault() is not { } first || !helper.Result.AcceptsSeed(first) ||
                    inputs.FirstOrDefault() is not { } seed || !helper.Result.AcceptsSeed(seed)))
            {
                Invalid("Internal state cannot use a textual initial condition; a strict transition without an initial condition requires both the first declared SQL argument and the first aggregated input to be binary compatible with the state type.");
                return false;
            }

            return true;
        }

        bool ValidateFinal(AggregateHelper? helper, AggregateType expectedState, bool extra)
        {
            if (helper is null)
            {
                return true;
            }

            AggregateType[] expected = [expectedState, .. aggregate.Direct, .. extra ? inputs : []];
            if (helper.Types.Length != expected.Length || !helper.Types.Zip(expected, static (left, right) => left.Matches(right)).All(static same => same) ||
                extra && (helper.Declaration.Strict || helper.Types.Skip(1 + aggregate.Direct.Length).Any(static value => !value.Nullable)))
            {
                Invalid("Final arguments must match state and direct inputs; FinalExtra requires nullable trailing aggregate input slots and a non-STRICT final callback.");
                return false;
            }

            return true;
        }

        AggregateDeclaration? Invalid(string message)
        {
            context.Report(s_invalid, type.Locations.FirstOrDefault(), type.Name, message);
            return null;
        }
    }

    private static bool ValidOperator(string value)
        => value.Length is > 0 and <= 63 && value.All(static character => "+-*/<>=~!@#%^&|`?".Contains(character)) &&
            !value.Contains("--") && !value.Contains("/*") && value != "=>" &&
            (value.Length == 1 || value[value.Length - 1] is not ('+' or '-') || value.Any(static character => "~!@#%^&|`?".Contains(character)));
}
