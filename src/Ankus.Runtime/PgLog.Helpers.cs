using System.Diagnostics.CodeAnalysis;

namespace Ankus;

public static partial class PgLog
{
    /// <summary>
    /// Reports debugging text at detail level five.
    /// </summary>
    /// <param name="message">The primary message, treated as literal text.</param>
    public static void Debug5(string message) => Write(PgLogLevel.Debug5, message);

    /// <summary>
    /// Reports debugging text at detail level five.
    /// </summary>
    /// <param name="diagnostic">The message and optional PostgreSQL diagnostic fields.</param>
    public static void Debug5(PgDiagnostic diagnostic) => Write(PgLogLevel.Debug5, diagnostic);

    /// <summary>
    /// Reports debugging text at detail level four.
    /// </summary>
    /// <param name="message">The primary message, treated as literal text.</param>
    public static void Debug4(string message) => Write(PgLogLevel.Debug4, message);

    /// <summary>
    /// Reports debugging text at detail level four.
    /// </summary>
    /// <param name="diagnostic">The message and optional PostgreSQL diagnostic fields.</param>
    public static void Debug4(PgDiagnostic diagnostic) => Write(PgLogLevel.Debug4, diagnostic);

    /// <summary>
    /// Reports debugging text at detail level three.
    /// </summary>
    /// <param name="message">The primary message, treated as literal text.</param>
    public static void Debug3(string message) => Write(PgLogLevel.Debug3, message);

    /// <summary>
    /// Reports debugging text at detail level three.
    /// </summary>
    /// <param name="diagnostic">The message and optional PostgreSQL diagnostic fields.</param>
    public static void Debug3(PgDiagnostic diagnostic) => Write(PgLogLevel.Debug3, diagnostic);

    /// <summary>
    /// Reports debugging text at detail level two.
    /// </summary>
    /// <param name="message">The primary message, treated as literal text.</param>
    public static void Debug2(string message) => Write(PgLogLevel.Debug2, message);

    /// <summary>
    /// Reports debugging text at detail level two.
    /// </summary>
    /// <param name="diagnostic">The message and optional PostgreSQL diagnostic fields.</param>
    public static void Debug2(PgDiagnostic diagnostic) => Write(PgLogLevel.Debug2, diagnostic);

    /// <summary>
    /// Reports debugging text at detail level one.
    /// </summary>
    /// <param name="message">The primary message, treated as literal text.</param>
    public static void Debug1(string message) => Write(PgLogLevel.Debug1, message);

    /// <summary>
    /// Reports debugging text at detail level one.
    /// </summary>
    /// <param name="diagnostic">The message and optional PostgreSQL diagnostic fields.</param>
    public static void Debug1(PgDiagnostic diagnostic) => Write(PgLogLevel.Debug1, diagnostic);

    /// <summary>
    /// Reports an operational message with PostgreSQL's LOG routing.
    /// </summary>
    /// <param name="message">The primary message, treated as literal text.</param>
    public static void Log(string message) => Write(PgLogLevel.Log, message);

    /// <summary>
    /// Reports an operational message with PostgreSQL's LOG routing.
    /// </summary>
    /// <param name="diagnostic">The message and optional PostgreSQL diagnostic fields.</param>
    public static void Log(PgDiagnostic diagnostic) => Write(PgLogLevel.Log, diagnostic);

    /// <summary>
    /// Reports an operational message to the server only.
    /// </summary>
    /// <param name="message">The primary message, treated as literal text.</param>
    public static void ServerOnly(string message) => Write(PgLogLevel.ServerOnly, message);

    /// <summary>
    /// Reports an operational message to the server only.
    /// </summary>
    /// <param name="diagnostic">The message and optional PostgreSQL diagnostic fields.</param>
    public static void ServerOnly(PgDiagnostic diagnostic) => Write(PgLogLevel.ServerOnly, diagnostic);

    /// <summary>
    /// Reports information to the client regardless of client_min_messages.
    /// </summary>
    /// <param name="message">The primary message, treated as literal text.</param>
    public static void Info(string message) => Write(PgLogLevel.Info, message);

    /// <summary>
    /// Reports information to the client regardless of client_min_messages.
    /// </summary>
    /// <param name="diagnostic">The message and optional PostgreSQL diagnostic fields.</param>
    public static void Info(PgDiagnostic diagnostic) => Write(PgLogLevel.Info, diagnostic);

