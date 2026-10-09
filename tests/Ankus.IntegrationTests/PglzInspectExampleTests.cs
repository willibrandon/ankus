using System.Globalization;
using Ankus.Examples.PglzInspect;
using Ankus.Testing;
using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Executes the ported pgrx pglz_inspect sample through its published Native AOT extension.
/// </summary>
/// <param name="context">The current cancellation and diagnostic context.</param>
/// <remarks>
/// PostgreSQL's own TOAST compression is the independent oracle: a text value stored inline with PGLZ occupies its
/// compressed length plus an eight-byte header, so <c>pg_column_size(v) - 8</c> must equal the compressed size the
/// sample reports for the same bytes.
/// </remarks>
[TestClass]
public sealed class PglzInspectExampleTests(TestContext context)
{
    /// <summary>
    /// The histogram labels in their reported order.
    /// </summary>
    private const string Buckets = "0.0-0.2,0.2-0.4,0.4-0.6,0.6-0.8,0.8-1.0,incompressible";

    /// <summary>
    /// Owns the test publication that installs the sample's ported pgrx backend tests.
    /// </summary>
    private static readonly SampleBackendFixture s_backend = new("Ankus.Examples.PglzInspect");

    /// <summary>
    /// Gets the sample's generated backend test cases.
    /// </summary>
    public static IEnumerable<TestDataRow<PgTestCase>> BackendCases
        => PglzInspectTests.PostgresTests.Cases.Select(static test => new TestDataRow<PgTestCase>(test)
        {
            DisplayName = test.Name,
            IgnoreMessage = test.IgnoreReason,
        });

    /// <summary>
    /// Runs every pgrx pglz_inspect test inside PostgreSQL: SQL functions through SPI and the wrapper directly.
    /// </summary>
    /// <param name="test">The generated backend test case.</param>
    [TestMethod]
    [DynamicData(nameof(BackendCases))]
    public async Task PglzInspectSampleBackendTestsPass(PgTestCase test)
    {
        PostgresExtensionTest extension = await s_backend.GetAsync(context.CancellationToken);
        await extension.RunTestAsync(test, context.CancellationToken);
    }

    /// <summary>
    /// Stops the backend-test cluster after the class finishes.
    /// </summary>
    /// <returns>A task that completes after shutdown.</returns>
    [ClassCleanup]
    public static async Task CleanupAsync() => await s_backend.DisposeAsync();

