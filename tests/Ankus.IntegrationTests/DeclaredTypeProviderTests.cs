using System.Globalization;
using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Executes SQL-provided raw and composite contracts through the installed Native AOT extension.
/// </summary>
/// <param name="context">The test cancellation context.</param>
[TestClass]
public sealed class DeclaredTypeProviderTests(TestContext context)
{
    /// <summary>
    /// Installation resolves every signature leaf and records exact catalog and extension identities.
    /// </summary>
    [TestMethod]
    public async Task DeclaredProvidersInstallEveryBoundSignature()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        uint u24 = await Scalar<uint>(connection, "SELECT 'type_providers.u24'::regtype::oid");
        uint pair = await Scalar<uint>(connection, "SELECT 'type_providers.pair'::regtype::oid");
        uint other = await Scalar<uint>(connection, "SELECT 'type_providers.other_pair'::regtype::oid");
        uint positive = await Scalar<uint>(connection, "SELECT 'type_providers.positive'::regtype::oid");
        uint rawArray = await Scalar<uint>(connection, "SELECT 'type_providers.u24[]'::regtype::oid");
        uint pairArray = await Scalar<uint>(connection, "SELECT 'type_providers.pair[]'::regtype::oid");
        Assert.AreNotEqual(pair, other);
        Assert.AreNotEqual(23U, u24);
        Assert.AreSequenceEqual([
            $"as_integer:{u24}:23:false", $"echo:{u24}:{u24}:false", $"equal:{u24} {u24}:16:false",
            $"first_pair:{pair}:{pair}:false", $"pair_array:{pairArray}:{pairArray}:false",
            $"pair_echo:{pair}:{pair}:false", $"pair_from_store::{pair}:false",
            $"pair_materialized:{pair} 16:{pair}:true", $"pair_rows:{pair} 16:{pair}:true",
            $"positive_echo:{positive}:{positive}:false", $"raw_array:{rawArray}:{rawArray}:false",
            $"wrong_pair:{other}:{pair}:false",
        ], await Strings(connection, """
            SELECT p.proname||':'||p.proargtypes::text||':'||p.prorettype::text||':'||p.proretset::text
            FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace WHERE n.nspname='type_providers'
                AND p.proname IN('as_integer','echo','equal','first_pair','pair_array','pair_echo','pair_from_store',
                    'pair_materialized','pair_rows','positive_echo','raw_array','wrong_pair') ORDER BY p.proname
            """));
        Assert.AreSequenceEqual([u24, pair, u24, pair], await Scalar<uint[]>(connection, """
            SELECT proallargtypes FROM pg_proc WHERE oid='type_providers.paired_table(type_providers.u24,type_providers.pair)'::regprocedure
            """));
        Assert.AreEqual("{i,i,t,t}", await Scalar<string>(connection, """
            SELECT proargmodes::text FROM pg_proc WHERE oid='type_providers.paired_table(type_providers.u24,type_providers.pair)'::regprocedure
            """));
        Assert.AreSequenceEqual(["other_pair:ankus_test", "pair:ankus_test", "positive:ankus_test", "required:ankus_test", "u24:ankus_test"],
            await Strings(connection, """
                SELECT t.typname||':'||e.extname FROM pg_type t JOIN pg_namespace n ON n.oid=t.typnamespace
                JOIN pg_depend d ON d.classid='pg_type'::regclass AND d.objid=t.oid AND d.deptype='e'
                JOIN pg_extension e ON e.oid=d.refobjid WHERE n.nspname='type_providers'
                    AND t.typname IN('other_pair','pair','positive','required','u24') ORDER BY t.typname
                """));
        Assert.AreEqual(u24, await Scalar<uint>(connection, "SELECT typelem FROM pg_type WHERE oid='type_providers.u24[]'::regtype"));
        Assert.AreEqual(pair, await Scalar<uint>(connection, "SELECT typelem FROM pg_type WHERE oid='type_providers.pair[]'::regtype"));
        Assert.AreEqual("42", await Scalar<string>(connection, "SELECT type_providers.echo('42')::text"));
    }

    /// <summary>
    /// The completed provider orders manual I/O before consumers without imposing varlena storage.
    /// </summary>
    /// <param name="value">An independently known unsigned 24-bit value.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(42)]
    [DataRow(16777215)]
    public async Task CompleteProviderPreservesManualByValueStorage(int value)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        int process = connection.ProcessID;
        Assert.AreEqual(value, await Scalar<int>(connection, $"SELECT type_providers.echo('{value}')::integer"));
        Assert.AreEqual(value.ToString(CultureInfo.InvariantCulture), await Scalar<string>(connection,
            $"SELECT type_providers.echo('{value}')::text"));
        Assert.AreEqual("4:true:true:type_providers.u24", await Scalar<string>(connection, """
            SELECT typlen::text||':'||typbyval::text||':'||typisdefined::text||':'||oid::regtype::text
            FROM pg_type WHERE oid='type_providers.u24'::regtype
            """));
        Assert.IsTrue(await Scalar<bool>(connection, $"SELECT '{value}'::type_providers.u24 OPERATOR(type_providers.@=) '{value}'"));
        Assert.IsFalse(await Scalar<bool>(connection, "SELECT '0'::type_providers.u24 OPERATOR(type_providers.@=) '16777215'"));
        Assert.IsTrue(await Scalar<bool>(connection, """
            SELECT type_providers.echo(NULL) IS NULL AND NULL::type_providers.u24::integer IS NULL
                AND (NULL::type_providers.u24 OPERATOR(type_providers.@=) '0') IS NULL
            """));
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => Execute(connection,
            "SELECT '16777216'::type_providers.u24"));
        Assert.AreEqual("22003", error.SqlState);
        Assert.AreEqual("provider value exceeds 24 bits", error.MessageText);
        Assert.AreEqual(42, await Scalar<int>(connection, "SELECT type_providers.echo('42')::integer"));
        Assert.AreEqual(process, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }

    /// <summary>
    /// Leaf providers preserve nominal composite identity, whole NULL and multidimensional array shape.
    /// </summary>
    [TestMethod]
    public async Task DeclaredProvidersPreserveCompositeAndArrayIdentity()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        int process = connection.ProcessID;
        Assert.AreEqual("(42,héllo)", await Scalar<string>(connection,
            "SELECT type_providers.pair_echo(ROW(42,'héllo')::type_providers.pair)::text"));
        Assert.IsTrue(await Scalar<bool>(connection, """
            SELECT record_send(type_providers.pair_echo(NULL)) IS NULL
                AND record_send(type_providers.pair_echo(ROW(NULL,NULL)::type_providers.pair)) IS NOT NULL
                AND type_providers.pair_echo(ROW(NULL,NULL)::type_providers.pair)::text='(,)'
                AND pg_typeof(type_providers.pair_echo(NULL))='type_providers.pair'::regtype
            """));
        Assert.AreEqual("[2:3][-1:0]={{0,NULL},{16777215,42}}", await Scalar<string>(connection, """
            SELECT type_providers.raw_array('[2:3][-1:0]={{0,NULL},{16777215,42}}')::text
            """));
        await Execute(connection, """
            CREATE TEMP TABLE provider_arrays(value type_providers.pair[]);
            INSERT INTO provider_arrays SELECT array_fill(NULL::type_providers.pair,ARRAY[2,2],ARRAY[2,-1]);
            UPDATE provider_arrays SET value[2][-1]=ROW(42,'héllo')::type_providers.pair;
            UPDATE provider_arrays SET value[3][-1]=ROW(NULL,NULL)::type_providers.pair;
            UPDATE provider_arrays SET value[3][0]=ROW(-7,'')::type_providers.pair;
            """);
        Assert.AreEqual("[2:3][-1:0]|(42,héllo)|true|(,)|(-7,\"\")", await Scalar<string>(connection, """
            WITH output AS(SELECT type_providers.pair_array(value) a FROM provider_arrays)
            SELECT array_dims(a)||'|'||(a[2][-1])::text||'|'||(record_send(a[2][0]) IS NULL)::text||'|'||
                (a[3][-1])::text||'|'||(a[3][0])::text FROM output
            """));
        Assert.IsTrue(await Scalar<bool>(connection, """
            SELECT type_providers.raw_array(NULL) IS NULL AND type_providers.pair_array(NULL) IS NULL
                AND cardinality(type_providers.raw_array('{}'))=0 AND cardinality(type_providers.pair_array('{}'))=0
                AND pg_typeof(type_providers.raw_array('{}'))='type_providers.u24[]'::regtype
                AND pg_typeof(type_providers.pair_array('{}'))='type_providers.pair[]'::regtype
            """));
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => Execute(connection,
            "SELECT type_providers.wrong_pair(ROW(42,'same shape')::type_providers.other_pair)"));
        Assert.AreEqual("42804", error.SqlState);
        Assert.AreEqual("Returned PostgreSQL type type_providers.other_pair does not match expected type type_providers.pair", error.MessageText);
        Assert.AreEqual("(7,recovered)", await Scalar<string>(connection,
            "SELECT type_providers.pair_echo(ROW(7,'recovered')::type_providers.pair)::text"));
        Assert.AreEqual(process, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }

    /// <summary>
    /// Domain providers preserve checks and required output constraints with exact recovery diagnostics.
    /// </summary>
    [TestMethod]
    public async Task DeclaredDomainConstraintsRecover()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        int process = connection.ProcessID;
        Assert.AreEqual(42, await Scalar<int>(connection, "SELECT type_providers.positive_echo(42)::integer"));
        Assert.IsTrue(await Scalar<bool>(connection, """
            SELECT type_providers.positive_echo(NULL) IS NULL
                AND pg_typeof(type_providers.positive_echo(42))='type_providers.positive'::regtype
            """));
        PostgresException check = await Assert.ThrowsExactlyAsync<PostgresException>(() => Execute(connection,
            "SELECT type_providers.positive_echo(-1)"));
        Assert.AreEqual("23514", check.SqlState);
        Assert.AreEqual("value for domain type_providers.positive violates check constraint \"positive_check\"", check.MessageText);
        PostgresException required = await Assert.ThrowsExactlyAsync<PostgresException>(() => Execute(connection,
            "SELECT type_providers.required_null()"));
        Assert.AreEqual("23502", required.SqlState);
        Assert.AreEqual("domain type_providers.required does not allow null values", required.MessageText);
        Assert.AreEqual(7, await Scalar<int>(connection, "SELECT type_providers.positive_echo(7)::integer"));
        Assert.AreEqual(process, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }

    /// <summary>
    /// Provider-bound set and TABLE wrappers retain values and complete iterator cleanup at every exit.
    /// </summary>
    /// <param name="function">The streaming or materializing entry point.</param>
    [TestMethod]
    [DataRow("pair_rows")]
    [DataRow("pair_materialized")]
    public async Task DeclaredSetAndTableProvidersRetainValues(string function)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        int process = connection.ProcessID;
        string label = string.Concat(Enumerable.Repeat("héllo", 10000));
        Assert.AreSequenceEqual(["42|" + label, "42|" + label, "<NULL>"], await Strings(connection,
            $"SELECT coalesce(number::text||'|'||label,'<NULL>') FROM type_providers.{function}(ROW(42,repeat('héllo',10000))::type_providers.pair,false)"));
        Assert.AreEqual(1, await Scalar<int>(connection, "SELECT type_providers.cleanup_count()"));
        Assert.AreEqual("early", await Scalar<string>(connection,
            $"SELECT label FROM type_providers.{function}(ROW(1,'early')::type_providers.pair,false) LIMIT 1"));
        Assert.AreEqual(2, await Scalar<int>(connection, "SELECT type_providers.cleanup_count()"));
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => Execute(connection,
            $"SELECT * FROM type_providers.{function}(ROW(7,'error')::type_providers.pair,true)"));
        Assert.AreEqual("P8401", error.SqlState);
        Assert.AreEqual("provider iterator failed", error.MessageText);
        Assert.AreEqual("second advance", error.Detail);
        Assert.AreEqual("retry without failure", error.Hint);
        Assert.AreEqual(3, await Scalar<int>(connection, "SELECT type_providers.cleanup_count()"));
        Assert.AreSequenceEqual(["0|(7,table)", "<NULL>|<NULL>"], await Strings(connection, """
            SELECT coalesce(number::text,'<NULL>')||'|'||coalesce(pair::text,'<NULL>')
            FROM type_providers.paired_table('0',ROW(7,'table')::type_providers.pair)
            """));
        Assert.AreSequenceEqual(["8|recovered", "8|recovered", "<NULL>"], await Strings(connection,
            $"SELECT coalesce(number::text||'|'||label,'<NULL>') FROM type_providers.{function}(ROW(8,'recovered')::type_providers.pair,false)"));
        Assert.AreEqual(4, await Scalar<int>(connection, "SELECT type_providers.cleanup_count()"));
        Assert.AreEqual(process, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }

    /// <summary>
    /// Provider metadata leaves native copies independent of SPI result, temporary owner and TOAST source lifetimes.
    /// </summary>
    [TestMethod]
    public async Task DeclaredProviderCopiesOutliveSourceStorage()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        int process = connection.ProcessID;
        await Execute(connection, """
            CREATE TEMP TABLE provider_toast(number integer,label text);
            ALTER TABLE provider_toast ALTER COLUMN label SET STORAGE EXTERNAL;
            INSERT INTO provider_toast VALUES(42,repeat('héllo',10000));
            """);
        Assert.AreEqual("(42," + string.Concat(Enumerable.Repeat("héllo", 10000)) + ")", await Scalar<string>(connection,
            "SELECT type_providers.pair_from_store()::text"));
        Assert.IsTrue(await Scalar<bool>(connection, "SELECT to_regclass('pg_temp.provider_toast') IS NULL"));
        Assert.AreEqual("(7,after)", await Scalar<string>(connection,
            "SELECT type_providers.pair_echo(ROW(7,'after')::type_providers.pair)::text"));
        Assert.AreEqual(process, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }

    /// <summary>
    /// Equal storage and typed NULL cannot hide a wrong OID or an expired native owner.
    /// </summary>
    /// <param name="mode">The violated result contract.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public async Task DeclaredProviderRejectsInvalidOutputs(int mode)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        int process = connection.ProcessID;
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => Execute(connection,
            $"SELECT type_providers.invalid_result({mode})"));
        Assert.AreEqual(mode < 2 ? "42804" : "38000", error.SqlState);
        Assert.AreEqual(mode < 2
            ? "Returned PostgreSQL type integer does not match expected type type_providers.u24"
            : "The datum's memory context has been deleted." + Environment.NewLine + "Object name: 'PgDatum'.", error.MessageText);
        Assert.AreEqual("0", await Scalar<string>(connection, "SELECT type_providers.echo('0')::text"));
        Assert.AreEqual(process, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }

    /// <summary>
    /// Aggregate helpers infer a raw composite provider and retain their first value across later transitions.
    /// </summary>
    [TestMethod]
    public async Task DeclaredProviderAggregateRetainsFirstValue()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.AreEqual("(42," + string.Concat(Enumerable.Repeat("héllo", 10000)) + ")", await Scalar<string>(connection, """
            SELECT type_providers.first_pair(value ORDER BY id)::text FROM (VALUES
                (0,NULL::type_providers.pair),(1,ROW(42,repeat('héllo',10000))::type_providers.pair),
                (2,ROW(7,'later')::type_providers.pair),(3,NULL::type_providers.pair)) input(id,value)
            """));
        Assert.IsTrue(await Scalar<bool>(connection, """
            SELECT record_send(type_providers.first_pair(NULL::type_providers.pair)) IS NULL
                AND (SELECT record_send(type_providers.first_pair(value)) IS NULL
                    FROM (SELECT NULL::type_providers.pair value WHERE false) empty)
            """));
        Assert.IsTrue(await Scalar<bool>(connection, """
            SELECT a.aggtranstype='type_providers.pair'::regtype AND p.prorettype=a.aggtranstype
                AND p.proargtypes[0]=a.aggtranstype AND p.proargtypes[1]=a.aggtranstype
            FROM pg_aggregate a JOIN pg_proc p ON p.oid=a.aggtransfn
            WHERE a.aggfnoid='type_providers.first_pair(type_providers.pair)'::regprocedure
            """));
    }

    /// <summary>
    /// Executes statements while observing errors before the next assertion.
    /// </summary>
    private async Task Execute(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(context.CancellationToken);
    }

    /// <summary>
    /// Reads one exact typed scalar.
    /// </summary>
    private async Task<T> Scalar<T>(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return Assert.IsInstanceOfType<T>(await command.ExecuteScalarAsync(context.CancellationToken));
    }

    /// <summary>
    /// Retains complete result order, duplicates and explicit NULL markers.
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
