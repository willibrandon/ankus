using System.Globalization;

namespace Ankus.TestExtension;

/// <summary>
/// Exercises recoverable and atomic explicit subtransaction scopes against a temporary table with a primary key.
/// </summary>
public static class SubtransactionModeFunctions
{
    /// <summary>
    /// Inserts <paramref name="count"/> rows in one scope, optionally attempting a duplicate first at one position.
    /// </summary>
    /// <param name="count">The number of distinct rows to insert.</param>
    /// <param name="atomic">Whether the scope is atomic.</param>
    /// <param name="failAt">The row before which a duplicate key is attempted and caught, or zero.</param>
    /// <returns>The inner outcomes, then the scope outcome, separated by a vertical bar.</returns>
    [PgFunction]
    public static string SubtransactionScopeInsert(int count, bool atomic, int failAt)
    {
        string inner = "none";
        try
        {
            PgTransaction.RunInSubtransaction(() =>
            {
                for (int value = 1; value <= count; value++)
                {
                    if (value == failAt)
                    {
                        inner = Attempt("INSERT INTO scope_values VALUES (1)") + ":" + Attempt(Insert(value));
                        continue;
                    }

                    _ = Spi.Execute(Insert(value));
                }
            }, atomic ? PgSubtransactionMode.Atomic : PgSubtransactionMode.Recoverable);
            return inner + "|ok";
        }
        catch (PgException exception)
        {
            return inner + "|" + exception.SqlState;
        }
    }

    /// <summary>
    /// Runs an atomic scope whose nested recoverable scope catches a duplicate key, then continues the outer scope.
    /// </summary>
    /// <returns>The nested outcome, then the outer outcome, separated by a vertical bar.</returns>
    [PgFunction]
    public static string SubtransactionScopeNested()
    {
        string nested = "none";
        PgTransaction.RunInSubtransaction(() =>
        {
            _ = Spi.Execute(Insert(1));
            try
            {
                PgTransaction.RunInSubtransaction(() => Spi.Execute(Insert(1)));
                nested = "inserted";
            }
            catch (PgException exception)
            {
                nested = exception.SqlState;
            }

            _ = Spi.Execute(Insert(2));
        }, PgSubtransactionMode.Atomic);
        return nested + "|ok";
    }

    private static string Insert(int value) => "INSERT INTO scope_values VALUES (" + value.ToString(CultureInfo.InvariantCulture) + ")";

    private static string Attempt(string sql)
    {
        try
        {
            _ = Spi.Execute(sql);
            return "ok";
        }
        catch (PgException exception)
        {
            return exception.SqlState;
        }
    }
}
