using System.Buffers.Binary;
using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies exact interval construction and overflow recovery across native PostgreSQL versions.
/// </summary>
/// <param name="context">The per-test cancellation and diagnostic context.</param>
[TestClass]
public sealed class IntervalFactoryTests(TestContext context)
{
    /// <summary>
    /// Native wire values preserve finite field limits, mixed signs, and PostgreSQL's microsecond rounding.
    /// </summary>
    /// <param name="arguments">The seven constructor arguments expressed as SQL literals.</param>
    /// <param name="months">The independently expected stored months.</param>
    /// <param name="days">The independently expected stored days.</param>
    /// <param name="microseconds">The independently expected stored time.</param>
    [TestMethod]
    [DataRow("0,0,0,0,0,0,0", 0, 0, 0L)]
    [DataRow("1,-2,3,-4,5,-6,7.1234567", 10, 17, 17647123457L)]
    [DataRow("178956970,7,0,0,0,0,0", int.MaxValue, 0, 0L)]
    [DataRow("-178956970,-8,0,0,0,0,0", int.MinValue, 0, 0L)]
    [DataRow("0,0,306783378,1,0,0,0", 0, int.MaxValue, 0L)]
    [DataRow("0,0,-306783378,-2,0,0,0", 0, int.MinValue, 0L)]
    [DataRow("0,2147483647,0,-2147483648,0,0,0", int.MaxValue, int.MinValue, 0L)]
    [DataRow("0,0,0,0,2147483647,2147483647,0", 0, 0, 7859790148020000000L)]
    [DataRow("0,0,0,0,-2147483648,-2147483648,0", 0, 0, -7859790151680000000L)]
    [DataRow("0,0,0,0,2147483647,-2147483648,0", 0, 0, 7602092110320000000L)]
    [DataRow("0,0,0,0,0,0,0.00000049", 0, 0, 0L)]
    [DataRow("0,0,0,0,0,0,-0.00000049", 0, 0, 0L)]
    [DataRow("0,0,0,0,0,0,0.0000005", 0, 0, 0L)]
    [DataRow("0,0,0,0,0,0,-0.0000005", 0, 0, 0L)]
    [DataRow("0,0,0,0,0,0,0.00000051", 0, 0, 1L)]
    [DataRow("0,0,0,0,0,0,-0.00000051", 0, 0, -1L)]
    [DataRow("0,0,0,0,0,0,0.0000015", 0, 0, 2L)]
    [DataRow("0,0,0,0,0,0,-0.0000015", 0, 0, -2L)]
    [DataRow("0,0,0,0,0,0,9223372036854.773", 0, 0, 9223372036854773760L)]
    [DataRow("0,0,0,0,0,0,-9223372036854.773", 0, 0, -9223372036854773760L)]
    [DataRow("0,0,0,0,0,0,-9223372036854.775808", 0, 0, long.MinValue)]
    [DataRow("0,0,0,0,0,-1,9223372036800", 0, 0, 9223372036740000000L)]
    [DataRow("0,0,0,0,0,1,-9223372036800", 0, 0, -9223372036740000000L)]
    public Task IntervalFactoryPreservesBoundaryComponents(string arguments, int months, int days, long microseconds)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(IntervalFactoryPreservesBoundaryComponents),
            async (connection, transaction, token) =>
            {
                await using var command = new NpgsqlCommand($"SELECT interval_send(datatype.interval_factory({arguments}))", connection, transaction);
                byte[] bytes = Assert.IsInstanceOfType<byte[]>(await command.ExecuteScalarAsync(token));
                Assert.HasCount(16, bytes);
                Assert.AreEqual(microseconds, BinaryPrimitives.ReadInt64BigEndian(bytes));
                Assert.AreEqual(days, BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(8)));
                Assert.AreEqual(months, BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(12)));
            }, context.CancellationToken);

    /// <summary>
    /// Every native overflow unwinds safely, restores the failed statement, and leaves managed and SQL state usable.
    /// </summary>
    /// <param name="arguments">The seven rejected constructor arguments expressed as SQL literals.</param>
    /// <param name="sqlState">The exact native error code.</param>
    /// <param name="message">The exact native error message.</param>
    [TestMethod]
    [DataRow("178956971,0,0,0,0,0,0", "22008", "interval out of range")]
    [DataRow("-178956971,0,0,0,0,0,0", "22008", "interval out of range")]
    [DataRow("178956971,-12,0,0,0,0,0", "22008", "interval out of range")]
    [DataRow("-178956971,12,0,0,0,0,0", "22008", "interval out of range")]
    [DataRow("178956970,8,0,0,0,0,0", "22008", "interval out of range")]
    [DataRow("-178956970,-9,0,0,0,0,0", "22008", "interval out of range")]
    [DataRow("0,0,306783379,0,0,0,0", "22008", "interval out of range")]
    [DataRow("0,0,-306783379,0,0,0,0", "22008", "interval out of range")]
    [DataRow("0,0,306783379,-7,0,0,0", "22008", "interval out of range")]
    [DataRow("0,0,-306783379,7,0,0,0", "22008", "interval out of range")]
    [DataRow("0,0,306783378,2,0,0,0", "22008", "interval out of range")]
    [DataRow("0,0,-306783378,-3,0,0,0", "22008", "interval out of range")]
    [DataRow("0,0,0,0,0,0,'NaN'::float8", "22008", "interval out of range")]
    [DataRow("0,0,0,0,0,0,'Infinity'::float8", "22008", "interval out of range")]
    [DataRow("0,0,0,0,0,0,'-Infinity'::float8", "22008", "interval out of range")]
    [DataRow("0,0,0,0,0,0,1e308", "22003", "value out of range: overflow")]
    [DataRow("0,0,0,0,0,0,-1e308", "22003", "value out of range: overflow")]
    [DataRow("0,0,0,0,0,0,1e18", "22008", "interval out of range")]
    [DataRow("0,0,0,0,0,0,-1e18", "22008", "interval out of range")]
    [DataRow("0,0,0,0,0,0,9223372036854.775808", "22008", "interval out of range")]
    [DataRow("0,0,0,0,0,0,-9223372036854.778", "22008", "interval out of range")]
    [DataRow("0,0,0,0,-1,0,9223372036854.775808", "22008", "interval out of range")]
    [DataRow("0,0,0,0,1,0,-9223372036854.778", "22008", "interval out of range")]
    [DataRow("0,0,0,0,0,1,9223372036800", "22008", "interval out of range")]
    [DataRow("0,0,0,0,0,-1,-9223372036800", "22008", "interval out of range")]
    public Task IntervalFactoryErrorsPreserveState(string arguments, string sqlState, string message)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(IntervalFactoryErrorsPreserveState),
            async (connection, transaction, token) =>
            {
                int backend = connection.ProcessID;
                await transaction.SaveAsync("interval_factory_error", token);
                await using var command = new NpgsqlCommand($"SELECT datatype.interval_factory({arguments})", connection, transaction);
                PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(token));
                Assert.AreEqual(sqlState, error.SqlState);
                Assert.AreEqual(message, error.MessageText);
                await transaction.RollbackAsync("interval_factory_error", token);
                command.CommandText = $"SELECT datatype.interval_factory_recovery({arguments})";
                Assert.AreEqual($"{sqlState}|{message}|50|50|True|0|2|10,17,17647123457", await command.ExecuteScalarAsync(token));
                command.CommandText = "SELECT 42";
                Assert.AreEqual(42, await command.ExecuteScalarAsync(token));
                Assert.AreEqual(backend, connection.ProcessID);
            }, context.CancellationToken);
}
