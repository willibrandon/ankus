using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies parameter token ownership, quoting and same-session recovery in the published extension.
/// </summary>
/// <param name="context">The current cancellation context.</param>
[TestClass]
public sealed class SpiCommandBoundaryTests(TestContext context)
{
    /// <summary>
    /// Quoted dollar text stays literal while a hostile interpolation is independently bound.
    /// </summary>
    /// <param name="setting">The ordinary-string escape setting.</param>
    [TestMethod]
    [DataRow("on")]
    [DataRow("off")]
    public Task DollarTokenContextsPreserveValuesAndMetadata(string setting)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(DollarTokenContextsPreserveValuesAndMetadata),
            async (connection, transaction, token) =>
            {
                int backend = connection.ProcessID;
                await using var configure = new NpgsqlCommand($"SET LOCAL standard_conforming_strings = {setting}", connection, transaction);
                if (setting == "off" && PostgresFixture.Cluster.Installation.Version.Major >= 19)
                {
                    await transaction.SaveAsync("string_mode", token);
                    PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => configure.ExecuteNonQueryAsync(token));
                    Assert.AreEqual(PostgresErrorCodes.FeatureNotSupported, error.SqlState);
                    await transaction.RollbackAsync("string_mode", token);
                    await transaction.ReleaseAsync("string_mode", token);
                }
                else
                {
                    await configure.ExecuteNonQueryAsync(token);
                }

                const string value = "雪'); SELECT 99; -- $10 \\";
                await using var query = new NpgsqlCommand("SELECT datatype.command_dollar_contexts($1)", connection, transaction);
                query.Parameters.AddWithValue(value);
                Assert.AreEqual(value + "|$1|42|$2", await query.ExecuteScalarAsync(token));
                query.Parameters.Clear();
                query.CommandText = "SELECT datatype.command_two_digit_bindings()";
                Assert.AreEqual(78, await query.ExecuteScalarAsync(token));
                Assert.AreEqual(backend, connection.ProcessID);
            }, context.CancellationToken);

    /// <summary>
    /// Literal parameter collisions and hidden bindings fail explicitly; malformed adjacency remains a native syntax error.
    /// </summary>
    /// <param name="mode">The collision or hidden-token partition.</param>
    /// <param name="error">The independently expected error category.</param>
    [TestMethod]
    [DataRow(0, "42601")]
    [DataRow(1, "ArgumentException")]
    [DataRow(2, "ArgumentException")]
    [DataRow(3, "ArgumentException")]
    [DataRow(4, "ArgumentException")]
    [DataRow(5, "ArgumentException")]
    public Task InvalidBindingContextsRecoverTheSameCallback(int mode, string error)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(InvalidBindingContextsRecoverTheSameCallback),
            async (connection, transaction, token) =>
            {
                int backend = connection.ProcessID;
                await using var query = new NpgsqlCommand("SELECT datatype.command_token_recovery($1)", connection, transaction);
                query.Parameters.AddWithValue(mode);
                Assert.AreEqual(error + "|42", await query.ExecuteScalarAsync(token));
                query.Parameters.Clear();
                query.CommandText = "SELECT 43";
                Assert.AreEqual(43, await query.ExecuteScalarAsync(token));
                Assert.AreEqual(backend, connection.ProcessID);
            }, context.CancellationToken);

    /// <summary>
    /// The documented quoting pattern preserves hostile identifier and literal contents as data.
    /// </summary>
    [TestMethod]
    public Task QuotedFragmentsPreserveColumnIdentityAndValue()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(QuotedFragmentsPreserveColumnIdentityAndValue),
            async (connection, transaction, token) =>
            {
                const string identifier = "odd\"name; SELECT 99; --";
                const string value = "café'\\; DROP TABLE important; -- $1";
                await using var query = new NpgsqlCommand("SELECT datatype.command_quoted_fragments($1,$2)", connection, transaction);
                query.Parameters.AddWithValue(identifier);
                query.Parameters.AddWithValue(value);
                Assert.AreEqual(identifier + "|" + value, await query.ExecuteScalarAsync(token));
            }, context.CancellationToken);
}
