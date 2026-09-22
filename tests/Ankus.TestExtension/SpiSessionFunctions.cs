namespace Ankus.TestExtension;

/// <summary>
/// Exercises scoped SPI connections, borrowed plans, nesting, and cleanup through ordinary attributed functions.
/// </summary>
public static class SpiSessionFunctions
{
    private static SpiSession? s_session;
    private static SpiPreparedStatement? s_statement;

    /// <summary>
    /// Reuses a session and its plan, then reads materialized values after both scopes have ended.
    /// </summary>
    /// <param name="text">The nullable text parameter.</param>
    /// <returns>The independent text result.</returns>
    [PgFunction]
    public static string? SessionOwnedResult(string? text)
    {
        SpiResult rows = Spi.Connect(session =>
        {
            using SpiPreparedStatement statement = session.Prepare("SELECT $1::text AS value", typeof(string));
            statement.Query(SpiParameter.Create("discard"));
            return statement.Query(SpiParameter.Create(text));
        });
        Spi.Execute("SELECT repeat('overwrite', 10000)");
        return rows[0].Get<string?>("value");
    }

    /// <summary>
    /// Demonstrates one connection per session and restores an outer session after a nested callback.
    /// </summary>
    /// <returns>The observed connection counts and outer-scope access rejection.</returns>
    [PgFunction]
    public static string SessionNested()
        => Spi.Connect(outer =>
        {
            const string sql = "SELECT count(*) FROM pg_backend_memory_contexts WHERE name = 'SPI Proc'";
            long before = outer.ExecuteScalar<long>(sql);
            string nested = Spi.Connect(inner =>
            {
                long count = inner.ExecuteScalar<long>(sql);
                try
                {
                    outer.Execute("SELECT 1");
                    return "unexpected success";
                }
                catch (InvalidOperationException)
                {
                    return count + ":rejected";
                }
            });
            long after = outer.ExecuteScalar<long>(sql);
            return before + "|" + nested + "|" + after;
        });

    /// <summary>
    /// Retains a reference to a scoped session to verify later callback access is rejected.
    /// </summary>
    [PgFunction]
    public static void SessionEscape() => Spi.Connect(session => s_session = session);

    /// <summary>
    /// Attempts to execute through a previously retained session reference.
    /// </summary>
    /// <returns>The query result if the scope is still accessible.</returns>
    [PgFunction]
    public static int SessionEscapedValue() => CachedSession().ExecuteScalar<int>("SELECT 42");

    /// <summary>
    /// Stores a session-bound or explicitly retained plan for a later callback.
    /// </summary>
    /// <param name="keep">Whether to retain the plan before the session ends.</param>
    [PgFunction]
    public static void SessionCachePlan(bool keep)
    {
        s_statement?.Dispose();
        s_statement = Spi.Connect(session =>
        {
            SpiPreparedStatement statement = session.Prepare("SELECT $1 + 2", typeof(int));
            if (keep)
            {
                statement.Keep();
                if (!ReferenceEquals(statement, statement.Keep()))
                {
                    throw new InvalidOperationException("Keep must preserve the statement owner.");
                }
            }

            return statement;
        });
    }

    /// <summary>
    /// Executes a cached statement after its original session has closed.
    /// </summary>
    /// <param name="value">The integer parameter.</param>
    /// <returns>The statement result.</returns>
    [PgFunction]
    public static int SessionCachedValue(int value) => CachedStatement().ExecuteScalar<int>(SpiParameter.Create(value));

    /// <summary>
    /// Attempts to retain a plan after its original scope has expired.
    /// </summary>
    [PgFunction]
    public static void SessionKeepExpired() => CachedStatement().Keep();

    /// <summary>
    /// Disposes a cached plan, including an already-expired borrowed plan.
    /// </summary>
    [PgFunction]
    public static void SessionDisposePlan() => s_statement?.Dispose();

