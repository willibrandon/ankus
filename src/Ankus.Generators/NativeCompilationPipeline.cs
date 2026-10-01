using Microsoft.CodeAnalysis;

namespace Ankus.Generators;

/// <summary>
/// Caches binding and node layout rendering independently of changing compiler state and extension capability selection.
/// </summary>
internal static class NativeCompilationPipeline
{
    /// <summary>
    /// Registers compilation metadata extraction followed by independent native constant renderers.
    /// </summary>
    /// <param name="context">The generator registration context.</param>
    /// <returns>The detached compiler capabilities and cached native fragments.</returns>
    internal static IncrementalValueProvider<Output> Register(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValueProvider<NativeCompilationModel> analysis = context.CompilationProvider.Select(static (compilation, token) =>
            NativeCompilationModel.Create(compilation, token)).WithTrackingName("NativeCompilationAnalysis");
        IncrementalValueProvider<string?> binding = analysis.Select(static (value, _) => value.Binding)
            .WithTrackingName("NativeBindingModel");
        IncrementalValueProvider<string> bindingSource = binding.Select(static (value, _) => NativeBindingBridge.Binding(value))
            .WithTrackingName("NativeBindingEmission");
        IncrementalValueProvider<string?> layouts = analysis.Select(static (value, _) => value.Layouts)
            .WithTrackingName("NativeNodeLayoutModel");
        IncrementalValueProvider<string> layoutSource = layouts.Select(static (value, _) => NativeNodeBridge.Layouts(value))
            .WithTrackingName("NativeNodeLayoutEmission");
        return analysis.Combine(bindingSource).Combine(layoutSource).Select(static (value, _) =>
            new Output(value.Left.Left, value.Left.Right, value.Right));
    }

    /// <summary>
    /// Contains only immutable compiler-wide metadata and independently cached native source fragments.
    /// </summary>
    /// <param name="Analysis">The current assembly and dependency capability inventory.</param>
    /// <param name="Binding">The validated native binding identity declaration.</param>
    /// <param name="Layouts">The validated native node layout lookup.</param>
    internal sealed record Output(NativeCompilationModel Analysis, string Binding, string Layouts);
}
