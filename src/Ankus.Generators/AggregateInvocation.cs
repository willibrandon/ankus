using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Ankus.Generators;

/// <summary>
/// Dispatches a closed aggregate capability through its static interface contract.
/// </summary>
/// <param name="implementation">The aggregate implementing the capability.</param>
/// <param name="contract">The closed static interface method.</param>
/// <param name="arguments">Managed argument groups after the invocation context.</param>
internal sealed class AggregateInvocation(INamedTypeSymbol implementation, IMethodSymbol contract, ImmutableArray<AggregateArgument> arguments)
{
    /// <summary>
    /// Gets the identity that distinguishes inherited callbacks on different aggregate containers.
    /// </summary>
    internal string Identity { get; } = implementation.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ":" + contract.ToDisplayString();

    /// <summary>
    /// Emits a constrained call that supports both implicit and private explicit interface implementations.
    /// </summary>
    internal void Emit(StringBuilder managed, string callback)
    {
        string parameters = string.Join(", ", contract.Parameters.Select((parameter, index) =>
            AggregateArgument.Format(parameter.Type) + " value" + index.ToString(CultureInfo.InvariantCulture)));
        managed.AppendLine("    private static " + AggregateArgument.Format(contract.ReturnType) + " " + callback + "_invoke<TImplementation>(" + parameters + ")");
        managed.AppendLine("        where TImplementation : " + AggregateArgument.Format(contract.ContainingType) +
            (implementation.IsRefLikeType ? ", allows ref struct" : string.Empty));
        managed.AppendLine("        => TImplementation." + contract.Name + "(" + string.Join(", ",
            Enumerable.Range(0, contract.Parameters.Length).Select(static index => "value" + index.ToString(CultureInfo.InvariantCulture))) + ");");
        managed.AppendLine();
    }

    /// <summary>
    /// Calls the closed generated dispatcher with reconstructed managed arguments.
    /// </summary>
    internal string Read(string callback, IReadOnlyList<string> values)
        => callback + "_invoke<" + AggregateArgument.Format(implementation) + ">(context" +
            string.Concat(arguments.Select(argument => ", " + argument.Read(values))) + ")";
}
