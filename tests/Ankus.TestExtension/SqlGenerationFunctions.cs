using Ankus;

[assembly: PgSql("sql-generation.supplied-step", """
    CREATE FUNCTION sql_generation.supplied_step(integer,integer) RETURNS integer
        LANGUAGE SQL IMMUTABLE STRICT AS 'SELECT $1+$2+100';
    """, Requires = ["sql-generation.disabled-helper"], Before = ["sql-generation.supplied-aggregate"])]

namespace Ankus.TestExtension;

/// <summary>
/// Observes native execution independently of each function's replacement installation SQL.
/// </summary>
[PgSchema("sql_generation", Id = "sql-generation.schema")]
public static class SqlGenerationFunctions
{
    private static int s_scalarCalls;
    private static int s_setStarted;
    private static int s_setRows;
    private static int s_setFinally;
    private static int s_tableFinally;
    private static int s_triggerCalls;
    private static int s_eventCalls;
    private static int s_aggregateCalls;
    private static int s_scalarFinally;
    private static readonly List<string> s_events = [];

    /// <summary>
    /// Resets observations in the current backend.
    /// </summary>
    [PgFunction(GenerateSql = true)]
    public static void ProbeReset()
    {
        s_scalarCalls = 0;
        s_setStarted = 0;
        s_setRows = 0;
        s_setFinally = 0;
        s_tableFinally = 0;
        s_triggerCalls = 0;
        s_eventCalls = 0;
        s_aggregateCalls = 0;
        s_scalarFinally = 0;
        s_events.Clear();
    }

    /// <summary>
    /// Returns scalar calls, iterator starts/rows/finally, table finally, trigger/event/aggregate calls and scalar finally.
    /// </summary>
    [PgFunction]
    public static int[] ProbeStatus() => [s_scalarCalls, s_setStarted, s_setRows, s_setFinally,
        s_tableFinally, s_triggerCalls, s_eventCalls, s_aggregateCalls, s_scalarFinally];

    /// <summary>
    /// Returns event metadata retained as owned strings after the callbacks finish.
    /// </summary>
    [PgFunction]
    public static string[] ProbeEvents() => [.. s_events];

    /// <summary>
    /// Exposes strict and nonstrict SQL aliases of exactly the same native wrapper.
    /// </summary>
    [PgFunction(Sql = """
        CREATE FUNCTION sql_generation.custom_scalar(integer) RETURNS integer
            AS '@MODULE_PATHNAME@','@FUNCTION_NAME@' LANGUAGE c VOLATILE CALLED ON NULL INPUT COST 9;
        CREATE FUNCTION sql_generation.strict_scalar(integer) RETURNS integer
            AS '@MODULE_PATHNAME@','@FUNCTION_NAME@' LANGUAGE c IMMUTABLE STRICT PARALLEL SAFE COST 7;
        COMMENT ON FUNCTION sql_generation.custom_scalar(integer) IS '@FUNCTION_NAME@';
        """)]
    public static int? ScalarOriginal(int? value)
    {
        s_scalarCalls++;
        try
        {
            return value switch
            {
                null => 111,
                -1 => throw new PgException("P8201", "replacement scalar failed", "replacement detail", "retry a nonnegative value"),
                -2 => throw new InvalidOperationException("ordinary replacement failure"),
                -3 => null,
                _ => checked(value * 3),
            };
        }
        finally
        {
            s_scalarFinally++;
        }
    }

    /// <summary>
    /// Leaves the compiled callback without a default SQL registration.
    /// </summary>
    [PgFunction(Sql = "")]
    public static int EmptyOriginal() => 1;

    /// <summary>
    /// Retains comment-only SQL without restoring the ordinary function declaration.
    /// </summary>
    [PgFunction(Sql = "-- SQL generation comment-only fixture\n")]
    public static int CommentOriginal() => 2;

    /// <summary>
    /// Produces an absent sequence or a lazy sequence with exact lifecycle observations.
    /// </summary>
    [PgFunction(SetMode = PgSetMode.ValuePerCall, Sql = """
        CREATE FUNCTION sql_generation.custom_set(boolean,integer,integer) RETURNS SETOF integer
            AS '@MODULE_PATHNAME@','@FUNCTION_NAME@' LANGUAGE c VOLATILE STRICT ROWS 17 COST 3;
        """)]
    public static IEnumerable<int?>? SetOriginal(bool absent, int count, int failureAt)
        => absent ? null : SetRows(count, failureAt);

    /// <summary>
    /// Emits renamed TABLE columns while retaining positional tuple values and nullability.
    /// </summary>
    [PgFunction(SetMode = PgSetMode.Materialize, Sql = """
        CREATE FUNCTION sql_generation.custom_table(integer) RETURNS TABLE(item integer,note text)
            AS '@MODULE_PATHNAME@','@FUNCTION_NAME@' LANGUAGE c VOLATILE STRICT ROWS 5;
        """)]
    public static IEnumerable<(int OriginalId, string? OriginalText)> TableOriginal(int count)
    {
        try
        {
            for (int index = 0; index < count; index++)
            {
                yield return (index + 1, index == 1 ? null : "café ' \\ ; " + index);
            }
        }
        finally
        {
            s_tableFinally++;
        }
    }

