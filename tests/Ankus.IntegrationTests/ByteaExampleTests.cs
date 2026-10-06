using System.IO.Compression;
using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Executes the pgrx bytea example through its published Native AOT library.
/// </summary>
/// <param name="context">The current native test cancellation context.</param>
[TestClass]
[DoNotParallelize]
public sealed class ByteaExampleTests(TestContext context)
{
    /// <summary>
    /// Preserves both upstream SQL examples and decodes independently generated gzip bytes.
    /// </summary>
    [TestMethod]
    public Task ByteaSampleMatchesUpstreamAndIndependentMembers()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(ByteaSampleMatchesUpstreamAndIndependentMembers), async (connection, transaction, token) =>
        {
            await InstallAsync(connection, transaction, token);
            await using var command = new NpgsqlCommand("""
                SELECT bytea_example.gunzip_as_text(bytea_example.gzip('hi there')),
                    bytea_example.gunzip(bytea_example.gzip('hi there'::bytea)),
                    bytea_example.gunzip($1), bytea_example.gunzip_as_text($1)
                """, connection, transaction);
            command.Parameters.AddWithValue(Convert.FromHexString("1F8B0800000000000003CBC85428C9482D4A0500EC76A3E308000000"));
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
            Assert.IsTrue(await reader.ReadAsync(token));
            Assert.AreEqual("hi there", reader.GetString(0));
            Assert.AreSequenceEqual("hi there"u8.ToArray(), reader.GetFieldValue<byte[]>(1));
            Assert.AreSequenceEqual("hi there"u8.ToArray(), reader.GetFieldValue<byte[]>(2));
            Assert.AreEqual("hi there", reader.GetString(3));
            Assert.AreEqual("bytea", reader.GetDataTypeName(1));
            Assert.AreEqual("text", reader.GetDataTypeName(3));
            Assert.IsFalse(await reader.ReadAsync(token));
        }, context.CancellationToken);

    /// <summary>
    /// Preserves empty, singleton, complete byte alphabets and externally stored bytea values.
    /// </summary>
    /// <param name="length">The independently constructed byte payload length.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(256)]
    [DataRow(65537)]
    public Task ByteaSampleRoundTripsNativeBytes(int length)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(ByteaSampleRoundTripsNativeBytes), async (connection, transaction, token) =>
        {
            await InstallAsync(connection, transaction, token);
            byte[] input = [.. Enumerable.Range(0, length).Select(static index => (byte)(index % 256))];
            await using var setup = new NpgsqlCommand("""
                CREATE TABLE bytea_source (value bytea);
                ALTER TABLE bytea_source ALTER COLUMN value SET STORAGE EXTERNAL
                """, connection, transaction);
            await setup.ExecuteNonQueryAsync(token);
            await using var insert = new NpgsqlCommand("INSERT INTO bytea_source VALUES ($1)", connection, transaction);
            insert.Parameters.AddWithValue(input);
            await insert.ExecuteNonQueryAsync(token);
            await using var command = new NpgsqlCommand("""
                SELECT bytea_example.gzip(value), bytea_example.gunzip(bytea_example.gzip(value)),
                    octet_length(value), pg_column_size(value) FROM bytea_source
                """, connection, transaction);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
            Assert.IsTrue(await reader.ReadAsync(token));
            byte[] member = reader.GetFieldValue<byte[]>(0);
            byte[] restored = reader.GetFieldValue<byte[]>(1);
            Assert.AreEqual(length, reader.GetInt32(2));
            if (length == 65537)
            {
                Assert.AreEqual(length, reader.GetInt32(3));
            }

            Assert.AreSequenceEqual(input, restored);
            // Ten header bytes, a complete DEFLATE block, and eight trailer bytes.
            Assert.IsGreaterThanOrEqualTo(20, member.Length);
            byte[] magic = [0x1f, 0x8b, 8];
            Assert.AreSequenceEqual(magic, member[..3]);
            using var compressed = new MemoryStream(member, writable: false);
            using var decoder = new GZipStream(compressed, CompressionMode.Decompress);
            using var plain = new MemoryStream();
            decoder.CopyTo(plain);
            Assert.AreSequenceEqual(input, plain.ToArray());
            Assert.IsFalse(await reader.ReadAsync(token));
        }, context.CancellationToken);

    /// <summary>
    /// Distinguishes strict SQL NULL from present empty bytea and empty text.
    /// </summary>
    [TestMethod]
    public Task ByteaSamplePreservesNullAndEmptyValues()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(ByteaSamplePreservesNullAndEmptyValues), async (connection, transaction, token) =>
        {
            await InstallAsync(connection, transaction, token);
            await using var command = new NpgsqlCommand("""
                SELECT bytea_example.gzip(NULL::bytea), bytea_example.gunzip(NULL::bytea),
                    bytea_example.gunzip_as_text(NULL::bytea),
                    bytea_example.gunzip(bytea_example.gzip(''::bytea)),
                    bytea_example.gunzip_as_text(bytea_example.gzip(''::bytea)),
                    (SELECT count(*) FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
                        WHERE n.nspname = 'bytea_example' AND p.proisstrict AND
                            p.proname IN ('gzip', 'gunzip', 'gunzip_as_text'))
                """, connection, transaction);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
            Assert.IsTrue(await reader.ReadAsync(token));
            Assert.IsTrue(reader.IsDBNull(0));
            Assert.IsTrue(reader.IsDBNull(1));
            Assert.IsTrue(reader.IsDBNull(2));
            Assert.IsEmpty(reader.GetFieldValue<byte[]>(3));
            Assert.AreEqual(string.Empty, reader.GetString(4));
            Assert.AreEqual(3L, reader.GetInt64(5));
            Assert.IsFalse(await reader.ReadAsync(token));
        }, context.CancellationToken);

    /// <summary>
    /// Decodes explicitly encoded text without losing Unicode or replacing its original UTF-8 bytes.
    /// </summary>
    /// <param name="text">The independently specified Unicode payload.</param>
    [TestMethod]
    [DataRow("")]
    [DataRow("hi there")]
    [DataRow("中文 € 😀")]
    public Task ByteaSamplePreservesUtf8Text(string text)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(ByteaSamplePreservesUtf8Text), async (connection, transaction, token) =>
        {
            await InstallAsync(connection, transaction, token);
            await using var command = new NpgsqlCommand("SELECT bytea_example.gunzip_as_text(bytea_example.gzip(convert_to($1, 'UTF8')))", connection, transaction);
            command.Parameters.AddWithValue(text);
            Assert.AreEqual(text, Assert.IsInstanceOfType<string>(await command.ExecuteScalarAsync(token)));
        }, context.CancellationToken);

    /// <summary>
    /// Typed text and varchar require explicit byte encoding rather than an implicit PostgreSQL function conversion.
    /// </summary>
    /// <param name="type">The independently selected textual PostgreSQL argument type.</param>
    [TestMethod]
    [DataRow("text")]
    [DataRow("varchar")]
    public Task ByteaSampleRequiresExplicitTypedTextConversion(string type)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(ByteaSampleRequiresExplicitTypedTextConversion), async (connection, transaction, token) =>
        {
            await InstallAsync(connection, transaction, token);
            await transaction.SaveAsync("bytea_type", token);
            await using var command = new NpgsqlCommand("SELECT bytea_example.gzip($1::" + type + ")", connection, transaction);
            command.Parameters.AddWithValue("hi there");
            PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(token));
            Assert.AreEqual("42883", error.SqlState);
            await transaction.RollbackAsync("bytea_type", token);
            command.CommandText = "SELECT bytea_example.gunzip_as_text(bytea_example.gzip(convert_to($1, 'UTF8')))";
            Assert.AreEqual("hi there", Assert.IsInstanceOfType<string>(await command.ExecuteScalarAsync(token)));
        }, context.CancellationToken);

    /// <summary>
    /// Ignores subsequent members and nonauthoritative ISIZE while verifying the first payload.
    /// </summary>
    /// <param name="suffix">Whether to append another complete independent member.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task ByteaSamplePreservesFirstMemberPolicy(bool suffix)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(ByteaSamplePreservesFirstMemberPolicy), async (connection, transaction, token) =>
        {
            await InstallAsync(connection, transaction, token);
            byte[] first = Convert.FromHexString("1F8B08000000000000FF010300FCFF616263C241243503000000");
            byte[] next = [.. first];
            first[^4] = 99;
            byte[] input = suffix ? [.. first, .. next] : [.. first, 0xff, 0];
            await using var command = new NpgsqlCommand("SELECT bytea_example.gunzip($1), bytea_example.gunzip_as_text($1)", connection, transaction);
            command.Parameters.AddWithValue(input);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
            Assert.IsTrue(await reader.ReadAsync(token));
            Assert.AreSequenceEqual("abc"u8.ToArray(), reader.GetFieldValue<byte[]>(0));
            Assert.AreEqual("abc", reader.GetString(1));
            Assert.IsFalse(await reader.ReadAsync(token));
        }, context.CancellationToken);

    /// <summary>
    /// Invalid headers, trailers, checksums and UTF-8 fail without losing the backend or later sample calls.
    /// </summary>
    /// <param name="scenario">The independently selected malformed-input contract.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    public Task ByteaSampleFailuresRecoverSameBackend(int scenario)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(ByteaSampleFailuresRecoverSameBackend), async (connection, transaction, token) =>
        {
            await InstallAsync(connection, transaction, token);
            int backend = connection.ProcessID;
            byte[] member = Convert.FromHexString("1F8B08000000000000FF010300FCFF616263C241243503000000");
            byte[] invalid = scenario switch
            {
                0 => [],
                1 => member[..10],
                2 => member[..^8],
                3 => [.. member],
                4 => [0xff],
                _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
            };
            if (scenario == 3)
            {
                invalid[^8] ^= 1;
            }

            await transaction.SaveAsync("bytea_failure", token);
            await using var command = new NpgsqlCommand(scenario == 4
                ? "SELECT bytea_example.gunzip_as_text(bytea_example.gzip($1))"
                : "SELECT bytea_example.gunzip($1)", connection, transaction);
            command.Parameters.AddWithValue(invalid);
            PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(token));
            Assert.AreEqual("38000", error.SqlState);
            Assert.IsNotEmpty(error.MessageText);
            await transaction.RollbackAsync("bytea_failure", token);
            command.Parameters.Clear();
            command.CommandText = "SELECT pg_backend_pid(), bytea_example.gunzip_as_text(bytea_example.gzip('hi there'))";
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
            Assert.IsTrue(await reader.ReadAsync(token));
            Assert.AreEqual(backend, reader.GetInt32(0));
            Assert.AreEqual("hi there", reader.GetString(1));
            Assert.IsFalse(await reader.ReadAsync(token));
        }, context.CancellationToken);

    /// <summary>
    /// Installs the independently published sample into its rollback-isolated consuming schema.
    /// </summary>
    /// <param name="connection">The isolated native backend.</param>
    /// <param name="transaction">The test's rollback scope.</param>
    /// <param name="token">Cancels installation.</param>
    /// <returns>The installation completion.</returns>
    private static async Task InstallAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken token)
    {
        await using var command = new NpgsqlCommand("CREATE SCHEMA bytea_example; CREATE EXTENSION ankus_bytea WITH SCHEMA bytea_example", connection, transaction);
        await command.ExecuteNonQueryAsync(token);
    }
}
