using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Compares real relation references and regclass conversions against independently queried PostgreSQL behavior.
/// </summary>
/// <param name="context">The test cancellation context.</param>
[TestClass]
public sealed class RelationTests(TestContext context)
{
    /// <summary>
    /// Native metadata matches pg_class for tables, indexes, sequences, views, composites, partitioned objects and TOAST.
    /// </summary>
    [TestMethod]
    public Task MetadataMatchesCatalog()
        => CheckAsync("""
            CREATE SCHEMA "relation 名";
            CREATE TABLE "relation 名"."café 🐘" (id int PRIMARY KEY, payload text);
            CREATE VIEW "relation 名".v AS SELECT 1 AS x;
            CREATE MATERIALIZED VIEW "relation 名".m AS SELECT 1 AS x;
            CREATE SEQUENCE "relation 名".s;
            CREATE TYPE "relation 名".c AS (x int);
            CREATE TABLE "relation 名".p (x int PRIMARY KEY) PARTITION BY RANGE(x);
            CREATE FOREIGN DATA WRAPPER relation_fdw;
            CREATE SERVER relation_server FOREIGN DATA WRAPPER relation_fdw;
            CREATE FOREIGN TABLE "relation 名".f (x int) SERVER relation_server;
            SELECT bool_and(relations.describe(c.oid::regclass) = ARRAY[c.oid::text,c.relname::text,c.relnamespace::text,n.nspname::text,
              c.relkind::text,nullif(c.reltuples,0)::text,
              (c.relkind='r')::int::text || (c.relkind='m')::int::text || (c.relkind='i')::int::text ||
              (c.relkind='v')::int::text || (c.relkind='S')::int::text || (c.relkind='c')::int::text ||
              (c.relkind='f')::int::text || (c.relkind='p')::int::text || (c.relkind='t')::int::text])
            FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
            WHERE n.nspname='relation 名' OR c.oid IN
              (SELECT reltoastrelid FROM pg_class WHERE relnamespace='"relation 名"'::regnamespace AND reltoastrelid<>0)
            """, true);

    /// <summary>
    /// Native search paths, quoted Unicode names, missing names and ordinary metadata reads survive lock release.
    /// </summary>
    [TestMethod]
    public Task NamesResolveAndErrorsRecover()
        => CheckAsync("""
            CREATE SCHEMA "relation 名";
            CREATE TABLE "relation 名"."café 🐘" (x int);
            SET LOCAL search_path="relation 名",public;
            SELECT relations.describe_name('"café 🐘"',1) = relations.describe('"café 🐘"'::regclass)
              AND relations.describe_name('missing_relation_name',1) IS NULL
              AND relations.missing(4294967295::oid) = 'XX000|True|42'
            """, true);

    /// <summary>
    /// Every native relation lock appears while held and is explicitly released when the wrapper is disposed.
    /// </summary>
    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    [DataRow(5)]
    [DataRow(6)]
    [DataRow(7)]
    [DataRow(8)]
    public Task LocksFollowDisposal(int mode)
        => CheckAsync($"SELECT relations.lock_cycle('tests.rollback_probe'::regclass::oid,{mode}) = ARRAY[false,true,false]", true);

    /// <summary>
    /// Scalar and array conversions preserve exact regclass identity, SQL NULL and multidimensional lower bounds.
    /// </summary>
    [TestMethod]
    public Task RegclassRoundTrips()
        => CheckAsync("""
            SELECT relations.identity(NULL::regclass) IS NULL
              AND relations.identity('pg_class'::regclass) = 'pg_class'::regclass
              AND pg_typeof(relations.identity('pg_class'::regclass)) = 'regclass'::regtype
              AND relations.vector(ARRAY['pg_class'::regclass,NULL,'pg_type'::regclass]) = ARRAY['pg_class'::regclass,NULL,'pg_type'::regclass]
              AND relations.shaped('[0:1][-2:-1]={{pg_class,NULL},{pg_type,pg_proc}}'::regclass[]) =
                  '[0:1][-2:-1]={{pg_class,NULL},{pg_type,pg_proc}}'::regclass[]
              AND relations.spi_and_raw('pg_class'::regclass) = format('%s|%s|True|%s|%s|2205',
                'pg_class'::regclass::oid,'pg_class'::regclass::oid,'pg_class'::regclass::oid,'pg_class'::regclass::oid)
            """, true);

    /// <summary>
    /// Edited SPI rows and composite fields preserve scalar and array identities after the supplied reference closes.
    /// </summary>
    [TestMethod]
    public Task EditedCellsRemainDetached()
        => CheckAsync("SELECT relations.detached_cells('pg_class'::regclass::oid)", true);

