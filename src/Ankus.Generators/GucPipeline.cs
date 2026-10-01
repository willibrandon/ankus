using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Ankus.Generators;

/// <summary>
/// Detaches configuration analysis from compiler state and caches declaration rendering per property.
/// </summary>
internal static class GucPipeline
{
    /// <summary>
    /// Registers configuration discovery, validation and independent rendering.
    /// </summary>
    /// <param name="context">The generator registration context.</param>
    /// <returns>The detached contracts, current diagnostic metadata and reusable source fragments.</returns>
    internal static IncrementalValueProvider<EquatableArray<Output>> Register(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<Analysis> analysis = context.SyntaxProvider.CreateSyntaxProvider(
            static (node, _) => node is PropertyDeclarationSyntax { AttributeLists.Count: > 0 } or IndexerDeclarationSyntax { AttributeLists.Count: > 0 },
            static (syntax, token) => Analyze(syntax, token))
            .Where(static value => value is not null).Select(static (value, _) => value!).WithTrackingName("GucAnalysis");
        IncrementalValuesProvider<GucModel?> models = analysis.Select(static (value, _) => value.Model).WithTrackingName("GucModel");
        IncrementalValuesProvider<GucEmission?> emission = models.Select(static (value, _) => value is null ? null : GucEmission.Create(value))
            .WithTrackingName("GucEmission");
        return analysis.Collect().Combine(emission.Collect()).Select(static (value, _) =>
            new EquatableArray<Output>(value.Left.Select((item, index) => new Output(item, value.Right[index]))));
    }

    /// <summary>
    /// Validates one setting while compiler objects remain confined to this transient operation.
    /// </summary>
    private static Analysis? Analyze(GeneratorSyntaxContext syntax, CancellationToken cancellationToken)
    {
        var property = syntax.SemanticModel.GetDeclaredSymbol((BasePropertyDeclarationSyntax)syntax.Node, cancellationToken) as IPropertySymbol;
        if (property is null || property.GetAttributes().Any(NativeCallbackDeclaration.IsAttribute) ||
            !property.GetAttributes().Any(GucDeclaration.IsGucAttribute))
        {
            return null;
        }

        Compilation compilation = syntax.SemanticModel.Compilation;
        var problems = new List<GeneratorProblem>();
        var diagnostics = new GeneratorDiagnostics((descriptor, location, arguments) =>
            problems.Add(new(descriptor, GeneratorLocation.Create(location, compilation), new(arguments))), cancellationToken);
        GucDeclaration? declaration = GucDeclaration.Create(property, diagnostics);
        return new(DeclarationIdentity.Create(property), property.Name, declaration is null ? null : GucModel.Create(declaration),
            new(problems), GeneratorLocation.Create(property.Locations.FirstOrDefault(), compilation));
    }

    /// <summary>
    /// Resolves current diagnostic locations and selects deterministic registration order without caching native values.
    /// </summary>
    /// <param name="outputs">The currently discovered setting declarations.</param>
    /// <param name="compilation">The current compilation owning diagnostic coordinates.</param>
    /// <param name="context">The diagnostic output destination.</param>
    /// <returns>The validated, distinct current settings in PostgreSQL name order.</returns>
    internal static List<Output> Select(EquatableArray<Output> outputs, Compilation compilation, SourceProductionContext context)
    {
        var result = new List<Output>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        var identities = new HashSet<DeclarationIdentity>();
        foreach (Output output in outputs)
        {
            Analysis analysis = output.Analysis;
            if (!identities.Add(analysis.Identity))
            {
                continue;
            }

            foreach (GeneratorProblem problem in analysis.Problems)
            {
                problem.Report(compilation, context);
            }

            if (analysis.Model is not { } model)
            {
                continue;
            }

            if (!names.Add(GucDeclaration.Fold(model.Name)))
            {
                var diagnostics = new GeneratorDiagnostics(context);
                diagnostics.Report(GucDeclaration.InvalidDiagnostic, analysis.Location?.Resolve(compilation), analysis.Name,
                    "GUC names must be unique under PostgreSQL's ASCII case-insensitive comparison.");
                continue;
            }

            result.Add(output);
        }

        result.Sort(static (left, right) => string.CompareOrdinal(GucDeclaration.Fold(left.Analysis.Model!.Name), GucDeclaration.Fold(right.Analysis.Model!.Name)));
        return result;
    }

    /// <summary>
    /// Retains source ownership and detached validation failures separately from rendering contracts.
    /// </summary>
    /// <param name="Identity">The property identity for canonical declaration selection.</param>
    /// <param name="Name">The managed property name for current duplicate diagnostics.</param>
    /// <param name="Model">The validated immutable setting contract, or null on failure.</param>
    /// <param name="Problems">The detached validation problems.</param>
    /// <param name="Location">The current declaration coordinates.</param>
    internal sealed record Analysis(DeclarationIdentity Identity, string Name, GucModel? Model, EquatableArray<GeneratorProblem> Problems,
        GeneratorLocation? Location);

    /// <summary>
    /// Supplies one setting's current metadata and independently cached source fragments.
    /// </summary>
    /// <param name="Analysis">The current declaration and diagnostics.</param>
    /// <param name="Emission">The cached source, or null after invalid validation.</param>
    internal sealed record Output(Analysis Analysis, GucEmission? Emission);

    /// <summary>
    /// Separates legacy native callback properties from detached configuration declarations.
    /// </summary>
    /// <param name="Callbacks">The native callback properties still requiring compiler-state conversion.</param>
    /// <param name="Settings">The detached configuration declarations.</param>
    internal sealed record PropertyInputs(ImmutableArray<IPropertySymbol> Callbacks, EquatableArray<Output> Settings);
}
