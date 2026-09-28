using Npgsql;

namespace Ankus.IntegrationTests;

public sealed partial class BorrowedBufferTests
{
    /// <summary>
    /// A failed third-column conversion releases both provisional native views before recovery in the same callback.
    /// </summary>
    [TestMethod]
    public async Task BorrowedBufferConversionFailuresReleaseEarlierColumns()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.AreSequenceEqual<string>(["InvalidCastException", "0", "42"],
            await Scalar<string[]>(connection, "SELECT borrowed_buffers.buffer_conversion_failure()"));
    }

    /// <summary>
    /// Owner changes expire nested aliases without invalidating copies, including source-only resets with live children.
    /// </summary>
    /// <param name="mode">The native source or view lifetime boundary.</param>
    /// <param name="remaining">The native child count immediately after invalidation.</param>
    [TestMethod]
    [DataRow(0, "4")]
    [DataRow(1, "0")]
    [DataRow(2, "0")]
    [DataRow(3, "0")]
    public async Task BorrowedBuffersRejectSourceExpiryAndReleaseChildren(int mode, string remaining)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        for (int index = 0; index < 3; index++)
        {
            Assert.AreSequenceEqual<string>(["4", remaining, "ObjectDisposedException", "ObjectDisposedException",
                "ObjectDisposedException", "ObjectDisposedException", "ObjectDisposedException", "ObjectDisposedException",
                "ObjectDisposedException", "café", "007FFF", "5", "3"],
                await Scalar<string[]>(connection, $"SELECT borrowed_buffers.buffer_source_reset({mode})"));
            Assert.AreEqual(0L, await Scalar<long>(connection,
                "SELECT count(*) FROM ankus_test_memory.contexts WHERE ident = 'Ankus borrowed buffer'"));
        }

        Assert.AreEqual(connection.ProcessID, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }

    /// <summary>
    /// Successful and failing scalar callbacks expire all escaped views and release private native storage.
    /// </summary>
    /// <param name="fail">Whether the original callback throws after capturing its aliases.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task BorrowedBufferCallbacksExpireAliasesAndRecover(bool fail)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        for (int index = 0; index < 3; index++)
        {
            await using var save = new NpgsqlCommand($"SELECT borrowed_buffers.buffer_save('\\x00ff'::bytea,'café',{fail})", connection);
            if (fail)
            {
                PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => save.ExecuteScalarAsync(context.CancellationToken));
                Assert.AreEqual("Borrowed buffer callback failed.", error.MessageText);
            }
            else
            {
                Assert.AreEqual("00FF:café", await save.ExecuteScalarAsync(context.CancellationToken));
            }

            Assert.AreSequenceEqual<string>(["ObjectDisposedException", "ObjectDisposedException", "ObjectDisposedException",
                "ObjectDisposedException", "ObjectDisposedException", "0"],
                await Scalar<string[]>(connection, "SELECT borrowed_buffers.buffer_expired()"));
        }

        Assert.AreEqual(42, await Scalar<int>(connection, "SELECT 42"));
        Assert.AreEqual(connection.ProcessID, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }

    /// <summary>
    /// Nested callbacks retain outer buffer bytes while the inner aliases expire and their native owners disappear.
    /// </summary>
    [TestMethod]
    public async Task BorrowedBuffersRetainEnclosingCallbackLifetimes()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.AreSequenceEqual<string>(["7F:nested", "ObjectDisposedException", "ObjectDisposedException", "00FF", "outer café", "2"],
            await Scalar<string[]>(connection, "SELECT borrowed_buffers.buffer_nested('\\x00ff'::bytea,'outer café')"));
        Assert.AreEqual(0L, await Scalar<long>(connection,
            "SELECT count(*) FROM ankus_test_memory.contexts WHERE ident = 'Ankus borrowed buffer'"));
    }

    /// <summary>
    /// Aggregate state retains original text and binary snapshots after their input callbacks return.
    /// </summary>
    [TestMethod]
    public async Task BorrowedBuffersSurviveAggregateTransitions()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.AreEqual("café", await Scalar<string>(connection, """
            SELECT borrowed_buffers.text_first(value ORDER BY position)
            FROM (VALUES (0,NULL::text),(1,'café'),(2,'later')) AS input(position,value)
            """));
        Assert.AreSequenceEqual<byte>([0, 0x7f, 0xff], await Scalar<byte[]>(connection, """
            SELECT borrowed_buffers.bytea_first(value ORDER BY position)
            FROM (VALUES (0,NULL::bytea),(1,'\x007fff'::bytea),(2,'\x00'::bytea)) AS input(position,value)
            """));
        Assert.IsTrue(await Scalar<bool>(connection,
            "SELECT borrowed_buffers.text_first(NULL::text) IS NULL AND borrowed_buffers.bytea_first(NULL::bytea) IS NULL"));
        Assert.AreEqual(0L, await Scalar<long>(connection,
            "SELECT count(*) FROM ankus_test_memory.contexts WHERE ident = 'Ankus borrowed buffer'"));
        Assert.AreEqual(connection.ProcessID, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }
}