    /// <summary>
    /// Per-value and column results agree byte for byte with PostgreSQL's TOAST compression, savings follow
    /// <c>reltuples</c>, and the recommended DDL runs.
    /// </summary>
    [TestMethod]
    public Task PglzInspectSampleMatchesToastCompression() => RunInstalledAsync(async (connection, transaction, token) =>
    {
        int version = await ScalarAsync<int>(connection, transaction, "SELECT current_setting('server_version_num')::integer", token);
        bool compressionClause = version >= 140000;
        await ExecuteAsync(connection, transaction, $"""
            {(compressionClause ? "SET LOCAL default_toast_compression = 'pglz';" : "")}
            CREATE TABLE oracle (v text);
            INSERT INTO oracle SELECT repeat(md5(g::text), 128) FROM generate_series(1, 20) AS g;
            """, token);
        string compressed = compressionClause ? "pg_column_compression(v) = 'pglz' AND " : "";
        Assert.AreEqual(20L, await ScalarAsync<long>(connection, transaction, $"""
            SELECT count(*) FROM oracle, pglz_size(convert_to(v, 'UTF8')) AS probe
            WHERE {compressed}probe.accepted AND probe.raw_bytes = 4096
                AND probe.compressed_bytes = pg_column_size(v) - 8
                AND probe.ratio = probe.compressed_bytes::float8 / probe.raw_bytes
            """, token));

        // The table has never been analyzed, so it has no row estimate and no estimated savings.
        string analysis = """
            SELECT concat_ws(',', sampled_rows, avg_raw_bytes, avg_compressed = expected.compressed, avg_ratio = expected.compressed / 4096,
                pct_accepted, pct_incompressible, est_savings_bytes, est_savings_bytes = trunc((4096 - expected.compressed) * 20))
            FROM pglz_analyze_column('oracle'::regclass, 'v'),
                (SELECT sum(pg_column_size(v) - 8)::float8 / count(*) AS compressed FROM oracle) AS expected
            """;
        string before = await ScalarAsync<string>(connection, transaction, analysis, token);
        Assert.StartsWith("20,4096,t,t,1,0,0,", before);
        await ExecuteAsync(connection, transaction, "ANALYZE oracle", token);
        string after = await ScalarAsync<string>(connection, transaction, analysis, token);
        Assert.StartsWith("20,4096,t,t,1,0,", after);
        Assert.EndsWith(",t", after);
        Assert.AreEqual("20,0,0,0,0,0", await ScalarAsync<string>(connection, transaction,
            "SELECT string_agg(row_count::text, ',' ORDER BY ordinality) FROM pglz_ratio_histogram('oracle'::regclass, 'v') WITH ORDINALITY",
            token));

        long totalCompressed = await ScalarAsync<long>(connection, transaction, "SELECT sum(pg_column_size(v) - 8)::bigint FROM oracle", token);
        double savings = Math.Max((1.0 - (double)totalCompressed / (20 * 4096)) * 100.0, 0.0);
        string ddl = compressionClause ? "ALTER TABLE oracle ALTER COLUMN v SET COMPRESSION pglz;"
            : "-- pg13: SET COMPRESSION unavailable; use a BEFORE INSERT trigger that PGLZ-compresses oracle.v into a bytea sibling column.";
        string recommendation = await ScalarAsync<string>(connection, transaction, "SELECT pglz_recommend('oracle'::regclass, 'v')", token);
        Assert.AreEqual(string.Create(CultureInfo.InvariantCulture,
            $"RECOMMEND: PGLZ saves ~{Math.Round(savings, MidpointRounding.ToEven):F0}% on oracle.v (20 of 20 sampled rows accepted). Run: {ddl}"),
            recommendation);
        if (compressionClause)
        {
            // The column used the default method; the recommended DDL selects PGLZ explicitly.
            const string Method = "SELECT attcompression::text FROM pg_attribute WHERE attrelid = 'oracle'::regclass AND attname = 'v'";
            Assert.AreEqual("", await ScalarAsync<string>(connection, transaction, Method, token));
            await ExecuteAsync(connection, transaction, recommendation[(recommendation.IndexOf("Run: ", StringComparison.Ordinal) + 5)..], token);
            Assert.AreEqual("p", await ScalarAsync<string>(connection, transaction, Method, token));
        }
    });