    /// <summary>
    /// Index enumeration and copied physical descriptors match independent catalog identities and dropped columns.
    /// </summary>
    [TestMethod]
    public Task IndexesAndDescriptorsRemainExact()
        => CheckAsync("""
            CREATE TABLE relation_items (id int PRIMARY KEY, removed int, payload text);
            ALTER TABLE relation_items DROP COLUMN removed;
            CREATE INDEX relation_payload ON relation_items(payload);
            SELECT relations.indices('relation_items'::regclass::oid) =
              (SELECT array_agg(indexrelid ORDER BY indexrelid) FROM pg_index WHERE indrelid='relation_items'::regclass)
              AND (relations.descriptor('relation_items'::regclass::oid)->>'TypeOid')::oid =
                (SELECT reltype FROM pg_class WHERE oid='relation_items'::regclass)
              AND jsonb_array_length(relations.descriptor('relation_items'::regclass::oid)->'Attributes') = 3
              AND (relations.descriptor('relation_items'::regclass::oid)->'Attributes'->1->>'IsDropped')::boolean
            """, true);

    /// <summary>
    /// Relation inputs survive repeated set callbacks and SPI operations, and returned references convert before release.
    /// </summary>
    [TestMethod]
    public Task IteratorsRetainArgumentsAndReleaseResults()
        => CheckAsync("""
            SELECT (SELECT array_agg(value) FROM relations.names('pg_class'::regclass,3) value) = ARRAY['pg_class0','pg_class1','pg_class2']
              AND (SELECT array_agg(value) FROM relations.values('pg_class'::regclass::oid) value) = ARRAY['pg_class'::regclass,NULL,'pg_class'::regclass]
              AND relations.disposals() = 1
            """, true);

    /// <summary>
    /// LIMIT and explicit portal closure dispose the iterator and release its retained relation lock.
    /// </summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task EarlyIteratorCleanupReleasesArguments(bool portal)
        => CheckAsync("CREATE TABLE relation_iterator(x int); " +
            (portal
                ? "DECLARE relation_cursor CURSOR FOR SELECT relations.names('relation_iterator'::regclass,100); FETCH 1 FROM relation_cursor; CLOSE relation_cursor; "
                : "SELECT relations.names('relation_iterator'::regclass,100) LIMIT 1; ") +
            "SELECT relations.disposals() = 1 AND NOT EXISTS (SELECT FROM pg_locks WHERE pid=pg_backend_pid() AND relation='relation_iterator'::regclass AND mode='AccessShareLock')", true);

    /// <summary>
    /// The materialized set path keeps input references live through its last advance and releases them after draining.
    /// </summary>
    [TestMethod]
    public Task MaterializedIteratorRetainsArguments()
        => CheckAsync("""
            CREATE TABLE relation_materialized(x int);
            SELECT (SELECT array_agg(value) FROM relations.materialized_names('relation_materialized'::regclass,3) value) =
              ARRAY['relation_materialized0','relation_materialized1','relation_materialized2'] AND relations.disposals() = 1;
            SELECT NOT EXISTS (SELECT FROM pg_locks WHERE pid=pg_backend_pid() AND relation='relation_materialized'::regclass AND mode='AccessShareLock')
            """, true);

    /// <summary>
    /// Closing the native statement owner invalidates an undisposed retained cache reference before its next callback.
    /// </summary>
    [TestMethod]
    public Task ResourceOwnerInvalidatesRetainedHandles()
        => CheckAsync("SELECT relations.retain('pg_class'::regclass::oid); SELECT relations.expired()", "55000");

    /// <summary>
    /// Native reference counts distinguish borrowed, adopted and transferred close obligations.
    /// </summary>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public Task NativeReferencesFollowOwnership(int mode)
        => CheckAsync($"SELECT tests.relation_borrow('relations.borrow(bigint,integer)'::regprocedure,'pg_class'::regclass::oid,{mode})", true);

    /// <summary>
    /// Every helper modifies exactly the backend-local counter prescribed by PostgreSQL, including signed large counts.
    /// </summary>
    [TestMethod]
    public Task StatisticsMatchNativeCounters()
        => CheckAsync("CREATE TABLE relation_stats (x int); SELECT relations.counters('relation_stats'::regclass::oid) = ARRAY[2,4999999994,1,1,1]::bigint[]", true);

    /// <summary>
    /// Statistics helpers remain no-ops for relations opened with statistics collection disabled.
    /// </summary>
    [TestMethod]
    public Task DisabledStatisticsStayDisabled()
        => CheckAsync("SET LOCAL track_counts=off; CREATE TABLE relation_no_stats (x int); SELECT relations.counters('relation_no_stats'::regclass::oid) = ARRAY[0,0,0,0,0]::bigint[]", true);

