using Microsoft.CodeAnalysis;

namespace Ankus.Generators;

/// <summary>
/// Retains compiler-checked dependency selection independently of mutable SQL graph ownership.
/// </summary>
/// <param name="Source">The attributed managed declaration.</param>
/// <param name="Kind">The original dependency attribute's class name.</param>
/// <param name="DeclarationId">The optional source SQL declaration selector.</param>
/// <param name="ExternalSupport">Whether the source already declares external planner support.</param>
/// <param name="Target">The compiler-selected target declaration, absent after semantic failure.</param>
/// <param name="Error">A constructor error that precedes source selection.</param>
/// <param name="TargetError">A semantic target error deferred until source selection succeeds.</param>
/// <param name="Location">The current attribute diagnostic coordinates.</param>
internal sealed record SqlReferenceModel(SqlReferenceModel.Declaration Source, string Kind, string? DeclarationId,
    bool ExternalSupport, SqlReferenceModel.Declaration? Target, string? Error, string? TargetError, GeneratorLocation? Location)
{
    /// <summary>
    /// Selects overloads and inherited members using the compiler's exact type comparer without retaining symbols.
    /// </summary>
    /// <param name="source">The attributed declaration.</param>
    /// <param name="attribute">The original dependency or support attribute.</param>
    /// <param name="compilation">The compiler state owning source coordinates.</param>
    /// <param name="cancellationToken">The current analysis cancellation token.</param>
    /// <returns>The immutable selection and original deferred validation failures.</returns>
    internal static SqlReferenceModel Create(ISymbol source, AttributeData attribute, Compilation compilation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string kind = attribute.AttributeClass!.Name;
        string? error = kind == "PgSupportFunctionAttribute" &&
            (attribute.ConstructorArguments.Length != 2 || attribute.ConstructorArguments[1].Value is not string)
            ? "A planner support reference requires a non-null method name." : null;
        AttributeData? function = source.GetAttributes().FirstOrDefault(static candidate =>
            candidate.AttributeClass?.ToDisplayString() == "Ankus.PgFunctionAttribute");
        (Declaration? target, string? targetError) = SelectTarget(attribute);
        return new(Declaration.Create(source), kind, AttributeValues.Get<string?>(attribute, "DeclarationId", null),
            function is not null && AttributeValues.Get<string?>(function, "SupportFunction", null) is not null,
            target, error, targetError, GeneratorLocation.Create(attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation(), compilation));
    }

    /// <summary>
    /// Preserves constructor validation, exact parameter selection and first declaring base-type lookup.
    /// </summary>
    private static (Declaration? Target, string? Error) SelectTarget(AttributeData attribute)
    {
        if (attribute.ConstructorArguments.Length != 2 ||
            attribute.ConstructorArguments[0].Value is not INamedTypeSymbol { TypeKind: not TypeKind.Error, IsUnboundGenericType: false } type)
        {
            return (null, "A SQL dependency requires a non-null declared type.");
        }

        string? member = attribute.ConstructorArguments[1].Value as string;
        TypedConstant parameters = attribute.NamedArguments.FirstOrDefault(static argument => argument.Key == "ParameterTypes").Value;
        bool selectedParameters = parameters.Kind == TypedConstantKind.Array && !parameters.IsNull;
        if (member is null)
        {
            return selectedParameters ? (null, "ParameterTypes requires a method name.") : (Declaration.Create(type), null);
        }

        if (string.IsNullOrWhiteSpace(member) || !SqlText.IsText(member))
        {
            return (null, "A SQL dependency method name must be nonempty valid text.");
        }

        if (selectedParameters && parameters.Values.Any(static parameter => parameter.Value is not ITypeSymbol { TypeKind: not TypeKind.Error }))
        {
            return (null, "ParameterTypes must contain non-null managed types.");
        }

        ISymbol[] members = [];
        for (INamedTypeSymbol? container = type; container is not null && members.Length == 0; container = container.BaseType)
        {
            members = [.. container.GetMembers(member)];
        }

        IMethodSymbol[] methods = [.. members.OfType<IMethodSymbol>().Where(method => !selectedParameters ||
            method.Parameters.Length == parameters.Values.Length && method.Parameters.Select(static parameter => parameter.Type)
                .SequenceEqual(parameters.Values.Select(static parameter => (ITypeSymbol)parameter.Value!), SymbolEqualityComparer.Default))];
        return methods.Length == 1 ? (Declaration.Create(methods[0]), null) : (null, methods.Length == 0
            ? $"SQL dependency method '{type.ToDisplayString()}.{member}' was not found with the selected parameter types."
            : $"SQL dependency method '{type.ToDisplayString()}.{member}' is ambiguous; set ParameterTypes to select one overload.");
    }

    /// <summary>
    /// Retains only declaration identity and graph-selection category after compiler validation.
    /// </summary>
    /// <param name="Identity">The exact assembly-qualified declaration identity.</param>
    /// <param name="Display">The original managed diagnostic spelling.</param>
    /// <param name="Method">Whether primary selection prefers a function node.</param>
    /// <param name="Assembly">Whether source selection requires an explicit SQL declaration ID.</param>
    internal sealed record Declaration(DeclarationIdentity Identity, string Display, bool Method, bool Assembly)
    {
        /// <summary>
        /// Freezes one compiler declaration without keeping its containing compilation alive.
        /// </summary>
        /// <param name="symbol">The selected compiler declaration.</param>
        /// <returns>The minimal exact graph-selection identity.</returns>
        internal static Declaration Create(ISymbol symbol)
            => new(DeclarationIdentity.Create(symbol), symbol.ToDisplayString(), symbol is IMethodSymbol, symbol is IAssemblySymbol);
    }
}
