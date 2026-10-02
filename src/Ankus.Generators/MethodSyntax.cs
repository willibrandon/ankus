using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Ankus.Generators;

/// <summary>
/// Locates authored calling-contract causes across ordinary methods, operators and conversions.
/// </summary>
internal static class MethodSyntax
{
    /// <summary>
    /// Finds the ordinary identifier or special-method keyword/token at the declaration.
    /// </summary>
    /// <param name="declaration">The current authored callable declaration.</param>
    /// <returns>The name location, or null when syntax is unavailable.</returns>
    internal static Location? Name(BaseMethodDeclarationSyntax? declaration)
        => declaration switch
        {
            MethodDeclarationSyntax method => method.Identifier.GetLocation(),
            OperatorDeclarationSyntax operation => operation.OperatorToken.GetLocation(),
            ConversionOperatorDeclarationSyntax conversion => conversion.ImplicitOrExplicitKeyword.GetLocation(),
            _ => null,
        };

    /// <summary>
    /// Finds the managed result type, including the target type of a conversion.
    /// </summary>
    /// <param name="declaration">The current authored callable declaration.</param>
    /// <returns>The result-type location, or null when syntax is unavailable.</returns>
    internal static Location? Result(BaseMethodDeclarationSyntax? declaration)
        => declaration switch
        {
            MethodDeclarationSyntax method => method.ReturnType.GetLocation(),
            OperatorDeclarationSyntax operation => operation.ReturnType.GetLocation(),
            ConversionOperatorDeclarationSyntax conversion => conversion.Type.GetLocation(),
            _ => null,
        };
}