    /// <summary>
    /// A retained reference observes relcache invalidation instead of returning metadata cached when it opened.
    /// </summary>
    [TestMethod]
    public Task MetadataRemainsLive()
        => CheckAsync("CREATE TABLE relation_live_before (x int); SELECT relations.live_metadata('relation_live_before'::regclass::oid) = ARRAY['relation_live_before','relation_live_after','1.25']", true);

    /// <summary>
    /// Name lookup follows native namespace permissions while relation inspection does not invent a SELECT requirement.
    /// </summary>
    [TestMethod]
    public Task NativePermissionsAndRetry()
        => CheckAsync("""
            CREATE ROLE relation_reader;
            GRANT USAGE ON SCHEMA relations TO relation_reader;
            CREATE SCHEMA relation_private;
            CREATE TABLE relation_private.t (x int);
            SET LOCAL ROLE relation_reader;
            SELECT relations.name_error('relation_private.t') = '42501';
            RESET ROLE;
            GRANT USAGE ON SCHEMA relation_private TO relation_reader;
            SET LOCAL ROLE relation_reader;
            SELECT (relations.describe_name('relation_private.t',1))[2] = 't'
            """, true);

    /// <summary>
    /// Returning an input repeatedly keeps it live across rows and closes independent cloned output values.
    /// </summary>
    [TestMethod]
    public Task RepeatedArgumentsStayLive()
        => CheckAsync("""
            SELECT array_agg(relation) = ARRAY['pg_class'::regclass,'pg_class'::regclass]
              AND count(*) = 2 AND bool_and(many[1]='pg_class'::regclass)
            FROM relations.repeated('pg_class'::regclass)
            """, true);

    /// <summary>
    /// A failed later SPI conversion closes both scalar and array relation references before retrying in the same callback.
    /// </summary>
    [TestMethod]
    public Task PartialSpiConversionReleasesReferences()
        => CheckAsync("SELECT relations.spi_partial('tests.rollback_probe'::regclass::oid)", true);

    /// <summary>
    /// A native commit callback fails after acquisition has escaped the guard's owner; recovery closes the pin and lock.
    /// </summary>
    [TestMethod]
    public Task FailedGuardCommitReleasesPublishedAcquisition()
        => CheckAsync("SELECT tests.relation_commit_fault('relations.commit_fault(oid)'::regprocedure,'tests.rollback_probe'::regclass::oid)", true);

    /// <summary>
    /// Failed native commits cannot consume a caller's external reference during adoption or borrowed-to-owned transfer.
    /// </summary>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    public Task FailedTransferPreservesExternalOwnership(int mode)
        => CheckAsync($"SELECT tests.relation_transfer_fault('relations.transfer_fault(bigint,integer)'::regprocedure,'tests.rollback_probe'::regclass::oid,{mode})", true);

    /// <summary>
    /// Real resource-owner commit and abort reclaim native references and invalidate checked managed handles.
    /// </summary>
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public Task ResourceOwnerCommitAndAbortInvalidateHandles(bool commit)
        => CheckAsync($"""
            SELECT tests.relation_owner('relations.retain(oid)'::regprocedure,'tests.rollback_probe'::regclass::oid,{(commit ? "true" : "false")});
            SELECT relations.expired() = '55000'
            """, true);

    /// <summary>
    /// Relation inputs and state reopen per aggregate callback, preserve NULL, and transfer only their regclass datum.
    /// </summary>
    [TestMethod]
    public Task AggregateRelationsPreserveStateAndNull()
        => CheckAsync("""
            SELECT relations.last_relation(value ORDER BY ordinal) = 'pg_class'::regclass
            FROM (VALUES (1,'pg_type'::regclass),(2,NULL),(3,'pg_class'::regclass),(4,NULL)) input(ordinal,value);
            SELECT relations.last_relation(NULL::regclass) IS NULL;
            SELECT relations.last_relation(value) IS NULL FROM (SELECT 'pg_type'::regclass AS value WHERE false) empty_input
            """, true);

    /// <summary>
    /// Partial input conversion and aborted set enumeration both unwind native references and preserve the backend session.
    /// </summary>
    [TestMethod]
    [DataRow("SELECT relations.pair('tests.rollback_probe'::regclass,4294967295::oid::regclass)", "XX000", 0)]
    [DataRow("SELECT relations.vector(ARRAY['tests.rollback_probe'::regclass,4294967295::oid::regclass])", "XX000", 0)]
    [DataRow("SELECT * FROM relations.fail_names('tests.rollback_probe'::regclass)", "22012", 1)]
    public Task ConversionAndIteratorErrorsRecover(string sql, string state, int disposals)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(ConversionAndIteratorErrorsRecover), async (connection, transaction, token) =>
        {
            int backend = connection.ProcessID;
            await using var command = new NpgsqlCommand("SELECT relations.disposals()", connection, transaction);
            _ = await command.ExecuteScalarAsync(token);
            await transaction.SaveAsync("relation_error", token);
            if (state == "XX000")
            {
                // Npgsql treats class XX as a broken connection. Catch the exact native error in PostgreSQL
                // so this test can observe backend recovery independently of that client policy.
                command.CommandText = $"""
                    DO $relation$ BEGIN
                      PERFORM {sql[7..]};
                      RAISE EXCEPTION 'Expected a missing relation error';
                    EXCEPTION WHEN SQLSTATE 'XX000' THEN
                      IF SQLERRM <> 'could not open relation with OID 4294967295' THEN RAISE; END IF;
                    END $relation$
                    """;
                await command.ExecuteNonQueryAsync(token);
            }
            else
            {
                command.CommandText = sql;
                PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteNonQueryAsync(token));
                Assert.AreEqual(state, error.SqlState);
            }

