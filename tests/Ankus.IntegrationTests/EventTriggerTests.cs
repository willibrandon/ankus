using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies Native AOT event callbacks, immutable metadata, nested ownership, and PostgreSQL transaction behavior.
/// </summary>
/// <param name="context">The per-test context.</param>
[TestClass]
[DoNotParallelize]
public sealed class EventTriggerTests(TestContext context)
{
    /// <summary>
    /// Distinguishes command start, catalog-visible end, and post-deletion SQL drop in their actual native order.
    /// </summary>
    [TestMethod]
    public Task EventsExposeExactKindsTagsAndCatalogTiming()
        => Run(nameof(EventsExposeExactKindsTagsAndCatalogTiming), async (connection, transaction, token) =>
        {
            await Execute(connection, transaction, """
                CREATE EVENT TRIGGER event_start ON ddl_command_start EXECUTE FUNCTION event_values.event_action();
                CREATE EVENT TRIGGER event_end ON ddl_command_end EXECUTE FUNCTION event_values.event_action();
                CREATE EVENT TRIGGER event_drop ON sql_drop EXECUTE FUNCTION event_values.event_action();
                CREATE TABLE event_values.subject(id integer);
                DROP TABLE event_values.subject;
                """, token);
            string[] expected =
            [
                "ddl_command_start:DdlCommandStart:CREATE TABLE:False",
                "ddl_command_end:DdlCommandEnd:CREATE TABLE:True",
                "ddl_command_start:DdlCommandStart:DROP TABLE:True",
                "sql_drop:SqlDrop:DROP TABLE:False",
                "ddl_command_end:DdlCommandEnd:DROP TABLE:False",
            ];
            Assert.AreSequenceEqual(expected, await Scalar<string[]>(connection, transaction,
                "SELECT array_agg(event||':'||kind||':'||tag||':'||detail ORDER BY position) FROM event_values.audit WHERE phase='callback'", token));
            Assert.IsTrue(await Scalar<bool>(connection, transaction, "SELECT to_regclass('event_values.subject') IS NULL", token));
        });

    /// <summary>
    /// Preserves object address values and empty successful command snapshots independently of context tags.
    /// </summary>
    [TestMethod]
    public Task DdlCommandsPreserveCatalogIdentityAndEmptyResults()
        => Run(nameof(DdlCommandsPreserveCatalogIdentityAndEmptyResults), async (connection, transaction, token) =>
        {
            await Attach(connection, transaction, "ddl_command_end", token);
            await Execute(connection, transaction, "CREATE TABLE event_values.subject(id serial PRIMARY KEY,note text)", token);
            string[] expected = ["CREATE INDEX:index:event_values.subject_pkey", "CREATE SEQUENCE:sequence:event_values.subject_id_seq", "CREATE TABLE:table:event_values.subject"];
            string[] actual = await Scalar<string[]>(connection, transaction, """
                SELECT array_agg(DISTINCT command_tag||':'||object_type||':'||identity ORDER BY command_tag||':'||object_type||':'||identity)
                FROM event_values.audit WHERE phase='snapshot' AND command_tag IN('CREATE INDEX','CREATE SEQUENCE','CREATE TABLE')
                """, token);
            Assert.AreSequenceEqual(expected, actual);
            Assert.IsTrue(await Scalar<bool>(connection, transaction, """
                SELECT class_id='pg_class'::regclass AND object_id='event_values.subject'::regclass AND sub_id=0
                    AND schema_name='event_values' AND NOT in_extension AND tag='CREATE TABLE'
                FROM event_values.audit WHERE phase='snapshot' AND object_type='table'
                """, token));
            await Execute(connection, transaction, "DELETE FROM event_values.audit; CREATE TABLE IF NOT EXISTS event_values.subject(id integer)", token);
            Assert.AreEqual("0", await Scalar<string>(connection, transaction,
                "SELECT detail FROM event_values.audit WHERE phase='snapshot count'", token));
            Assert.AreEqual(0L, await Scalar<long>(connection, transaction,
                "SELECT count(*) FROM event_values.audit WHERE phase='snapshot'", token));
        });

    /// <summary>
    /// Preserves the genuinely nullable IDs and identities that PostgreSQL emits for privilege commands.
    /// </summary>
    /// <param name="sql">The privilege operation.</param>
    /// <param name="tag">The expected command tag.</param>
    /// <param name="objectType">The PostgreSQL object-type text.</param>
    [TestMethod]
    [DataRow("GRANT SELECT ON event_values.subject TO public", "GRANT", "TABLE")]
    [DataRow("REVOKE SELECT ON event_values.subject FROM public", "REVOKE", "TABLE")]
    [DataRow("ALTER DEFAULT PRIVILEGES GRANT SELECT ON TABLES TO public", "ALTER DEFAULT PRIVILEGES", "TABLES")]
    public Task PrivilegeSnapshotsKeepNullAddressFields(string sql, string tag, string objectType)
        => Run(nameof(PrivilegeSnapshotsKeepNullAddressFields), async (connection, transaction, token) =>
        {
            await Execute(connection, transaction, "CREATE TABLE event_values.subject(id integer)", token);
            await Attach(connection, transaction, "ddl_command_end", token);
            await Execute(connection, transaction, sql, token);
            await using var command = new NpgsqlCommand("""
                SELECT class_id,object_id,sub_id,schema_name,identity,command_tag,object_type,in_extension
                FROM event_values.audit WHERE phase='snapshot'
                """, connection, transaction);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
            Assert.IsTrue(await reader.ReadAsync(token));
            for (int index = 0; index < 5; index++)
            {
                Assert.IsTrue(reader.IsDBNull(index), $"Nullable address field {index}.");
            }

            Assert.AreEqual(tag, reader.GetString(5));
            Assert.AreEqual(objectType, reader.GetString(6));
            Assert.IsFalse(reader.GetBoolean(7));
            Assert.IsFalse(await reader.ReadAsync(token));
        });

