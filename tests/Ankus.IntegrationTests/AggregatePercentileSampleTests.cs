using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Compares the public ordered-set example with PostgreSQL's native discrete percentile semantics.
/// </summary>
/// <param name="context">The current test context.</param>
[TestClass]
public sealed class AggregatePercentileSampleTests(TestContext context)
{
    /// <summary>
    /// Ceiling rank, endpoints, direct nulls, empty groups and reverse ordering agree with PostgreSQL.
    /// </summary>
    /// <param name="fraction">The direct SQL fraction expression.</param>
    /// <param name="values">The typed aggregated input array.</param>
    /// <param name="order">The native ordering direction and null placement.</param>
    /// <param name="expected">The independently expected discrete percentile.</param>
    [TestMethod]
    [DataRow("0", "ARRAY[30,10,20]", "ASC", 10)]
    [DataRow("0.4", "ARRAY[30,10,20]", "ASC", 20)]
    [DataRow("0.5", "ARRAY[30,10,20]", "ASC", 20)]
    [DataRow("1", "ARRAY[30,10,20]", "ASC", 30)]
    [DataRow("0", "ARRAY[30,10,20]", "DESC", 30)]
    [DataRow("1", "ARRAY[30,10,20]", "DESC", 10)]
    [DataRow("0.4", "ARRAY[NULL,30,10,NULL,20]", "DESC NULLS FIRST", 20)]
    [DataRow("0.4", "ARRAY[NULL,30,10,NULL,20]", "ASC NULLS LAST", 20)]
    [DataRow("NULL::double precision", "ARRAY[30,10,20]", "ASC", null)]
    [DataRow("0.4", "ARRAY[]::integer[]", "ASC", null)]
    [DataRow("0.4", "ARRAY[NULL,NULL]::integer[]", "ASC", null)]
    public Task PercentileSampleMatchesPostgresBoundaries(string fraction, string values, string order, int? expected)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(PercentileSampleMatchesPostgresBoundaries), async (connection, transaction, token) =>
        {
            await using var command = new NpgsqlCommand("CREATE EXTENSION ankus_aggregates", connection, transaction);
            await command.ExecuteNonQueryAsync(token);
            command.CommandText = "SELECT integer_percentile(" + fraction + ") WITHIN GROUP (ORDER BY value " + order + "), " +
                "percentile_disc(" + fraction + ") WITHIN GROUP (ORDER BY value " + order + ") FROM unnest(" + values + ") AS value";
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
            Assert.IsTrue(await reader.ReadAsync(token));
            int? actual = reader.IsDBNull(0) ? null : reader.GetInt32(0);
            int? native = reader.IsDBNull(1) ? null : reader.GetInt32(1);
            Assert.AreEqual(expected, actual);
            Assert.AreEqual(native, actual);
            Assert.IsFalse(await reader.ReadAsync(token));
        }, context.CancellationToken);

    /// <summary>
    /// Invalid direct fractions fail even for empty groups, then the same connection can evaluate a valid percentile.
    /// </summary>
    /// <param name="fraction">The invalid fraction expression.</param>
    [TestMethod]
    [DataRow("-0.01")]
    [DataRow("1.01")]
    [DataRow("'NaN'::double precision")]
    [DataRow("'Infinity'::double precision")]
    public Task PercentileSampleRejectsInvalidFractionsAndRecovers(string fraction)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(PercentileSampleRejectsInvalidFractionsAndRecovers), async (connection, transaction, token) =>
        {
            await using var command = new NpgsqlCommand("CREATE EXTENSION ankus_aggregates; SAVEPOINT fraction_check", connection, transaction);
            await command.ExecuteNonQueryAsync(token);
            command.CommandText = "SELECT integer_percentile(" + fraction + ") WITHIN GROUP (ORDER BY value) " +
                "FROM unnest(ARRAY[]::integer[]) AS value";
            PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(token));
            Assert.AreEqual("22003", error.SqlState);
            Assert.AreEqual("Percentile fraction must be between zero and one.", error.MessageText);
            command.CommandText = "ROLLBACK TO SAVEPOINT fraction_check";
            await command.ExecuteNonQueryAsync(token);
            command.CommandText = "SELECT percentile_disc(" + fraction + ") WITHIN GROUP (ORDER BY value) " +
                "FROM unnest(ARRAY[]::integer[]) AS value";
            PostgresException native = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(token));
            Assert.AreEqual("22003", native.SqlState);
            command.CommandText = "ROLLBACK TO SAVEPOINT fraction_check";
            await command.ExecuteNonQueryAsync(token);
            command.CommandText = "SELECT integer_percentile(0.4) WITHIN GROUP (ORDER BY value) FROM unnest(ARRAY[30,10,20]) AS value";
            Assert.AreEqual(20, Assert.IsInstanceOfType<int>(await command.ExecuteScalarAsync(token)));
        }, context.CancellationToken);
}
