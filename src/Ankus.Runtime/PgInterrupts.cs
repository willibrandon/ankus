namespace Ankus;

/// <summary>
/// Cooperates with PostgreSQL cancellation and shutdown during managed work.
/// </summary>
public static class PgInterrupts
{
    /// <summary>
    /// Processes pending PostgreSQL interrupts on the active backend thread.
    /// </summary>
    /// <remarks>
    /// Call periodically in long loops that do not otherwise enter PostgreSQL.
    /// An idle check reads callback-scoped native flags without a native call.
    /// PostgreSQL interrupt holdoffs still apply. Catching a cancellation does
    /// not clear it: subsequent checks and callback completion retain the error.
    /// A worker transaction retains cancellation until rollback; its caller can then handle the exception
    /// and continue through <see cref="PgBackgroundWorker.RunTransaction(Action)"/>.
    /// </remarks>
    /// <exception cref="InvalidOperationException">No backend callback is active, or a held lock forbids backend access.</exception>
    /// <exception cref="PgQueryCanceledException">PostgreSQL has canceled the current query.</exception>
    public static void Check() => NativeMemoryContext.CheckInterrupts();
}
