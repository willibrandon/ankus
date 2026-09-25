using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Executes literal function registrations and their unchanged Native AOT callbacks in PostgreSQL.
/// </summary>
/// <param name="context">The current test context.</param>
[TestClass]
[DoNotParallelize]
public sealed class SqlGenerationTests(TestContext context)
{
    /// <summary>
    /// Replacement aliases share the retained native export while SQL options govern NULL dispatch.
    /// </summary>
    [TestMethod]
    public async Task CustomSqlScalarAliasesPreserveNativeIdentityAndOptions()
    {
        await using NpgsqlConnection connection = await Open();
        Assert.IsTrue(await Scalar<bool>(connection, "SELECT sql_generation.strict_scalar(NULL) IS NULL"));
        Assert.AreSequenceEqual(new int[9], await Status(connection));
        Assert.AreEqual(111, await Scalar<int>(connection, "SELECT sql_generation.custom_scalar(NULL)"));
        Assert.AreEqual(0, await Scalar<int>(connection, "SELECT sql_generation.custom_scalar(0)"));
        Assert.AreEqual(21, await Scalar<int>(connection, "SELECT sql_generation.custom_scalar(7)"));
        Assert.AreEqual(27, await Scalar<int>(connection, "SELECT sql_generation.strict_scalar(9)"));
        Assert.IsTrue(await Scalar<bool>(connection, "SELECT sql_generation.custom_scalar(-3) IS NULL"));
        Assert.AreSequenceEqual([5, 0, 0, 0, 0, 0, 0, 0, 5], await Status(connection));
        Assert.AreSequenceEqual(["custom_scalar:false:v:u:9", "strict_scalar:true:i:s:7"], await Strings(connection, """
            SELECT proname||':'||proisstrict::text||':'||provolatile::text||':'||proparallel::text||':'||procost::text
            FROM pg_proc WHERE oid IN('sql_generation.custom_scalar(integer)'::regprocedure,
                'sql_generation.strict_scalar(integer)'::regprocedure) ORDER BY proname
            """));
        Assert.IsTrue(await Scalar<bool>(connection, """
            SELECT a.prosrc=b.prosrc AND a.probin=b.probin AND a.prosrc=obj_description(a.oid,'pg_proc')
                AND a.prosrc LIKE 'ankus_fn_%_scalar_original'
                AND a.probin=(SELECT probin FROM pg_proc WHERE oid='sql_generation.probe_status()'::regprocedure)
            FROM pg_proc a,pg_proc b WHERE a.oid='sql_generation.custom_scalar(integer)'::regprocedure
                AND b.oid='sql_generation.strict_scalar(integer)'::regprocedure
            """));
        Assert.IsTrue(await Scalar<bool>(connection, """
            SELECT to_regprocedure('sql_generation.scalar_original(integer)') IS NULL
                AND to_regprocedure('sql_generation.empty_original()') IS NULL
                AND to_regprocedure('sql_generation.comment_original()') IS NULL
            """));
    }

    /// <summary>
    /// A replacement SETOF preserves present NULL rows, empty and absent sequences, and strict input suppression.
    /// </summary>
    /// <param name="arguments">The SQL argument values.</param>
    /// <param name="expected">The complete scalar row sequence.</param>
    /// <param name="started">The exact iterator-start count.</param>
    /// <param name="rows">The exact emitted-row count.</param>
    [TestMethod]
    [DataRow("false,3,-1", new[] { "0", "<NULL>", "20" }, 1, 3)]
    [DataRow("false,1,-1", new[] { "0" }, 1, 1)]
    [DataRow("false,0,-1", new string[0], 1, 0)]
    [DataRow("true,3,-1", new string[0], 0, 0)]
    [DataRow("NULL,3,-1", new string[0], 0, 0)]
    public async Task CustomSqlSetFunctionsPreserveRowsAndNulls(string arguments, string[] expected, int started, int rows)
    {
        await using NpgsqlConnection connection = await Open();
        Assert.AreSequenceEqual(expected, await Strings(connection,
            $"SELECT coalesce(value::text,'<NULL>') FROM sql_generation.custom_set({arguments}) WITH ORDINALITY AS result(value,position) ORDER BY position"));
        Assert.AreSequenceEqual([0, started, rows, started, 0, 0, 0, 0, 0], await Status(connection));
        Assert.AreEqual("true:true:17:3", await Scalar<string>(connection, """
            SELECT proretset::text||':'||proisstrict::text||':'||prorows::text||':'||procost::text
            FROM pg_proc WHERE oid='sql_generation.custom_set(boolean,integer,integer)'::regprocedure
            """));
    }

