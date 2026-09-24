using System.Globalization;

namespace Ankus.TestExtension;

/// <summary>
/// Exercises injected call metadata, native argument ownership, and iterator cleanup.
/// </summary>
public static class FunctionContextFunctions
{
    private static PgFunctionContext? s_saved;
    private static PgDatum? s_copy;
    private static int s_cleanup;

    /// <summary>
    /// Reads the complete snapshot after recursion, native error recovery, and a temporary context switch.
    /// </summary>
    /// <param name="number">The first SQL argument.</param>
    /// <param name="call">The injected call snapshot.</param>
    /// <param name="text">The second SQL argument.</param>
    /// <param name="memory">The independently injected memory owner.</param>
    /// <param name="again">The same call snapshot injected at another position.</param>
    /// <returns>The exact native identities and argument values.</returns>
    [PgFunction]
    public static string ContextSnapshot(int? number, PgFunctionContext call, string? text, PgMemoryContext memory, PgFunctionContext again)
    {
        if (!ReferenceEquals(call, again) || !memory.IsAlive || call.Arguments[0].Read<int?>() != number || call.Arguments[1].Read<string?>() != text)
        {
            throw new InvalidOperationException("Injected parameters changed argument order or identity.");
        }

        uint nested = Spi.ExecuteScalar<uint>("SELECT datatype.context_identity()");
        if (nested == call.FunctionOid)
        {
            throw new InvalidOperationException("Nested call retained its caller's function identity.");
        }

        try
        {
            Spi.Execute("SELECT 1 / 0");
        }
        catch (PgException exception) when (exception.SqlState == "22012")
        {
            return PgMemoryContext.RunTransient("Function context probe", _ => Snapshot(call));
        }

        throw new InvalidOperationException("Native error probe did not fail.");
    }

    /// <summary>
    /// Returns the identity of a call with no SQL arguments.
    /// </summary>
    /// <param name="call">The injected context.</param>
    /// <returns>The function's catalog OID.</returns>
    [PgFunction]
    public static uint ContextIdentity(PgFunctionContext call)
    {
        if (call.Arguments.Count != 0 || call.ResultTypeOid != 26 || call.CollationOid != 0)
        {
            throw new InvalidOperationException("Incorrect context-only function metadata.");
        }

        return call.FunctionOid;
    }

    /// <summary>
    /// Reads the operand through an operator expression's function-call context.
    /// </summary>
    /// <param name="value">The SQL operand.</param>
    /// <param name="call">The injected call context.</param>
    /// <returns>The incremented raw operand.</returns>
    [PgOperator("@~#")]
    public static int ContextOperand(int value, PgFunctionContext call)
    {
        if (call.Arguments.Count != 1 || call.ResultTypeOid != 23 || call.CollationOid != 0)
        {
            throw new InvalidOperationException("Incorrect operator metadata.");
        }

        return checked(call.Arguments[0].Read<int>() + 1);
    }

    /// <summary>
    /// Retains a snapshot and an explicit transaction-owned copy for later lifetime checks.
    /// </summary>
    /// <param name="call">The injected context.</param>
    /// <param name="number">The nullable scalar.</param>
    /// <param name="text">The nullable referenced value.</param>
    [PgFunction]
    public static void ContextRemember(PgFunctionContext call, int? number, string? text)
    {
        s_saved = call;
        s_copy = call.Arguments[1].CopyTo(PgMemoryContext.Get(PgMemoryContextKind.TopTransaction)!);
    }

    /// <summary>
    /// Checks stale arguments while reading the independent copy and retained metadata.
    /// </summary>
    /// <returns>The rejected access count, original types, and copied text.</returns>
    [PgFunction]
    public static string ContextExpired()
    {
        PgFunctionContext saved = s_saved ?? throw new InvalidOperationException("No saved context.");
        int rejected = 0;
        foreach (PgDatum argument in saved.Arguments)
        {
            try
            {
                argument.DangerousGetBits();
            }
            catch (ObjectDisposedException)
            {
                rejected++;
            }
        }

        return rejected.ToString(CultureInfo.InvariantCulture) + "|" + saved.ResultTypeOid.ToString(CultureInfo.InvariantCulture) + "|" +
            string.Join(',', saved.Arguments.Select(static value => value.TypeOid.ToString(CultureInfo.InvariantCulture))) + "|" + s_copy!.ToPostgresString();
    }