    /// <summary>
    /// Verifies recovery leaves a session and its borrowed plan usable after a failed command.
    /// </summary>
    /// <returns>The rolled-back error's SQLSTATE and successful plan result.</returns>
    [PgFunction]
    public static string SessionRecover()
        => Spi.Connect(session =>
        {
            session.Execute("CREATE TEMP TABLE session_values (value int)");
            session.Execute("INSERT INTO session_values VALUES (40)");
            using SpiPreparedStatement statement = session.Prepare("SELECT sum(value)::int + $1 FROM session_values", typeof(int));
            try
            {
                session.Execute("INSERT INTO session_values VALUES (99); SELECT 1 / 0");
                return "unexpected success";
            }
            catch (PgException exception)
            {
                return exception.SqlState + ":" + statement.ExecuteScalar<int>(SpiParameter.Create(2));
            }
        });

    /// <summary>
    /// Verifies failure of a nested session restores the outer connection, resource owner, and borrowed plan.
    /// </summary>
    /// <returns>The recovered state and outer plan result.</returns>
    [PgFunction]
    public static string SessionNestedRecovery()
        => Spi.Connect(outer =>
        {
            using SpiPreparedStatement statement = outer.Prepare("SELECT 42");
            try
            {
                Spi.Connect(inner =>
                {
                    inner.Prepare("SELECT 1");
                    inner.Execute("SELECT 1 / 0");
                });
                return "unexpected success";
            }
            catch (PgException exception)
            {
                return exception.SqlState + ":" + statement.ExecuteScalar<int>();
            }
        });

    /// <summary>
    /// Verifies a borrowed plan follows PostgreSQL invalidation after its relation is replaced within the session.
    /// </summary>
    /// <returns>The result of the replanned query.</returns>
    [PgFunction]
    public static int SessionReplan()
        => Spi.Connect(session =>
        {
            session.Execute("CREATE TEMP TABLE session_replan (value int); INSERT INTO session_replan VALUES (1)");
            using SpiPreparedStatement statement = session.Prepare("SELECT value FROM session_replan");
            if (statement.ExecuteScalar<int>() != 1)
            {
                throw new InvalidOperationException("Unexpected original value.");
            }

            session.Execute("DROP TABLE session_replan; CREATE TEMP TABLE session_replan (value int); " +
                "INSERT INTO session_replan VALUES (42)");
            return statement.ExecuteScalar<int>();
        });

    /// <summary>
    /// Verifies session command counts and complete write effects when scalar materialization reads only the first cell.
    /// </summary>
    /// <returns>The command count, scalar, and final ordered values.</returns>
    [PgFunction]
    public static string SessionWrites()
        => Spi.Connect(session =>
        {
            session.Execute("CREATE TEMP TABLE session_writes (value int)");
            long count = session.Execute("INSERT INTO session_writes VALUES (1), (2)");
            int first = session.ExecuteScalar<int>("INSERT INTO session_writes SELECT generate_series(3, 5) RETURNING value");
            SpiResult rows = session.Query("SELECT value FROM session_writes ORDER BY value");
            return count + "|" + first + "|" + string.Join(',', rows.Select(static row => row.Get<int>(0)));
        });

    /// <summary>
    /// Verifies a caught managed callback exception closes its session while preserving already successful commands.
    /// </summary>
    /// <returns>The persisted value visible in the enclosing transaction.</returns>
    [PgFunction]
    public static int SessionManagedFailure()
    {
        try
        {
            Spi.Connect(session =>
            {
                session.Execute("CREATE TEMP TABLE session_managed (value int); INSERT INTO session_managed VALUES (42)");
                session.Prepare("SELECT value FROM session_managed");
                throw new InvalidOperationException("callback failure");
            });
        }
        catch (InvalidOperationException exception) when (exception.Message == "callback failure")
        {
            return Spi.ExecuteScalar<int>("SELECT value FROM session_managed");
        }

        return -1;
    }

    /// <summary>
    /// Opens a cursor from session SQL or a borrowed plan and fetches after the native session closes.
    /// </summary>
    /// <param name="prepared">Whether to open from a borrowed plan.</param>
    /// <returns>The cursor's ordered values.</returns>
    [PgFunction]
    public static string SessionCursor(bool prepared)
    {
        using SpiCursor cursor = Spi.Connect(session =>
        {
            const string sql = "SELECT generate_series(1, $1)";
            return prepared
                ? session.Prepare(sql, typeof(int)).OpenCursor(SpiParameter.Create(3))
                : session.OpenCursor(sql, readOnly: true, SpiParameter.Create(3));
        });
        return string.Join(',', cursor.Fetch(10).Select(static row => row.Get<int>(0)));
    }

