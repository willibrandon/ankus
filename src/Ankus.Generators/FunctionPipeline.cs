using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Ankus.Generators;

/// <summary>
/// Detaches ordinary function validation and conversion contracts from compiler state.
/// </summary>
internal static class FunctionPipeline
{
    /// <summary>
    /// Registers per-function semantic analysis before extension-wide graph composition.
    /// </summary>
    /// <param name="context">The incremental generator registration context.</param>
    /// <returns>The validated models and independently located diagnostics.</returns>
    internal static IncrementalValueProvider<EquatableArray<FunctionOutput>> Register(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<FunctionAnalysis> analysis = context.SyntaxProvider.ForAttributeWithMetadataName(
            "Ankus.PgFunctionAttribute", static (node, _) => node is MethodDeclarationSyntax,
            static (attribute, token) => Analyze(attribute, token))
            .Where(static value => value is not null).Select(static (value, _) => value!)
            .WithTrackingName("FunctionAnalysis");
        IncrementalValuesProvider<FunctionModel?> models = analysis.Select(static (value, _) => value.Model)
            .WithTrackingName("FunctionModel");
        IncrementalValuesProvider<FunctionEmission?> emission = models.Select(static (value, _) => value is null ? null :
            value.Set is null ? PgFunctionEmitter.EmitBoundary(value.Invocation, [.. value.Parameters], value.Result!, value.Callback) :
                PgSetEmitter.EmitBoundary(value.Invocation, [.. value.Parameters], value.Set, value.Callback))
            .WithTrackingName("FunctionEmission");
        return analysis.Collect().Combine(emission.Collect()).Select(static (value, _) =>
            new EquatableArray<FunctionOutput>(value.Left.Select((item, index) => new FunctionOutput(item, value.Right[index]))));
    }

    /// <summary>
    /// Validates one ordinary attributed function while compiler objects remain confined to analysis.
    /// </summary>
    private static FunctionAnalysis? Analyze(GeneratorAttributeSyntaxContext attribute, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var method = (IMethodSymbol)attribute.TargetSymbol;
        if (method.GetAttributes().Any(static value => value.AttributeClass?.ToDisplayString() is
            "Ankus.PgTestAttribute" or "Ankus.PgTriggerAttribute" or "Ankus.PgEventTriggerAttribute" or
            "Ankus.PgInitializeAttribute" or "Ankus.PgModuleLoadAttribute" or "Ankus.PgBackgroundWorkerAttribute"))
        {
            return null;
        }

        var problems = new List<GeneratorProblem>();
        var diagnostics = new GeneratorDiagnostics((descriptor, location, arguments) =>
            problems.Add(new(descriptor, GeneratorLocation.Create(location, attribute.SemanticModel.Compilation), new(arguments))), cancellationToken);
        FunctionParameter[] parameters = FunctionParameter.Create(method);
        SetResult? set = SetResult.Create(method, diagnostics, out bool validSet);
        FunctionType? result = set is null ? FunctionType.CreateResult(method) : null;
        bool valid = validSet && SqlTypeReference.Validate(method, ref set, diagnostics);
        if (valid && !PgFunctionGenerator.IsSupported(method, parameters, set, result))
        {
            PgFunctionGenerator.ReportUnsupported(method, diagnostics);
            valid = false;
        }

        valid = valid && SqlNullability.Validate(method, method.Parameters.Where((_, index) => !parameters[index].IsInjected),
            set is null ? [method.ReturnType] : SetResult.OutputTypes(method), diagnostics);
        valid = valid && NumericConstraint.Validate(method, diagnostics, set);
        return new(DeclarationIdentity.Create(method), valid ? new(new(parameters), result, set, MethodInvocation.Create(method),
            PgFunctionGenerator.GetCallbackName(method, PgFunctionGenerator.GetSqlName(method))) : null,
            new(problems), GeneratorLocation.Create(method.Locations.FirstOrDefault(), attribute.SemanticModel.Compilation));
    }

    /// <summary>
    /// Contains the immutable SQL-slot and invocation values required by managed and native renderers.
    /// </summary>
    /// <param name="Parameters">The ordered SQL and injected argument contracts.</param>
    /// <param name="Result">The scalar result, or null for a set.</param>
    /// <param name="Set">The validated iterator output, or null for a scalar.</param>
    /// <param name="Invocation">The managed call and output policies.</param>
    /// <param name="Callback">The assembly-specific callback identity for this overload and SQL name.</param>
    internal sealed record FunctionModel(EquatableArray<FunctionParameter> Parameters, FunctionType? Result, SetResult? Set, MethodInvocation Invocation, string Callback);

    /// <summary>
    /// Separates a function's conversion model from source-dependent diagnostic coordinates.
    /// </summary>
    /// <param name="Identity">The assembly-qualified method identity.</param>
    /// <param name="Model">The validated conversion model, or null after a reported validation failure.</param>
    /// <param name="Problems">Diagnostics to resolve on the current source trees.</param>
    /// <param name="Location">The declaring source coordinates, including invalid duplicate signatures.</param>
    internal sealed record FunctionAnalysis(DeclarationIdentity Identity, FunctionModel? Model, EquatableArray<GeneratorProblem> Problems, GeneratorLocation? Location);

    /// <summary>
    /// Supplies one analyzed function and its independently cached boundary artifacts.
    /// </summary>
    /// <param name="Analysis">The detached semantic values and current diagnostic coordinates.</param>
    /// <param name="Emission">The rendered boundary, or null after semantic validation failed.</param>
    internal sealed record FunctionOutput(FunctionAnalysis Analysis, FunctionEmission? Emission);

    /// <summary>
    /// Supplies fresh legacy declarations and detached function contracts to the remaining graph conversion.
    /// </summary>
    /// <param name="Methods">The declarations still needed by unconverted graph and callback families.</param>
    /// <param name="Functions">The independently analyzed ordinary functions.</param>
    internal sealed record MethodInputs(ImmutableArray<IMethodSymbol> Methods, EquatableArray<FunctionOutput> Functions);
}
