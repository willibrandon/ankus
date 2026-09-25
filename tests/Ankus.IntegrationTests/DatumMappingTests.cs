using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Executes reusable scalar mappings through the real Native AOT PostgreSQL boundary.
/// </summary>
/// <param name="context">The test cancellation context.</param>
[TestClass]
public sealed class DatumMappingTests(TestContext context)
{
    /// <summary>
    /// Registration and absent values do not construct converters, while zero remains present.
    /// </summary>
    [TestMethod]
    public async Task MappedRegistrationAndNullsDoNotInvokeConverters()
    {
        await using NpgsqlConnection connection = await Open();
        Assert.AreEqual("0|0|0", await Scalar<string>(connection, "SELECT datum_mappings.counts()"));
        Assert.IsTrue(await Scalar<bool>(connection, "SELECT datum_mappings.echo(NULL) IS NULL"));
        Assert.AreEqual("0|0|0", await Scalar<string>(connection, "SELECT datum_mappings.counts()"));
        Assert.AreEqual(0, await Scalar<int>(connection, "SELECT datum_mappings.make(0::oid)::integer"));
        Assert.AreEqual("1|1|1", await Scalar<string>(connection, "SELECT datum_mappings.counts()"));
        Assert.AreEqual(42, await Scalar<int>(connection, "SELECT datum_mappings.make(42::oid)::integer"));
        Assert.AreEqual("1|2|2", await Scalar<string>(connection, "SELECT datum_mappings.counts()"));
    }

    /// <summary>
    /// Independently known stored values exercise both wrappers and their distinct converters.
    /// </summary>
    /// <param name="value">The stored manual integer.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(42)]
    [DataRow(16777215)]
    public async Task MappedByValueTypesKeepBitsAndManagedIdentity(int value)
    {
        await using NpgsqlConnection connection = await Open();
        int process = connection.ProcessID;
        Assert.AreEqual(value, await Scalar<int>(connection, $"SELECT datum_mappings.echo('{value}')::integer"));
        Assert.AreEqual(value + 1, await Scalar<int>(connection, $"SELECT datum_mappings.alias_integer('{value}')"));
        Assert.AreEqual(value, await Scalar<int>(connection, $"SELECT datum_mappings.alias_make({value + 1}::oid)::integer"));
        Assert.AreEqual("4:true:true", await Scalar<string>(connection, """
            SELECT typlen::text||':'||typbyval::text||':'||typisdefined::text
            FROM pg_type WHERE oid='datum_mappings.u24'::regtype
            """));
        Assert.IsTrue(await Scalar<bool>(connection, $"SELECT '{value}'::datum_mappings.u24 OPERATOR(datum_mappings.@=) '{value}'"));
        Assert.IsFalse(await Scalar<bool>(connection, "SELECT '0'::datum_mappings.u24 OPERATOR(datum_mappings.@=) '1'"));
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => Execute(connection,
            "SELECT datum_mappings.make(16777216::oid)"));
        Assert.AreEqual("22003", error.SqlState);
        Assert.AreEqual("mapped value exceeds 24 bits", error.MessageText);
        Assert.AreEqual(7, await Scalar<int>(connection, "SELECT datum_mappings.make(7::oid)::integer"));
        Assert.AreEqual(process, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }

    /// <summary>
    /// Fixed-size by-reference storage preserves both components and survives source-owner disposal.
    /// </summary>
    [TestMethod]
    public async Task MappedFixedStoragePreservesEveryComponentAndOwner()
    {
        await using NpgsqlConnection connection = await Open();
        Assert.AreEqual("16:false:double:p", await Scalar<string>(connection, """
            SELECT typlen::text||':'||typbyval::text||':'||CASE typalign WHEN 'd' THEN 'double' ELSE typalign::text END||':'||typstorage::text
            FROM pg_type WHERE oid='datum_mappings.complex'::regtype
            """));
        Assert.AreEqual("1.25,-2.5", await Scalar<string>(connection, "SELECT datum_mappings.complex_echo('1.25,-2.5')::text"));
        Assert.AreEqual("1.25,-2.5", await Scalar<string>(connection, "SELECT datum_mappings.complex_from_spi()::text"));
        Assert.AreEqual("-9223372036854775808|0", await Scalar<string>(connection, "SELECT datum_mappings.complex_bits('-0,0')"));
        Assert.AreEqual("4608308318706860032|-4610560118520545280", await Scalar<string>(connection,
            "SELECT datum_mappings.complex_bits(datum_mappings.complex_from_spi())"));
        Assert.IsTrue(await Scalar<bool>(connection, "SELECT datum_mappings.complex_echo(NULL) IS NULL"));
        await Execute(connection, "CREATE TEMP TABLE mapped_complex_values(value datum_mappings.complex); INSERT INTO mapped_complex_values VALUES('3.5,-7.25'),('0,-0')");
        Assert.AreSequenceEqual(["3.5,-7.25", "0,-0"], await Strings(connection,
            "SELECT datum_mappings.complex_echo(value)::text FROM mapped_complex_values ORDER BY ctid"));
    }

