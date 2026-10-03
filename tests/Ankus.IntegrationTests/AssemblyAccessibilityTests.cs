using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Executes generated assembly-visible methods, partial properties, hooks and nested codecs in PostgreSQL.
/// </summary>
/// <param name="context">The current test cancellation context.</param>
[TestClass]
public sealed class AssemblyAccessibilityTests(TestContext context)
{
    /// <summary>
    /// Native dispatch invokes the generated partial property and the protected-internal show hook.
    /// </summary>
    [TestMethod]
    public Task AssemblyAccessibleSettingsAndFunctionsExecute()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(AssemblyAccessibleSettingsAndFunctionsExecute),
            async (connection, transaction, token) =>
            {
                await using var command = new NpgsqlCommand("""
                    SET LOCAL "ankus_access.limit" = 11;
                    SELECT assembly_access.assembly_access_read(), current_setting('ankus_access.limit'),
                           assembly_access.assembly_access_increment('{"Count":31}')::text
                    """, connection, transaction);
                await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
                Assert.IsTrue(await reader.ReadAsync(token));
                Assert.AreEqual(11, reader.GetInt32(0));
                Assert.AreEqual("limit=11", reader.GetString(1));
                Assert.AreEqual("{\"Count\":42}", reader.GetString(2));
                Assert.IsFalse(await reader.ReadAsync(token));
            }, context.CancellationToken);

    /// <summary>
    /// Nested custom codecs retain integer boundaries, nullable text and SQL NULL after physical table storage.
    /// </summary>
    [TestMethod]
    public Task AssemblyAccessibleNestedValuesSurviveStorage()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(AssemblyAccessibleNestedValuesSurviveStorage),
            async (connection, transaction, token) =>
            {
                await using var command = new NpgsqlCommand("""
                    CREATE TEMP TABLE access_values (position int, value assembly_access.payload);
                    INSERT INTO access_values VALUES
                        (1, '{"Number":-2147483648,"Label":null}'),
                        (2, '{"Number":0,"Label":""}'),
                        (3, '{"Number":2147483647,"Label":"café"}'),
                        (4, NULL);
                    SELECT assembly_access.assembly_access_payload(value)::text
                    FROM access_values ORDER BY position
                    """, connection, transaction);
                await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
                string[] expectedValues =
                [
                    "{\"Number\":-2147483648,\"Label\":null}",
                    "{\"Number\":0,\"Label\":\"\"}",
                    "{\"Number\":2147483647,\"Label\":\"caf\\u00E9\"}",
                ];
                foreach (string expected in expectedValues)
                {
                    Assert.IsTrue(await reader.ReadAsync(token));
                    Assert.AreEqual(expected, reader.GetString(0));
                }

                Assert.IsTrue(await reader.ReadAsync(token));
                Assert.IsTrue(reader.IsDBNull(0));
                Assert.IsFalse(await reader.ReadAsync(token));
            }, context.CancellationToken);
}
