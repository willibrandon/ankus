using System.Text;

namespace Ankus.Build;

/// <summary>
/// Gives compiler-dependent PostgreSQL function-like macros an addressable signature for native contract checks.
/// </summary>
internal static class NativeBindingCompilerShims
{
    /// <summary>
    /// Retains the selected headers' spin-delay implementation whether it is a function or the generic macro fallback.
    /// </summary>
    /// <param name="source">The translation unit after its native headers.</param>
    /// <param name="symbols">The complete set of declarations being checked in this translation unit.</param>
    internal static void Write(StringBuilder source, IEnumerable<NativeHeaderSymbol> symbols)
    {
        if (!symbols.Any(static symbol => symbol.IsFunction && symbol.NativeName == "pg_spin_delay_impl")) { return; }

        source.AppendLine("""
            #if defined(pg_spin_delay_impl)
            #if !defined(_WIN32)
            __attribute__((visibility("hidden")))
            #endif
            void ankus_compiler_pg_spin_delay_impl(void)
            {
                pg_spin_delay_impl();
            }
            #else
            #define ankus_compiler_pg_spin_delay_impl pg_spin_delay_impl
            #endif
            """);
    }

    /// <summary>
    /// Selects an addressable implementation without changing the catalog's native or linkage identity.
    /// </summary>
    /// <param name="symbol">The native function or global being checked.</param>
    /// <returns>The native declaration or its compiler-specific validation shim.</returns>
    internal static string Reference(NativeHeaderSymbol symbol)
        => symbol.IsFunction && symbol.NativeName == "pg_spin_delay_impl" ? "ankus_compiler_pg_spin_delay_impl" : symbol.NativeName;
}
