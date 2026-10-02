using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Compares Native AOT interval ordering with PostgreSQL's independent comparison and relational operators.
/// </summary>
/// <param name="context">The per-test cancellation context.</param>
[TestClass]
public sealed class IntervalOrderingTests(TestContext context)
{
    /// <summary>
    /// Ordinary, equal-duration and mixed-sign intervals agree with the native backend comparison.
    /// </summary>
    /// <param name="left">The left interval input.</param>
    /// <param name="right">The right interval input.</param>
    [TestMethod]
    [DataRow("0", "0")]
    [DataRow("-1 microsecond", "0")]
    [DataRow("1 microsecond", "0")]
    [DataRow("1 month", "30 days")]
    [DataRow("-1 month", "-30 days")]
    [DataRow("1 day", "24 hours")]
    [DataRow("1 month -30 days -1 microsecond", "0")]
    [DataRow("1 month -30 days", "0")]
    [DataRow("1 month -30 days 1 microsecond", "0")]
    [DataRow("1 month -31 days 24 hours 1 microsecond", "0")]
    [DataRow("-1 month 31 days -24 hours -1 microsecond", "0")]
    public Task ManagedIntervalOrderingMatchesPostgres(string left, string right)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(ManagedIntervalOrderingMatchesPostgres),
            async (connection, transaction, token) =>
            {
                await using var command = new NpgsqlCommand($"""
                    WITH inputs AS (SELECT $1::interval AS l, $2::interval AS r)
                    {ComparisonSql}
                    """, connection, transaction);
                command.Parameters.AddWithValue(left);
                command.Parameters.AddWithValue(right);
                await AssertComparisonAsync(command, token);
            }, context.CancellationToken);

    /// <summary>
    /// The full native components require wide intermediate arithmetic and preserve one-microsecond distinctions.
    /// </summary>
    /// <param name="leftMonths">The left months.</param>
    /// <param name="leftDays">The left days.</param>
    /// <param name="leftMicros">The left microseconds.</param>
    /// <param name="rightMonths">The right months.</param>
    /// <param name="rightDays">The right days.</param>
    /// <param name="rightMicros">The right microseconds.</param>
    [TestMethod]
    [DataRow(int.MaxValue, int.MaxValue, long.MaxValue - 1, 0, 0, long.MaxValue)]
    [DataRow(int.MinValue, int.MinValue, long.MinValue + 1, 0, 0, long.MinValue)]
    [DataRow(int.MaxValue, int.MinValue, long.MinValue, 0, 0, long.MaxValue)]
    [DataRow(int.MinValue, int.MaxValue, long.MaxValue, 0, 0, long.MinValue)]
    [DataRow(int.MaxValue, int.MaxValue, long.MaxValue - 2, int.MaxValue, int.MaxValue, long.MaxValue - 1)]
    public Task FullRangeOrderingMatchesPostgres(int leftMonths, int leftDays, long leftMicros,
        int rightMonths, int rightDays, long rightMicros)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(FullRangeOrderingMatchesPostgres),
            async (connection, transaction, token) =>
            {
                await using var command = new NpgsqlCommand($"""
                    WITH inputs AS (SELECT datatype.interval_from_parts($1,$2,$3) AS l,
                        datatype.interval_from_parts($4,$5,$6) AS r)
                    {ComparisonSql}
                    """, connection, transaction);
                command.Parameters.AddWithValue(leftMonths);
                command.Parameters.AddWithValue(leftDays);
                command.Parameters.AddWithValue(leftMicros);
                command.Parameters.AddWithValue(rightMonths);
                command.Parameters.AddWithValue(rightDays);
                command.Parameters.AddWithValue(rightMicros);
                await AssertComparisonAsync(command, token);
            }, context.CancellationToken);

    /// <summary>
    /// Native infinities follow PostgreSQL's version support and order outside finite values.
    /// </summary>
    /// <param name="left">The left interval input.</param>
    /// <param name="right">The right interval input.</param>
    [TestMethod]
    [DataRow("-infinity", "infinity")]
    [DataRow("infinity", "-infinity")]
    [DataRow("-infinity", "-infinity")]
    [DataRow("infinity", "infinity")]
    [DataRow("infinity", "1 month")]
    [DataRow("-infinity", "-1 month")]
    public Task NativeInfinityOrderingRetainsVersionSupport(string left, string right)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(NativeInfinityOrderingRetainsVersionSupport),
            async (connection, transaction, token) =>
            {
                await using var command = new NpgsqlCommand($"""
                    WITH inputs AS (SELECT $1::interval AS l, $2::interval AS r)
                    {ComparisonSql}
                    """, connection, transaction);
                command.Parameters.AddWithValue(left);
                command.Parameters.AddWithValue(right);
                if (PostgresFixture.Cluster.Installation.Version.Major >= 17)
                {
                    await AssertComparisonAsync(command, token);
                }
                else
                {
                    await using var savepoint = new NpgsqlCommand("SAVEPOINT interval_ordering", connection, transaction);
                    await savepoint.ExecuteNonQueryAsync(token);
                    int backend = connection.ProcessID;
                    PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(
                        async () => await command.ExecuteReaderAsync(token));
                    Assert.AreEqual("22007", error.SqlState);
                    await using var recover = new NpgsqlCommand("ROLLBACK TO SAVEPOINT interval_ordering; SELECT 42", connection, transaction);
                    Assert.AreEqual(42, await recover.ExecuteScalarAsync(token));
                    Assert.AreEqual(backend, connection.ProcessID);
                }
            }, context.CancellationToken);

    /// <summary>
    /// Independently evaluates native ordering and the compiled managed callback on the same input datums.
    /// </summary>
    private const string ComparisonSql = """
        SELECT ARRAY[interval_cmp(l,r),(l<r)::int,(l>r)::int,(l<=r)::int,(l>=r)::int,(l=r)::int,(l<>r)::int,
            CASE WHEN l=r THEN (interval_hash(l)=interval_hash(r))::int ELSE 1 END],
            datatype.interval_ordering(l,r) FROM inputs
        """;

    /// <summary>
    /// Checks exact signs, every operator and reader exhaustion against PostgreSQL's independent oracle.
    /// </summary>
    /// <param name="command">The query containing native and managed results.</param>
    /// <param name="token">The test cancellation token.</param>
    private static async Task AssertComparisonAsync(NpgsqlCommand command, CancellationToken token)
    {
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
        Assert.IsTrue(await reader.ReadAsync(token));
        Assert.AreSequenceEqual(reader.GetFieldValue<int[]>(0), reader.GetFieldValue<int[]>(1));
        Assert.IsFalse(await reader.ReadAsync(token));
    }
}
