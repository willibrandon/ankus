using Microsoft.CodeAnalysis;

namespace Ankus.Generators;

/// <summary>
/// Retains authored provider inventory independently of compiler attributes and current SQL graph ownership.
/// </summary>
/// <param name="Function">Whether the provider declares a SQL function signature instead of a type.</param>
/// <param name="BlockId">The original custom SQL block identifier.</param>
/// <param name="Name">The exact type name or authored function signature.</param>
/// <param name="Schema">The optional explicitly selected type schema.</param>
/// <param name="Managed">Whether type selection uses the managed constructor overload.</param>
/// <param name="SchemaAuthored">Whether Schema was explicitly supplied, including null.</param>
/// <param name="Type">The exact managed type identity, absent for named or invalid selectors.</param>
/// <param name="Location">The current attribute diagnostic coordinates.</param>
internal sealed record SqlProviderModel(bool Function, string? BlockId, string? Name, string? Schema,
    bool Managed, bool SchemaAuthored, ManagedTypeIdentity? Type, GeneratorLocation? Location)
{
    /// <summary>
    /// Freezes constructor shape and exact compiler identity without resolving catalog ownership or executing SQL.
    /// </summary>
    /// <param name="attribute">The selected assembly-level provider attribute.</param>
    /// <param name="compilation">The current compiler state owning source coordinates.</param>
    /// <param name="cancellationToken">The current analysis cancellation token.</param>
    /// <returns>The immutable declaration values used by current provider validation.</returns>
    internal static SqlProviderModel Create(AttributeData attribute, Compilation compilation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        bool validArity = attribute.ConstructorArguments.Length == 2;
        bool managed = attribute.AttributeConstructor is { Parameters.Length: 2 } constructor &&
            constructor.Parameters[1].Type.ToDisplayString() == "System.Type";
        return new(attribute.AttributeClass!.Name == "PgSqlFunctionProviderAttribute",
            validArity ? attribute.ConstructorArguments[0].Value as string : null,
            validArity ? attribute.ConstructorArguments[1].Value as string : null,
            AttributeValues.Get<string?>(attribute, "Schema", null), managed,
            attribute.NamedArguments.Any(static argument => argument.Key == "Schema"),
            validArity && attribute.ConstructorArguments[1].Value is ITypeSymbol supplied ? ManagedTypeIdentity.Create(supplied) : null,
            GeneratorLocation.Create(attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation(), compilation));
    }
}
