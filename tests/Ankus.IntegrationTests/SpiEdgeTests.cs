using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies SPI boundary cases from pgrx's SPI tests inside a real backend, each followed by SPI in the same function.
/// </summary>
/// <param name="context">The current test context.</param>
[TestClass]
public sealed class SpiEdgeTests(TestContext context)
{
    /// <summary>
    /// Each case reports its exact result or failure, and the backend keeps running SPI afterward.
    /// </summary>
    /// <param name="scenario">The boundary case.</param>
    /// <param name="expected">The exact outcome, followed by the follow-up query's value.</param>
    [TestMethod]
    [DataRow(0, "ArgumentException::The statement requires 1 parameters, but received 0. (Parameter 'parameters')|42")]
    [DataRow(1, "ArgumentException::The statement requires 1 parameters, but received 2. (Parameter 'parameters')|42")]
    [DataRow(2, "PgException:42601:syntax error at or near \"THIS\"|42")]
    [DataRow(3, "columns=0;rows=0|42")]
    [DataRow(4, "IndexOutOfRangeException::Index was outside the bounds of the array.|42")]
    [DataRow(5, "unquoted.\"actually-quoted\" \"actually-quoted\".unquoted|42")]
    [DataRow(6, "hello;True|42")]
    [DataRow(7, "InvalidOperationException::The query returned no rows; read a nullable type to treat an empty result as SQL NULL.|42")]
    public Task SpiBoundaryCasesReportExactOutcomes(int scenario, string expected)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(SpiBoundaryCasesReportExactOutcomes), async (connection, transaction, token) =>
        {
            await using var command = new NpgsqlCommand("""
                CREATE DOMAIN pg_temp.inner_domain AS text;
                CREATE DOMAIN pg_temp.outer_domain AS pg_temp.inner_domain;
                """, connection, transaction);
            await command.ExecuteNonQueryAsync(token);
            command.CommandText = "SELECT spi_edges.spi_edge($1)";
            command.Parameters.AddWithValue(scenario);
            Assert.AreEqual(expected, await command.ExecuteScalarAsync(token));
        }, context.CancellationToken);
}
