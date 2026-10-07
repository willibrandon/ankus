using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Ankus.Generators;

/// <summary>
/// Detaches benchmark declarations and configuration before extension composition.
/// </summary>
internal static class PgBenchmarkPipeline
{
    private const string Help = "https://willibrandon.github.io/ankus/benchmarks/";
    private static readonly DiagnosticDescriptor s_declaration = new(
        "ANKUS130", "Invalid PostgreSQL benchmark declaration", "Benchmark '{0}' must be an accessible synchronous static void method with one PgBencher parameter",
        "Ankus", DiagnosticSeverity.Error, isEnabledByDefault: true, helpLinkUri: Help);
    private static readonly DiagnosticDescriptor s_setup = new(
        "ANKUS131", "Invalid PostgreSQL benchmark setup", "Benchmark '{0}' setup '{1}' must identify one accessible synchronous static void method with no parameters",
        "Ankus", DiagnosticSeverity.Error, isEnabledByDefault: true, helpLinkUri: Help);
    private static readonly DiagnosticDescriptor s_configuration = new(
        "ANKUS132", "Invalid PostgreSQL benchmark configuration", "Benchmark '{0}' has an invalid {1} value",
        "Ankus", DiagnosticSeverity.Error, isEnabledByDefault: true, helpLinkUri: Help);
    private static readonly DiagnosticDescriptor s_conflict = new(
        "ANKUS133", "Conflicting PostgreSQL benchmark role", "Benchmark '{0}' cannot also declare an extension export, backend test, initialization callback, or worker",
        "Ankus", DiagnosticSeverity.Error, isEnabledByDefault: true, helpLinkUri: Help);

    /// <summary>
    /// Registers one independently comparable analysis value per attributed method.
    /// </summary>
    internal static IncrementalValueProvider<EquatableArray<Output>> Register(IncrementalGeneratorInitializationContext context)
        => context.SyntaxProvider.ForAttributeWithMetadataName("Ankus.PgBenchmarkAttribute",
            static (node, _) => node is MethodDeclarationSyntax,
            static (attribute, token) => Analyze(attribute, token)).Collect()
            .Select(static (values, _) => new EquatableArray<Output>(values))
            .WithTrackingName("PgBenchmarkAnalysis");

    private static Output Analyze(GeneratorAttributeSyntaxContext attribute, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var method = (IMethodSymbol)attribute.TargetSymbol;
        var problems = new List<GeneratorProblem>();
        var diagnostics = new GeneratorDiagnostics((descriptor, location, arguments) =>
            problems.Add(new(descriptor, GeneratorLocation.Create(location, attribute.SemanticModel.Compilation), new(arguments))), cancellationToken);
        Model? model = Create(method, diagnostics);
        return new(DeclarationIdentity.Create(method), model, new(problems),
            GeneratorLocation.Create(method.Locations.FirstOrDefault(), attribute.SemanticModel.Compilation));
    }

    private static Model? Create(IMethodSymbol method, GeneratorDiagnostics diagnostics)
    {
        IParameterSymbol? parameter = method.Parameters.Length == 1 ? method.Parameters[0] : null;
        INamedTypeSymbol? bencher = parameter?.Type as INamedTypeSymbol;
        if (!method.IsStatic || method.IsAsync || method.IsGenericMethod || method.IsAbstract || !method.ReturnsVoid ||
            method.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal) ||
            parameter?.RefKind != RefKind.None || bencher is not { Name: "PgBencher", Arity: 0 } ||
            bencher.ContainingNamespace.ToDisplayString() != "Ankus" || !ValidContainers(method.ContainingType))
        {
            diagnostics.Report(s_declaration, method.Locations.FirstOrDefault(), method.Name);
            return null;
        }

        if (method.GetAttributes().Any(static value => value.AttributeClass?.ToDisplayString() is
            "Ankus.PgFunctionAttribute" or "Ankus.PgTestAttribute" or "Ankus.PgOperatorAttribute" or "Ankus.PgCastAttribute" or
            "Ankus.PgTriggerAttribute" or "Ankus.PgEventTriggerAttribute" or "Ankus.PgInitializeAttribute" or
            "Ankus.PgModuleLoadAttribute" or "Ankus.PgBackgroundWorkerAttribute"))
        {
            diagnostics.Report(s_conflict, method.Locations.FirstOrDefault(), method.Name);
            return null;
        }

