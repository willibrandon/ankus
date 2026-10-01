using System.Text;

namespace Ankus.Generators;

/// <summary>
/// Retains independently rendered configuration descriptors, dispatchers and partial getter source.
/// </summary>
/// <param name="Symbol">The native descriptor symbol used by current registration.</param>
/// <param name="Managed">The optional managed hook dispatcher.</param>
/// <param name="Native">The native descriptor and hook definitions.</param>
/// <param name="Property">The complete partial property source.</param>
internal sealed record GucEmission(string Symbol, string Managed, string Native, string Property)
{
    /// <summary>
    /// Renders one validated configuration contract without graph or compiler state.
    /// </summary>
    /// <param name="model">The immutable registration and managed invocation contract.</param>
    /// <returns>The independently reusable source fragments.</returns>
    internal static GucEmission Create(GucModel model)
    {
        var managed = new StringBuilder();
        var native = new StringBuilder();
        string symbol = PgGucEmitter.Emit(model, model.Callback, managed, native);
        return new(symbol, managed.ToString(), native.ToString(), PgGucEmitter.EmitProperty(model));
    }
}
