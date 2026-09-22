using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Compares network values and operations with independent PostgreSQL expressions under Native AOT.
/// </summary>
/// <param name="context">The test cancellation and diagnostic context.</param>
[TestClass]
public sealed class NetworkDatumTests(TestContext context)
{
    /// <summary>
    /// All scalar and array SPI paths preserve bytes, prefixes, NULLs, families, dimensions and lower bounds.
    /// </summary>
    [TestMethod]
    [DataRow("inet", "inet", "'192.0.2.129/25'")]
    [DataRow("inet", "inet", "'::ffff:192.0.2.1/120'")]
    [DataRow("inet", "inet", "'::/0'")]
    [DataRow("inet", "inet", "NULL")]
    [DataRow("cidr", "cidr", "'192.0.2.128/25'")]
    [DataRow("cidr", "cidr", "'2001:db8::/33'")]
    [DataRow("cidr", "cidr", "NULL")]
    [DataRow("address", "inet", "'255.255.255.255'")]
    [DataRow("address", "inet", "'::ffff:192.0.2.1'")]
    [DataRow("address", "inet", "NULL")]
    [DataRow("clr", "cidr", "'2001:db8::/32'")]
    [DataRow("clr", "cidr", "NULL")]
    [DataRow("inets", "inet[]", "'[0:1][-2:-1]={{192.0.2.1/24,NULL},{::1,::ffff:192.0.2.1}}'")]
    [DataRow("inets", "inet[]", "'{}'")]
    [DataRow("inets", "inet[]", "NULL")]
    [DataRow("cidrs", "cidr[]", "'[-2:0]={192.0.2.0/24,NULL,2001:db8::/32}'")]
    [DataRow("cidrs", "cidr[]", "'{}'")]
    [DataRow("addresses", "inet[]", "'{192.0.2.1,NULL,::1,::ffff:192.0.2.1}'")]
    [DataRow("clrs", "cidr[]", "'{192.0.2.0/24,NULL,2001:db8::/32}'")]
    public Task NetworkOwnershipPathsPreserveValues(string function, string type, string literal)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(NetworkOwnershipPathsPreserveValues), async (connection, transaction, token) =>
        {
            for (int mode = 0; mode <= 7; mode++)
            {
                await using var command = new NpgsqlCommand($"SELECT datatype.network_{function}({literal}::{type}, {mode}) IS NOT DISTINCT FROM {literal}::{type}", connection, transaction);
                Assert.IsTrue(Assert.IsInstanceOfType<bool>(await command.ExecuteScalarAsync(token)), $"{function}, mode {mode}");
            }
        }, context.CancellationToken);

    /// <summary>
    /// PostgreSQL parsing handles shortened IPv4 input and enforces cidr network rules.
    /// </summary>
    [TestMethod]
    [DataRow("inet", "192.168/16")]
    [DataRow("inet", "192.168.1.99/24")]
    [DataRow("inet", "::ffff:192.0.2.1/120")]
    [DataRow("inet", "ffff:ffff:ffff:ffff:ffff:ffff:ffff:ffff/128")]
    [DataRow("cidr", "10")]
    [DataRow("cidr", "192.168.1")]
    [DataRow("cidr", "2001:db8::/32")]
    public Task NetworkParsersMatchPostgres(string type, string text)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(NetworkParsersMatchPostgres), async (connection, transaction, token) =>
        {
            await using var command = new NpgsqlCommand($"SELECT datatype.network_parse_{type}($1) = $1::{type}", connection, transaction);
            command.Parameters.AddWithValue(text);
            Assert.IsTrue(Assert.IsInstanceOfType<bool>(await command.ExecuteScalarAsync(token)));
        }, context.CancellationToken);

    /// <summary>
    /// Detached masks and prefix changes match the native server for every legal prefix length.
    /// </summary>
    [TestMethod]
    [DataRow("192.0.2.129", 32)]
    [DataRow("a123:b456:c789:abcd:1234:5678:90ab:cdef", 128)]
    public Task NetworkOperationsMatchEveryPrefix(string address, int width)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(NetworkOperationsMatchEveryPrefix), async (connection, transaction, token) =>
        {
            await using var command = new NpgsqlCommand("""
                SELECT count(*) FROM generate_series(0, $2) p
                CROSS JOIN LATERAL (SELECT set_masklen($1::inet, p) AS v) x
                WHERE datatype.network_operation(v, 'network', 0) IS DISTINCT FROM network(v)::inet
                   OR datatype.network_operation(v, 'broadcast', 0) IS DISTINCT FROM broadcast(v)
                   OR datatype.network_operation(v, 'netmask', 0) IS DISTINCT FROM netmask(v)
                   OR datatype.network_operation(v, 'hostmask', 0) IS DISTINCT FROM hostmask(v)
                   OR datatype.network_operation(v, 'prefix', p) IS DISTINCT FROM set_masklen(v, p)
                   OR datatype.network_operation(v, 'cidr-prefix', greatest(0,p-1)) IS DISTINCT FROM set_masklen(network(v), greatest(0,p-1))::inet
                """, connection, transaction);
            command.Parameters.AddWithValue(address);
            command.Parameters.AddWithValue(width);
            Assert.AreEqual(0L, await command.ExecuteScalarAsync(token));
        }, context.CancellationToken);

    /// <summary>
    /// Pairwise comparisons distinguish host bits, common prefixes, equal networks, and IPv4 versus mapped IPv6.
    /// </summary>
    [TestMethod]
    public Task NetworkOrderingMatchesPostgres()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(NetworkOrderingMatchesPostgres), async (connection, transaction, token) =>
        {
            await using var command = new NpgsqlCommand("""
                WITH values(v) AS (SELECT unnest(ARRAY['0.0.0.0/0','192.0.2.1/24','192.0.2.2/24','192.0.2.0/25',
                    '192.0.3.1/24','255.255.255.255','::/0','::1','::ffff:192.0.2.1','2001:db8::/32',
                    '2001:db8::8001/113','2001:db8::8002/113','ffff:ffff:ffff:ffff:ffff:ffff:ffff:ffff']::inet[]))
                SELECT count(*) FROM values a CROSS JOIN values b
                WHERE datatype.network_compare(a.v, b.v) IS DISTINCT FROM ARRAY[
                    sign(network_cmp(a.v,b.v)), (a.v >>= b.v)::int, (a.v >> b.v)::int,
                    (a.v = b.v)::int, (a.v < b.v)::int, (a.v <= b.v)::int, (a.v > b.v)::int, (a.v >= b.v)::int]::int[]
                """, connection, transaction);
            Assert.AreEqual(0L, await command.ExecuteScalarAsync(token));
        }, context.CancellationToken);

    /// <summary>
    /// Packed tuple storage and domains use the same guarded network conversion as direct arguments.
    /// </summary>
    [TestMethod]
    public Task PackedNetworkStorageAndDomains()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(PackedNetworkStorageAndDomains), async (connection, transaction, token) =>
        {
            await using var command = new NpgsqlCommand("""
                CREATE DOMAIN pg_temp.network_domain AS inet;
                CREATE TEMP TABLE network_storage(a inet, b cidr, d pg_temp.network_domain);
                INSERT INTO network_storage VALUES ('192.0.2.1/24', '2001:db8::/32', '::ffff:192.0.2.1');
                SELECT datatype.network_inet(a, 1) = a AND datatype.network_cidr(b, 2) = b
                    AND datatype.network_inet(d, 3) = d AND pg_column_size(a) < 24 FROM network_storage
                """, connection, transaction);
            Assert.IsTrue(Assert.IsInstanceOfType<bool>(await command.ExecuteScalarAsync(token)));
        }, context.CancellationToken);

    /// <summary>
    /// Host-only .NET mappings reject prefix loss during input and SPI result conversion.
    /// </summary>
    [TestMethod]
    [DataRow("SELECT datatype.network_address('192.0.2.1/24', 0)")]
    [DataRow("SELECT datatype.network_addresses(ARRAY['::1/64'::inet], 1)")]
    [DataRow("SELECT datatype.network_spi_host('::1/64')")]
    public Task HostMappingsRejectPrefixLoss(string sql)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(HostMappingsRejectPrefixLoss), async (connection, transaction, token) =>
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(token));
            Assert.AreEqual("38000", error.SqlState);
            Assert.Contains("prefix would be lost", error.MessageText);
        }, context.CancellationToken);

    /// <summary>
    /// Repeated native parse errors unwind managed finally blocks and preserve the surrounding session state.
    /// </summary>
    [TestMethod]
    public Task NetworkInputFailureRecovery()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(NetworkInputFailureRecovery), async (connection, transaction, token) =>
        {
            await using var command = new NpgsqlCommand("SELECT datatype.network_recovery()", connection, transaction);
            Assert.AreEqual("100:50:2:0", await command.ExecuteScalarAsync(token));
        }, context.CancellationToken);

    /// <summary>
    /// Independently constructed values have PostgreSQL's expected on-wire address bytes and generated defaults.
    /// </summary>
    [TestMethod]
    public Task NetworkConstructionHasCorrectWireBytes()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(NetworkConstructionHasCorrectWireBytes), async (connection, transaction, token) =>
        {
            await using var command = new NpgsqlCommand("""
                SELECT encode(inet_send(datatype.network_construct(decode('c0000281','hex'),25)),'hex'),
                       encode(inet_send(datatype.network_construct(decode('20010db8000000000000000000000001','hex'),64)),'hex'),
                       datatype.network_defaults()
                """, connection, transaction);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
            Assert.IsTrue(await reader.ReadAsync(token));
            Assert.AreEqual("02190004c0000281", reader.GetString(0));
            Assert.AreEqual("0340001020010db8000000000000000000000001", reader.GetString(1));
            Assert.IsTrue(reader.GetBoolean(2));
        }, context.CancellationToken);

    /// <summary>
    /// Compressed large arrays and domains over network arrays retain their values after detoasting and cursor cleanup.
    /// </summary>
    [TestMethod]
    public Task NetworkArrayToastingAndDomains()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(NetworkArrayToastingAndDomains), async (connection, transaction, token) =>
        {
            await using var command = new NpgsqlCommand("""
                CREATE DOMAIN pg_temp.inet_array_domain AS inet[];
                CREATE TEMP TABLE network_arrays(a pg_temp.inet_array_domain, b cidr[]);
                INSERT INTO network_arrays
                SELECT array_agg(CASE WHEN n%3=0 THEN NULL ELSE '2001:db8::1/32'::inet END),
                       array_agg(CASE WHEN n%3=0 THEN NULL ELSE '192.0.2.0/24'::cidr END)
                FROM generate_series(1,10000) n;
                SELECT pg_column_size(a) < 10000, pg_column_size(b) < 10000,
                    datatype.network_inets(a, 4) = a, datatype.network_cidrs(b, 5) = b FROM network_arrays
                """, connection, transaction);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
            Assert.IsTrue(await reader.ReadAsync(token));
            for (int i = 0; i < 4; i++)
            {
                Assert.IsTrue(reader.GetBoolean(i), $"Column {i}");
            }
        }, context.CancellationToken);

    /// <summary>
    /// Source-generated JSON uses network strings and retains JSON paths on native input errors.
    /// </summary>
    [TestMethod]
    [DataRow("inet", "192.0.2.1/24", "192.0.2.1/24")]
    [DataRow("inet", "::ffff:192.0.2.1/128", "::ffff:192.0.2.1")]
    [DataRow("cidr", "10", "10.0.0.0/8")]
    [DataRow("cidr", "2001:db8::/32", "2001:db8::/32")]
    public Task NetworkJsonUsesAotMetadata(string type, string text, string expected)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(NetworkJsonUsesAotMetadata), async (connection, transaction, token) =>
        {
            await using var command = new NpgsqlCommand("SELECT datatype.scalar_json($1, json_build_object('Value', $2::text))->>'Value'", connection, transaction);
            command.Parameters.AddWithValue(type);
            command.Parameters.AddWithValue(text);
            Assert.AreEqual(expected, await command.ExecuteScalarAsync(token));
            command.CommandText = "SELECT datatype.scalar_json_recovery($1, '{\"Value\":\"256.1.1.1\"}'::json)";
            command.Parameters.RemoveAt(1);
            Assert.AreEqual("$.Value:22P02:50:0:2", await command.ExecuteScalarAsync(token));
        }, context.CancellationToken);
}
