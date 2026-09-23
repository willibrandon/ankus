using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Checks aggregate declarations against native catalog validation and binary-coercible state seeding.
/// </summary>
public sealed partial class AggregateTests
{
    /// <summary>
    /// PostgreSQL seeds compatible OID states without calling managed code or changing signed input bits.
    /// </summary>
    /// <param name="seed">The initial signed integer datum.</param>
    /// <param name="expected">The unsigned OID value represented by the datum.</param>
    [TestMethod]
    [DataRow(-1, 4294967295L)]
    [DataRow(0, 0L)]
    [DataRow(2147483647, 2147483647L)]
    public Task StrictOidStateUsesNativeBinarySeed(int seed, long expected)
        => Run(nameof(StrictOidStateUsesNativeBinarySeed), async (connection, transaction, token) =>
        {
            await Execute(connection, transaction, "SELECT datatype.aggregate_seed_reset()", token);
            Assert.AreEqual(expected, await Scalar<long>(connection, transaction, $"""
                SELECT datatype.state_seed_oid(v ORDER BY ord)::bigint
                FROM (VALUES(1,NULL::integer),(2,{seed}),(3,42)) AS input(ord,v)
                """, token));
            Assert.AreEqual(1, await Scalar<int>(connection, transaction, "SELECT datatype.aggregate_seed_calls()", token));
        });

    /// <summary>
    /// PostgreSQL can seed INET state with a CIDR datum and retain its address family and prefix.
    /// </summary>
    /// <param name="network">The first nonnull network value.</param>
    [TestMethod]
    [DataRow("192.0.2.0/24")]
    [DataRow("2001:db8::/32")]
    public Task StrictInetStateUsesNativeCidrSeed(string network)
        => Run(nameof(StrictInetStateUsesNativeCidrSeed), async (connection, transaction, token) =>
        {
            await Execute(connection, transaction, "SELECT datatype.aggregate_seed_reset()", token);
            Assert.AreEqual(network, await Scalar<string>(connection, transaction, $"""
                SELECT datatype.state_seed_inet(v ORDER BY ord)::text
                FROM (VALUES(1,NULL::cidr),(2,'{network}'::cidr),(3,'10.0.0.0/8'::cidr)) AS input(ord,v)
                """, token));
            Assert.AreEqual(1, await Scalar<int>(connection, transaction, "SELECT datatype.aggregate_seed_calls()", token));
        });

    /// <summary>
    /// The server catalog preserves explicit aggregate options independently of support-function settings.
    /// </summary>
    [TestMethod]
    public Task CatalogPreservesAggregateAndSupportOptions()
        => Run(nameof(CatalogPreservesAggregateAndSupportOptions), async (connection, transaction, token) =>
        {
            Assert.AreEqual("2147483647:4096:0:0:true:true:s:r:r:s:i:42:true", await Scalar<string>(connection, transaction, """
                SELECT concat_ws(':',a.aggtransspace,a.aggmtransspace,a.agginitval,a.aggminitval,
                    a.aggfinalextra::text,a.aggmfinalextra::text,a.aggfinalmodify,a.aggmfinalmodify,p.proparallel,
                    t.proparallel,t.provolatile,t.procost,t.proisstrict::text)
                FROM pg_aggregate a JOIN pg_proc p ON p.oid=a.aggfnoid JOIN pg_proc t ON t.oid=a.aggtransfn
                WHERE a.aggfnoid='datatype.state_catalog_options(integer)'::regprocedure
                """, token));
            Assert.AreEqual("0:0:true:true:w:s:0", await Scalar<string>(connection, transaction, """
                SELECT concat_ws(':',aggfinalfn::oid,aggmfinalfn::oid,aggfinalextra::text,aggmfinalextra::text,
                    aggfinalmodify,aggmfinalmodify,aggmtranstype)
                FROM pg_aggregate WHERE aggfnoid='datatype.state_catalog_flags(integer)'::regprocedure
                """, token));
            Assert.AreEqual(6L, await Scalar<long>(connection, transaction,
                "SELECT datatype.state_catalog_options(v) FROM (VALUES(1),(2),(3)) AS input(v)", token));
            Assert.AreSequenceEqual([1, 3, 5], await Scalar<long[]>(connection, transaction, """
                SELECT array_agg(total ORDER BY v) FROM (
                    SELECT v,datatype.state_catalog_options(v) OVER(ORDER BY v ROWS BETWEEN 1 PRECEDING AND CURRENT ROW) AS total
                    FROM (VALUES(1),(2),(3)) AS input(v)) AS output
                """, token));
        });

    /// <summary>
    /// Native catalog failures remain transactional and do not invalidate existing generated aggregates.
    /// </summary>
    /// <param name="options">The deliberately invalid aggregate definition options.</param>
    /// <param name="sqlState">The expected PostgreSQL diagnostic.</param>
    [TestMethod]
    [DataRow("SFUNC=aggregate_values.sum_values_transition,STYPE=integer,INITCOND='invalid'", "22P02")]
    [DataRow("SFUNC=datatype.catalog_options_transition,STYPE=bigint", "42P13")]
    [DataRow("SFUNC=pg_catalog.int4eq,STYPE=integer,INITCOND='0'", "42804")]
    [DataRow("SFUNC=aggregate_values.sum_values_transition,STYPE=integer,INITCOND='0',COMBINEFUNC=pg_catalog.int4eq", "42804")]
    [DataRow("SFUNC=aggregate_values.sum_values_transition,STYPE=integer,INITCOND='0',SERIALFUNC=pg_catalog.int8_avg_serialize", "42P13")]
    [DataRow("SFUNC=aggregate_values.sum_values_transition,STYPE=integer,INITCOND='0',SERIALFUNC=pg_catalog.int8_avg_serialize,DESERIALFUNC=pg_catalog.int8_avg_deserialize", "42P13")]
    [DataRow("SFUNC=aggregate_values.sum_values_transition,STYPE=integer,INITCOND='0',MSFUNC=aggregate_values.sum_values_transition,MSTYPE=integer,MINITCOND='0',MINVFUNC=pg_catalog.int4mi", "42P13")]
    [DataRow("SFUNC=pg_catalog.int4pl,STYPE=integer,INITCOND='0',FINALFUNC=pg_catalog.int4pl,FINALFUNC_EXTRA", "42P13")]
    [DataRow("SFUNC=pg_catalog.int4pl,STYPE=integer,INITCOND='0',MSFUNC=pg_catalog.int4pl,MINVFUNC=pg_catalog.int4mi,MSTYPE=integer,MINITCOND='0',MFINALFUNC=pg_catalog.int4out", "42P13")]
    public Task NativeDefinitionErrorsRollbackCleanly(string options, string sqlState)
        => Run(nameof(NativeDefinitionErrorsRollbackCleanly), async (connection, transaction, token) =>
        {
            await transaction.SaveAsync("definition", token);
            PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => Execute(connection, transaction,
                $"CREATE AGGREGATE aggregate_values.invalid_definition(integer)({options})", token));
            Assert.AreEqual(sqlState, error.SqlState);
            await transaction.RollbackAsync("definition", token);
            Assert.IsTrue(await Scalar<bool>(connection, transaction,
                "SELECT to_regprocedure('aggregate_values.invalid_definition(integer)') IS NULL", token));
            Assert.AreEqual(42, await Scalar<int>(connection, transaction,
                "SELECT aggregate_values.sum_values(v) FROM (VALUES(20),(22)) AS input(v)", token));
        });
}
