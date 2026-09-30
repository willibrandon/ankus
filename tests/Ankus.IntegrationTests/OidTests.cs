using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies versioned OID helpers, catalog identities and raw datum semantics in the published Native AOT extension.
/// </summary>
/// <param name="context">The test cancellation context.</param>
[TestClass]
public sealed class OidTests(TestContext context)
{
    /// <summary>
    /// AOT enumeration and native-header version selection reproduce the complete independently pinned catalog.
    /// </summary>
    [TestMethod]
    public Task ActiveCatalogMatchesServerVersion()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(ActiveCatalogMatchesServerVersion), async (connection, transaction, token) =>
        {
            await using var command = new NpgsqlCommand("SELECT current_setting('server_version_num')::int / 10000", connection, transaction);
            int major = (int)(await command.ExecuteScalarAsync(token))!;
            (int count, string hash) = major switch
            {
                13 => (295, "D57324B651DF04A876C8F95E354C8F18520C35D2425C447EEB0CCCA3AFAE5FE9"),
                14 => (324, "025280FF026DFDAB3208E38B0311CA5C702BAB20E1E96870B315D213BC030338"),
                15 or 16 or 17 => (326, "6205E9C21E8958AB9EC981AB31B239302EF431F201505DB13B114254DE172EAF"),
                18 => (325, "A7653DB2C89301BBA873DBCF7D959893D17E7122E59102A891EB85A60A6A66F0"),
                19 => (330, "99A7DE2D198455BFE89576401F9B28E7AD24430FE7C3AA49935FD8A6AF0CC632"),
                _ => throw new InvalidOperationException($"Unexpected test server major {major}."),
            };
            command.CommandText = "SELECT value,name FROM oid_catalog.entries() ORDER BY name COLLATE \"C\"";
            List<string> entries = [];
            await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(token))
            {
                while (await reader.ReadAsync(token))
                {
                    entries.Add($"{reader.GetString(1)}={reader.GetFieldValue<uint>(0).ToString(CultureInfo.InvariantCulture)}\n");
                }
            }

            Assert.HasCount(count, entries);
            Assert.AreEqual(hash, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Concat(entries)))));
        }, context.CancellationToken);

    /// <summary>
    /// Typed constants independently identify live type, relation, procedure, access-method, collation and operator-class rows.
    /// </summary>
    [TestMethod]
    public Task ConstantsMatchIndependentCatalogRows()
        => CheckAsync("""
            SELECT oid_catalog.known_values() = ARRAY[
              'boolean'::regtype::oid, 'integer'::regtype::oid, 'integer[]'::regtype::oid,
              'money'::regtype::oid, 'pg_node_tree'::regtype::oid, 'pg_class'::regclass::oid,
              'pg_proc'::regclass::oid, 'hashoid(oid)'::regprocedure::oid,
              (SELECT oid FROM pg_am WHERE amname='btree'),
              (SELECT oid FROM pg_collation WHERE collname='C' AND collnamespace='pg_catalog'::regnamespace),
              (SELECT oid FROM pg_opclass WHERE opcname='text_ops' AND opcmethod=(SELECT oid FROM pg_am WHERE amname='btree')),
              (SELECT oid FROM pg_opfamily WHERE opfname='integer_ops' AND opfmethod=(SELECT oid FROM pg_am WHERE amname='btree')),
              'pg_aggregate'::regclass::oid, 'pg_constraint'::regclass::oid]::oid[]
            """, true);

    /// <summary>
    /// Classification keeps zero distinct from SQL NULL and does not mistake a valid user type for a built-in constant.
    /// </summary>
    [TestMethod]
    public Task ClassificationsRetainValuesAndCatalogBoundaries()
        => CheckAsync("""
            CREATE DOMAIN oid_test_domain AS integer;
            SELECT concat_ws(';', oid_catalog.describe(NULL::oid), oid_catalog.describe(0::oid),
              oid_catalog.describe(23::oid), oid_catalog.describe(4294967295::oid),
              oid_catalog.describe('oid_test_domain'::regtype::oid) =
                'Custom|' || 'oid_test_domain'::regtype::oid::text || '|',
              oid_catalog.describe(6::oid), oid_catalog.oversized())
            """, "null;Invalid|0|;BuiltIn|23|INT4OID;Custom|4294967295|;t;BuiltIn|6|PROGRESS_CREATEIDX_INDEX_OID;False|0|TooBig");

    /// <summary>
    /// Raw datum returns preserve oid identity and map only the Invalid tag to SQL NULL.
    /// </summary>
    [TestMethod]
    public Task DatumConversionDistinguishesInvalidAndCustomZero()
        => CheckAsync("""
            SELECT concat_ws('|', oid_catalog.as_datum(0,false) IS NULL,
              oid_catalog.as_datum(0,true) IS NOT NULL, oid_catalog.as_datum(0,true),
              oid_catalog.as_datum(23,false), oid_catalog.as_datum(4294967295,false),
              pg_typeof(oid_catalog.as_datum(0,false))::text, oid_catalog.datum_lifetime())
            """, "t|t|0|23|4294967295|oid|False|0|4294967295|26|False|True|42");

    /// <summary>
    /// Invalid, absent and maximum built-in candidates unwind through managed finally and permit same-session work.
    /// </summary>
    [TestMethod]
    public Task InvalidBuiltInValuesRecover()
        => CheckAsync("""
            SELECT concat_ws(';', oid_catalog.reject_built_in(0), oid_catalog.reject_built_in(65536),
              oid_catalog.reject_built_in(4294967295), oid_catalog.describe(16::oid))
            """, "value|True|42;value|True|42;value|True|42;BuiltIn|16|BOOLOID");

    private Task CheckAsync(string sql, object expected)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(OidTests), async (connection, transaction, token) =>
        {
            int backend = connection.ProcessID;
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            Assert.AreEqual(expected, await command.ExecuteScalarAsync(token));
            command.CommandText = "SELECT 42";
            Assert.AreEqual(42, await command.ExecuteScalarAsync(token));
            Assert.AreEqual(backend, connection.ProcessID);
        }, context.CancellationToken);
}
