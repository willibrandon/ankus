namespace Ankus.Generators;

/// <summary>
/// Retains one initialization phase's invocation and assembly-specific native identity without compiler state.
/// </summary>
/// <param name="ModuleLoad">Whether this phase performs immediate module registration.</param>
/// <param name="Target">The fully qualified managed invocation target.</param>
/// <param name="Callback">The assembly-specific managed dispatcher symbol.</param>
internal sealed record LifecycleDeclaration(bool ModuleLoad, string Target, string Callback);