    /// <summary>
    /// Rejects off-thread native access while permitting immutable metadata reads.
    /// </summary>
    /// <param name="call">The injected context.</param>
    /// <param name="number">The argument whose access is checked.</param>
    /// <returns>The owned metadata and backend-thread rejection.</returns>
    [PgFunction]
    public static bool ContextThread(PgFunctionContext call, int number)
        => Task.Run(() =>
        {
            if (call.Arguments.Count != 1 || call.Arguments[0].TypeOid != 23 || call.Arguments[0].IsNull)
            {
                return false;
            }

            try
            {
                call.Arguments[0].DangerousGetBits();
                return false;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        }).GetAwaiter().GetResult();

    /// <summary>
    /// Streams rows while retaining the original call's raw values.
    /// </summary>
    /// <param name="call">The injected context.</param>
    /// <param name="text">The reference argument.</param>
    /// <param name="count">The row count.</param>
    /// <param name="fail">Whether to throw after the first row.</param>
    /// <returns>The repeated call snapshots.</returns>
    [PgFunction(SetMode = PgSetMode.ValuePerCall)]
    public static IEnumerable<string> ContextRows(PgFunctionContext call, string? text, int count, bool fail)
        => Rows(call, count, fail);

    /// <summary>
    /// Materializes rows under the same argument ownership contract.
    /// </summary>
    /// <param name="text">The reference argument.</param>
    /// <param name="call">The injected context.</param>
    /// <param name="count">The row count.</param>
    /// <param name="fail">Whether to throw after the first row.</param>
    /// <returns>The repeated call snapshots.</returns>
    [PgFunction(SetMode = PgSetMode.Materialize)]
    public static IEnumerable<string> ContextMaterialized(string? text, PgFunctionContext call, int count, bool fail)
        => Rows(call, count, fail);

    /// <summary>
    /// Returns the number of cleanup observations with live argument storage.
    /// </summary>
    /// <returns>The cleanup count.</returns>
    [PgFunction]
    public static int ContextCleanup() => s_cleanup;

    /// <summary>
    /// Retains the snapshot through yields and verifies native storage during normal and abort cleanup.
    /// </summary>
    /// <param name="call">The captured call.</param>
    /// <param name="count">The requested row count.</param>
    /// <param name="fail">Whether to raise a managed error on the second iteration.</param>
    /// <returns>The repeated snapshots.</returns>
    private static IEnumerable<string> Rows(PgFunctionContext call, int count, bool fail)
    {
        s_cleanup = 0;
        try
        {
            for (int index = 0; index < count; index++)
            {
                if (index == 1 && fail)
                {
                    throw new PgException("P7805", "function context iterator failure");
                }

                Spi.Execute("SELECT repeat('overwrite', 10000)");
                yield return Snapshot(call);
            }
        }
        finally
        {
            if (call.Arguments[1].DangerousGetBits() == (nuint)count)
            {
                s_cleanup++;
            }
        }
    }

    /// <summary>
    /// Formats native call identities and argument types, NULL flags, and PostgreSQL output values.
    /// </summary>
    /// <param name="call">The captured call.</param>
    /// <returns>The ordered independent observations.</returns>
    private static string Snapshot(PgFunctionContext call)
        => call.FunctionOid.ToString(CultureInfo.InvariantCulture) + "|" + call.ResultTypeOid.ToString(CultureInfo.InvariantCulture) + "|" +
            call.CollationOid.ToString(CultureInfo.InvariantCulture) + "|" + string.Join(';', call.Arguments.Select(static value =>
                value.TypeOid.ToString(CultureInfo.InvariantCulture) + ":" + (value.IsNull ? "NULL" : value.ToPostgresString())));
}
