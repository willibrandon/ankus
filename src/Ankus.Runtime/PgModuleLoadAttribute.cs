namespace Ankus;

/// <summary>
/// Registers native extension hooks and providers when PostgreSQL loads the library.
/// </summary>
/// <remarks>
/// Apply this attribute to one accessible, synchronous, non-generic, parameterless static void method per assembly.
/// The method runs before PgInitialize, including before a parallel worker's first executor entry.
/// SQL is unavailable while PostgreSQL restores worker state or during postmaster startup.
/// Use PgInitialize for work that requires restored worker settings and transaction state.
/// Failed registration can be retried; managed state and installed hooks are not automatically rolled back.
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public sealed class PgModuleLoadAttribute : Attribute;