    /// <summary>
    /// Rejects worker-thread access without disturbing the session's connection or plan.
    /// </summary>
    /// <param name="mode">Zero executes a query; one, two, and three execute, retain, or dispose a plan.</param>
    /// <returns>The rejection message and a successful backend result.</returns>
    [PgFunction]
    public static string SessionWorker(int mode)
        => Spi.Connect(session =>
        {
            using SpiPreparedStatement statement = session.Prepare("SELECT 42");
            string diagnostic = Task.Run(() =>
            {
                try
                {
                    switch (mode)
                    {
                        case 0: session.Execute("SELECT 1"); break;
                        case 1: statement.Execute(); break;
                        case 2: statement.Keep(); break;
                        case 3: statement.Dispose(); break;
                        default: throw new ArgumentOutOfRangeException(nameof(mode));
                    }

                    return "unexpected success";
                }
                catch (InvalidOperationException exception)
                {
                    return exception.Message;
                }
            }).GetAwaiter().GetResult();
            return diagnostic + "|" + statement.ExecuteScalar<int>();
        });

    /// <summary>
    /// Calls another backend function while a session is active, then verifies the outer connection is still usable.
    /// </summary>
    /// <returns>The recursive access rejection and the follow-up result.</returns>
    [PgFunction]
    public static string SessionRecursive()
        => Spi.Connect(session =>
        {
            s_session = session;
            string rejection = session.ExecuteScalar<string>("SELECT datatype.session_recursive_access()");
            return rejection + "|" + session.ExecuteScalar<int>("SELECT 42");
        });

    /// <summary>
    /// Attempts to reuse an outer callback's native SPI frame and opens an independent nested session instead.
    /// </summary>
    /// <returns>The ownership diagnostic and nested result.</returns>
    [PgFunction]
    public static string SessionRecursiveAccess()
    {
        try
        {
            CachedSession().Execute("SELECT 1");
            return "unexpected success";
        }
        catch (InvalidOperationException exception)
        {
            int value = Spi.Connect(session => session.ExecuteScalar<int>("SELECT 7"));
            return exception.Message + ":" + value;
        }
    }

    /// <summary>
    /// Verifies read-only execution, row limits, and plan parameter validation through a scoped connection.
    /// </summary>
    /// <param name="mode">Selects a PostgreSQL or managed failure.</param>
    [PgFunction]
    public static void SessionFail(int mode)
        => Spi.Connect(session =>
        {
            session.Prepare("SELECT 42");
            switch (mode)
            {
                case 0: session.Execute("SELECT 1 / 0"); break;
                case 1: session.Execute("SELECT pg_sleep(5)"); break;
                case 2: session.Query("CREATE TEMP TABLE forbidden (value int)", readOnly: true, limit: 0); break;
                case 3: session.Prepare("SELECT $1", typeof(int)).Execute(SpiParameter.Create("bad")); break;
                case 4: session.Query("SELECT 1", readOnly: false, limit: -1); break;
                case 5: session.Prepare("SELECT missing_column"); break;
                default: throw new ArgumentOutOfRangeException(nameof(mode));
            }
        });

    /// <summary>
    /// Exercises repeated materialization without accumulating SPI tuple tables within a long-lived session.
    /// </summary>
    /// <returns>The returned row count, retained tuple-context count, and final scalar.</returns>
    [PgFunction]
    public static string SessionTupleCleanup()
        => Spi.Connect(session =>
        {
            SpiResult rows = session.Query("SELECT generate_series(1, 5)", readOnly: true, limit: 2);
            for (int index = 0; index < 100; index++)
            {
                session.Query("SELECT repeat('large value', 1000) FROM generate_series(1, 10)");
            }

            long contexts = session.ExecuteScalar<long>("SELECT count(*) FROM pg_backend_memory_contexts WHERE name = 'SPI TupTable'");
            return rows.Count + ":" + contexts + ":" + session.ExecuteScalar<int>("SELECT 42");
        });

    private static SpiSession CachedSession() => s_session ?? throw new InvalidOperationException("No cached session.");

    private static SpiPreparedStatement CachedStatement() => s_statement ?? throw new InvalidOperationException("No cached statement.");
}
