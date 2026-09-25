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
                13 => (263, "8FFE0D0326CDC3915617F774EF9250DECE3B082E1BF2A6D93617E89672A903D7"),
                14 => (292, "2A31F4C8749C75CC6F2DFF4B072D48B7ECD6AC2A92600A0B4D83C594ACB33B99"),
                15 or 16 or 17 => (292, "1898211C84F3F04CED37F80ED7DC6A49A17C1532FAA8BD21415E7E93DE24D156"),
                18 => (291, "F93EC9CDE08558E0505BEF6EF7E251BC3E6874758637B22856A862D66A6DB3A6"),
                19 => (296, "3C7FCF0265D299EC10DD85E60AA0242645C0A30830E87D390A1F6D471F40CB77"),
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
              (SELECT oid FROM pg_opfamily WHERE opfname='integer_ops' AND opfmethod=(SELECT oid FROM pg_am WHERE amname='btree'))]::oid[]
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
