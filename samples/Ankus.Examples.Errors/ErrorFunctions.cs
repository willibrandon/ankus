namespace Ankus.Examples.Errors;

/// <summary>
/// Ports pgrx's errors example to managed exceptions and PostgreSQL reports.
/// </summary>
[PgSchema("errors")]
public static class ErrorFunctions
{
    /// <summary>
    /// Sums present array elements and rejects SQL NULL cells.
    /// </summary>
    /// <param name="input">The nullable integer cells to sum.</param>
    /// <returns>The checked 64-bit sum.</returns>
    [PgFunction]
    public static long ArrayWithNullAndPanic(int?[] input)
    {
        long sum = 0;
        foreach (int? value in input)
        {
            sum = checked(sum + (value ?? throw new InvalidOperationException("NULL elements in input array are not supported.")));
        }

        return sum;
    }

    /// <summary>
    /// Demonstrates an ordinary managed failure caused by an absent nullable value.
    /// </summary>
    [PgFunction]
    public static void CauseUnwrapPanic()
    {
        int? value = null;
        _ = value ?? throw new InvalidOperationException("The nullable value is absent.");
    }

    /// <summary>
    /// Demonstrates a PostgreSQL error raised by a guarded native operation.
    /// </summary>
    [PgFunction]
    public static void CausePgError()
    {
        using PgRelation relation = PgRelation.Open("invalid table syntax");
    }

    /// <summary>
    /// Throws an ordinary managed exception with caller-supplied text.
    /// </summary>
    /// <param name="message">The error text.</param>
    [PgFunction]
    public static void ThrowManagedException(string message) => throw new InvalidOperationException(message);

    /// <summary>
    /// Reports information to the PostgreSQL client.
    /// </summary>
    /// <param name="message">The literal message.</param>
    [PgFunction]
    public static void RaisePgInfo(string message) => PgLog.Info(message);

    /// <summary>
    /// Reports a PostgreSQL warning without stopping execution.
    /// </summary>
    /// <param name="message">The literal message.</param>
    [PgFunction]
    public static void RaisePgWarning(string message) => PgLog.Warning(message);

    /// <summary>
    /// Raises a catchable PostgreSQL ERROR after managed frames unwind.
    /// </summary>
    /// <param name="message">The literal message.</param>
    [PgFunction]
    public static void ThrowPgError(string message) => PgLog.Error(message);

    /// <summary>
    /// Ends the current backend with PostgreSQL FATAL after managed frames unwind.
    /// </summary>
    /// <param name="message">The literal message.</param>
    [PgFunction]
    public static void ThrowPgFatal(string message) => PgLog.Fatal(message);

    /// <summary>
    /// Starts PostgreSQL crash recovery with PANIC after managed frames unwind.
    /// </summary>
    /// <param name="message">The literal message.</param>
    [PgFunction]
    public static void ThrowPgPanic(string message) => PgLog.Panic(message);
}