        AttributeData benchmark = method.GetAttributes().First(static value =>
            value.AttributeClass?.ToDisplayString() == "Ankus.PgBenchmarkAttribute");
        string? setupName = AttributeValues.Get<string?>(benchmark, "Setup", null);
        string? setupTarget = null;
        if (setupName is not null)
        {
            IMethodSymbol[] candidates = [.. method.ContainingType.GetMembers(setupName).OfType<IMethodSymbol>().Where(static candidate =>
                candidate.IsStatic && !candidate.IsAsync && !candidate.IsGenericMethod && !candidate.IsAbstract && candidate.ReturnsVoid &&
                candidate.Parameters.Length == 0 && candidate.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal)];
            if (candidates.Length != 1)
            {
                diagnostics.Report(s_setup, BenchmarkOption(benchmark, "Setup", method), method.Name, setupName);
                return null;
            }

            setupTarget = Target(candidates[0]);
        }

        int transaction = AttributeValues.Get(benchmark, "Transaction", 0);
        int sampleSize = AttributeValues.Get(benchmark, "SampleSize", 100);
        int measurement = AttributeValues.Get(benchmark, "MeasurementTimeMilliseconds", 5_000);
        int warmup = AttributeValues.Get(benchmark, "WarmupTimeMilliseconds", 3_000);
        int resamples = AttributeValues.Get(benchmark, "ResampleCount", 100_000);
        double noise = AttributeValues.Get(benchmark, "NoiseThreshold", 0.01);
        double significance = AttributeValues.Get(benchmark, "SignificanceLevel", 0.05);
        if (transaction is < 0 or > 2)
        {
            return Invalid("Transaction");
        }

        if (sampleSize < 10)
        {
            return Invalid("SampleSize");
        }

        if (measurement <= 0)
        {
            return Invalid("MeasurementTimeMilliseconds");
        }

        if (warmup <= 0)
        {
            return Invalid("WarmupTimeMilliseconds");
        }

        if (resamples <= 0)
        {
            return Invalid("ResampleCount");
        }

        if (double.IsNaN(noise) || double.IsInfinity(noise) || noise < 0)
        {
            return Invalid("NoiseThreshold");
        }

        if (double.IsNaN(significance) || double.IsInfinity(significance) || significance is <= 0 or >= 1)
        {
            return Invalid("SignificanceLevel");
        }

        string display = method.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        using SHA256 hash = SHA256.Create();
        byte[] digest = hash.ComputeHash(Encoding.UTF8.GetBytes(method.ContainingAssembly.Name + ":" + display));
        string suffix = string.Concat(digest.Take(16).Select(static value => value.ToString("x2", CultureInfo.InvariantCulture)));
        string runName = "ankus_bench_" + suffix;
        string describeName = runName + "_describe";
        return new(display, Target(method), setupTarget, setupName, runName, describeName,
            PgFunctionGenerator.GetCallbackName(method, runName), PgFunctionGenerator.GetCallbackName(method, describeName),
            transaction, sampleSize, measurement, warmup, resamples, noise, significance);

        Model? Invalid(string option)
        {
            diagnostics.Report(s_configuration, BenchmarkOption(benchmark, option, method), method.Name, option);
            return null;
        }
    }

    private static bool ValidContainers(INamedTypeSymbol owner)
    {
        for (INamedTypeSymbol? type = owner; type is not null; type = type.ContainingType)
        {
            if (type.TypeKind != TypeKind.Class || type.IsGenericType || type.IsFileLocal ||
                type.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal))
            {
                return false;
            }
        }

        return true;
    }

    private static string Target(IMethodSymbol method)
        => method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ".@" + method.Name;

    private static Location? BenchmarkOption(AttributeData attribute, string name, IMethodSymbol method)
    {
        SyntaxNode? syntax = attribute.ApplicationSyntaxReference?.GetSyntax();
        AttributeArgumentSyntax? argument = syntax?.DescendantNodes().OfType<AttributeArgumentSyntax>()
            .FirstOrDefault(value => value.NameEquals?.Name.Identifier.ValueText == name);
        return argument?.Expression.GetLocation() ?? method.Locations.FirstOrDefault();
    }

    /// <summary>
    /// Contains one validated benchmark independent of compiler symbols and physical source lines.
    /// </summary>
    internal sealed record Model(
        string Display,
        string Target,
        string? SetupTarget,
        string? SetupName,
        string RunName,
        string DescribeName,
        string RunCallback,
        string DescribeCallback,
        int Transaction,
        int SampleSize,
        int MeasurementTimeMilliseconds,
        int WarmupTimeMilliseconds,
        int ResampleCount,
        double NoiseThreshold,
        double SignificanceLevel);

    /// <summary>
    /// Retains one benchmark and its separately located diagnostics.
    /// </summary>
    internal sealed record Output(DeclarationIdentity Identity, Model? Model, EquatableArray<GeneratorProblem> Problems, GeneratorLocation? Location);
}
