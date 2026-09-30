using Microsoft.CodeAnalysis;

namespace Ankus.Generators;

/// <summary>
/// Captures only the managed call and return options needed to render a scalar or set dispatcher.
/// </summary>
/// <param name="Target">The fully qualified containing type and escaped method name.</param>
/// <param name="NumericPrecision">The optional validated output rescaling constraint.</param>
/// <param name="SetMode">The selected PostgreSQL set execution policy.</param>
internal sealed record MethodInvocation(string Target, NumericPrecision? NumericPrecision, int SetMode)
{
    /// <summary>
    /// Detaches invocation metadata from the compiler's attributed method.
    /// </summary>
    /// <param name="method">The semantically validated static method.</param>
    /// <returns>The immutable invocation and return options.</returns>
    internal static MethodInvocation Create(IMethodSymbol method)
    {
        AttributeData? attribute = method.GetAttributes().FirstOrDefault(static item =>
            item.AttributeClass?.ToDisplayString() == "Ankus.PgFunctionAttribute");
        return new(method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ".@" + method.Name,
            NumericConstraint.Read(method.GetReturnTypeAttributes()), attribute is null ? 0 : AttributeValues.Get(attribute, "SetMode", 0));
    }
}
