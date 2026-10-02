using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Ankus.Generators;

/// <summary>
/// Rejects interpolation that sends formatted values directly into raw PostgreSQL command text.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SpiInterpolationAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor s_interpolatedSql = new(
        "ANKUS044", "SQL interpolation must bind parameters",
        "Bind values with Spi.Sql or positional SQL parameters instead of formatting them into command text",
        "Ankus", DiagnosticSeverity.Error, isEnabledByDefault: true,
        helpLinkUri: "https://willibrandon.github.io/ankus/spi/#parameterized-interpolation");

    /// <summary>
    /// Gets the compiler error for an interpolated raw SQL command.
    /// </summary>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [s_interpolatedSql];

    /// <summary>
    /// Registers semantic checks against the actual PostgreSQL SPI entry points.
    /// </summary>
    /// <param name="context">The analyzer registration context.</param>
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static start =>
        {
            INamedTypeSymbol? spi = start.Compilation.GetTypeByMetadataName("Ankus.Spi");
            INamedTypeSymbol? session = start.Compilation.GetTypeByMetadataName("Ankus.SpiSession");
            if (spi?.ContainingAssembly.Name != "Ankus.Runtime" || session?.ContainingAssembly.Name != "Ankus.Runtime")
            {
                return;
            }

            start.RegisterOperationAction(operation =>
            {
                var invocation = (IInvocationOperation)operation.Operation;
                INamedTypeSymbol type = invocation.TargetMethod.ContainingType;
                if (!SymbolEqualityComparer.Default.Equals(type, spi) && !SymbolEqualityComparer.Default.Equals(type, session))
                {
                    return;
                }

                foreach (IArgumentOperation argument in invocation.Arguments)
                {
                    if (argument.Parameter is { Name: "commandText", Type.SpecialType: SpecialType.System_String } &&
                        !argument.Value.ConstantValue.HasValue && ContainsInterpolation(argument.Value))
                    {
                        operation.ReportDiagnostic(Diagnostic.Create(s_interpolatedSql, argument.Value.Syntax.GetLocation()));
                    }
                }
            }, OperationKind.Invocation);
        });
    }

    /// <summary>
    /// Finds direct interpolation through string conversions and concatenation without treating escaped helper results as raw interpolation.
    /// </summary>
    /// <param name="operation">The raw command expression.</param>
    /// <returns>Whether the expression directly formats values into command text.</returns>
    private static bool ContainsInterpolation(IOperation operation)
        => operation switch
        {
            IInterpolatedStringOperation => true,
            IConversionOperation conversion => ContainsInterpolation(conversion.Operand),
            IParenthesizedOperation parentheses => ContainsInterpolation(parentheses.Operand),
            IBinaryOperation { OperatorKind: BinaryOperatorKind.Add, Type.SpecialType: SpecialType.System_String } binary
                => ContainsInterpolation(binary.LeftOperand) || ContainsInterpolation(binary.RightOperand),
            _ => false,
        };
}
