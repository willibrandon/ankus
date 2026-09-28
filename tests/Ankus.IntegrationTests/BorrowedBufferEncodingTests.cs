using Npgsql;

namespace Ankus.IntegrationTests;

public sealed partial class BorrowedBufferTests
{
    /// <summary>
    /// Invalid native UTF-8 and embedded NUL reject at the guarded backend boundary without lossy replacement.
    /// </summary>
    /// <param name="value">An invalid leading byte, truncated character or zero byte.</param>
    [TestMethod]
    [DataRow(255)]
    [DataRow(195)]
    [DataRow(0)]
    public async Task BorrowedTextRejectsMalformedNativeEncodingAndRecovers(int value)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        await using var command = new NpgsqlCommand($"""
            SELECT tests.buffer_invalid_text('borrowed_buffers.text_storage(text,bigint,bigint,text)'::regprocedure,{value})
            """, connection);
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(context.CancellationToken));
        Assert.AreEqual("22021", error.SqlState);
        Assert.Contains("invalid byte sequence", error.MessageText);
        Assert.AreEqual(42, await Scalar<int>(connection, "SELECT 42"));
        Assert.AreEqual(connection.ProcessID, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
        Assert.AreEqual(0L, await Scalar<long>(connection,
            "SELECT count(*) FROM ankus_test_memory.contexts WHERE ident = 'Ankus borrowed buffer'"));
    }

    /// <summary>
    /// LATIN1 input is converted to exact UTF-8 bytes while return transport retains original server encoding.
    /// </summary>
    [TestMethod]
    public async Task BorrowedTextConvertsLatin1WithoutChangingReturnedStorage()
    {
        string database = "borrowed_latin1_" + Guid.NewGuid().ToString("N");
        await using NpgsqlConnection administrator = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        await using (var create = new NpgsqlCommand(
            $"CREATE DATABASE {database} TEMPLATE template0 ENCODING 'LATIN1' LC_COLLATE 'C' LC_CTYPE 'C'", administrator))
        {
            await create.ExecuteNonQueryAsync(context.CancellationToken);
        }

        try
        {
            var builder = new NpgsqlConnectionStringBuilder(administrator.ConnectionString) { Database = database, Pooling = false };
            await using var connection = new NpgsqlConnection(builder.ConnectionString);
            await connection.OpenAsync(context.CancellationToken);
            await using (var install = new NpgsqlCommand("CREATE EXTENSION ankus_test; CREATE SCHEMA tests;" +
                AllocatorFixtureCompiler.InstallationSql, connection))
            {
                await install.ExecuteNonQueryAsync(context.CancellationToken);
            }

            Assert.AreEqual("LATIN1", await Scalar<string>(connection, "SHOW server_encoding"));
            Assert.AreSequenceEqual<string>(["25", "5", "café", "636166C3A9"],
                await Scalar<string[]>(connection, "SELECT borrowed_buffers.text_snapshot('café')"));
            Assert.AreEqual("café", await Scalar<string>(connection, "SELECT borrowed_buffers.text_identity('café')"));
            Assert.AreSequenceEqual<string>(["flat", "False", "False", "borrowed", "5", "café"],
                await Scalar<string[]>(connection, """
                    SELECT tests.buffer_argument('borrowed_buffers.text_storage(text,bigint,bigint,text)'::regprocedure,'café'::text)
                    """));
            Assert.AreEqual(connection.ProcessID, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE {database} WITH (FORCE)", administrator);
            await drop.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// Existing domain values retain their original type despite new constraints, and bpchar padding remains observable.
    /// </summary>
    [TestMethod]
    public async Task BorrowedBuffersPreserveDomainIdentityAndPaddedText()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(context.CancellationToken);
        await using (var setup = new NpgsqlCommand("""
            CREATE DOMAIN pg_temp.borrowed_text_domain AS text;
            CREATE DOMAIN pg_temp.borrowed_bytea_domain AS bytea;
            CREATE TEMP TABLE borrowed_domains(label pg_temp.borrowed_text_domain, data pg_temp.borrowed_bytea_domain);
            INSERT INTO borrowed_domains VALUES ('café','\x0000ff');
            ALTER DOMAIN pg_temp.borrowed_text_domain ADD CHECK (length(VALUE) > 100) NOT VALID;
            ALTER DOMAIN pg_temp.borrowed_bytea_domain ADD CHECK (octet_length(VALUE) > 100) NOT VALID;
            """, connection, transaction))
        {
            await setup.ExecuteNonQueryAsync(context.CancellationToken);
        }

        string textOid = await Scalar<string>(connection, "SELECT 'pg_temp.borrowed_text_domain'::regtype::oid::text");
        string byteaOid = await Scalar<string>(connection, "SELECT 'pg_temp.borrowed_bytea_domain'::regtype::oid::text");
        Assert.AreSequenceEqual<string>([textOid, "5", "café", "636166C3A9"],
            await Scalar<string[]>(connection, "SELECT borrowed_buffers.raw_text_snapshot(label) FROM borrowed_domains"));
        Assert.AreSequenceEqual<string>([byteaOid, "3", "0000FF"],
            await Scalar<string[]>(connection, "SELECT borrowed_buffers.raw_bytea_snapshot(data) FROM borrowed_domains"));
        Assert.AreSequenceEqual<string>(["1043", "5", "café", "636166C3A9"],
            await Scalar<string[]>(connection, "SELECT borrowed_buffers.raw_text_snapshot('café'::varchar(8))"));
        Assert.AreSequenceEqual<string>(["1042", "9", "café    ", "636166C3A920202020"],
            await Scalar<string[]>(connection, "SELECT borrowed_buffers.raw_text_snapshot('café'::character(8))"));
    }

    /// <summary>
    /// Incompatible raw types raise owned native diagnostics and leave the same backend ready for another query.
    /// </summary>
    /// <param name="method">The raw borrowed view constructor.</param>
    /// <param name="expression">The incompatible native value.</param>
    [TestMethod]
    [DataRow("raw_text_snapshot", "42")]
    [DataRow("raw_text_snapshot", "'\\x4142'::bytea")]
    [DataRow("raw_bytea_snapshot", "42")]
    [DataRow("raw_bytea_snapshot", "'text'::text")]
    public async Task BorrowedBufferTypeErrorsPreserveBackend(string method, string expression)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        await using var command = new NpgsqlCommand($"SELECT borrowed_buffers.{method}({expression})", connection);
        PostgresException failure = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(context.CancellationToken));
        Assert.AreEqual("42804", failure.SqlState);
        Assert.AreEqual("The borrowed value is not a compatible buffer type", failure.MessageText);
        Assert.AreEqual(42, await Scalar<int>(connection, "SELECT 42"));
        Assert.AreEqual(connection.ProcessID, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }
}
