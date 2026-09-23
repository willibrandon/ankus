using System.Globalization;
using Microsoft.CodeAnalysis;

namespace Ankus.Generators;

/// <summary>
/// Separates managed invocation order from SQL argument slots and injected memory contexts.
/// </summary>
/// <param name="symbol">The managed parameter declaration.</param>
/// <param name="type">The SQL conversion contract, or null for a context or unsupported type.</param>
/// <param name="sqlIndex">The zero-based SQL slot, or minus one for an injected context.</param>
internal sealed class FunctionParameter(IParameterSymbol symbol, FunctionType? type, int sqlIndex)
{
    /// <summary>
    /// Gets the managed parameter declaration, including its attributes and passing mode.
    /// </summary>
    internal IParameterSymbol Symbol { get; } = symbol;

    /// <summary>
    /// Gets the SQL conversion contract, or null for a context or unsupported type.
    /// </summary>
    internal FunctionType? Type { get; } = type;

    /// <summary>
    /// Gets the SQL argument slot, excluding preceding injected contexts.
    /// </summary>
    internal int SqlIndex { get; } = sqlIndex;

    /// <summary>
    /// Gets whether the backend supplies this parameter without consuming a SQL argument.
    /// </summary>
    internal bool IsMemoryContext => SqlIndex < 0;

    /// <summary>
    /// Builds the ordered managed invocation parameters while assigning contiguous SQL slots.
    /// </summary>
    /// <param name="method">The method whose invocation is generated.</param>
    /// <returns>Every managed parameter, including unsupported SQL types for validation.</returns>
    internal static FunctionParameter[] Create(IMethodSymbol method)
    {
        var parameters = new FunctionParameter[method.Parameters.Length];
        int sqlIndex = 0;
        for (int index = 0; index < parameters.Length; index++)
        {
            IParameterSymbol parameter = method.Parameters[index];
            bool memoryContext = parameter.Type is INamedTypeSymbol { Name: "PgMemoryContext", Arity: 0, ContainingType: null } named &&
                named.ContainingNamespace.ToDisplayString() == "Ankus";
            parameters[index] = new FunctionParameter(parameter, memoryContext ? null : FunctionType.Create(parameter),
                memoryContext ? -1 : sqlIndex++);
        }

        return parameters;
    }

    /// <summary>
    /// Emits a managed argument expression after validation and entry into the memory capability.
    /// </summary>
    /// <returns>A checked context lookup or a conversion from the parameter's SQL slot.</returns>
    internal string ReadExpression()
        => IsMemoryContext ? "global::Ankus.PgMemoryContext.Current" :
            ManagedConversion.Read(Type!, "arguments[" + SqlIndex.ToString(CultureInfo.InvariantCulture) + "]",
                NumericConstraint.Rescale(Symbol.GetAttributes()));
}
