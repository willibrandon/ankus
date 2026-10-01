using System.Text;

namespace Ankus.Generators;

/// <summary>
/// Keeps phase dispatch and immediate registration guards independent of current global initialization composition.
/// </summary>
/// <param name="Declaration">The immutable phase and invocation contract.</param>
/// <param name="Managed">The managed callback and ordered owned-error cleanup.</param>
/// <param name="Native">The immediate registration host, or empty for deferred initialization.</param>
internal sealed record LifecycleEmission(LifecycleDeclaration Declaration, string Managed, string Native)
{
    /// <summary>
    /// Renders independently reusable phase artifacts from a detached invocation contract.
    /// </summary>
    /// <param name="declaration">The validated phase and callback identities.</param>
    /// <returns>The managed dispatcher and optional native registration host.</returns>
    internal static LifecycleEmission Create(LifecycleDeclaration declaration)
    {
        var managed = new StringBuilder();
        var native = new StringBuilder();
        PgInitializeEmitter.EmitManaged(declaration.Target, declaration.Callback, managed);
        if (declaration.ModuleLoad)
        {
            PgModuleLoadEmitter.Emit(declaration.Callback, native);
        }

        return new(declaration, managed.ToString(), native.ToString());
    }
}
