using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Maps pgrx's ten-thousand-string/top-five borrow rejection to checked native expiry and independent managed copies.
/// </summary>
/// <param name="context">The current test cancellation context.</param>
[TestClass]
public sealed class LargeArrayLifetimeTests(TestContext context)
{
    /// <summary>
    /// Every owner-ending path invalidates all five borrows while preserving copied text and the caller's original native array.
    /// </summary>
    /// <param name="mode">The independently selected owner-ending operation.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public Task LargeArrayPrefixBorrowsExpireAndCopiesSurvive(int mode)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(LargeArrayPrefixBorrowsExpireAndCopiesSurvive), async (connection, transaction, token) =>
        {
            await using var command = new NpgsqlCommand("""
                CREATE TEMP TABLE large_array_lifetime(a text[]);
                INSERT INTO large_array_lifetime
                    SELECT array_agg(input.value::text ORDER BY input.value) FROM generate_series(1,10000) AS input(value);
                """, connection, transaction);
            await command.ExecuteNonQueryAsync(token);
            command.CommandText = "SELECT array_send(a) FROM large_array_lifetime";
            byte[] before = Assert.IsInstanceOfType<byte[]>(await command.ExecuteScalarAsync(token));
            command.CommandText = "SELECT cardinality(a) FROM large_array_lifetime";
            Assert.AreEqual(10000, await command.ExecuteScalarAsync(token));
            command.CommandText = $"SELECT array_lifetimes.large_array_prefix_lifetime(a,{mode}) FROM large_array_lifetime";
            Assert.AreSequenceEqual<string?>(["ObjectDisposedException", "ObjectDisposedException", "ObjectDisposedException",
                "ObjectDisposedException", "ObjectDisposedException", "1", "2", "3", "4", "5", "1"],
                Assert.IsInstanceOfType<string?[]>(await command.ExecuteScalarAsync(token)));
            command.CommandText = "SELECT array_send(a) FROM large_array_lifetime";
            Assert.AreSequenceEqual(before, Assert.IsInstanceOfType<byte[]>(await command.ExecuteScalarAsync(token)));
            command.CommandText = "SELECT count(*) FROM ankus_test_memory.contexts WHERE ident LIKE 'Ankus borrowed array%' OR ident LIKE 'Ankus borrowed buffer%' OR ident='large array ownership'";
            Assert.AreEqual(0L, await command.ExecuteScalarAsync(token));
            command.CommandText = "SELECT 42";
            Assert.AreEqual(42, await command.ExecuteScalarAsync(token));
            command.CommandText = "SELECT pg_backend_pid()";
            Assert.AreEqual(connection.ProcessID, await command.ExecuteScalarAsync(token));
        }, context.CancellationToken);
}
