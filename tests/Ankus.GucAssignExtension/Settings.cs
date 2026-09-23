namespace Ankus.GucAssignExtension;

/// <summary>
/// Exercises an assign-only library without managed initialization or SQL exports.
/// </summary>
public static partial class Settings
{
    private static int s_previous = 7;

    /// <summary>
    /// Gets a setting with an assignment callback and no other hooks.
    /// </summary>
    [PgGucInt("ankus_guc_assign.count", 7, "Assignment example", Assign = nameof(Assign))]
    public static partial int Count { get; }

    /// <summary>
    /// Checks the native old value before recording the newly accepted value.
    /// </summary>
    /// <param name="accepted">The accepted integer.</param>
    /// <param name="extra">The optional copied hook data.</param>
    internal static void Assign(int accepted, PgGucExtra? extra)
    {
        if (Count != s_previous)
        {
            throw new InvalidOperationException("Assign-only native old value was incorrect.");
        }

        if (accepted == 666)
        {
            throw new InvalidOperationException("Assign-only callback entered.");
        }

        if (accepted is 667 or 668 or 669)
        {
            try
            {
                PgLog.Write(accepted == 667 ? PgLogLevel.Error : PgLogLevel.Fatal,
                    new PgDiagnostic(accepted == 669 ? "Unrepresentable 🐘" : "Assignment requested termination.")
                    {
                        SqlState = "P0001",
                        Detail = "Managed frames unwind first.",
                    });
            }
            finally
            {
                PgLog.Write(PgLogLevel.Notice, $"Assignment finally {accepted}.");
            }
        }

        string sql;
        try
        {
            sql = Spi.ExecuteScalar<int>("SELECT 42").ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (InvalidOperationException)
        {
            sql = "unavailable";
        }

        PgLog.Write(PgLogLevel.Notice, new PgDiagnostic($"assign={accepted};old={Count};sql={sql};café 100%")
        {
            SqlState = "01000",
            Detail = "Owned détail",
            Hint = "Keep the accepted value.",
            SchemaName = "guc_schema",
            TableName = "guc_table",
            ColumnName = "guc_column",
            DataTypeName = "integer",
            ConstraintName = "guc_constraint",
            Context = "Assignment hook",
            File = "Settings.cs",
            Routine = "Assign",
            Line = 73,
            Position = 3,
            InternalQuery = "SELECT 7",
            InternalPosition = 8,
            DetailLog = "Server assignment detail",
        });
        PgLog.Write(PgLogLevel.Debug1, "Assignment debug message.");
        s_previous = accepted;
    }
}