    /// <summary>
    /// Distinguishes extension-script metadata from ordinary user DDL.
    /// </summary>
    [TestMethod]
    public Task ExtensionCommandsMarkTheirOrigin()
        => Run(nameof(ExtensionCommandsMarkTheirOrigin), async (connection, transaction, token) =>
        {
            await Attach(connection, transaction, "ddl_command_end", token);
            await Execute(connection, transaction, "CREATE SCHEMA event_sample; CREATE EXTENSION ankus_triggers WITH SCHEMA event_sample", token);
            Assert.IsTrue(await Scalar<bool>(connection, transaction, """
                SELECT in_extension AND object_id='event_sample.pets'::regclass FROM event_values.audit
                WHERE phase='snapshot' AND identity='event_sample.pets' AND command_tag='CREATE TABLE'
                """, token));
            await Execute(connection, transaction, "CREATE TABLE event_values.subject(id integer)", token);
            Assert.IsFalse(await Scalar<bool>(connection, transaction,
                "SELECT in_extension FROM event_values.audit WHERE phase='snapshot' AND identity='event_values.subject' AND command_tag='CREATE TABLE'", token));
        });

    /// <summary>
    /// Keeps dependency classifications and addresses for root, dependent, and internal dropped objects.
    /// </summary>
    [TestMethod]
    public Task DroppedObjectsPreserveDependencyAndAddressMetadata()
        => Run(nameof(DroppedObjectsPreserveDependencyAndAddressMetadata), async (connection, transaction, token) =>
        {
            await Execute(connection, transaction, """
                CREATE TABLE event_values.subject(id integer);
                CREATE VIEW event_values.dependent AS SELECT * FROM event_values.subject;
                """, token);
            uint originalOid = await Scalar<uint>(connection, transaction, "SELECT 'event_values.subject'::regclass::oid", token);
            await Attach(connection, transaction, "sql_drop", token);
            await Execute(connection, transaction, "DROP TABLE event_values.subject CASCADE", token);
            await using var command = new NpgsqlCommand("""
                SELECT class_id,object_id,sub_id,original,normal,is_temporary,schema_name,object_name,address_names,address_arguments
                FROM event_values.audit WHERE phase='snapshot' AND object_type='table' AND identity='event_values.subject'
                """, connection, transaction);
            await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(token))
            {
                Assert.IsTrue(await reader.ReadAsync(token));
                Assert.AreEqual(1259U, reader.GetFieldValue<uint>(0));
                Assert.AreEqual(originalOid, reader.GetFieldValue<uint>(1));
                Assert.AreEqual(0, reader.GetInt32(2));
                Assert.IsTrue(reader.GetBoolean(3));
                Assert.IsFalse(reader.GetBoolean(4));
                Assert.IsFalse(reader.GetBoolean(5));
                Assert.AreEqual("event_values", reader.GetString(6));
                Assert.AreEqual("subject", reader.GetString(7));
                string[] names = ["event_values", "subject"];
                Assert.AreSequenceEqual(names, reader.GetFieldValue<string[]>(8));
                Assert.IsEmpty(reader.GetFieldValue<string[]>(9));
                Assert.IsFalse(await reader.ReadAsync(token));
            }

            Assert.IsTrue(await Scalar<bool>(connection, transaction, """
                SELECT NOT original AND normal FROM event_values.audit
                WHERE phase='snapshot' AND object_type='view' AND identity='event_values.dependent'
                """, token));
            Assert.AreEqual(2L, await Scalar<long>(connection, transaction, """
                SELECT count(*) FROM event_values.audit WHERE phase='snapshot' AND object_type='type'
                    AND identity IN('event_values.subject','event_values.subject[]') AND NOT original AND NOT normal
                """, token));
            Assert.IsTrue(await Scalar<bool>(connection, transaction,
                "SELECT to_regclass('event_values.subject') IS NULL AND to_regclass('event_values.dependent') IS NULL", token));
        });

    /// <summary>
    /// Preserves subobject ordinals, overloaded function argument identities, and canonical temporary addresses.
    /// </summary>
    [TestMethod]
    public Task DropSnapshotsKeepColumnsFunctionsAndTemporaryNames()
        => Run(nameof(DropSnapshotsKeepColumnsFunctionsAndTemporaryNames), async (connection, transaction, token) =>
        {
            await Execute(connection, transaction, """
                CREATE TABLE event_values.subject(id integer,note text);
                CREATE FUNCTION event_values.overloaded(integer,text) RETURNS integer LANGUAGE sql AS 'SELECT $1';
                CREATE TEMP TABLE temporary_event(id integer DEFAULT 42);
                """, token);
            await Attach(connection, transaction, "sql_drop", token);
            await Execute(connection, transaction, """
                ALTER TABLE event_values.subject DROP COLUMN note;
                DROP FUNCTION event_values.overloaded(integer,text);
                DROP TABLE temporary_event;
                """, token);
            Assert.IsTrue(await Scalar<bool>(connection, transaction, """
                SELECT tag='ALTER TABLE' AND object_id='event_values.subject'::regclass AND sub_id=2
                    AND object_name IS NULL AND identity='event_values.subject.note'
                    AND address_names=ARRAY['event_values','subject','note'] AND address_arguments=ARRAY[]::text[]
                FROM event_values.audit WHERE phase='snapshot' AND object_type='table column'
                """, token));
            Assert.IsTrue(await Scalar<bool>(connection, transaction, """
                SELECT object_name IS NULL AND identity='event_values.overloaded(integer,pg_catalog.text)'
                    AND address_names=ARRAY['event_values','overloaded'] AND address_arguments=ARRAY['integer','pg_catalog.text']
                FROM event_values.audit WHERE phase='snapshot' AND object_type='function'
                """, token));
            Assert.IsTrue(await Scalar<bool>(connection, transaction, """
                SELECT is_temporary AND schema_name='pg_temp' AND object_name='temporary_event'
                    AND identity='pg_temp.temporary_event' AND address_names=ARRAY['pg_temp','temporary_event']
                    AND address_arguments=ARRAY[]::text[]
                FROM event_values.audit WHERE phase='snapshot' AND object_type='table' AND is_temporary
                """, token));
            await Execute(connection, transaction, "DELETE FROM event_values.audit; DROP TABLE IF EXISTS event_values.missing", token);
            Assert.AreEqual(0L, await Scalar<long>(connection, transaction, "SELECT count(*) FROM event_values.audit", token));
        });

    /// <summary>
    /// Reports individual and combined rewrite reasons while preserving real table data.
    /// </summary>
    /// <param name="sql">The rewriting ALTER statement.</param>
    /// <param name="reason">The exact PostgreSQL reason bitmap.</param>
    [TestMethod]
    [DataRow("ALTER TABLE event_values.subject SET UNLOGGED", 1)]
    [DataRow("ALTER TABLE event_values.subject ADD COLUMN created timestamptz DEFAULT clock_timestamp()", 2)]
    [DataRow("ALTER TABLE event_values.subject ALTER COLUMN id TYPE bigint", 4)]
    [DataRow("ALTER TABLE event_values.subject ADD COLUMN created timestamptz DEFAULT clock_timestamp(),ALTER COLUMN id TYPE bigint", 6)]
    [DataRow("ALTER TABLE event_values.subject SET ACCESS METHOD event_heap", 8)]
    public Task TableRewriteReportsRelationAndReasonBitmap(string sql, int reason)
        => Run(nameof(TableRewriteReportsRelationAndReasonBitmap), async (connection, transaction, token) =>
        {
            await Execute(connection, transaction, """
                CREATE TABLE event_values.subject(id integer,note text);
                INSERT INTO event_values.subject VALUES(7,'preserved');
                CREATE ACCESS METHOD event_heap TYPE TABLE HANDLER heap_tableam_handler;
                """, token);
            await Attach(connection, transaction, "table_rewrite", token);
            await Execute(connection, transaction, sql, token);
            Assert.AreEqual(reason, await Scalar<int>(connection, transaction,
                "SELECT rewrite_reason FROM event_values.audit WHERE phase='snapshot'", token));
            Assert.IsTrue(await Scalar<bool>(connection, transaction, """
                SELECT rewrite_oid='event_values.subject'::regclass AND tag='ALTER TABLE' AND kind='TableRewrite'
                FROM event_values.audit WHERE phase='snapshot'
                """, token));
            Assert.AreEqual("7:preserved", await Scalar<string>(connection, transaction, "SELECT id||':'||note FROM event_values.subject", token));
            Assert.AreEqual(reason == 1 ? "u" : "p", await Scalar<string>(connection, transaction,
                "SELECT relpersistence::text FROM pg_class WHERE oid='event_values.subject'::regclass", token));
            Assert.AreEqual((reason & 4) != 0 ? "bigint" : "integer", await Scalar<string>(connection, transaction,
                "SELECT pg_typeof(id)::text FROM event_values.subject", token));
            if ((reason & 2) != 0)
            {
                Assert.IsTrue(await Scalar<bool>(connection, transaction,
                    "SELECT created IS NOT NULL AND pg_typeof(created)='timestamptz'::regtype FROM event_values.subject", token));
            }

            if ((reason & 8) != 0)
            {
                Assert.IsTrue(await Scalar<bool>(connection, transaction,
                    "SELECT relam=(SELECT oid FROM pg_am WHERE amname='event_heap') FROM pg_class WHERE oid='event_values.subject'::regclass", token));
            }

            await Execute(connection, transaction, "DELETE FROM event_values.audit; ALTER TABLE event_values.subject ADD COLUMN fast integer DEFAULT 42", token);
            Assert.AreEqual(0L, await Scalar<long>(connection, transaction, "SELECT count(*) FROM event_values.audit", token));
            Assert.AreEqual(42, await Scalar<int>(connection, transaction, "SELECT fast FROM event_values.subject", token));
        });

    /// <summary>
    /// Excludes unchanged persistence and CLUSTER from table-rewrite callbacks while proving their real storage effects.
    /// </summary>
    [TestMethod]
    public Task NonEventRewritesAndUnchangedPersistenceDoNotFire()
        => Run(nameof(NonEventRewritesAndUnchangedPersistenceDoNotFire), async (connection, transaction, token) =>
        {
            await Execute(connection, transaction, """
                CREATE UNLOGGED TABLE event_values.subject(id integer PRIMARY KEY);
                INSERT INTO event_values.subject VALUES(3),(1),(2);
                """, token);
            await Attach(connection, transaction, "table_rewrite", token);
            await Execute(connection, transaction, "ALTER TABLE event_values.subject SET UNLOGGED", token);
            Assert.AreEqual(0L, await Scalar<long>(connection, transaction, "SELECT count(*) FROM event_values.audit", token));
            uint before = await Scalar<uint>(connection, transaction, "SELECT pg_relation_filenode('event_values.subject')", token);
            await Execute(connection, transaction, "CLUSTER event_values.subject USING subject_pkey", token);
            uint after = await Scalar<uint>(connection, transaction, "SELECT pg_relation_filenode('event_values.subject')", token);
            Assert.AreNotEqual(before, after);
            Assert.AreEqual(0L, await Scalar<long>(connection, transaction, "SELECT count(*) FROM event_values.audit", token));
            int[] expected = [1, 2, 3];
            Assert.AreSequenceEqual(expected, await Scalar<int[]>(connection, transaction,
                "SELECT array_agg(id ORDER BY id) FROM event_values.subject", token));
        });

    /// <summary>
    /// Owns helper rows after sessions and plans end, while repeated queries and later cursor batches remain exact.
    /// </summary>
    /// <param name="eventName">The command or drop metadata function.</param>
    [TestMethod]
    [DataRow("ddl_command_end")]
    [DataRow("sql_drop")]
    public Task MetadataQueriesWorkAcrossSessionPlanAndCursorOwners(string eventName)
        => Run(nameof(MetadataQueriesWorkAcrossSessionPlanAndCursorOwners), async (connection, transaction, token) =>
        {
            if (eventName == "sql_drop")
            {
                await Execute(connection, transaction, "CREATE TABLE event_values.subject(id integer)", token);
            }

            await Attach(connection, transaction, eventName, token);
            await Execute(connection, transaction, "SET LOCAL ankus.event_mode='owners'", token);
            await Execute(connection, transaction, eventName == "sql_drop" ? "DROP TABLE event_values.subject" : "CREATE TABLE event_values.subject(id integer)", token);
            string identities = eventName == "sql_drop" ? "event_values.subject|event_values.subject|event_values.subject[]" : "event_values.subject";
            string[] expected =
            [
                "owner session repeat:" + identities,
                "owner session:" + identities,
                "owner kept plan:" + identities,
                "owner disposed plan cursor:" + identities,
            ];
            Assert.AreSequenceEqual(expected, await Scalar<string[]>(connection, transaction,
                "SELECT array_agg(phase||':'||detail ORDER BY position) FROM event_values.audit WHERE phase LIKE 'owner %'", token));
        });

    /// <summary>
    /// Keeps helper snapshots detached and denies stale access after later DDL has replaced native state.
    /// </summary>
    /// <param name="eventName">The event whose snapshot is retained.</param>
    [TestMethod]
    [DataRow("ddl_command_end")]
    [DataRow("sql_drop")]
    [DataRow("table_rewrite")]
    public Task RetainedSnapshotsOwnValuesAndRejectFreshHelpers(string eventName)
        => Run(nameof(RetainedSnapshotsOwnValuesAndRejectFreshHelpers), async (connection, transaction, token) =>
        {
            await Execute(connection, transaction, "CREATE TABLE event_values.subject(id integer)", token);
            uint oid = await Scalar<uint>(connection, transaction, "SELECT 'event_values.subject'::regclass::oid", token);
            await Attach(connection, transaction, eventName, token);
            await Execute(connection, transaction, "SET LOCAL ankus.event_mode='retain'", token);
            string action = eventName switch
            {
                "ddl_command_end" => "CREATE TABLE event_values.owned(id integer)",
                "sql_drop" => "DROP TABLE event_values.subject",
                _ => "ALTER TABLE event_values.subject SET UNLOGGED",
            };
            await Execute(connection, transaction, action, token);
            await Execute(connection, transaction, "SET LOCAL ankus.event_mode='audit'; CREATE TABLE event_values.churn(id integer)", token);
            string expected = eventName switch
            {
                "ddl_command_end" => "ddl_command_end:CREATE TABLE:InvalidOperationException:CREATE TABLE:table:event_values:event_values.owned",
                "sql_drop" => "sql_drop:DROP TABLE:InvalidOperationException:table:event_values.subject:event_values,subject:",
                _ => "table_rewrite:ALTER TABLE:InvalidOperationException:" + oid + ":1",
            };
            Assert.AreEqual(expected, await Scalar<string>(connection, transaction, "SELECT event_values.event_retained()", token));
        });

    /// <summary>
    /// Rejects helpers for the wrong event and worker thread, while valid repeated reads keep their values.
    /// </summary>
    /// <param name="mode">The access partition.</param>
    [TestMethod]
    [DataRow("wrong_kind")]
    [DataRow("worker")]
    [DataRow("repeat")]
    public Task HelpersEnforcePhaseThreadAndRepeatableOwnership(string mode)
        => Run(nameof(HelpersEnforcePhaseThreadAndRepeatableOwnership), async (connection, transaction, token) =>
        {
            await Attach(connection, transaction, "ddl_command_end", token);
            await Execute(connection, transaction, $"SET LOCAL ankus.event_mode='{mode}'; CREATE TABLE event_values.subject(id integer)", token);
            if (mode == "wrong_kind")
            {
                Assert.AreEqual("drop protected|rewrite protected", await Scalar<string>(connection, transaction,
                    "SELECT detail FROM event_values.audit WHERE phase='guards'", token));
            }
            else if (mode == "worker")
            {
                Assert.AreEqual("InvalidOperationException", await Scalar<string>(connection, transaction,
                    "SELECT detail FROM event_values.audit WHERE phase='guards'", token));
                Assert.AreEqual("event_values.subject", await Scalar<string>(connection, transaction,
                    "SELECT identity FROM event_values.audit WHERE phase='after worker'", token));
            }
            else
            {
                string[] expected = ["event_values.subject", "event_values.subject"];
                Assert.AreSequenceEqual(expected, await Scalar<string[]>(connection, transaction,
                    "SELECT array_agg(identity ORDER BY position) FROM event_values.audit WHERE phase IN('snapshot','repeated')", token));
            }

            Assert.IsTrue(await Scalar<bool>(connection, transaction, "SELECT to_regclass('event_values.subject') IS NOT NULL", token));
        });

    /// <summary>
    /// Preserves native diagnostics, rolls back the affected DDL, and subsequently uses the same event callback.
    /// </summary>
    /// <param name="eventName">The failure phase.</param>
    [TestMethod]
    [DataRow("ddl_command_start")]
    [DataRow("ddl_command_end")]
    [DataRow("sql_drop")]
    [DataRow("table_rewrite")]
    public Task ErrorsRollBackDdlAndRecoverOnSameConnection(string eventName)
        => Run(nameof(ErrorsRollBackDdlAndRecoverOnSameConnection), async (connection, transaction, token) =>
        {
            bool exists = eventName is "sql_drop" or "table_rewrite";
            if (exists)
            {
                await Execute(connection, transaction, "CREATE TABLE event_values.subject(id integer); INSERT INTO event_values.subject VALUES(7)", token);
            }

            await Attach(connection, transaction, eventName, token);
            await Execute(connection, transaction, "SELECT event_values.event_reset(); SET LOCAL ankus.event_mode='error'", token);
            await transaction.SaveAsync("before_error", token);
            string sql = eventName switch
            {
                "sql_drop" => "DROP TABLE event_values.subject",
                "table_rewrite" => "ALTER TABLE event_values.subject SET UNLOGGED",
                _ => "CREATE TABLE event_values.subject(id integer)",
            };
            PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => Execute(connection, transaction, sql, token));
            Assert.AreEqual("P7701", error.SqlState);
            Assert.AreEqual("event rejected command", error.MessageText);
            Assert.AreEqual("owned event detail", error.Detail);
            Assert.AreEqual("retry valid DDL", error.Hint);
            await transaction.RollbackAsync("before_error", token);
            Assert.AreEqual(exists, await Scalar<bool>(connection, transaction, "SELECT to_regclass('event_values.subject') IS NOT NULL", token));
            if (exists)
            {
                Assert.AreEqual(7, await Scalar<int>(connection, transaction, "SELECT id FROM event_values.subject", token));
                Assert.AreEqual("p", await Scalar<string>(connection, transaction,
                    "SELECT relpersistence::text FROM pg_class WHERE oid='event_values.subject'::regclass", token));
            }

            Assert.AreEqual(0L, await Scalar<long>(connection, transaction, "SELECT count(*) FROM event_values.audit", token));
            Assert.AreEqual(eventName + ":" + (eventName == "sql_drop" ? "DROP TABLE" : eventName == "table_rewrite" ? "ALTER TABLE" : "CREATE TABLE"),
                await Scalar<string>(connection, transaction, "SELECT event_values.event_calls()", token));
            await Execute(connection, transaction, "SET LOCAL ankus.event_mode='recover'", token);
            await Execute(connection, transaction, sql, token);
            Assert.AreEqual("22012:42", await Scalar<string>(connection, transaction,
                "SELECT detail FROM event_values.audit WHERE phase='recovery'", token));
        });

    /// <summary>
    /// Skips command-end callbacks after a DDL error and propagates managed errors and cancellation safely.
    /// </summary>
    /// <param name="mode">The managed failure, cancellation, or underlying PostgreSQL error.</param>
    /// <param name="sqlState">The exact expected diagnostic.</param>
    [TestMethod]
    [DataRow("managed_error", "38000")]
    [DataRow("wait", "57014")]
    [DataRow("audit", "42P07")]
    public Task FailedCommandsDoNotRunEndAndBackendRecovers(string mode, string sqlState)
        => Run(nameof(FailedCommandsDoNotRunEndAndBackendRecovers), async (connection, transaction, token) =>
        {
            await Execute(connection, transaction, "CREATE TABLE event_values.subject(id integer)", token);
            await Execute(connection, transaction, """
                CREATE EVENT TRIGGER event_start ON ddl_command_start EXECUTE FUNCTION event_values.event_action();
                CREATE EVENT TRIGGER event_end ON ddl_command_end EXECUTE FUNCTION event_values.event_action();
                """, token);
            await Execute(connection, transaction, $"SELECT event_values.event_reset(); SET LOCAL ankus.event_mode='{mode}'", token);
            if (mode == "wait")
            {
                await Execute(connection, transaction, "SET LOCAL statement_timeout='100ms'", token);
            }

            await transaction.SaveAsync("failed", token);
            PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() =>
                Execute(connection, transaction, "CREATE TABLE event_values.subject(id integer)", token));
            Assert.AreEqual(sqlState, error.SqlState);
            await transaction.RollbackAsync("failed", token);
            await Execute(connection, transaction, "SET LOCAL statement_timeout=0; SET LOCAL ankus.event_mode='audit'", token);
            Assert.AreEqual("ddl_command_start:CREATE TABLE", await Scalar<string>(connection, transaction, "SELECT event_values.event_calls()", token));
            Assert.AreEqual(0L, await Scalar<long>(connection, transaction, "SELECT count(*) FROM event_values.audit", token));
            await Execute(connection, transaction, "CREATE TABLE event_values.recovered(id integer)", token);
            Assert.IsTrue(await Scalar<bool>(connection, transaction, "SELECT to_regclass('event_values.recovered') IS NOT NULL", token));
        });

    /// <summary>
    /// Restores parent identity and helper lists after recursive DDL, including a caught child event failure.
    /// </summary>
    /// <param name="fail">Whether the child callback throws.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task NestedDdlRestoresParentContextAndSnapshot(bool fail)
        => Run(nameof(NestedDdlRestoresParentContextAndSnapshot), async (connection, transaction, token) =>
        {
            await Attach(connection, transaction, "ddl_command_end", token);
            await Execute(connection, transaction,
                $"SET LOCAL ankus.event_mode='{(fail ? "nested_error" : "nested")}'; CREATE TABLE event_values.subject(id integer)", token);
            string[] expected = ["event_values.subject", "event_values.subject"];
            Assert.AreSequenceEqual(expected, await Scalar<string[]>(connection, transaction, """
                SELECT array_agg(identity ORDER BY position) FROM event_values.audit WHERE depth=1 AND phase IN('snapshot','restored')
                """, token));
            Assert.AreEqual(!fail, await Scalar<bool>(connection, transaction, "SELECT to_regclass('event_values.inner_table') IS NOT NULL", token));
            if (fail)
            {
                Assert.AreEqual("P7711", await Scalar<string>(connection, transaction,
                    "SELECT detail FROM event_values.audit WHERE phase='nested error'", token));
                Assert.AreEqual(0L, await Scalar<long>(connection, transaction, "SELECT count(*) FROM event_values.audit WHERE depth=2", token));
            }
            else
            {
                Assert.AreEqual("InvalidOperationException", await Scalar<string>(connection, transaction,
                    "SELECT detail FROM event_values.audit WHERE phase='parent guard'", token));
                Assert.AreEqual("event_values.inner_table", await Scalar<string>(connection, transaction,
                    "SELECT identity FROM event_values.audit WHERE depth=2 AND phase='snapshot'", token));
            }
        });

    /// <summary>
    /// Separates event and row-trigger scopes and restores transition relations after nested DDL errors.
    /// </summary>
    /// <param name="fail">Whether nested event handling throws.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task RowTransitionScopeIsIsolatedAndRestoredAroundEvents(bool fail)
        => Run(nameof(RowTransitionScopeIsIsolatedAndRestoredAroundEvents), async (connection, transaction, token) =>
        {
            await Execute(connection, transaction, """
                CREATE TABLE event_values.rows(id integer);
                CREATE TRIGGER row_ddl AFTER INSERT ON event_values.rows REFERENCING NEW TABLE AS parent_rows
                    FOR EACH STATEMENT EXECUTE FUNCTION event_values.event_rows_with_ddl();
                """, token);
            await Attach(connection, transaction, "ddl_command_end", token);
            await Execute(connection, transaction,
                $"SET LOCAL ankus.event_mode='{(fail ? "row_isolation_error" : "row_isolation")}'; INSERT INTO event_values.rows VALUES(1),(2),(3)", token);
            Assert.AreEqual("3:3:" + (fail ? "P7712" : "ok"), await Scalar<string>(connection, transaction,
                "SELECT detail FROM event_values.audit WHERE phase='outer row'", token));
            Assert.AreEqual(3L, await Scalar<long>(connection, transaction, "SELECT count(*) FROM event_values.rows", token));
            Assert.AreEqual(!fail, await Scalar<bool>(connection, transaction, "SELECT to_regclass('event_values.from_row') IS NOT NULL", token));
            if (!fail)
            {
                Assert.AreEqual("42P01:42", await Scalar<string>(connection, transaction,
                    "SELECT detail FROM event_values.audit WHERE phase='row scope'", token));
            }
        });

    /// <summary>
    /// Executes row-trigger DML inside an event callback and retains the original DDL snapshot afterward.
    /// </summary>
    [TestMethod]
    public Task EventContextSurvivesNestedRowTrigger()
        => Run(nameof(EventContextSurvivesNestedRowTrigger), async (connection, transaction, token) =>
        {
            await Execute(connection, transaction, """
                CREATE TABLE event_values.rows(id integer);
                CREATE TRIGGER event_row BEFORE INSERT ON event_values.rows FOR EACH ROW EXECUTE FUNCTION event_values.event_audit_row();
                """, token);
            await Attach(connection, transaction, "ddl_command_end", token);
            await Execute(connection, transaction, "SET LOCAL ankus.event_mode='row_dml'; CREATE TABLE event_values.subject(id integer)", token);
            Assert.AreEqual("Insert:7:1", await Scalar<string>(connection, transaction,
                "SELECT detail FROM event_values.audit WHERE phase='row'", token));
            Assert.AreEqual("event_values.subject", await Scalar<string>(connection, transaction,
                "SELECT identity FROM event_values.audit WHERE phase='after row'", token));
            Assert.AreEqual(7, await Scalar<int>(connection, transaction, "SELECT id FROM event_values.rows", token));
        });

    /// <summary>
    /// Preserves alphabetical ordering, TAG filters, and native replication enable modes.
    /// </summary>
    [TestMethod]
    public Task EventOrderingFiltersAndEnableModesFollowPostgres()
        => Run(nameof(EventOrderingFiltersAndEnableModesFollowPostgres), async (connection, transaction, token) =>
        {
            await Execute(connection, transaction, """
                CREATE EVENT TRIGGER z_second ON ddl_command_start WHEN TAG IN('CREATE TABLE') EXECUTE FUNCTION event_values.event_second();
                CREATE EVENT TRIGGER a_first ON ddl_command_start WHEN TAG IN('create table') EXECUTE FUNCTION event_values.event_first();
                CREATE VIEW event_values.excluded AS SELECT 1;
                CREATE TABLE event_values.subject(id integer);
                """, token);
            string[] expected = ["first:0", "second:1"];
            Assert.AreSequenceEqual(expected, await Scalar<string[]>(connection, transaction,
                "SELECT array_agg(phase||':'||detail ORDER BY position) FROM event_values.audit", token));
            await Execute(connection, transaction, """
                DELETE FROM event_values.audit;
                ALTER EVENT TRIGGER a_first DISABLE;
                ALTER EVENT TRIGGER z_second ENABLE REPLICA;
                CREATE TABLE event_values.origin(id integer);
                """, token);
            Assert.AreEqual(0L, await Scalar<long>(connection, transaction, "SELECT count(*) FROM event_values.audit", token));
            await Execute(connection, transaction, "SET LOCAL session_replication_role=replica; CREATE TABLE event_values.replica(id integer)", token);
            Assert.AreEqual("second:0", await Scalar<string>(connection, transaction, "SELECT phase||':'||detail FROM event_values.audit", token));
            await Execute(connection, transaction, """
                DELETE FROM event_values.audit;
                ALTER EVENT TRIGGER a_first ENABLE ALWAYS;
                CREATE TABLE event_values.always(id integer);
                """, token);
            Assert.AreSequenceEqual(expected, await Scalar<string[]>(connection, transaction,
                "SELECT array_agg(phase||':'||detail ORDER BY position) FROM event_values.audit", token));
            await Execute(connection, transaction, """
                DROP EVENT TRIGGER a_first;
                DROP EVENT TRIGGER z_second;
                SET LOCAL session_replication_role=origin;
                DELETE FROM event_values.audit;
                SELECT event_values.event_reset();
                CREATE EVENT TRIGGER shared_a ON ddl_command_start EXECUTE FUNCTION event_values.event_action();
                CREATE EVENT TRIGGER shared_z ON ddl_command_start EXECUTE FUNCTION event_values.event_action();
                """, token);
            Assert.AreEqual("", await Scalar<string>(connection, transaction, "SELECT event_values.event_calls()", token));
            await Execute(connection, transaction, "CREATE TABLE event_values.shared_handler(id integer)", token);
            Assert.AreEqual("ddl_command_start:CREATE TABLE|ddl_command_start:CREATE TABLE",
                await Scalar<string>(connection, transaction, "SELECT event_values.event_calls()", token));
            Assert.AreEqual(2L, await Scalar<long>(connection, transaction, "SELECT count(*) FROM event_values.audit WHERE phase='callback'", token));
        });

    /// <summary>
    /// Resolves unqualified enums against each function's schema and restores the outer function after nested DDL.
    /// </summary>
    /// <param name="fail">Whether the event callback raises a guarded error.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task NestedEventRestoresFunctionSchemaForEnumResolution(bool fail)
        => Run(nameof(NestedEventRestoresFunctionSchemaForEnumResolution), async (connection, transaction, token) =>
        {
            await Execute(connection, transaction, """
                CREATE SCHEMA outer_scope;
                CREATE SCHEMA event_scope;
                CREATE TYPE outer_scope.enum_mood AS ENUM('Low');
                CREATE TYPE event_scope.enum_mood AS ENUM('Low');
                DO $body$ DECLARE p pg_proc; BEGIN
                    SELECT * INTO p FROM pg_proc WHERE oid='event_values.event_enum_scope(boolean)'::regprocedure;
                    EXECUTE format('CREATE FUNCTION outer_scope.invoke(boolean) RETURNS text AS %L,%L LANGUAGE c',p.probin,p.prosrc);
                    SELECT * INTO p FROM pg_proc WHERE oid='event_values.event_action()'::regprocedure;
                    EXECUTE format('CREATE FUNCTION event_scope.handle() RETURNS event_trigger AS %L,%L LANGUAGE c',p.probin,p.prosrc);
                END $body$;
                CREATE EVENT TRIGGER enum_scope ON ddl_command_end WHEN TAG IN('CREATE TABLE') EXECUTE FUNCTION event_scope.handle();
                """, token);
            uint outerOid = await Scalar<uint>(connection, transaction, "SELECT 'outer_scope.enum_mood'::regtype::oid", token);
            uint eventOid = await Scalar<uint>(connection, transaction, "SELECT 'event_scope.enum_mood'::regtype::oid", token);
            string outcome = fail ? "P7713" : "ok";
            Assert.AreEqual($"{outerOid}:{eventOid}:{outerOid}:{outcome}", await Scalar<string>(connection, transaction,
                $"SELECT outer_scope.invoke({(fail ? "true" : "false")})", token));
            Assert.AreEqual(!fail, await Scalar<bool>(connection, transaction,
                "SELECT to_regclass('event_values.enum_scope_target') IS NOT NULL", token));
            Assert.AreEqual(42, await Scalar<int>(connection, transaction, "SELECT 42", token));
        });

    /// <summary>
    /// Reaches the event-specific native guard through a scalar alias and recovers without managed dispatch.
    /// </summary>
    [TestMethod]
    public Task OrdinaryInvocationRejectsEventProtocolBeforeDispatch()
        => Run(nameof(OrdinaryInvocationRejectsEventProtocolBeforeDispatch), async (connection, transaction, token) =>
        {
            await Execute(connection, transaction, """
                DO $body$ DECLARE p pg_proc; BEGIN
                    SELECT * INTO p FROM pg_proc WHERE oid='event_values.event_action()'::regprocedure;
                    EXECUTE format('CREATE FUNCTION event_values.invalid_call() RETURNS integer AS %L,%L LANGUAGE c',p.probin,p.prosrc);
                END $body$;
                SELECT event_values.event_reset();
                """, token);
            await transaction.SaveAsync("invalid", token);
            PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() =>
                Scalar<int>(connection, transaction, "SELECT event_values.invalid_call()", token));
            Assert.AreEqual("39P03", error.SqlState);
            await transaction.RollbackAsync("invalid", token);
            Assert.AreEqual("", await Scalar<string>(connection, transaction, "SELECT event_values.event_calls()", token));
            await Attach(connection, transaction, "ddl_command_end", token);
            await Execute(connection, transaction, "CREATE TABLE event_values.subject(id integer)", token);
            Assert.AreEqual("ddl_command_end:CREATE TABLE", await Scalar<string>(connection, transaction, "SELECT event_values.event_calls()", token));
        });

    private Task Run(string name, Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, Task> action)
        => PostgresFixture.Cluster.RunInTransactionAsync(name, action, context.CancellationToken);

    private static Task<int> Attach(NpgsqlConnection connection, NpgsqlTransaction transaction, string eventName, CancellationToken token)
        => Execute(connection, transaction, $"CREATE EVENT TRIGGER managed_event ON {eventName} EXECUTE FUNCTION event_values.event_action()", token);

    private static async Task<int> Execute(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, CancellationToken token)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        return await command.ExecuteNonQueryAsync(token);
    }

    private static async Task<T> Scalar<T>(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, CancellationToken token)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        return Assert.IsInstanceOfType<T>(await command.ExecuteScalarAsync(token));
    }
}