    /// <summary>
    /// Declared domains retain exact identity for present and absent raw reads and native output checks.
    /// </summary>
    [TestMethod]
    public async Task MappedDomainsRejectSiblingAndBaseIdentity()
    {
        await using NpgsqlConnection connection = await Open();
        int process = connection.ProcessID;
        Assert.AreEqual(42, await Scalar<int>(connection, "SELECT datum_mappings.positive_make(42)::integer"));
        Assert.AreEqual(42, await Scalar<int>(connection, "SELECT datum_mappings.positive_read('SELECT 42::datum_mappings.positive')"));
        Assert.IsTrue(await Scalar<bool>(connection, "SELECT datum_mappings.positive_read('SELECT NULL::datum_mappings.positive') IS NULL"));
        Assert.AreEqual("datum_mappings.positive", await Scalar<string>(connection, "SELECT pg_typeof(datum_mappings.positive_make(42))::text"));
        uint expected = await Scalar<uint>(connection, "SELECT 'datum_mappings.positive'::regtype::oid");
        foreach (string sql in new[] { "SELECT 42", "SELECT NULL::integer", "SELECT 42::datum_mappings.other_positive", "SELECT NULL::datum_mappings.other_positive" })
        {
            uint actual = sql.Contains("other_positive", StringComparison.Ordinal)
                ? await Scalar<uint>(connection, "SELECT 'datum_mappings.other_positive'::regtype::oid") : 23U;
            PostgresException identity = await Assert.ThrowsExactlyAsync<PostgresException>(() => Execute(connection,
                $"SELECT datum_mappings.positive_read('{sql}')"));
            Assert.AreEqual("38000", identity.SqlState);
            Assert.AreEqual($"PostgreSQL datum type OID {actual} does not match mapped type OID {expected}.", identity.MessageText);
            Assert.AreEqual(7, await Scalar<int>(connection, "SELECT datum_mappings.positive_read('SELECT 7::datum_mappings.positive')"));
        }

        PostgresException constraint = await Assert.ThrowsExactlyAsync<PostgresException>(() => Execute(connection,
            "SELECT datum_mappings.positive_make(-1)"));
        Assert.AreEqual("23514", constraint.SqlState);
        Assert.AreEqual("value for domain datum_mappings.positive violates check constraint \"positive_check\"", constraint.MessageText);
        Assert.AreEqual(9, await Scalar<int>(connection, "SELECT datum_mappings.positive_make(9)::integer"));
        Assert.AreEqual(process, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }

    /// <summary>
    /// One-way mappings work in their permitted directions, and defaults require only type identity.
    /// </summary>
    [TestMethod]
    public async Task MappedDirectionsAndDeclaredParametersUseTheirOwnConverters()
    {
        await using NpgsqlConnection connection = await Open();
        Assert.AreEqual(107, await Scalar<int>(connection, "SELECT datum_mappings.read_only(7)"));
        Assert.AreEqual(-7, await Scalar<int>(connection, "SELECT datum_mappings.write_only(7)"));
        Assert.AreEqual(-4, await Scalar<int>(connection, "SELECT datum_mappings.call_argument(7)"));
        Assert.AreEqual(17, await Scalar<int>(connection, "SELECT datum_mappings.call_default()"));
        Assert.AreEqual("héllo", await Scalar<string>(connection, "SELECT datum_mappings.declared_parameter('héllo')"));
        Assert.IsTrue(await Scalar<bool>(connection, "SELECT datum_mappings.declared_parameter(NULL) IS NULL"));
        Assert.AreSequenceEqual(["-1", "0", "1", "42", "<NULL>"], await Strings(connection, """
            SELECT coalesce(datum_mappings.sign_echo(value)::text,'<NULL>')
            FROM (VALUES(1,-1),(2,0),(3,1),(4,42),(5,NULL)) AS t(position,value) ORDER BY position
            """));
    }

    /// <summary>
    /// Writer-produced typed NULL remains valid while wrong OIDs and expired owners are rejected.
    /// </summary>
    /// <param name="mode">The invalid identity or lifetime mode.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public async Task MappedWritersValidatePresentAndNullResults(int mode)
    {
        await using NpgsqlConnection connection = await Open();
        int process = connection.ProcessID;
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => Execute(connection,
            $"SELECT datum_mappings.invalid_result({mode})"));
        Assert.AreEqual("38000", error.SqlState);
        Assert.AreEqual(mode < 2 ? "PostgreSQL datum type OID 20 does not match mapped type OID 23."
            : "The datum's memory context has been deleted." + Environment.NewLine + "Object name: 'PgDatum'.", error.MessageText);
        Assert.IsTrue(await Scalar<bool>(connection, "SELECT datum_mappings.invalid_result(4) IS NULL"));
        Assert.AreEqual(42, await Scalar<int>(connection, "SELECT datum_mappings.invalid_result(5)"));
        Assert.AreEqual(process, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }

    /// <summary>
    /// Detached text survives raw result cleanup, external TOAST storage removal, and subsequent writing.
    /// </summary>
    [TestMethod]
    public async Task MappedReferenceValuesOutliveToastedSources()
    {
        await using NpgsqlConnection connection = await Open();
        string expected = string.Concat(Enumerable.Repeat("héllo", 10000));
        await Execute(connection, """
            CREATE TEMP TABLE mapped_toast(value text);
            ALTER TABLE mapped_toast ALTER COLUMN value SET STORAGE EXTERNAL;
            INSERT INTO mapped_toast VALUES(repeat('héllo',10000));
            """);
        Assert.AreEqual(expected, await Scalar<string>(connection, "SELECT datum_mappings.text_from_store()"));
        Assert.IsTrue(await Scalar<bool>(connection, "SELECT to_regclass('mapped_toast') IS NULL"));
        Assert.AreEqual(expected, await Scalar<string>(connection, "SELECT datum_mappings.text_echo(repeat('héllo',10000))"));
        Assert.AreEqual(string.Empty, await Scalar<string>(connection, "SELECT datum_mappings.text_echo('')"));
        Assert.IsTrue(await Scalar<bool>(connection, "SELECT datum_mappings.text_echo(NULL) IS NULL"));
    }

    /// <summary>
    /// Converter errors preserve diagnostics and leave the same backend able to execute another call.
    /// </summary>
    /// <param name="value">The input triggering one converter direction.</param>
    /// <param name="state">The expected SQLSTATE.</param>
    /// <param name="message">The exact message.</param>
    /// <param name="detail">The exact detail.</param>
    [TestMethod]
    [DataRow("reader-error", "P8501", "mapped reader failed", "detached text")]
    [DataRow("writer-error", "P8502", "mapped writer failed", "managed text")]
    public async Task MappedConverterErrorsRecoverInTheSameSession(string value, string state, string message, string detail)
    {
        await using NpgsqlConnection connection = await Open();
        int process = connection.ProcessID;
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => Execute(connection,
            $"SELECT datum_mappings.text_echo('{value}')"));
        Assert.AreEqual(state, error.SqlState);
        Assert.AreEqual(message, error.MessageText);
        Assert.AreEqual(detail, error.Detail);
        Assert.AreEqual("use another value", error.Hint);
        Assert.AreEqual("recovered", await Scalar<string>(connection, "SELECT datum_mappings.text_echo('recovered')"));
        Assert.AreEqual(process, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }

    /// <summary>
    /// Both set executors and independent TABLE columns convert retained values and finish cleanup.
    /// </summary>
    /// <param name="function">The streaming or materializing function.</param>
    [TestMethod]
    [DataRow("text_rows")]
    [DataRow("text_materialized")]
    public async Task MappedSetsAndTablesKeepValuesAndCleanup(string function)
    {
        await using NpgsqlConnection connection = await Open();
        int process = connection.ProcessID;
        string value = string.Concat(Enumerable.Repeat("héllo", 10000));
        Assert.AreSequenceEqual([value, value, "<NULL>"], await Strings(connection,
            $"SELECT coalesce(v,'<NULL>') FROM datum_mappings.{function}(repeat('héllo',10000),false) AS v"));
        Assert.AreEqual(1, await Scalar<int>(connection, "SELECT datum_mappings.cleanup_count()"));
        Assert.AreEqual("early", await Scalar<string>(connection,
            $"SELECT v FROM datum_mappings.{function}('early',false) AS v LIMIT 1"));
        Assert.AreEqual(2, await Scalar<int>(connection, "SELECT datum_mappings.cleanup_count()"));
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => Execute(connection,
            $"SELECT * FROM datum_mappings.{function}('failure',true)"));
        Assert.AreEqual("P8503", error.SqlState);
        Assert.AreEqual("mapped iterator failed", error.MessageText);
        Assert.AreEqual(3, await Scalar<int>(connection, "SELECT datum_mappings.cleanup_count()"));
        Assert.AreSequenceEqual(["0|1.25,-2.5", "<NULL>|<NULL>"], await Strings(connection, """
            SELECT coalesce(number::text,'<NULL>')||'|'||coalesce(complex::text,'<NULL>')
            FROM datum_mappings.mapped_table('0','1.25,-2.5')
            """));
        Assert.AreSequenceEqual(["recovered", "recovered", "<NULL>"], await Strings(connection,
            $"SELECT coalesce(v,'<NULL>') FROM datum_mappings.{function}('recovered',false) AS v"));
        Assert.AreEqual(4, await Scalar<int>(connection, "SELECT datum_mappings.cleanup_count()"));
        Assert.AreEqual(process, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }

    /// <summary>
    /// Aggregate transitions retain mapped reference state after many later callbacks and SQL NULLs.
    /// </summary>
    [TestMethod]
    public async Task MappedAggregateStateRetainsDetachedValues()
    {
        await using NpgsqlConnection connection = await Open();
        string expected = string.Concat(Enumerable.Repeat("héllo", 10000));
        Assert.AreEqual(expected, await Scalar<string>(connection, """
            SELECT datum_mappings.first_text(CASE WHEN n<3 THEN NULL WHEN n=3 THEN repeat('héllo',10000) ELSE n::text END ORDER BY n)
            FROM generate_series(1,2000) AS n
            """));
        Assert.IsTrue(await Scalar<bool>(connection, "SELECT datum_mappings.first_text(NULL::text) IS NULL FROM generate_series(1,10)"));
        Assert.IsTrue(await Scalar<bool>(connection, "SELECT datum_mappings.first_text(value) IS NULL FROM (SELECT ''::text AS value WHERE false) t"));
        Assert.AreEqual(string.Empty, await Scalar<string>(connection, "SELECT datum_mappings.first_text(value ORDER BY n) FROM (VALUES(1,''),(2,'later')) t(n,value)"));
    }

    /// <summary>
    /// Live catalog lookups observe replacement OIDs and refuse to reinterpret retained parameters.
    /// </summary>
    [TestMethod]
    public async Task MappedExternalIdentityTracksDdlWithoutRebindingOldParameters()
    {
        await using NpgsqlConnection connection = await Open();
        int process = connection.ProcessID;
        await Execute(connection, "CREATE SCHEMA datum_mapping_live; CREATE DOMAIN datum_mapping_live.value AS integer");
        try
        {
            uint first = await Scalar<uint>(connection, "SELECT 'datum_mapping_live.value'::regtype::oid");
            Assert.AreEqual($"42|{first}", await Scalar<string>(connection, "SELECT datum_mappings.live_parameter(42,true)"));
            Assert.AreEqual(42, await Scalar<int>(connection, "SELECT datum_mappings.saved_parameter()"));
            await Execute(connection, "DROP DOMAIN datum_mapping_live.value");
            await AssertUndefinedMappedIdentity(connection, "value", "SELECT datum_mappings.live_parameter(7,false)");
            await Execute(connection, "CREATE TYPE datum_mapping_live.value");
            Assert.IsFalse(await Scalar<bool>(connection, "SELECT typisdefined FROM pg_type WHERE typnamespace='datum_mapping_live'::regnamespace AND typname='value'"));
            await AssertUndefinedMappedIdentity(connection, "value", "SELECT datum_mappings.live_parameter(7,false)");
            await Execute(connection, "DROP TYPE datum_mapping_live.value; CREATE DOMAIN datum_mapping_live.value AS integer");
            uint second = await Scalar<uint>(connection, "SELECT 'datum_mapping_live.value'::regtype::oid");
            Assert.AreNotEqual(first, second);
            PostgresException stale = await Assert.ThrowsExactlyAsync<PostgresException>(() => Execute(connection, "SELECT datum_mappings.saved_parameter()"));
            Assert.AreEqual("38000", stale.SqlState);
            Assert.AreEqual("The mapped PostgreSQL parameter type has changed since the parameter was created.", stale.MessageText);
            Assert.AreEqual($"7|{second}", await Scalar<string>(connection, "SELECT datum_mappings.live_parameter(7,false)"));
            Assert.AreEqual(process, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
        }
        finally
        {
            await Execute(connection, "DROP SCHEMA datum_mapping_live CASCADE");
        }
    }

    /// <summary>
    /// Unsupported ordinary typed results fail before sequence increments, which SQL rollback cannot hide.
    /// </summary>
    /// <param name="mode">The direct, mixed, session, prepared or function result path.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    [DataRow(5)]
    [DataRow(6)]
    public async Task UnsupportedMappedResultsFailBeforeSqlSideEffects(int mode)
    {
        await using NpgsqlConnection connection = await Open();
        int process = connection.ProcessID;
        await Execute(connection, """
            CREATE TEMP SEQUENCE mapped_effect;
            CREATE FUNCTION pg_temp.mapped_effect() RETURNS text LANGUAGE sql AS $f$ SELECT nextval('mapped_effect')::text $f$;
            """);
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => Execute(connection,
            $"SELECT datum_mappings.unsupported_result({mode})"));
        Assert.AreEqual("38000", error.SqlState);
        Assert.AreEqual("The mapped PostgreSQL type has no datum reader.", error.MessageText);
        Assert.IsFalse(await Scalar<bool>(connection, "SELECT is_called FROM mapped_effect"));
        Assert.AreEqual("recovered", await Scalar<string>(connection, "SELECT datum_mappings.text_echo('recovered')"));
        Assert.AreEqual(process, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }

    /// <summary>
    /// Pseudotype lookup fails inside the native guard before constructing or invoking the mapped writer.
    /// </summary>
    [TestMethod]
    public async Task MappedConcreteResolverRejectsPseudotypes()
    {
        await using NpgsqlConnection connection = await Open();
        int process = connection.ProcessID;
        await AssertUndefinedMappedIdentity(connection, "cstring", "SELECT datum_mappings.pseudo_parameter()");
        Assert.AreEqual(42, await Scalar<int>(connection, "SELECT datum_mappings.make(42::oid)::integer"));
        Assert.AreEqual(process, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }

    /// <summary>
    /// Raw mapped enum arrays select their declared converter and preserve unnamed enum values and SQL NULL.
    /// </summary>
    /// <param name="absent">Whether the input is SQL NULL.</param>
    /// <param name="shaped">Whether the target is a shaped array.</param>
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public async Task MappedArrayReadsPreserveDeclaredEnumValuesAndSqlNull(bool absent, bool shaped)
    {
        await using NpgsqlConnection connection = await Open();
        Assert.AreEqual(absent ? "NULL" : "-1|0|1|42", await Scalar<string>(connection,
            $"SELECT datum_mappings.read_mapped_array({absent},{shaped})"));
        Assert.AreEqual(42, await Scalar<int>(connection, "SELECT datum_mappings.sign_echo(42)"));
    }

    /// <summary>
    /// Each set transport handles present CLR values producing SQL NULL and still checks invalid NULL envelopes.
    /// </summary>
    /// <param name="function">The streaming, materializing or TABLE path.</param>
    [TestMethod]
    [DataRow("writer_rows")]
    [DataRow("writer_materialized")]
    [DataRow("writer_table")]
    public async Task MappedSetWritersPreserveAndValidateTypedNull(string function)
    {
        await using NpgsqlConnection connection = await Open();
        int process = connection.ProcessID;
        string from = function == "writer_table" ? "AS t" : "AS t(value)";
        Assert.AreSequenceEqual(["<NULL>", "42", "<NULL>"], await Strings(connection,
            $"SELECT coalesce(value::text,'<NULL>') FROM datum_mappings.{function}(4) {from}"));
        if (function == "writer_table")
        {
            Assert.AreSequenceEqual(["1", "2", "3"], await Strings(connection,
                "SELECT position::text FROM datum_mappings.writer_table(4)"));
        }

        foreach (int mode in new[] { 1, 3 })
        {
            PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => Execute(connection,
                $"SELECT * FROM datum_mappings.{function}({mode})"));
            Assert.AreEqual("38000", error.SqlState);
            Assert.AreEqual(mode == 1 ? "PostgreSQL datum type OID 20 does not match mapped type OID 23."
                : "The datum's memory context has been deleted." + Environment.NewLine + "Object name: 'PgDatum'.", error.MessageText);
        }

        Assert.AreSequenceEqual(["42", "42", "<NULL>"], await Strings(connection,
            $"SELECT coalesce(value::text,'<NULL>') FROM datum_mappings.{function}(5) {from}"));
        Assert.AreEqual(process, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }

    /// <summary>
    /// Aggregate final callbacks preserve writer-produced NULL while rejecting wrong and expired typed NULLs.
    /// </summary>
    [TestMethod]
    public async Task MappedAggregateWriterValidatesTypedNullResults()
    {
        await using NpgsqlConnection connection = await Open();
        int process = connection.ProcessID;
        Assert.IsTrue(await Scalar<bool>(connection, "SELECT datum_mappings.writer_final(value) IS NULL FROM (VALUES(1),(3)) t(value)"));
        Assert.AreEqual(42, await Scalar<int>(connection, "SELECT datum_mappings.writer_final(value) FROM (VALUES(2),(3)) t(value)"));
        foreach (int mode in new[] { 1, 3 })
        {
            PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => Execute(connection,
                $"SELECT datum_mappings.writer_final({mode})"));
            Assert.AreEqual("38000", error.SqlState);
            Assert.AreEqual(mode == 1 ? "PostgreSQL datum type OID 20 does not match mapped type OID 23."
                : "The datum's memory context has been deleted." + Environment.NewLine + "Object name: 'PgDatum'.", error.MessageText);
        }

        Assert.AreEqual(42, await Scalar<int>(connection, "SELECT datum_mappings.writer_final(5)"));
        Assert.AreEqual(process, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }

    /// <summary>
    /// Exactly typed writer NULLs reach PostgreSQL's NOT NULL domain check on every output transport.
    /// </summary>
    /// <param name="sql">The scalar, set, TABLE or aggregate output path.</param>
    [TestMethod]
    [DataRow("SELECT datum_mappings.required_make(0)")]
    [DataRow("SELECT * FROM datum_mappings.required_rows()")]
    [DataRow("SELECT * FROM datum_mappings.required_materialized()")]
    [DataRow("SELECT * FROM datum_mappings.required_table()")]
    [DataRow("SELECT datum_mappings.required_final(0)")]
    public async Task MappedWriterNullsCannotBypassDomainConstraints(string sql)
    {
        await using NpgsqlConnection connection = await Open();
        int process = connection.ProcessID;
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => Execute(connection, sql));
        Assert.AreEqual("23502", error.SqlState);
        Assert.AreEqual("domain datum_mappings.required does not allow null values", error.MessageText);
        Assert.AreEqual(42, await Scalar<int>(connection, "SELECT datum_mappings.required_make(1)::integer"));
        Assert.AreEqual(process, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }

    /// <summary>
    /// Named and OID invocation validate writer-produced domain NULLs but leave default placeholders unevaluated.
    /// </summary>
    /// <param name="byOid">Whether to call by exact function OID.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task MappedFunctionArgumentsValidateNullDomainsBeforeStrictTargets(bool byOid)
    {
        await using NpgsqlConnection connection = await Open();
        int process = connection.ProcessID;
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => Execute(connection,
            $"SELECT datum_mappings.required_call(0,false,{byOid})"));
        Assert.AreEqual("23502", error.SqlState);
        Assert.AreEqual("domain datum_mappings.required does not allow null values", error.MessageText);
        Assert.AreEqual(42, await Scalar<int>(connection, $"SELECT datum_mappings.required_call(0,true,{byOid})"));
        Assert.AreEqual(42, await Scalar<int>(connection, $"SELECT datum_mappings.required_call(1,false,{byOid})"));
        Assert.AreEqual(process, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }

    /// <summary>
    /// Asserts the concrete catalog resolver's exact guarded diagnostic for a missing, shell or pseudo type.
    /// </summary>
    private async Task AssertUndefinedMappedIdentity(NpgsqlConnection connection, string name, string sql)
    {
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => Execute(connection, sql));
        Assert.AreEqual("42704", error.SqlState);
        Assert.AreEqual($"PostgreSQL concrete defined type \"{name}\" does not exist in the declared schema", error.MessageText);
    }

    /// <summary>
    /// Opens a fresh PostgreSQL backend so converter state and cleanup counters are independent.
    /// </summary>
    private Task<NpgsqlConnection> Open() => PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);

    /// <summary>
    /// Executes a scalar assertion without changing the session.
    /// </summary>
    private async Task<T> Scalar<T>(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        object? value = await command.ExecuteScalarAsync(context.CancellationToken);
        return (T)value!;
    }

    /// <summary>
    /// Executes statements and consumes every result before the next same-session assertion.
    /// </summary>
    private async Task Execute(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(context.CancellationToken);
    }

    /// <summary>
    /// Copies all text rows in executor order for independent expected-value comparisons.
    /// </summary>
    private async Task<string[]> Strings(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(context.CancellationToken);
        var values = new List<string>();
        while (await reader.ReadAsync(context.CancellationToken))
        {
            values.Add(reader.GetString(0));
        }

        return [.. values];
    }
}
