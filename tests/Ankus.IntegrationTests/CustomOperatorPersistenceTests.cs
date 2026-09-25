using System.Text.Json;
using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies committed hash indexes and generated operator extension lifetimes in real backends.
/// </summary>
/// <param name="context">The current test context.</param>
[TestClass]
[DoNotParallelize]
public sealed class CustomOperatorPersistenceTests(TestContext context)
{
    /// <summary>
    /// Distinct backends reuse committed buckets, reject collisions and observe rebuilt index contents.
    /// </summary>
    [TestMethod]
    public async Task CustomOperatorHashesPersistAcrossFreshBackends()
    {
        string table = "operator_hash_" + Guid.NewGuid().ToString("N");
        await using NpgsqlConnection first = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.IsFalse(new NpgsqlConnectionStringBuilder(first.ConnectionString).Pooling);
        try
        {
            await using (NpgsqlTransaction transaction = await first.BeginTransactionAsync(context.CancellationToken))
            {
                await Execute(first, $"""
                    CREATE TABLE derived_ops.{table}(id integer, value derived_ops.key);
                    INSERT INTO derived_ops.{table} VALUES
                        (1,'Alpha#1'),(2,'ALPHA#99'),(3,'beta#3'),
                        (4,'collision-a#4'),(5,'COLLISION-A#5'),(6,'collision-b#6'),(7,NULL);
                    CREATE INDEX {table}_index ON derived_ops.{table} USING hash(value);
                    ANALYZE derived_ops.{table};
                    """);
                await transaction.CommitAsync(context.CancellationToken);
            }

            await AssertLiteralHashes(first);
            uint index = await Scalar<uint>(first, $"SELECT 'derived_ops.{table}_index'::regclass::oid");
            await using NpgsqlConnection second = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
            Assert.IsFalse(new NpgsqlConnectionStringBuilder(second.ConnectionString).Pooling);
            Assert.AreNotEqual(first.ProcessID, second.ProcessID);
            Assert.AreEqual(first.ProcessID, await Scalar<int>(first, "SELECT pg_backend_pid()"));
            Assert.AreEqual(second.ProcessID, await Scalar<int>(second, "SELECT pg_backend_pid()"));
            Assert.AreEqual(index, await Scalar<uint>(second, $"SELECT 'derived_ops.{table}_index'::regclass::oid"));
            await AssertLiteralHashes(second);
            await Execute(second, "SET enable_seqscan=off; SET enable_bitmapscan=off");
            await AssertHashRows(second, table, "alpha#0", ["1:Alpha#1", "2:ALPHA#99"]);
            await AssertHashRows(second, table, "collision-a#99", ["4:collision-a#4", "5:COLLISION-A#5"]);
            await AssertHashRows(second, table, "collision-b#99", ["6:collision-b#6"]);
            await AssertHashRows(second, table, "gamma#0", []);

            await using (NpgsqlTransaction transaction = await second.BeginTransactionAsync(context.CancellationToken))
            {
                await Execute(second, $"""
                    UPDATE derived_ops.{table} SET value='BETA#22' WHERE id=2;
                    DELETE FROM derived_ops.{table} WHERE id=4;
                    INSERT INTO derived_ops.{table} VALUES(8,'aLpHa#8');
                    REINDEX INDEX derived_ops.{table}_index;
                    """);
                await transaction.CommitAsync(context.CancellationToken);
            }

            await Execute(first, "SET enable_seqscan=off; SET enable_bitmapscan=off");
            await AssertHashRows(first, table, "alpha#0", ["1:Alpha#1", "8:aLpHa#8"]);
            await AssertHashRows(first, table, "beta#0", ["2:BETA#22", "3:beta#3"]);
            await AssertHashRows(first, table, "collision-a#99", ["5:COLLISION-A#5"]);
            await AssertHashRows(first, table, "collision-b#99", ["6:collision-b#6"]);
            await AssertLiteralHashes(first);
            Assert.AreEqual(7L, await Scalar<long>(first, $"SELECT count(*) FROM derived_ops.{table}"));
            Assert.AreSequenceEqual(["7"], await Strings(first, $"SELECT id::text FROM derived_ops.{table} WHERE value IS NULL"));
        }
        finally
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await using NpgsqlConnection cleanup = await PostgresFixture.Cluster.OpenConnectionAsync(timeout.Token);
            await using var command = new NpgsqlCommand($"DROP TABLE IF EXISTS derived_ops.{table}", cleanup);
            await command.ExecuteNonQueryAsync(timeout.Token);
        }
    }

    /// <summary>
    /// Extension members retain identity and usable indexes when relocated, disappear on drop and reinstall independently.
    /// </summary>
    [TestMethod]
    public Task CustomOperatorSampleRelocatesAndReinstalls()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(CustomOperatorSampleRelocatesAndReinstalls), async (connection, transaction, token) =>
        {
            await using var setup = new NpgsqlCommand("""
                CREATE SCHEMA operators_first;
                CREATE SCHEMA operators_second;
                CREATE SCHEMA operators_shadow;
                CREATE TYPE operators_shadow.ordered_key AS ENUM('shadow');
                CREATE FUNCTION operators_shadow.ordered_key_hash(integer) RETURNS integer
                    LANGUAGE SQL IMMUTABLE STRICT AS 'SELECT pg_catalog.hashint4($1)';
                CREATE FUNCTION operators_shadow.ordered_key_cmp(integer,integer) RETURNS integer
                    LANGUAGE SQL IMMUTABLE STRICT AS 'SELECT pg_catalog.btint4cmp($1,$2)';
                CREATE OPERATOR operators_shadow.= (LEFTARG=integer, RIGHTARG=integer, FUNCTION=pg_catalog.int4eq);
                CREATE OPERATOR FAMILY operators_shadow.ordered_key_hash_ops USING hash;
                CREATE OPERATOR CLASS operators_shadow.ordered_key_hash_ops FOR TYPE integer USING hash
                    FAMILY operators_shadow.ordered_key_hash_ops AS
                    OPERATOR 1 operators_shadow.=(integer,integer),
                    FUNCTION 1 operators_shadow.ordered_key_hash(integer);
                CREATE OPERATOR FAMILY operators_shadow.ordered_key_btree_ops USING btree;
                CREATE OPERATOR CLASS operators_shadow.ordered_key_btree_ops FOR TYPE integer USING btree
                    FAMILY operators_shadow.ordered_key_btree_ops AS
                    OPERATOR 1 pg_catalog.<(integer,integer), OPERATOR 2 pg_catalog.<=(integer,integer),
                    OPERATOR 3 operators_shadow.=(integer,integer), OPERATOR 4 pg_catalog.>=(integer,integer),
                    OPERATOR 5 pg_catalog.>(integer,integer), FUNCTION 1 operators_shadow.ordered_key_cmp(integer,integer);
                CREATE TABLE operators_shadow.items(value integer);
                INSERT INTO operators_shadow.items VALUES(42),(7),(42);
                CREATE INDEX shadow_hash ON operators_shadow.items USING hash(value operators_shadow.ordered_key_hash_ops);
                SET LOCAL search_path=operators_shadow,pg_catalog;
                CREATE EXTENSION ankus_custom_types WITH SCHEMA operators_first;
                SET LOCAL enable_seqscan=off;
                SET LOCAL enable_bitmapscan=off;
                """, connection, transaction);
            await setup.ExecuteNonQueryAsync(token);
            uint[] shadow = await ShadowIdentities(connection);
            Dictionary<string, uint> original = await Members(connection, "operators_first");
            await CreateLifecycleIndexes(connection, "operators_first");
            await AssertLifecycleIndexes(connection, "operators_first");

            await Execute(connection, "ALTER EXTENSION ankus_custom_types SET SCHEMA operators_second");
            Dictionary<string, uint> moved = await Members(connection, "operators_second");
            foreach ((string name, uint oid) in original)
            {
                Assert.AreEqual(oid, moved[name], name);
            }

            Assert.IsTrue(await Scalar<bool>(connection, "SELECT to_regtype('operators_first.ordered_key') IS NULL"));
            await AssertLifecycleIndexes(connection, "operators_second");
            await Execute(connection, "REINDEX INDEX lifecycle_hash_index; REINDEX INDEX lifecycle_btree_index");
            await AssertLifecycleIndexes(connection, "operators_second");
            await Execute(connection, "DROP TABLE lifecycle_hash, lifecycle_btree; DROP EXTENSION ankus_custom_types");
            Assert.IsTrue(await Scalar<bool>(connection, """
                SELECT to_regtype('operators_second.ordered_key') IS NULL
                    AND NOT EXISTS(SELECT 1 FROM pg_opclass WHERE opcnamespace='operators_second'::regnamespace)
                    AND NOT EXISTS(SELECT 1 FROM pg_opfamily WHERE opfnamespace='operators_second'::regnamespace)
                    AND NOT EXISTS(SELECT 1 FROM pg_proc WHERE pronamespace='operators_second'::regnamespace)
                    AND NOT EXISTS(SELECT 1 FROM pg_operator WHERE oprnamespace='operators_second'::regnamespace)
                """));
            await using (var references = new NpgsqlCommand("""
                SELECT NOT EXISTS(SELECT 1 FROM pg_amop WHERE amopfamily=ANY($1))
                    AND NOT EXISTS(SELECT 1 FROM pg_amproc WHERE amprocfamily=ANY($1))
                """, connection, transaction))
            {
                uint[] families = [original["family:btree"], original["family:hash"]];
                references.Parameters.AddWithValue(NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Oid, families);
                Assert.IsTrue(Assert.IsInstanceOfType<bool>(await references.ExecuteScalarAsync(token)));
            }

            Assert.AreSequenceEqual(shadow, await ShadowIdentities(connection));
            Assert.AreEqual("shadow", await Scalar<string>(connection, "SELECT 'shadow'::operators_shadow.ordered_key::text"));
            const string shadowQuery = "SELECT value::text FROM operators_shadow.items WHERE value OPERATOR(operators_shadow.=) 42";
            await AssertIndexPlan(connection, shadowQuery, "shadow_hash");
            Assert.AreSequenceEqual(["42", "42"], await Strings(connection, shadowQuery));

            await Execute(connection, "CREATE EXTENSION ankus_custom_types WITH SCHEMA operators_first");
            Dictionary<string, uint> reinstalled = await Members(connection, "operators_first");
            foreach ((string name, uint oid) in original)
            {
                Assert.AreNotEqual(oid, reinstalled[name], name);
            }

            await CreateLifecycleIndexes(connection, "operators_first");
            await AssertLifecycleIndexes(connection, "operators_first");
            Assert.AreSequenceEqual(shadow, await ShadowIdentities(connection));
        }, context.CancellationToken);

    /// <summary>
    /// Pins normalized ASCII hashes to independent SeaHash 4.1.0 reference output in every backend.
    /// </summary>
    private async Task AssertLiteralHashes(NpgsqlConnection connection)
        => Assert.AreSequenceEqual([-1171862696, -1171862696, -894071121, 181705683, 628087993, 7, 7],
            await Scalar<int[]>(connection, """
                SELECT ARRAY[derived_ops.key_hash('Alpha#1'),derived_ops.key_hash('ALPHA#99'),
                    derived_ops.key_hash('beta#3'),derived_ops.key_hash('gamma#0'),derived_ops.key_hash('#0'),
                    derived_ops.key_hash('collision-a#4'),derived_ops.key_hash('collision-b#6')]
                """));

    /// <summary>
    /// Verifies both a constrained hash-index scan and every matching stored row.
    /// </summary>
    private async Task AssertHashRows(NpgsqlConnection connection, string table, string key, string[] expected)
    {
        string query = $"SELECT id::text || ':' || value::text FROM derived_ops.{table} WHERE value OPERATOR(derived_ops.=) '{key}'::derived_ops.key ORDER BY id";
        await AssertIndexPlan(connection, query, table + "_index");
        Assert.AreSequenceEqual(expected, await Strings(connection, query));
    }

    /// <summary>
    /// Builds separate access-method indexes using the generated default classes.
    /// </summary>
    private Task CreateLifecycleIndexes(NpgsqlConnection connection, string schema)
        => Execute(connection, $$"""
            CREATE TEMP TABLE lifecycle_hash(id integer, value {{schema}}.ordered_key);
            INSERT INTO lifecycle_hash VALUES
                (1,'{"Value":"alpha"}'),(2,'{"Value":"ALPHA"}'),(3,'{"Value":"beta"}'),(4,'{"Value":"Gamma"}'),(5,NULL);
            CREATE TEMP TABLE lifecycle_btree(LIKE lifecycle_hash);
            INSERT INTO lifecycle_btree SELECT * FROM lifecycle_hash;
            CREATE INDEX lifecycle_hash_index ON lifecycle_hash USING hash(value);
            CREATE INDEX lifecycle_btree_index ON lifecycle_btree USING btree(value);
            ANALYZE lifecycle_hash;
            ANALYZE lifecycle_btree;
            """);

    /// <summary>
    /// Checks actual hash and btree predicates, exact retained payloads and the schema-qualified hash helper.
    /// </summary>
    private async Task AssertLifecycleIndexes(NpgsqlConnection connection, string schema)
    {
        string hashQuery = $$"""
            SELECT id::text || ':' || (value::text::jsonb->>'Value') FROM lifecycle_hash
            WHERE value OPERATOR({{schema}}.=) '{"Value":"aLpHa"}'::{{schema}}.ordered_key ORDER BY id
            """;
        await AssertIndexPlan(connection, hashQuery, "lifecycle_hash_index");
        Assert.AreSequenceEqual(["1:alpha", "2:ALPHA"], await Strings(connection, hashQuery));
        string btreeQuery = $$"""
            SELECT id::text || ':' || (value::text::jsonb->>'Value') FROM lifecycle_btree
            WHERE value OPERATOR({{schema}}.>=) '{"Value":"BETA"}'::{{schema}}.ordered_key
            ORDER BY value USING OPERATOR({{schema}}.<)
            """;
        await AssertIndexPlan(connection, btreeQuery, "lifecycle_btree_index");
        Assert.AreSequenceEqual(["3:beta", "4:Gamma"], await Strings(connection, btreeQuery));
        Assert.AreSequenceEqual(["1:1", "1:2", "2:1", "2:2", "3:3", "4:4"], await Strings(connection, $"""
            SELECT h.id::text || ':' || b.id::text FROM lifecycle_hash h JOIN lifecycle_btree b
                ON h.value OPERATOR({schema}.=) b.value ORDER BY h.id,b.id
            """));
        Assert.AreEqual(-1171862696, await Scalar<int>(connection,
            $$"""SELECT {{schema}}.ordered_key_hash('{"Value":"aLpHa"}'::{{schema}}.ordered_key)"""));
    }

    /// <summary>
    /// Requires every expected named member to belong to the extension and captures its exact catalog identity.
    /// </summary>
    private async Task<Dictionary<string, uint>> Members(NpgsqlConnection connection, string schema)
    {
        await using var command = new NpgsqlCommand($"""
            WITH members(label, classid, objid) AS (
                SELECT 'type', 'pg_type'::regclass, '{schema}.ordered_key'::regtype::oid
                UNION ALL SELECT 'class:'||a.amname, 'pg_opclass'::regclass, c.oid
                    FROM pg_opclass c JOIN pg_am a ON a.oid=c.opcmethod
                    WHERE c.opcnamespace='{schema}'::regnamespace AND c.opcintype='{schema}.ordered_key'::regtype
                UNION ALL SELECT 'family:'||a.amname, 'pg_opfamily'::regclass, f.oid
                    FROM pg_opfamily f JOIN pg_am a ON a.oid=f.opfmethod
                    WHERE f.opfnamespace='{schema}'::regnamespace AND f.opfname IN('ordered_key_btree_ops','ordered_key_hash_ops')
                UNION ALL SELECT 'function:'||substring(p.proname FROM 13), 'pg_proc'::regclass, p.oid
                    FROM pg_proc p WHERE p.pronamespace='{schema}'::regnamespace
                    AND p.proname IN('ordered_key_eq','ordered_key_ne','ordered_key_lt','ordered_key_le',
                        'ordered_key_gt','ordered_key_ge','ordered_key_cmp','ordered_key_hash')
                UNION ALL SELECT 'operator:'||o.oprname, 'pg_operator'::regclass, o.oid
                    FROM pg_operator o WHERE o.oprnamespace='{schema}'::regnamespace
                    AND o.oprleft='{schema}.ordered_key'::regtype AND o.oprright='{schema}.ordered_key'::regtype)
            SELECT m.label, m.objid, EXISTS(SELECT 1 FROM pg_depend d JOIN pg_extension e ON e.oid=d.refobjid
                WHERE d.refclassid='pg_extension'::regclass AND d.classid=m.classid AND d.objid=m.objid
                    AND d.deptype='e' AND e.extname='ankus_custom_types')
            FROM members m ORDER BY m.label COLLATE "C"
            """, connection);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(context.CancellationToken);
        var result = new Dictionary<string, uint>(StringComparer.Ordinal);
        while (await reader.ReadAsync(context.CancellationToken))
        {
            string name = reader.GetString(0);
            Assert.IsTrue(reader.GetBoolean(2), name);
            result.Add(name, reader.GetFieldValue<uint>(1));
        }

        Assert.AreSequenceEqual(["class:btree", "class:hash", "family:btree", "family:hash", "function:cmp",
            "function:eq", "function:ge", "function:gt", "function:hash", "function:le", "function:lt", "function:ne",
            "operator:<", "operator:<=", "operator:<>", "operator:=", "operator:>", "operator:>=", "type"], [.. result.Keys]);
        return result;
    }

    /// <summary>
    /// Captures unrelated shadow types, helpers, operator families, classes and their equality operator.
    /// </summary>
    private Task<uint[]> ShadowIdentities(NpgsqlConnection connection)
        => Scalar<uint[]>(connection, """
            SELECT ARRAY['operators_shadow.ordered_key'::regtype::oid,
                'operators_shadow.ordered_key_hash(integer)'::regprocedure::oid,
                'operators_shadow.ordered_key_cmp(integer,integer)'::regprocedure::oid,
                'operators_shadow.=(integer,integer)'::regoperator::oid,
                (SELECT oid FROM pg_opfamily WHERE opfnamespace='operators_shadow'::regnamespace AND opfname='ordered_key_btree_ops'),
                (SELECT oid FROM pg_opfamily WHERE opfnamespace='operators_shadow'::regnamespace AND opfname='ordered_key_hash_ops'),
                (SELECT oid FROM pg_opclass WHERE opcnamespace='operators_shadow'::regnamespace AND opcname='ordered_key_btree_ops'),
                (SELECT oid FROM pg_opclass WHERE opcnamespace='operators_shadow'::regnamespace AND opcname='ordered_key_hash_ops')]
            """);

    /// <summary>
    /// Executes statements whose results are checked separately.
    /// </summary>
    private async Task Execute(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(context.CancellationToken);
    }

    /// <summary>
    /// Reads one exact typed SQL scalar.
    /// </summary>
    private async Task<T> Scalar<T>(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return Assert.IsInstanceOfType<T>(await command.ExecuteScalarAsync(context.CancellationToken));
    }

    /// <summary>
    /// Reads every text row without collapsing duplicates.
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

    /// <summary>
    /// Requires the intended index to constrain an actual index scan rather than filtering a full scan.
    /// </summary>
    private async Task AssertIndexPlan(NpgsqlConnection connection, string sql, string index)
    {
        using JsonDocument plan = JsonDocument.Parse(await Scalar<string>(connection, "EXPLAIN(FORMAT JSON,COSTS OFF) " + sql));
        Assert.Contains(node =>
            node.GetProperty("Node Type").GetString() == "Index Scan" &&
            node.TryGetProperty("Index Name", out JsonElement name) && name.GetString() == index &&
            node.TryGetProperty("Index Cond", out JsonElement condition) && !string.IsNullOrWhiteSpace(condition.GetString()),
            PlanNodes(plan.RootElement[0].GetProperty("Plan")), plan.RootElement.ToString());
    }

    /// <summary>
    /// Enumerates nested plans so a sort or aggregate does not hide the access-method scan.
    /// </summary>
    private static IEnumerable<JsonElement> PlanNodes(JsonElement plan)
    {
        yield return plan;
        if (plan.TryGetProperty("Plans", out JsonElement children))
        {
            foreach (JsonElement child in children.EnumerateArray())
            {
                foreach (JsonElement nested in PlanNodes(child))
                {
                    yield return nested;
                }
            }
        }
    }
}
