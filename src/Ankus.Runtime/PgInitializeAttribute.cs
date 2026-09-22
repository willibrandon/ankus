namespace Ankus;

/// <summary>
/// Runs a static method when PostgreSQL first loads the extension in a backend process.
/// </summary>
/// <remarks>
/// Apply this attribute to one accessible, synchronous, non-generic, parameterless static void method per assembly.
/// Initialization runs before the first extension function and can run again if an earlier initialization failed.
/// PostgreSQL APIs that require a transaction are available only when the library is loaded inside a transaction.
/// Managed initialization cannot run in the forking postmaster through shared_preload_libraries;
/// load the extension in a backend using session_preload_libraries, LOAD, or a function call instead.
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public sealed class PgInitializeAttribute : Attribute;