    /// <summary>
    /// Reports an expected event without stopping execution.
    /// </summary>
    /// <param name="message">The primary message, treated as literal text.</param>
    public static void Notice(string message) => Write(PgLogLevel.Notice, message);

    /// <summary>
    /// Reports an expected event without stopping execution.
    /// </summary>
    /// <param name="diagnostic">The message and optional PostgreSQL diagnostic fields.</param>
    public static void Notice(PgDiagnostic diagnostic) => Write(PgLogLevel.Notice, diagnostic);

    /// <summary>
    /// Reports an unexpected event without stopping execution.
    /// </summary>
    /// <param name="message">The primary message, treated as literal text.</param>
    public static void Warning(string message) => Write(PgLogLevel.Warning, message);

    /// <summary>
    /// Reports an unexpected event without stopping execution.
    /// </summary>
    /// <param name="diagnostic">The message and optional PostgreSQL diagnostic fields.</param>
    public static void Warning(PgDiagnostic diagnostic) => Write(PgLogLevel.Warning, diagnostic);

    /// <summary>
    /// Throws a catchable PostgreSQL error after validating the active logging capability.
    /// </summary>
    /// <remarks>
    /// The default SQLSTATE is XX000. Explicit diagnostic fields and codes retain their meaning.
    /// An unhandled error is raised by PostgreSQL after managed frames unwind.
    /// </remarks>
    /// <param name="message">The primary message, treated as literal text.</param>
    [DoesNotReturn]
    public static void Error(string message) => throw CreateTerminal(PgLogLevel.Error, new PgDiagnostic(message));

    /// <summary>
    /// Throws a catchable PostgreSQL error after validating the active logging capability.
    /// </summary>
    /// <remarks>
    /// The default SQLSTATE is XX000. Explicit diagnostic fields and codes retain their meaning.
    /// An unhandled error is raised by PostgreSQL after managed frames unwind.
    /// </remarks>
    /// <param name="diagnostic">The message and optional PostgreSQL diagnostic fields.</param>
    [DoesNotReturn]
    public static void Error(PgDiagnostic diagnostic) => throw CreateTerminal(PgLogLevel.Error, diagnostic);

    /// <summary>
    /// Records a terminal report and unwinds managed code before PostgreSQL terminates the connection.
    /// </summary>
    /// <remarks>
    /// The default SQLSTATE is XX000. Explicit diagnostic fields and codes retain their meaning.
    /// Catching the managed exception does not discard the terminal report.
    /// </remarks>
    /// <param name="message">The primary message, treated as literal text.</param>
    [DoesNotReturn]
    public static void Fatal(string message) => throw CreateTerminal(PgLogLevel.Fatal, new PgDiagnostic(message));

    /// <summary>
    /// Records a terminal report and unwinds managed code before PostgreSQL terminates the connection.
    /// </summary>
    /// <remarks>
    /// The default SQLSTATE is XX000. Explicit diagnostic fields and codes retain their meaning.
    /// Catching the managed exception does not discard the terminal report.
    /// </remarks>
    /// <param name="diagnostic">The message and optional PostgreSQL diagnostic fields.</param>
    [DoesNotReturn]
    public static void Fatal(PgDiagnostic diagnostic) => throw CreateTerminal(PgLogLevel.Fatal, diagnostic);

    /// <summary>
    /// Records a terminal report and unwinds managed code before PostgreSQL starts cluster crash recovery.
    /// </summary>
    /// <remarks>
    /// The default SQLSTATE is XX000. Explicit diagnostic fields and codes retain their meaning.
    /// Catching the managed exception does not discard the terminal report.
    /// </remarks>
    /// <param name="message">The primary message, treated as literal text.</param>
    [DoesNotReturn]
    public static void Panic(string message) => throw CreateTerminal(PgLogLevel.Panic, new PgDiagnostic(message));

    /// <summary>
    /// Records a terminal report and unwinds managed code before PostgreSQL starts cluster crash recovery.
    /// </summary>
    /// <remarks>
    /// The default SQLSTATE is XX000. Explicit diagnostic fields and codes retain their meaning.
    /// Catching the managed exception does not discard the terminal report.
    /// </remarks>
    /// <param name="diagnostic">The message and optional PostgreSQL diagnostic fields.</param>
    [DoesNotReturn]
    public static void Panic(PgDiagnostic diagnostic) => throw CreateTerminal(PgLogLevel.Panic, diagnostic);
}