            await transaction.RollbackAsync("relation_error", token);
            command.CommandText = "SELECT relations.disposals()";
            Assert.AreEqual(disposals, await command.ExecuteScalarAsync(token));
            command.CommandText = "SELECT count(*)::int FROM pg_locks WHERE pid=pg_backend_pid() AND relation='tests.rollback_probe'::regclass";
            Assert.AreEqual(0, await command.ExecuteScalarAsync(token));
            command.CommandText = "SELECT 42";
            Assert.AreEqual(42, await command.ExecuteScalarAsync(token));
            Assert.AreEqual(backend, connection.ProcessID);
        }, context.CancellationToken);

    /// <summary>
    /// An independent backend is blocked by a retained relation lock and can acquire its conflicting lock after disposal.
    /// </summary>
    [TestMethod]
    [DataRow(1)]
    [DataRow(8)]
    public Task ConcurrentLocksBlockAndRecover(int mode)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(ConcurrentLocksBlockAndRecover), async (connection, transaction, token) =>
        {
            await using NpgsqlConnection holder = await PostgresFixture.Cluster.OpenConnectionAsync(token);
            long signal = 821740000L + mode;
            await using var command = new NpgsqlCommand("SELECT pg_advisory_lock($1)", connection, transaction);
            command.Parameters.AddWithValue(signal);
            await command.ExecuteNonQueryAsync(token);
            await using var held = new NpgsqlCommand($"SELECT relations.hold_until_signal('tests.relation_locks_{mode}'::regclass::oid,$1,$2)", holder);
            held.Parameters.AddWithValue(mode);
            held.Parameters.AddWithValue(signal);
            Task execution = held.ExecuteNonQueryAsync(token);
            try
            {
                command.Parameters.Clear();
                command.Parameters.AddWithValue(holder.ProcessID);
                command.CommandText = "SELECT EXISTS(SELECT FROM pg_locks WHERE pid=$1 AND locktype='advisory' AND NOT granted)";
                bool waiting = false;
                for (int attempt = 0; attempt < 100; attempt++)
                {
                    if ((bool)(await command.ExecuteScalarAsync(token))!) { waiting = true; break; }

                    await Task.Delay(20, token);
                }

                Assert.IsTrue(waiting, "The holder did not reach the advisory barrier.");
                command.Parameters.Clear();
                command.CommandText = "SET LOCAL lock_timeout='100ms'";
                await command.ExecuteNonQueryAsync(token);
                await transaction.SaveAsync("relation_conflict", token);
                command.CommandText = $"LOCK TABLE tests.relation_locks_{mode} IN ACCESS EXCLUSIVE MODE";
                PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteNonQueryAsync(token));
                Assert.AreEqual("55P03", error.SqlState);
                await transaction.RollbackAsync("relation_conflict", token);
            }
            finally
            {
                command.Parameters.Clear();
                command.Parameters.AddWithValue(signal);
                command.CommandText = "SELECT pg_advisory_unlock($1)";
                await command.ExecuteNonQueryAsync(token);
                await execution;
            }

            command.Parameters.Clear();
            command.CommandText = $"LOCK TABLE tests.relation_locks_{mode} IN ACCESS EXCLUSIVE MODE; SELECT 42";
            Assert.AreEqual(42, await command.ExecuteScalarAsync(token));
        }, context.CancellationToken);

    private Task CheckAsync(string sql, object expected)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(RelationTests), async (connection, transaction, token) =>
        {
            int backend = connection.ProcessID;
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(token))
            {
                object? last = null;
                do
                {
                    while (await reader.ReadAsync(token))
                    {
                        last = reader.GetValue(0);
                        if (expected is bool && last is bool) { Assert.AreEqual(expected, last); }
                    }
                }
                while (await reader.NextResultAsync(token));

                Assert.AreEqual(expected, last);
            }

            command.CommandText = "SELECT 42";
            Assert.AreEqual(42, await command.ExecuteScalarAsync(token));
            Assert.AreEqual(backend, connection.ProcessID);
        }, context.CancellationToken);
}
