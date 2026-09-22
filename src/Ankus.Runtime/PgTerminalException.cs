namespace Ankus;

/// <summary>
/// Carries a terminal report to the native dispatcher after managed finally blocks have executed.
/// </summary>
internal sealed class PgTerminalException(PgLogLevel level, PgDiagnostic diagnostic) : Exception(diagnostic.Message)
{
    internal PgLogLevel Level { get; } = level;

    internal PgDiagnostic Diagnostic { get; } = diagnostic;
}
