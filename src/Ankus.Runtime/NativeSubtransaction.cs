using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;

namespace Ankus;

/// <summary>
/// Contains managed callback failures until native subtransaction recovery has finished.
/// </summary>
internal static unsafe class NativeSubtransaction
{
    [ThreadStatic]
    private static Invocation? s_current;

    /// <summary>
    /// Runs a callback through the native transaction guard and preserves its original exception.
    /// </summary>
    /// <typeparam name="TResult">The managed result type.</typeparam>
    /// <param name="action">The synchronous callback.</param>
    /// <returns>The result after native release succeeds.</returns>
    internal static TResult Run<TResult>(Func<TResult> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        NativeBackend.CheckAccess();
        TResult result = default!;
        var invocation = new Invocation(() => result = action());
        GCHandle handle = GCHandle.Alloc(invocation);
        try
        {
            NativeBackend.RunSubtransaction((nint)(delegate* unmanaged[Cdecl]<nint, int>)&Dispatch,
                GCHandle.ToIntPtr(handle));
        }
        catch (PgException) when (invocation.Failure is not null)
        {
            ExceptionDispatchInfo.Capture(invocation.Failure).Throw();
            throw;
        }
        finally
        {
            handle.Free();
        }

        if (invocation.Failure is { } failure)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }

        return result;
    }

    /// <summary>
    /// Marks the innermost scope for rollback after a raw native call fails.
    /// </summary>
    /// <param name="exception">The owned native diagnostic.</param>
    internal static void RecordFailure(PgException exception)
    {
        if (s_current is { } current)
        {
            current.Failure ??= exception;
        }
    }

    /// <summary>
    /// Prevents additional SQL and raw native work before the failed scope rolls back.
    /// </summary>
    internal static void CheckAccess()
    {
        if (s_current?.Failure is { } failure)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    /// <summary>
    /// Gets whether rollback must reclaim native SPI frames instead of ordinary session close.
    /// </summary>
    internal static bool HasFailure => s_current?.Failure is not null;

    /// <summary>
    /// Returns every managed exception to native code as a status before recovery begins.
    /// </summary>
    /// <param name="state">The live handle borrowed by this synchronous invocation.</param>
    /// <returns>Zero on success, or one when the scope must roll back.</returns>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int Dispatch(nint state)
    {
        Invocation? invocation = null;
        Invocation? previous = s_current;
        try
        {
            invocation = (Invocation)GCHandle.FromIntPtr(state).Target!;
            s_current = invocation;
            invocation.Action();
            return invocation.Failure is null ? 0 : 1;
        }
        catch (Exception exception)
        {
            if (invocation is not null)
            {
                invocation.Failure ??= exception;
            }

            return 1;
        }
        finally
        {
            s_current = previous;
        }
    }

    /// <summary>
    /// Retains a synchronous callback and the first failure requiring rollback.
    /// </summary>
    /// <param name="action">The callback with its managed result destination.</param>
    private sealed class Invocation(Action action)
    {
        /// <summary>
        /// Gets the work to invoke once from the native guard.
        /// </summary>
        internal Action Action { get; } = action;

        /// <summary>
        /// Gets or sets the first error to rethrow after rollback.
        /// </summary>
        internal Exception? Failure
        {
            get;
            set;
        }
    }
}
