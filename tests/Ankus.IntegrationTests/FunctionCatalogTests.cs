using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies the complete pgrx function catalog surface and native default trees in PostgreSQL.
/// </summary>
/// <param name="context">The cancellation context.</param>
[TestClass]
public sealed class FunctionCatalogTests(TestContext context)
{
    /// <summary>
    /// Controlled native parser and registry failures clean up ownership and permit an exact-value retry.
    /// </summary>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    [DataRow(5)]
    public Task DefaultParserFailuresRecover(int mode)
        => CheckAsync($"SELECT tests.function_defaults_fault({mode}) = 'error|released|retry|42|empty'", true);

    /// <summary>
    /// Independent catalog SQL checks every field and all argument modes across created and built-in routines.
    /// </summary>
    [TestMethod]
    public Task MetadataMatchesCatalog()
        => CheckAsync("""
            CREATE DOMAIN fc_domain AS integer;
            CREATE FUNCTION fc_rich(IN a fc_domain DEFAULT 42, INOUT "rés" text DEFAULT 'café', OUT tail bigint)
            RETURNS record LANGUAGE SQL VOLATILE STRICT SECURITY DEFINER LEAKPROOF PARALLEL RESTRICTED COST 12.5
            SET work_mem='4MB' SET search_path=pg_catalog AS 'SELECT $2, $1::bigint';
            CREATE FUNCTION fc_variadic(integer, VARIADIC rest text[]) RETURNS integer
            LANGUAGE SQL IMMUTABLE PARALLEL SAFE AS 'SELECT $1';
            CREATE FUNCTION fc_table(a integer) RETURNS TABLE(x integer,y text)
            LANGUAGE SQL STABLE ROWS 123.5 AS 'SELECT $1, ''a''::text';
            CREATE FUNCTION fc_partial(integer, named text) RETURNS text LANGUAGE SQL AS 'SELECT $2';
            CREATE FUNCTION fc_unnamed(integer,text) RETURNS text LANGUAGE SQL AS 'SELECT $2';
            CREATE FUNCTION fc_empty() RETURNS integer LANGUAGE SQL AS 'SELECT 42';
            CREATE PROCEDURE fc_procedure(INOUT a integer) LANGUAGE SQL AS 'SELECT a';
            CREATE TEMP TABLE fc_comparison AS
            SELECT p.proname, function_catalog.describe(p.oid) AS actual,
            jsonb_build_object(
              'Oid', p.oid::bigint, 'OwnerOid', p.proowner::bigint, 'Cost', p.procost, 'Rows', p.prorows,
              'VariadicElementTypeOid', nullif(p.provariadic,0)::bigint, 'SupportFunctionOid', p.prosupport::oid::bigint,
              'Kind', CASE p.prokind WHEN 'f' THEN 0 WHEN 'p' THEN 1 WHEN 'a' THEN 2 WHEN 'w' THEN 3 END,
              'IsSecurityDefiner', p.prosecdef, 'IsLeakProof', p.proleakproof, 'LanguageOid', p.prolang::bigint,
              'Source', p.prosrc, 'Binary', p.probin, 'Configuration', p.proconfig,
              'ArgumentModes', CASE WHEN p.proargmodes IS NULL THEN
                to_jsonb(array_fill(0, ARRAY[coalesce(cardinality(p.proargnames),p.pronargs)::int]))
                ELSE (SELECT jsonb_agg(CASE mode WHEN 'i' THEN 0 WHEN 'o' THEN 1 WHEN 'b' THEN 2 WHEN 'v' THEN 3 WHEN 't' THEN 4 END ORDER BY position)
                      FROM unnest(p.proargmodes) WITH ORDINALITY AS modes(mode,position)) END,
              'InputArgumentCount', p.pronargs, 'DefaultArgumentCount', p.pronargdefaults,
              'ArgumentNames', coalesce(p.proargnames,array_fill(NULL::text,ARRAY[p.pronargs::int])),
              'InputArgumentTypeOids', p.proargtypes::oid[]::bigint[],
              'AllArgumentTypeOids', coalesce(p.proallargtypes,p.proargtypes::oid[])::bigint[],
              'ReturnTypeOid', p.prorettype::bigint, 'IsStrict', p.proisstrict,
              'Volatility', CASE p.provolatile WHEN 'v' THEN 0 WHEN 's' THEN 1 WHEN 'i' THEN 2 END,
              'ParallelSafety', CASE p.proparallel WHEN 'u' THEN 0 WHEN 'r' THEN 1 WHEN 's' THEN 2 END,
              'ReturnsSet', p.proretset) AS expected
            FROM pg_proc p
            WHERE p.proname IN ('fc_rich','fc_variadic','fc_table','fc_partial','fc_unnamed','fc_empty','fc_procedure','array_append')
               OR p.oid IN ('sum(integer)'::regprocedure,'row_number()'::regprocedure,'int4abs(integer)'::regprocedure,
                            'function_catalog.describe(oid)'::regprocedure);
            SELECT bool_and(actual = expected),
              jsonb_agg(jsonb_build_object('routine',proname,'actual',actual,'expected',expected))
                FILTER (WHERE actual IS DISTINCT FROM expected)
            FROM fc_comparison
            """, true);

    /// <summary>
    /// Invalid zero, an unknown maximum and a non-routine catalog identity return SQL NULL.
    /// </summary>
    [TestMethod]
    public Task MissingRowsReturnNull()
        => CheckAsync("""
            SELECT function_catalog.describe(0::oid) IS NULL
               AND function_catalog.describe(4294967295::oid) IS NULL
               AND function_catalog.describe('integer'::regtype::oid) IS NULL
            """, true);