    /// <summary>
    /// Reproduces the README's events and mixed-distribution demos; the column histogram agrees with per-value probes.
    /// </summary>
    [TestMethod]
    public Task PglzInspectSampleReproducesReadmeDemos() => RunInstalledAsync(async (connection, transaction, token) =>
    {
        await ExecuteAsync(connection, transaction, """
            CREATE TABLE events AS
            SELECT i AS id, ('{"user":'||i||',"action":"click","meta":'||repeat('"x",',50)||'1}') AS payload
            FROM generate_series(1, 10000) i;
            """, token);
        Assert.AreEqual(Buckets + "|1000", await ScalarAsync<string>(connection, transaction,
            "SELECT string_agg(bucket, ',' ORDER BY ordinality) || '|' || sum(row_count) FROM pglz_ratio_histogram('events'::regclass, 'payload') WITH ORDINALITY",
            token));
        string recommendation = await ScalarAsync<string>(connection, transaction, "SELECT pglz_recommend('events'::regclass, 'payload')", token);
        Assert.StartsWith("RECOMMEND: PGLZ saves ~", recommendation);
        Assert.EndsWith("% on events.payload (1000 of 1000 sampled rows accepted). Run: " +
            await RecommendedDdlAsync(connection, transaction, "events", "payload", token), recommendation);

        await ExecuteAsync(connection, transaction, """
            CREATE TABLE mixed AS
            SELECT i AS id,
                   CASE (i % 5)
                     WHEN 0 THEN repeat('aaaaa', 100)
                     WHEN 1 THEN '{"user_id":'||i||',"event_type":"page_view","timestamp":"2024-01-'||(i%28+1)||'","session":"'||md5(i::text)||'"}'
                     WHEN 2 THEN repeat(md5(i::text), 3) || md5((i+1)::text)
                     WHEN 3 THEN 'log_entry_' || md5(i::text) || md5((i+1)::text) || md5((i+2)::text)
                     ELSE (SELECT string_agg(md5(random()::text), '') FROM generate_series(1, 4))
                   END AS payload
            FROM generate_series(1, 5000) i;
            """, token);
        string expected = await ScalarAsync<string>(connection, transaction, """
            SELECT string_agg(label || ':' || coalesce(counted, 0), ',' ORDER BY ordinality)
            FROM unnest(ARRAY['0.0-0.2','0.2-0.4','0.4-0.6','0.6-0.8','0.8-1.0','incompressible']) WITH ORDINALITY AS labels(label, ordinality)
            LEFT JOIN (
                SELECT CASE WHEN NOT probe.accepted THEN 'incompressible'
                    ELSE (ARRAY['0.0-0.2','0.2-0.4','0.4-0.6','0.6-0.8','0.8-1.0'])[least(floor(probe.ratio * 5)::integer, 4) + 1] END AS bucket,
                    count(*) AS counted
                FROM mixed, pglz_size(convert_to(payload, 'UTF8')) AS probe
                GROUP BY 1) AS probes ON probes.bucket = labels.label
            """, token);
        Assert.AreEqual(expected, await ScalarAsync<string>(connection, transaction,
            "SELECT string_agg(bucket || ':' || row_count, ',' ORDER BY ordinality) FROM pglz_ratio_histogram('mixed'::regclass, 'payload', 5000) WITH ORDINALITY",
            token));
        context.WriteLine($"Mixed-distribution histogram: {expected}");
    });

    /// <summary>
    /// Text is measured as its stored bytes, including backslashes that a bytea cast would reinterpret, and bytea and
    /// JSON columns keep their own representations.
    /// </summary>
    [TestMethod]
    public Task PglzInspectSampleMeasuresStoredBytes() => RunInstalledAsync(async (connection, transaction, token) =>
    {
        await ExecuteAsync(connection, transaction, """
            CREATE TABLE paths (v text, j jsonb, b bytea);
            INSERT INTO paths
            SELECT repeat('C:\Windows\System32\', 40), jsonb_build_object('line', repeat(E'a\nb', 50)), decode(repeat('00ff', 300), 'hex')
            FROM generate_series(1, 10);
            """, token);
        Assert.AreEqual("10,800,1", await ScalarAsync<string>(connection, transaction,
            "SELECT concat_ws(',', sampled_rows, avg_raw_bytes, pct_accepted) FROM pglz_analyze_column('paths'::regclass, 'v')", token));
        Assert.AreEqual("10,t", await ScalarAsync<string>(connection, transaction, """
            SELECT concat_ws(',', sampled_rows, avg_raw_bytes = (SELECT avg(octet_length(j::text)) FROM paths))
            FROM pglz_analyze_column('paths'::regclass, 'j')
            """, token));
        Assert.AreEqual("10,600", await ScalarAsync<string>(connection, transaction,
            "SELECT concat_ws(',', sampled_rows, avg_raw_bytes) FROM pglz_analyze_column('paths'::regclass, 'b')", token));
        Assert.AreEqual("0,0,f,1", await ScalarAsync<string>(connection, transaction,
            "SELECT concat_ws(',', raw_bytes, compressed_bytes, accepted, ratio) FROM pglz_size(''::bytea)", token));
        Assert.AreEqual(0L, await ScalarAsync<long>(connection, transaction, "SELECT count(*) FROM pglz_size(NULL)", token));
    });

