using System.Globalization;

namespace Ankus.TestExtension;

/// <summary>
/// Exercises PostgreSQL diagnostics retained across managed catches, recursive calls, and transaction boundaries.
/// </summary>
public static class DiagnosticFunctions
{
    private static PgException? s_error;

    /// <summary>
    /// Catches and retains a PostgreSQL exception, then performs another native allocation before returning.
    /// </summary>
    /// <param name="sql">The failing command.</param>
    /// <returns>The captured SQLSTATE.</returns>
    [PgFunction]
    public static string DiagnosticCache(string sql)
    {
        try
        {
            Spi.Execute(sql);
        }
        catch (PgException error)
        {
            s_error = error;
            Spi.Execute("SELECT repeat('overwrite', 10000)");
            return error.SqlState;
        }

        throw new InvalidOperationException("The diagnostic probe did not fail.");
    }

    /// <summary>
    /// Reads one field from an exception retained beyond its original native error context.
    /// </summary>
    /// <param name="field">The diagnostic field name.</param>
    /// <returns>The captured value, including null for an absent field.</returns>
    [PgFunction]
    public static string? DiagnosticRead(string field)
    {
        PgException error = s_error ?? throw new InvalidOperationException("No cached error.");
        return field switch
        {
            "message" => error.Message,
            "detail" => error.Detail,
            "hint" => error.Hint,
            "context" => error.Context,
            "schema" => error.SchemaName,
            "table" => error.TableName,
            "column" => error.ColumnName,
            "datatype" => error.DataTypeName,
            "constraint" => error.ConstraintName,
            "position" => error.Position.ToString(CultureInfo.InvariantCulture),
            "internalPosition" => error.InternalPosition.ToString(CultureInfo.InvariantCulture),
            "query" => error.InternalQuery,
            "file" => error.File,
            "line" => error.Line.ToString(CultureInfo.InvariantCulture),
            "routine" => error.Routine,
            "detailLog" => error.DetailLog,
            "backtrace" => error.Backtrace,
            "incomplete" => error.DiagnosticsIncomplete ? "true" : "false",
            _ => throw new ArgumentException("Unknown diagnostic field.", nameof(field)),
        };
    }

    /// <summary>
    /// Rethrows the retained exception from a subsequent callback.
    /// </summary>
    [PgFunction]
    public static void DiagnosticRethrow()
    {
        if (s_error is null)
        {
            throw new InvalidOperationException("No cached error.");
        }

        throw s_error;
    }

    /// <summary>
    /// Reports a managed error with all supported object, context, query, and source diagnostics.
    /// </summary>
    /// <param name="text">Text used in the primary and secondary diagnostics.</param>
    [PgFunction]
    public static void DiagnosticReport(string text)
        => throw new PgException(PgSqlStates.InvalidParameterValue, text, "detail " + text, "hint " + text)
        {
            Context = "context " + text,
            SchemaName = "schéma",
            TableName = "table 🐘",
            ColumnName = "cölumn",
            DataTypeName = "custom_type",
            ConstraintName = "check_value",
            Position = 7,
            InternalPosition = 5,
            InternalQuery = "SELECT value",
            File = "diagnostic.cs",
            Line = 42,
            Routine = "ReportDiagnostic",
            DetailLog = "server only " + text,
            Backtrace = "native backtrace " + text,
        };

    /// <summary>
    /// Reports diagnostics in LATIN1, optionally triggering an encoding error while copying the context field.
    /// </summary>
    /// <param name="incompatible">Whether to include a character that LATIN1 cannot represent.</param>
    [PgFunction]
    public static void DiagnosticEncoding(bool incompatible)
        => throw new PgException(PgSqlStates.InvalidParameterValue, "message café", "détail", "réessayer")
        {
            Context = incompatible ? "context 🐘" : "context café",
            SchemaName = "schéma",
            TableName = "tablë",
            ColumnName = "cölumn",
            DataTypeName = "typé",
            ConstraintName = "consträint",
            File = "encoding.cs",
            Line = 73,
            Routine = "ReportEncoding",
        };

    /// <summary>
    /// Repeatedly catches native errors within one callback to exercise recovery-context cleanup.
    /// </summary>
    /// <param name="count">The number of failures to recover from.</param>
    /// <returns>The number of recovered division-by-zero errors.</returns>
    [PgFunction]
    public static int DiagnosticRepeat(int count)
    {
        int recovered = 0;
        for (int index = 0; index < count; index++)
        {
            try
            {
                Spi.Execute("SELECT 1 / 0");
            }
            catch (PgException error) when (error.SqlState == PgSqlStates.DivisionByZero && error.Routine == "int4div")
            {
                recovered++;
            }
        }

        long contexts = Spi.ExecuteScalar<long>("SELECT count(*) FROM pg_backend_memory_contexts WHERE name = 'Ankus error diagnostics'");
        return contexts == 0 ? recovered : -1;
    }
}