    /// <summary>
    /// ProjectSet early termination disposes the original iterator after exactly one produced row.
    /// </summary>
    [TestMethod]
    public async Task CustomSqlSetEarlyTerminationDisposesIterator()
    {
        await using NpgsqlConnection connection = await Open();
        Assert.AreSequenceEqual(["0"], await Strings(connection, "SELECT sql_generation.custom_set(false,100,-1)::text LIMIT 1"));
        Assert.AreSequenceEqual([0, 1, 1, 1, 0, 0, 0, 0, 0], await Status(connection));
        Assert.AreEqual(12, await Scalar<int>(connection, "SELECT sql_generation.custom_scalar(4)"));
        Assert.AreEqual(connection.ProcessID, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }

    /// <summary>
    /// TABLE replacement names preserve Unicode, nullable fields, row order and materialized iterator cleanup.
    /// </summary>
    [TestMethod]
    public async Task CustomSqlTableFunctionsPreserveColumnsAndCleanup()
    {
        await using NpgsqlConnection connection = await Open();
        Assert.AreSequenceEqual(["1:café ' \\ ; 0", "2:<NULL>", "3:café ' \\ ; 2"], await Strings(connection, """
            SELECT item::text||':'||coalesce(note,'<NULL>') FROM sql_generation.custom_table(3) ORDER BY item
            """));
        Assert.AreSequenceEqual([0, 0, 0, 0, 1, 0, 0, 0, 0], await Status(connection));
        Assert.AreEqual(0L, await Scalar<long>(connection, "SELECT count(*) FROM sql_generation.custom_table(0)"));
        Assert.AreSequenceEqual([0, 0, 0, 0, 2, 0, 0, 0, 0], await Status(connection));
        Assert.AreSequenceEqual(["1:café ' \\ ; 0"], await Strings(connection,
            "SELECT item::text||':'||note FROM sql_generation.custom_table(1)"));
        Assert.AreSequenceEqual([0, 0, 0, 0, 3, 0, 0, 0, 0], await Status(connection));
        Assert.AreEqual("TABLE(item integer, note text)", await Scalar<string>(connection,
            "SELECT pg_get_function_result('sql_generation.custom_table(integer)'::regprocedure)"));
        Assert.IsTrue(await Scalar<bool>(connection, """
            SELECT to_regprocedure('sql_generation.table_original(integer)') IS NULL
                AND to_regprocedure('sql_generation.set_original(boolean,integer,integer)') IS NULL
            """));
    }

    /// <summary>
    /// Custom and ordinary errors leave managed finally blocks complete and the same backend usable.
    /// </summary>
    /// <param name="sql">The failing callback invocation.</param>
    /// <param name="state">The exact SQLSTATE.</param>
    /// <param name="message">The exact primary diagnostic.</param>
    /// <param name="detail">The owned detail or its absence.</param>
    /// <param name="hint">The owned hint or its absence.</param>
    /// <param name="iterator">Whether the failure arises after the iterator yielded a row.</param>
    [TestMethod]
    [DataRow("SELECT sql_generation.custom_scalar(-1)", "P8201", "replacement scalar failed", "replacement detail", "retry a nonnegative value", false)]
    [DataRow("SELECT sql_generation.custom_scalar(-2)", "38000", "ordinary replacement failure", null, null, false)]
    [DataRow("SELECT count(*) FROM sql_generation.custom_set(false,3,1)", "P8202", "replacement iterator failed", "iterator replacement detail", "retry with no failing row", true)]
    public async Task CustomSqlFunctionErrorsRecoverInSameBackend(string sql, string state, string message, string? detail, string? hint, bool iterator)
    {
        await using NpgsqlConnection connection = await Open();
        int process = connection.ProcessID;
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => Execute(connection, sql));
        Assert.AreEqual(state, error.SqlState);
        Assert.AreEqual(message, error.MessageText);
        Assert.AreEqual(detail, error.Detail);
        Assert.AreEqual(hint, error.Hint);
        Assert.AreSequenceEqual(iterator ? [0, 1, 1, 1, 0, 0, 0, 0, 0] : [1, 0, 0, 0, 0, 0, 0, 0, 1], await Status(connection));
        Assert.AreEqual(42, await Scalar<int>(connection, "SELECT sql_generation.custom_scalar(14)"));
        Assert.AreSequenceEqual(["0", "<NULL>", "20"], await Strings(connection,
            "SELECT coalesce(value::text,'<NULL>') FROM sql_generation.custom_set(false,3,-1) WITH ORDINALITY AS result(value,position) ORDER BY position"));
        Assert.AreEqual(process, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }

    /// <summary>
    /// A renamed native trigger replaces values, retains SQL NULL, skips rows and recovers after rejection.
    /// </summary>
    [TestMethod]
    public async Task CustomSqlTriggerFunctionsExecuteAndRecover()
    {
        await using NpgsqlConnection connection = await Open();
        int process = connection.ProcessID;
        await Execute(connection, """
            CREATE TEMP TABLE replacement_rows(id integer,value integer,note text);
            CREATE TRIGGER replacement BEFORE INSERT ON replacement_rows FOR EACH ROW
                EXECUTE FUNCTION sql_generation.custom_trigger('renamed');
            INSERT INTO replacement_rows VALUES(1,2,'hello'),(2,NULL,NULL),(0,99,'skip');
            """);
        Assert.AreSequenceEqual(["1:12:renamed:hello", "2:<NULL>:renamed:<NULL>"], await Strings(connection, """
            SELECT id::text||':'||coalesce(value::text,'<NULL>')||':'||note FROM replacement_rows ORDER BY id
            """));
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() =>
            Execute(connection, "INSERT INTO replacement_rows VALUES(-1,3,'reject')"));
        Assert.AreEqual("P8203", error.SqlState);
        Assert.AreEqual("replacement trigger failed", error.MessageText);
        Assert.AreEqual("trigger replacement detail", error.Detail);
        Assert.AreEqual("retry a positive id", error.Hint);
        Assert.AreEqual(2L, await Scalar<long>(connection, "SELECT count(*) FROM replacement_rows"));
        await Execute(connection, "INSERT INTO replacement_rows VALUES(3,5,'after error')");
        Assert.AreSequenceEqual(["1:12:renamed:hello", "2:<NULL>:renamed:<NULL>", "3:15:renamed:after error"], await Strings(connection, """
            SELECT id::text||':'||coalesce(value::text,'<NULL>')||':'||note FROM replacement_rows ORDER BY id
            """));
        Assert.AreSequenceEqual([0, 0, 0, 0, 0, 5, 0, 0, 0], await Status(connection));
        Assert.AreEqual(process, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
        Assert.IsTrue(await Scalar<bool>(connection, """
            SELECT tgfoid='sql_generation.custom_trigger()'::regprocedure
                AND to_regprocedure('sql_generation.trigger_original()') IS NULL
            FROM pg_trigger WHERE tgrelid='replacement_rows'::regclass AND tgname='replacement'
            """));
    }

    /// <summary>
    /// Renamed event callbacks observe actual DDL and recover after a rejected command without leaking its object.
    /// </summary>
    [TestMethod]
    public Task CustomSqlEventTriggerFunctionsExecuteAndRecover()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(CustomSqlEventTriggerFunctionsExecuteAndRecover), async (connection, transaction, token) =>
        {
            int process = connection.ProcessID;
            await Execute(connection, """
                SELECT sql_generation.probe_reset();
                SET LOCAL ankus.sql_generation_event_error='off';
                CREATE EVENT TRIGGER sql_generation_event ON ddl_command_end WHEN TAG IN('CREATE TABLE','ALTER TABLE')
                    EXECUTE FUNCTION sql_generation.custom_event();
                CREATE TABLE sql_generation.event_subject(id integer);
                ALTER TABLE sql_generation.event_subject ADD COLUMN note text;
                INSERT INTO sql_generation.event_subject VALUES(7,'event value');
                """);
            Assert.AreSequenceEqual(["ddl_command_end:CREATE TABLE", "ddl_command_end:ALTER TABLE"],
                await Scalar<string[]>(connection, "SELECT sql_generation.probe_events()"));
            Assert.AreEqual("7:event value", await Scalar<string>(connection, "SELECT id::text||':'||note FROM sql_generation.event_subject"));
            await Execute(connection, "SAVEPOINT event_failure; SET LOCAL ankus.sql_generation_event_error='on'");
            PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() =>
                Execute(connection, "CREATE TABLE sql_generation.event_rejected(id integer)"));
            Assert.AreEqual("P8204", error.SqlState);
            Assert.AreEqual("replacement event failed", error.MessageText);
            Assert.AreEqual("event replacement detail", error.Detail);
            Assert.AreEqual("retry without the event fault", error.Hint);
            await Execute(connection, "ROLLBACK TO SAVEPOINT event_failure; RELEASE SAVEPOINT event_failure");
            Assert.IsTrue(await Scalar<bool>(connection, "SELECT to_regclass('sql_generation.event_rejected') IS NULL"));
            await Execute(connection, "CREATE TABLE sql_generation.event_recovered(id integer)");
            Assert.IsTrue(await Scalar<bool>(connection, "SELECT to_regclass('sql_generation.event_recovered') IS NOT NULL"));
            Assert.AreSequenceEqual(["ddl_command_end:CREATE TABLE", "ddl_command_end:ALTER TABLE",
                "ddl_command_end:CREATE TABLE", "ddl_command_end:CREATE TABLE"],
                await Scalar<string[]>(connection, "SELECT sql_generation.probe_events()"));
            Assert.AreSequenceEqual([0, 0, 0, 0, 0, 0, 4, 0, 0], await Status(connection));
            await using var command = new NpgsqlCommand("""
                SELECT evtfoid='sql_generation.custom_event()'::regprocedure
                    AND to_regprocedure('sql_generation.event_original()') IS NULL
                    AND pg_backend_pid()=$1 FROM pg_event_trigger WHERE evtname='sql_generation_event'
                """, connection, transaction);
            command.Parameters.AddWithValue(process);
            Assert.IsTrue(Assert.IsInstanceOfType<bool>(await command.ExecuteScalarAsync(token)));
        }, context.CancellationToken);

    /// <summary>
    /// One replacement helper serves shared roles, and disabled helper SQL does not suppress its aggregate.
    /// </summary>
    [TestMethod]
    public async Task CustomSqlAggregateHelpersExecuteIndependentlyOfParents()
    {
        await using NpgsqlConnection connection = await Open();
        Assert.AreEqual(6, await Scalar<int>(connection,
            "SELECT sql_generation.custom_sum(value) FROM(VALUES(1),(NULL),(2),(3)) rows(value)"));
        Assert.AreSequenceEqual([0, 0, 0, 0, 0, 0, 0, 3, 0], await Status(connection));
        Assert.AreEqual(0, await Scalar<int>(connection, "SELECT sql_generation.custom_sum(value) FROM(SELECT 1 AS value WHERE false) rows"));
        Assert.AreEqual(306, await Scalar<int>(connection,
            "SELECT sql_generation.supplied_sum(value) FROM(VALUES(1),(NULL),(2),(3)) rows(value)"));
        Assert.AreSequenceEqual([0, 0, 0, 0, 0, 0, 0, 3, 0], await Status(connection));
        Assert.IsTrue(await Scalar<bool>(connection, """
            SELECT aggtransfn=aggcombinefn AND aggtransfn='sql_generation.shared_step(integer,integer)'::regprocedure
                AND (SELECT count(*) FROM pg_proc WHERE pronamespace='sql_generation'::regnamespace AND proname='shared_step')=1
            FROM pg_aggregate WHERE aggfnoid='sql_generation.custom_sum(integer)'::regprocedure
            """));
        Assert.AreSequenceEqual(["shared_step:c:4", "supplied_step:sql:100"], await Strings(connection, """
            SELECT p.proname||':'||l.lanname||':'||p.procost::text FROM pg_proc p JOIN pg_language l ON l.oid=p.prolang
            WHERE p.oid IN('sql_generation.shared_step(integer,integer)'::regprocedure,
                'sql_generation.supplied_step(integer,integer)'::regprocedure) ORDER BY p.proname
            """));
        Assert.IsTrue(await Scalar<bool>(connection, """
            SELECT aggtransfn='sql_generation.supplied_step(integer,integer)'::regprocedure
            FROM pg_aggregate WHERE aggfnoid='sql_generation.supplied_sum(integer)'::regprocedure
            """));
    }

    /// <summary>
    /// A literal replaces attached operator and cast SQL together, including names, options and exact nominal identities.
    /// </summary>
    [TestMethod]
    public async Task CustomSqlOperatorAndCastBundlesExecuteOnce()
    {
        await using NpgsqlConnection connection = await Open();
        int process = connection.ProcessID;
        Assert.AreEqual(103, await Scalar<int>(connection, "SELECT OPERATOR(sql_generation.##) 'One'::sql_generation.override_token"));
        Assert.AreEqual(109, await Scalar<int>(connection, "SELECT 'Two'::sql_generation.override_token::integer"));
        Assert.IsTrue(await Scalar<bool>(connection, """
            SELECT (OPERATOR(sql_generation.##) 'Empty'::sql_generation.override_token) IS NULL
                AND (NULL::sql_generation.override_token::integer) IS NULL
                AND (OPERATOR(sql_generation.##) NULL::sql_generation.override_token) IS NULL
            """));
        await Execute(connection, "CREATE TEMP TABLE assigned_token(value integer); INSERT INTO assigned_token SELECT 'Two'::sql_generation.override_token");
        Assert.AreEqual(109, await Scalar<int>(connection, "SELECT value FROM assigned_token"));
        Assert.AreEqual("a:f:true", await Scalar<string>(connection, """
            SELECT castcontext::text||':'||castmethod::text||':'||(castfunc='sql_generation.custom_convert(sql_generation.override_token)'::regprocedure)::text
            FROM pg_cast WHERE castsource='sql_generation.override_token'::regtype AND casttarget='integer'::regtype
            """));
        Assert.IsTrue(await Scalar<bool>(connection, """
            SELECT oprleft=0 AND oprright='sql_generation.override_token'::regtype AND oprresult='integer'::regtype
                AND oprcode='sql_generation.custom_convert(sql_generation.override_token)'::regprocedure
                AND NOT EXISTS(SELECT 1 FROM pg_operator WHERE oprnamespace='sql_generation'::regnamespace AND oprname='!@')
                AND to_regprocedure('sql_generation.declared_convert(sql_generation.override_token)') IS NULL
            FROM pg_operator WHERE oprnamespace='sql_generation'::regnamespace AND oprname='##'
            """));
        PostgresException implicitCall = await Assert.ThrowsExactlyAsync<PostgresException>(() =>
            Execute(connection, "SELECT sql_generation.custom_scalar('One'::sql_generation.override_token)"));
        Assert.AreEqual("42883", implicitCall.SqlState);
        PostgresException invalid = await Assert.ThrowsExactlyAsync<PostgresException>(() =>
            Execute(connection, "SELECT 'Invalid'::sql_generation.override_token::integer"));
        Assert.AreEqual("P8205", invalid.SqlState);
        Assert.AreEqual("replacement cast failed", invalid.MessageText);
        Assert.AreEqual(103, await Scalar<int>(connection, "SELECT 'One'::sql_generation.override_token::integer"));
        Assert.AreEqual(process, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }

    /// <summary>
    /// PostgreSQL owns the exact custom functions, parent aggregates, type, operator and cast as extension members.
    /// </summary>
    [TestMethod]
    public async Task CustomSqlFunctionObjectsAreExtensionMembers()
    {
        await using NpgsqlConnection connection = await Open();
        Assert.AreSequenceEqual(["cast:true", "custom_convert:true", "custom_event:true", "custom_scalar:true",
            "custom_set:true", "custom_sum:true", "custom_table:true", "custom_trigger:true", "operator:true",
            "shared_step:true", "strict_scalar:true", "supplied_step:true", "supplied_sum:true", "type:true"],
            await Strings(connection, """
                WITH expected(label,classid,objid) AS (
                    SELECT proname::text,'pg_proc'::regclass,oid FROM pg_proc WHERE pronamespace='sql_generation'::regnamespace
                        AND proname IN('custom_scalar','strict_scalar','custom_set','custom_table','custom_trigger','custom_event',
                            'shared_step','custom_sum','supplied_step','supplied_sum','custom_convert')
                    UNION ALL SELECT 'type','pg_type'::regclass,'sql_generation.override_token'::regtype::oid
                    UNION ALL SELECT 'operator','pg_operator'::regclass,oid FROM pg_operator
                        WHERE oprnamespace='sql_generation'::regnamespace AND oprname='##'
                    UNION ALL SELECT 'cast','pg_cast'::regclass,oid FROM pg_cast
                        WHERE castsource='sql_generation.override_token'::regtype AND casttarget='integer'::regtype)
                SELECT label||':'||EXISTS(SELECT 1 FROM pg_depend d JOIN pg_extension e ON e.oid=d.refobjid
                    WHERE d.refclassid='pg_extension'::regclass AND d.classid=x.classid AND d.objid=x.objid
                        AND d.deptype='e' AND e.extname='ankus_test')::text
                FROM expected x ORDER BY label COLLATE "C"
                """));
    }

    /// <summary>
    /// Opens an independent backend and resets its managed observations.
    /// </summary>
    private async Task<NpgsqlConnection> Open()
    {
        NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        try
        {
            await Execute(connection, "SELECT sql_generation.probe_reset()");
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// Reads all independent lifecycle counters after a completed statement.
    /// </summary>
    private Task<int[]> Status(NpgsqlConnection connection) => Scalar<int[]>(connection, "SELECT sql_generation.probe_status()");

    /// <summary>
    /// Executes statements whose effects are asserted separately.
    /// </summary>
    private async Task Execute(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(context.CancellationToken);
    }

    /// <summary>
    /// Reads one exact typed scalar value.
    /// </summary>
    private async Task<T> Scalar<T>(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return Assert.IsInstanceOfType<T>(await command.ExecuteScalarAsync(context.CancellationToken));
    }

    /// <summary>
    /// Reads the entire ordered text result without removing duplicates or NULL witnesses.
    /// </summary>
    private async Task<string[]> Strings(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(context.CancellationToken);
        var rows = new List<string>();
        while (await reader.ReadAsync(context.CancellationToken))
        {
            rows.Add(reader.GetString(0));
        }

        return [.. rows];
    }
}