    /// <summary>
    /// Catalog inspection remains available when the current role cannot execute the inspected routine.
    /// </summary>
    [TestMethod]
    public Task MetadataDoesNotInvokeOrRequireExecute()
        => CheckAsync("""
            CREATE FUNCTION fc_private() RETURNS integer LANGUAGE SQL AS 'SELECT 1/0';
            REVOKE ALL ON FUNCTION fc_private() FROM PUBLIC;
            CREATE ROLE fc_catalog_reader;
            GRANT USAGE ON SCHEMA function_catalog TO fc_catalog_reader;
            SET LOCAL ROLE fc_catalog_reader;
            SELECT function_catalog.describe(p.oid)->>'Source' = 'SELECT 1/0'
               AND NOT has_function_privilege(p.oid,'EXECUTE')
            FROM pg_proc p WHERE proname='fc_private';
            RESET ROLE
            """, true);

    /// <summary>
    /// Owned snapshots survive ALTER, DROP and subsequent callbacks, retaining parseable original defaults.
    /// </summary>
    [TestMethod]
    public Task SnapshotsSurviveCatalogChanges()
        => CheckAsync("""
            CREATE FUNCTION fc_snapshot(integer DEFAULT 42) RETURNS integer LANGUAGE SQL IMMUTABLE AS 'SELECT $1';
            CREATE TEMP TABLE fc_before AS SELECT function_catalog.describe('fc_snapshot(integer)'::regprocedure::oid) AS info;
            SELECT function_catalog.save('fc_snapshot(integer)'::regprocedure::oid);
            ALTER FUNCTION fc_snapshot(integer) VOLATILE COST 17;
            DO $do$ BEGIN
              IF function_catalog.saved() <> (SELECT info FROM fc_before) OR
                 function_catalog.saved() = function_catalog.describe('fc_snapshot(integer)'::regprocedure::oid)
              THEN RAISE EXCEPTION 'catalog snapshot changed'; END IF;
            END $do$;
            DROP FUNCTION fc_snapshot(integer);
            SELECT function_catalog.saved() = (SELECT info FROM fc_before)
              AND function_catalog.saved_defaults() = ARRAY['42']
            """, true);

    /// <summary>
    /// Actual native trees retain order, Unicode, NULL and dynamic expressions without evaluation during parsing.
    /// </summary>
    [TestMethod]
    public Task DefaultTreesPreserveValuesAndLifetime()
        => CheckAsync("""
            CREATE SEQUENCE fc_sequence;
            CREATE FUNCTION fc_defaults(integer DEFAULT 42, text DEFAULT 'café 🐘', bigint DEFAULT nextval('fc_sequence'), text DEFAULT NULL)
            RETURNS integer LANGUAGE SQL AS 'SELECT $1';
            CREATE FUNCTION fc_no_defaults(integer) RETURNS integer LANGUAGE SQL AS 'SELECT $1';
            SELECT function_catalog.defaults('fc_defaults(integer,text,bigint,text)'::regprocedure::oid)
                     IS NOT DISTINCT FROM ARRAY['42','café 🐘','1',NULL]
              AND (SELECT last_value FROM fc_sequence) = 2
              AND function_catalog.defaults('fc_no_defaults(integer)'::regprocedure::oid) IS NULL
            """, true);

    /// <summary>
    /// Errors raised by default evaluation unwind native frames and allow managed catch/finally and same-backend retry.
    /// </summary>
    [TestMethod]
    public Task DefaultErrorsRecover()
        => CheckAsync("""
            CREATE FUNCTION fc_bad_default(integer DEFAULT 1/0) RETURNS integer LANGUAGE SQL AS 'SELECT $1';
            CREATE FUNCTION fc_good_default(integer DEFAULT 7) RETURNS integer LANGUAGE SQL AS 'SELECT $1';
            SELECT function_catalog.default_error('fc_bad_default(integer)'::regprocedure::oid) = '22012|True|42'
              AND function_catalog.defaults('fc_good_default(integer)'::regprocedure::oid) = ARRAY['7']
            """, true);

    /// <summary>
    /// Checks observable results and same-session recovery after each scenario.
    /// </summary>
    private Task CheckAsync(string sql, bool expected)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(FunctionCatalogTests), async (connection, transaction, token) =>
        {
            await using var command = new NpgsqlCommand("SELECT pg_backend_pid()", connection, transaction);
            int pid = (int)(await command.ExecuteScalarAsync(token))!;
            command.CommandText = sql;
            await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(token))
            {
                object? last = null;
                string? diagnostic = null;
                do
                {
                    while (await reader.ReadAsync(token))
                    {
                        last = reader.GetValue(0);
                        diagnostic = reader.FieldCount > 1 ? reader.GetValue(1).ToString() : null;
                    }
                }
                while (await reader.NextResultAsync(token));

                Assert.AreEqual(expected, last, diagnostic);
            }

            command.CommandText = "SELECT pg_backend_pid(),42";
            await using NpgsqlDataReader recovered = await command.ExecuteReaderAsync(token);
            Assert.IsTrue(await recovered.ReadAsync(token));
            Assert.AreEqual(pid, recovered.GetInt32(0));
            Assert.AreEqual(42, recovered.GetInt32(1));
        }, context.CancellationToken);
}