    /// <summary>
    /// A LATIN1 database stores one byte per accented letter, so its text sample is half the UTF-8 size.
    /// </summary>
    [TestMethod]
    public async Task PglzInspectSampleUsesTheDatabaseEncoding()
    {
        CancellationToken token = context.CancellationToken;
        string database = "pglz_" + Guid.NewGuid().ToString("N");
        await using NpgsqlConnection administrator = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        await ExecuteAsync(administrator, null, $"CREATE DATABASE {database} TEMPLATE template0 ENCODING 'LATIN1' LC_COLLATE 'C' LC_CTYPE 'C'", token);
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(administrator.ConnectionString) { Database = database, Pooling = false };
            await using var connection = new NpgsqlConnection(builder.ConnectionString);
            await connection.OpenAsync(token);
            // PostgreSQL 19 stores new values with LZ4 when it is available; compare against PGLZ storage.
            string compression = PostgresFixture.Cluster.Installation.Version.Major >= 14
                ? "ALTER TABLE accents ALTER COLUMN v SET COMPRESSION pglz;"
                : string.Empty;
            await ExecuteAsync(connection, null, $"""
                CREATE EXTENSION ankus_pglz_inspect;
                CREATE TABLE accents (v text);
                {compression}
                INSERT INTO accents SELECT repeat('é', 3000) FROM generate_series(1, 3);
                """, token);
            Assert.AreEqual("3,3000,t", await ScalarAsync<string>(connection, null, """
                SELECT concat_ws(',', sampled_rows, avg_raw_bytes,
                    avg_compressed = (SELECT avg(pg_column_size(v) - 8) FROM accents))
                FROM pglz_analyze_column('accents'::regclass, 'v')
                """, token));
            Assert.AreEqual(6000, await ScalarAsync<int>(connection, null, "SELECT raw_bytes FROM pglz_size(convert_to(repeat('é', 3000), 'UTF8'))", token));
        }
        finally
        {
            await ExecuteAsync(administrator, null, $"DROP DATABASE {database} WITH (FORCE)", CancellationToken.None);
        }
    }

    /// <summary>
    /// Invalid strategies, columns, relations and privileges fail with specific SQLSTATEs; quoted names, NULLs and
    /// empty samples are handled, and the same backend recovers after each error.
    /// </summary>
    [TestMethod]
    public Task PglzInspectSampleRejectsInvalidInputAndRecovers() => RunInstalledAsync(async (connection, transaction, token) =>
    {
        await ExecuteAsync(connection, transaction, """"
            CREATE SCHEMA "Mixed Case";
            CREATE TABLE "Mixed Case"."Odd ""Name""" ("Value Column" text, secret text);
            INSERT INTO "Mixed Case"."Odd ""Name""" SELECT repeat('quoted ', 100), 'x' FROM generate_series(1, 12);
            CREATE ROLE pglz_inspect_reader;
            GRANT USAGE ON SCHEMA "Mixed Case" TO pglz_inspect_reader;
            """", token);
        const string Table = "'\"Mixed Case\".\"Odd \"\"Name\"\"\"'::regclass";
        foreach ((string sql, string state, string message) in new[]
        {
            ($"SELECT * FROM pglz_analyze_column({Table}, 'Value Column', 5, 'ALWAYS')", "22023",
                "unknown strategy \"ALWAYS\": expected 'default' or 'always'"),
            ($"SELECT * FROM pglz_analyze_column({Table}, 'Value Column', 5, E'a\"b\\\\c\\n\\u200b')", "22023",
                "unknown strategy \"a\\\"b\\\\c\\n\\u{200b}\": expected 'default' or 'always'"),
            ($"SELECT pglz_recommend({Table}, 'missing')", "42703", "column \"missing\" does not exist"),
            ("SELECT * FROM pglz_ratio_histogram(4294967295, 'v')", "42P01", "relation with OID 4294967295 does not exist"),
            ($"SET LOCAL ROLE pglz_inspect_reader; SELECT pglz_recommend({Table}, 'Value Column')", "42501",
                "permission denied for table Odd \"Name\""),
        })
        {
            await transaction.SaveAsync("pglz_failure", token);
            PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => ExecuteAsync(connection, transaction, sql, token));
            Assert.AreEqual(state, error.SqlState, sql);
            Assert.AreEqual(message, error.MessageText, sql);
            await transaction.RollbackAsync("pglz_failure", token);
            Assert.AreEqual(12, await ScalarAsync<int>(connection, transaction,
                $"SELECT sampled_rows FROM pglz_analyze_column({Table}, 'Value Column', 50, 'always')", token));
        }

        string recommendation = await ScalarAsync<string>(connection, transaction, $"SELECT pglz_recommend({Table}, 'Value Column')", token);
        Assert.StartsWith("RECOMMEND: PGLZ saves ~", recommendation);
        Assert.EndsWith("% on \"Mixed Case\".\"Odd \"\"Name\"\"\".Value Column (12 of 12 sampled rows accepted). Run: " +
            await RecommendedDdlAsync(connection, transaction, "\"Mixed Case\".\"Odd \"\"Name\"\"\"", "\"Value Column\"", token), recommendation);
        Assert.AreEqual("NO DATA: no non-null rows sampled from \"Mixed Case\".\"Odd \"\"Name\"\"\".Value Column", await ScalarAsync<string>(connection, transaction,
            $"SELECT pglz_recommend({Table}, 'Value Column', -5)", token));
        Assert.AreEqual(Buckets.Replace(",", ":0,", StringComparison.Ordinal) + ":0", await ScalarAsync<string>(connection, transaction,
            $"SELECT string_agg(bucket || ':' || row_count, ',' ORDER BY ordinality) FROM pglz_ratio_histogram({Table}, 'Value Column', 0) WITH ORDINALITY",
            token));
        Assert.AreEqual("0,0,0,1,0,0,0", await ScalarAsync<string>(connection, transaction,
            $"SELECT concat_ws(',', sampled_rows, avg_raw_bytes, avg_compressed, avg_ratio, pct_accepted, pct_incompressible, est_savings_bytes) FROM pglz_analyze_column({Table}, 'secret', 0)",
            token));
        Assert.IsTrue(await ScalarAsync<bool>(connection, transaction,
            $"SELECT pglz_recommend(NULL, 'v') IS NULL AND pglz_recommend({Table}, NULL) IS NULL AND NOT EXISTS (SELECT FROM pglz_analyze_column({Table}, 'secret', NULL))",
            token));
        Assert.AreEqual(connection.ProcessID, await ScalarAsync<int>(connection, transaction, "SELECT pg_backend_pid()", token));
    });

    /// <summary>
    /// Installs the sample in a rolled-back transaction of the shared cluster.
    /// </summary>
    private Task RunInstalledAsync(Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, Task> test)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(PglzInspectExampleTests), async (connection, transaction, token) =>
        {
            await ExecuteAsync(connection, transaction, "CREATE EXTENSION ankus_pglz_inspect", token);
            await test(connection, transaction, token);
        }, context.CancellationToken);

    /// <summary>
    /// Returns the DDL the sample recommends. PostgreSQL 14 added column compression; on 13 the sample gives pgrx's trigger advice.
    /// </summary>
    private static async Task<string> RecommendedDdlAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string table,
        string column, CancellationToken token)
        => await ScalarAsync<int>(connection, transaction, "SELECT current_setting('server_version_num')::integer", token) >= 140000
            ? $"ALTER TABLE {table} ALTER COLUMN {column} SET COMPRESSION pglz;"
            : $"-- pg13: SET COMPRESSION unavailable; use a BEFORE INSERT trigger that PGLZ-compresses {table}.{column} into a bytea sibling column.";

    private static async Task<T> ScalarAsync<T>(NpgsqlConnection connection, NpgsqlTransaction? transaction, string sql,
        CancellationToken token)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        return Assert.IsInstanceOfType<T>(await command.ExecuteScalarAsync(token));
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, string sql, CancellationToken token)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync(token);
    }
}
