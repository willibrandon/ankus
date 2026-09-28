using Npgsql;

namespace Ankus.IntegrationTests;

public sealed partial class CStringTests
{
    /// <summary>
    /// An independent C caller proves exact storage and the distinction between a null address and SQL NULL.
    /// </summary>
    /// <param name="method">The owned, borrowed or raw observation callback.</param>
    /// <param name="absent">Whether the native input address is zero while isnull remains false.</param>
    [TestMethod]
    [DataRow("owned_storage", false)]
    [DataRow("owned_storage", true)]
    [DataRow("borrowed_storage", false)]
    [DataRow("borrowed_storage", true)]
    [DataRow("raw_storage", false)]
    [DataRow("raw_storage", true)]
    public async Task CStringNativeArgumentsPreserveBytesAndNullAddress(string method, bool absent)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.IsTrue(await Scalar<bool>(connection,
            $"SELECT tests.cstring_argument('cstrings.{method}(cstring,bigint)'::regprocedure,{absent})"));
        await AssertCleanBackend(connection);
    }

    /// <summary>
    /// Both successful and failing callbacks expire every native alias while preserving the owned copy.
    /// </summary>
    /// <param name="decode">Whether strict UTF-8 conversion rejects the deliberately non-UTF-8 payload.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CStringCallbacksExpireAliasesAndRecover(bool decode)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        await using var command = new NpgsqlCommand(
            $"SELECT cstrings.save_view(cstrings.create_cstring('\\x80ff'::bytea),{decode})", connection);
        if (decode)
        {
            PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(context.CancellationToken));
            Assert.AreEqual("38000", error.SqlState);
            Assert.Contains("Unable to translate bytes", error.MessageText);
        }
        else
        {
            Assert.AreEqual("80FF", await command.ExecuteScalarAsync(context.CancellationToken));
        }

        Assert.AreSequenceEqual<string>(["ObjectDisposedException", "ObjectDisposedException", "ObjectDisposedException",
            "ObjectDisposedException", "ObjectDisposedException", "0", "80FF"],
            await Scalar<string[]>(connection, "SELECT cstrings.expired_view()"));
        Assert.AreEqual("café", await Scalar<string>(connection,
            "SELECT cstrings.save_view(cstrings.create_cstring('\\x636166c3a9'::bytea),true)"));
        await AssertCleanBackend(connection);
    }

    /// <summary>
    /// Native source resets, deletion and view disposal invalidate every dependent alias before access.
    /// </summary>
    /// <param name="mode">The ownership boundary to invalidate.</param>
    /// <param name="remaining">The native private-context count immediately after invalidation.</param>
    [TestMethod]
    [DataRow(0, "2")]
    [DataRow(1, "0")]
    [DataRow(2, "0")]
    [DataRow(3, "0")]
    public async Task CStringSourcesExpireWithoutInvalidatingCopies(int mode, string remaining)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.AreSequenceEqual<string>(["2", remaining, "ObjectDisposedException", "ObjectDisposedException", "ObjectDisposedException",
            "ObjectDisposedException", "ObjectDisposedException", "80FF", "80FF", "2"],
            await Scalar<string[]>(connection, $"SELECT cstrings.source_reset({mode})"));
        await AssertCleanBackend(connection);
    }

    /// <summary>
    /// Aggregate state retains the original native snapshot and releases later inputs after callbacks finish.
    /// </summary>
    [TestMethod]
    public async Task CStringAggregateRetainsFirstPresentBytes()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.AreEqual("80ff", await Scalar<string>(connection, """
            SELECT encode(cstrings.first_bytes(cstrings.create_cstring(value) ORDER BY position),'hex')
            FROM (VALUES (0,NULL::bytea),(1,'\x80ff'::bytea),(2,'\x01'::bytea)) AS input(position,value)
            """));
        Assert.IsTrue(await Scalar<bool>(connection, "SELECT cstrings.first_bytes(NULL::cstring) IS NULL"));
        await AssertCleanBackend(connection);
    }

    /// <summary>
    /// Native bytes remain unchanged in a non-UTF-8 database while explicit UTF-8 conversion remains strict.
    /// </summary>
    [TestMethod]
    public async Task CStringLatin1PreservesBytesWithoutTranscoding()
    {
        string database = "cstring_latin1_" + Guid.NewGuid().ToString("N");
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
            await using (var install = new NpgsqlCommand("CREATE EXTENSION ankus_test", connection))
            {
                await install.ExecuteNonQueryAsync(context.CancellationToken);
            }

            Assert.AreEqual("LATIN1", await Scalar<string>(connection, "SHOW server_encoding"));
            Assert.AreEqual("e9", await Scalar<string>(connection,
                "SELECT encode(cstrings.owned_bytes(cstrings.borrowed_echo(cstrings.create_cstring('\\xe9'::bytea))),'hex')"));
            Assert.AreEqual("café", await Scalar<string>(connection,
                "SELECT cstrings.save_view(cstrings.create_cstring('\\x636166c3a9'::bytea),true)"));
            await using var invalid = new NpgsqlCommand("SELECT cstrings.save_view(cstrings.create_cstring('\\xe9'::bytea),true)", connection);
            PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => invalid.ExecuteScalarAsync(context.CancellationToken));
            Assert.AreEqual("38000", error.SqlState);
            Assert.Contains("Unable to translate bytes", error.MessageText);
            await AssertCleanBackend(connection);
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE {database} WITH (FORCE)", administrator);
            await drop.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// Verifies independent native ownership cleanup and same-session recovery after each boundary.
    /// </summary>
    private async Task AssertCleanBackend(NpgsqlConnection connection)
    {
        Assert.AreEqual(0L, await Scalar<long>(connection,
            "SELECT count(*) FROM ankus_test_memory.contexts WHERE ident = 'Ankus borrowed buffer'"));
        Assert.AreEqual(42, await Scalar<int>(connection, "SELECT 42"));
        Assert.AreEqual(connection.ProcessID, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }
}