    /// <summary>
    /// Applies tuple replacement, row suppression and a recoverable error through the custom trigger name.
    /// </summary>
    [PgTrigger]
    [PgFunction(Sql = """
        CREATE FUNCTION sql_generation.custom_trigger() RETURNS trigger
            AS '@MODULE_PATHNAME@','@FUNCTION_NAME@' LANGUAGE c VOLATILE;
        """)]
    public static PgHeapTuple? TriggerOriginal(PgTriggerContext context)
    {
        s_triggerCalls++;
        PgHeapTuple row = context.New!;
        int id = row.Get<int>("id");
        if (id < 0)
        {
            throw new PgException("P8203", "replacement trigger failed", "trigger replacement detail", "retry a positive id");
        }

        if (id == 0)
        {
            return null;
        }

        PgHeapTuple result = row.Clone();
        result.Set("value", row.Get<int?>("value") + 10);
        result.Set("note", context.Arguments[0] + ":" + (row.Get<string?>("note") ?? "<NULL>"));
        return result;
    }

    /// <summary>
    /// Captures exact event metadata and can reject DDL through the replacement wrapper registration.
    /// </summary>
    [PgEventTrigger]
    [PgFunction(Sql = """
        CREATE FUNCTION sql_generation.custom_event() RETURNS event_trigger
            AS '@MODULE_PATHNAME@','@FUNCTION_NAME@' LANGUAGE c VOLATILE;
        """)]
    public static void EventOriginal(PgEventTriggerContext context)
    {
        s_eventCalls++;
        s_events.Add(context.Event + ":" + context.CommandTag);
        if (Spi.ExecuteScalar<string?>("SELECT current_setting('ankus.sql_generation_event_error',true)") == "on")
        {
            throw new PgException("P8204", "replacement event failed", "event replacement detail", "retry without the event fault");
        }
    }

    /// <summary>
    /// Reuses one configured helper for transition and combine while the aggregate definition remains generated.
    /// </summary>
    [PgAggregate(Name = "custom_sum", Transition = nameof(SharedSum.Add), Combine = nameof(SharedSum.Add), InitialCondition = "0")]
    public static class SharedSum
    {
        /// <summary>
        /// Adds values through the single literal helper registration.
        /// </summary>
        [PgFunction(Name = "shared_step", Sql = """
            CREATE FUNCTION sql_generation.shared_step(integer,integer) RETURNS integer
                AS '@MODULE_PATHNAME@','@FUNCTION_NAME@' LANGUAGE c IMMUTABLE STRICT COST 4;
            """)]
        public static int Add(int state, int value)
        {
            s_aggregateCalls++;
            return checked(state + value);
        }
    }

    /// <summary>
    /// Keeps its generated aggregate while explicit SQL supplies the disabled support declaration.
    /// </summary>
    [PgAggregate(Name = "supplied_sum", InitialCondition = "0", Id = "sql-generation.supplied-aggregate")]
    public static class SuppliedSum
    {
        /// <summary>
        /// Remains compiled but must never execute because a SQL-language helper replaces its disabled declaration.
        /// </summary>
        [PgFunction(Name = "supplied_step", GenerateSql = false, Id = "sql-generation.disabled-helper")]
        public static int Transition(int state, int value) => throw new InvalidOperationException("disabled helper executed");
    }

    /// <summary>
    /// Gives the replacement cast an independent nominal PostgreSQL source type.
    /// </summary>
    [PgEnum(Name = "override_token")]
    public enum OverrideToken
    {
        /// <summary>
        /// Converts to the first nonordinal result.
        /// </summary>
        One = 3,

        /// <summary>
        /// Converts to a second nonordinal result.
        /// </summary>
        Two = 9,

        /// <summary>
        /// Produces SQL NULL from a present enum label.
        /// </summary>
        Empty = -1,

        /// <summary>
        /// Selects the managed error branch.
        /// </summary>
        Invalid = -2,
    }

    /// <summary>
    /// Replaces a complete function/operator/cast bundle with different SQL names and cast context.
    /// </summary>
    [PgOperator("!@", Id = "sql-generation.original-operator")]
    [PgCast(PgCastContext.Implicit, Id = "sql-generation.original-cast", Requires = ["sql-generation.original-operator"])]
    [PgFunction(Name = "declared_convert", Sql = """
        CREATE FUNCTION sql_generation.custom_convert(sql_generation.override_token) RETURNS integer
            AS '@MODULE_PATHNAME@','@FUNCTION_NAME@' LANGUAGE c IMMUTABLE STRICT;
        CREATE OPERATOR sql_generation.## (RIGHTARG=sql_generation.override_token, FUNCTION=sql_generation.custom_convert);
        CREATE CAST (sql_generation.override_token AS integer) WITH FUNCTION sql_generation.custom_convert(sql_generation.override_token) AS ASSIGNMENT;
        """)]
    public static int? ConvertOriginal(OverrideToken value) => value switch
    {
        OverrideToken.One => 103,
        OverrideToken.Two => 109,
        OverrideToken.Empty => null,
        _ => throw new PgException("P8205", "replacement cast failed"),
    };

    /// <summary>
    /// Enumerates scalar rows while exposing disposal after exhaustion, early termination and errors.
    /// </summary>
    private static IEnumerable<int?> SetRows(int count, int failureAt)
    {
        s_setStarted++;
        try
        {
            for (int index = 0; index < count; index++)
            {
                if (index == failureAt)
                {
                    throw new PgException("P8202", "replacement iterator failed", "iterator replacement detail", "retry with no failing row");
                }

                s_setRows++;
                yield return index == 1 ? null : index * 10;
            }
        }
        finally
        {
            s_setFinally++;
        }
    }
}
