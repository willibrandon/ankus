using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Runs property tests inside PostgreSQL with <see cref="PgPropertyRunner"/>, porting pgrx's <c>proptests</c>.
/// </summary>
/// <param name="context">The per-test context.</param>
[TestClass]
public sealed class PropertyRunnerTests(TestContext context)
{
    /// <summary>
    /// Each of pgrx's temporal round-trip properties holds for 256 generated raw values, through SPI parameters and
    /// through the type's own text output read back as a literal.
    /// </summary>
    /// <param name="name">The pgrx test name.</param>
    [TestMethod]
    [DataRow("date_spi_roundtrip")]
    [DataRow("date_literal_spi_roundtrip")]
    [DataRow("time_spi_roundtrip")]
    [DataRow("time_literal_spi_roundtrip")]
    [DataRow("timestamp_spi_roundtrip")]
    [DataRow("timestamp_literal_spi_roundtrip")]
    [DataRow("timetz_spi_roundtrip")]
    [DataRow("timetz_literal_spi_roundtrip")]
    public Task TemporalValuesRoundTripForGeneratedInputs(string name)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(TemporalValuesRoundTripForGeneratedInputs),
            async (connection, transaction, token) =>
            {
                await using var command = new NpgsqlCommand("SELECT datatype.temporal_round_trip_property($1, $2)", connection, transaction);
                command.Parameters.AddWithValue(name);
                command.Parameters.AddWithValue(20261009L);
                Assert.AreEqual(256, await command.ExecuteScalarAsync(token));
            }, context.CancellationToken);

    /// <summary>
    /// A backend error fails its input, rolls back that input's writes and shrinks to the smallest failing input;
    /// passing inputs keep their writes, and the session continues.
    /// </summary>
    [TestMethod]
    public Task BackendErrorsAreFailingInputs()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(BackendErrorsAreFailingInputs),
            async (connection, transaction, token) =>
            {
                await using (var command = new NpgsqlCommand("SELECT datatype.property_backend_error_shrinks($1)", connection, transaction))
                {
                    command.Parameters.AddWithValue(7L);
                    Assert.AreEqual("0|22012|division by zero|0|True", await command.ExecuteScalarAsync(token));
                }

                await using var after = new NpgsqlCommand("SELECT 1", connection, transaction);
                Assert.AreEqual(1, await after.ExecuteScalarAsync(token));
            }, context.CancellationToken);
}
