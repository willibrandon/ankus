using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Exercises Native AOT login callbacks in disposable databases while preserving an administrative connection.
/// </summary>
/// <param name="context">The current cancellation and diagnostic context.</param>
[TestClass]
[DoNotParallelize]
public sealed class EventTriggerLoginTests(TestContext context)
{
    /// <summary>
    /// Every physical connection invokes the managed login callback and commits its effects before client queries.
    /// </summary>
    [TestMethod]
    public Task LoginCallbacksCommitExactMetadataForEachPhysicalConnection()
        => RunIsolated(async (keeper, builder, token) =>
        {
            await Execute(keeper, "CREATE EVENT TRIGGER managed_login ON login EXECUTE FUNCTION event_values.event_action()", token);
            await using var first = new NpgsqlConnection(builder.ConnectionString);
            await using var second = new NpgsqlConnection(builder.ConnectionString);
            await first.OpenAsync(token);
            await second.OpenAsync(token);
            Assert.AreNotEqual(first.ProcessID, second.ProcessID);
            string[] expected = ["login:Login:LOGIN:1", "login:Login:LOGIN:1"];
            Assert.AreSequenceEqual(expected, await Scalar<string[]>(keeper,
                "SELECT array_agg(event||':'||kind||':'||tag||':'||depth ORDER BY position) FROM event_values.audit WHERE phase='callback'", token));
            Assert.AreEqual(2L, await Scalar<long>(keeper, "SELECT count(*) FROM event_values.audit", token));
            Assert.AreEqual(42, await Scalar<int>(first, "SELECT 42", token));
            Assert.AreEqual(43, await Scalar<int>(second, "SELECT 43", token));
        });

    /// <summary>
    /// Startup failures roll back login writes, leave the keeper usable, and allow recovery by disabling the trigger.
    /// </summary>
    /// <param name="mode">The structured or ordinary managed failure mode.</param>
    /// <param name="sqlState">The error code transported during connection startup.</param>
    /// <param name="message">The expected primary error text.</param>
    [TestMethod]
    [DataRow("login_error", "P7701", "event rejected command")]
    [DataRow("managed_error", "38000", "managed event failure")]
    public Task LoginFailuresRollBackAndAllowAdministrativeRecovery(string mode, string sqlState, string message)
        => RunIsolated(async (keeper, builder, token) =>
        {
            await Execute(keeper, $"""
                CREATE EVENT TRIGGER managed_login ON login EXECUTE FUNCTION event_values.event_action();
                ALTER DATABASE {builder.Database} SET ankus.event_mode='{mode}';
                """, token);
            await using (var failed = new NpgsqlConnection(builder.ConnectionString))
            {
                PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => failed.OpenAsync(token));
                Assert.AreEqual(sqlState, error.SqlState);
                Assert.AreEqual(message, error.MessageText);
                if (mode == "login_error")
                {
                    Assert.AreEqual("owned event detail", error.Detail);
                    Assert.AreEqual("retry valid DDL", error.Hint);
                }
            }

            Assert.AreEqual(0L, await Scalar<long>(keeper, "SELECT count(*) FROM event_values.audit", token));
            Assert.AreEqual(42, await Scalar<int>(keeper, "SELECT 42", token));
            await Execute(keeper, "ALTER EVENT TRIGGER managed_login DISABLE", token);
            await using (var bypassed = new NpgsqlConnection(builder.ConnectionString))
            {
                await bypassed.OpenAsync(token);
                Assert.AreEqual(43, await Scalar<int>(bypassed, "SELECT 43", token));
            }

            Assert.AreEqual(0L, await Scalar<long>(keeper, "SELECT count(*) FROM event_values.audit", token));
            await Execute(keeper, $"""
                ALTER DATABASE {builder.Database} RESET ankus.event_mode;
                ALTER EVENT TRIGGER managed_login ENABLE;
                """, token);
            await using var recovered = new NpgsqlConnection(builder.ConnectionString);
            await recovered.OpenAsync(token);
            Assert.AreEqual("login:Login:LOGIN", await Scalar<string>(keeper,
                "SELECT event||':'||kind||':'||tag FROM event_values.audit", token));
            Assert.AreEqual(1L, await Scalar<long>(keeper, "SELECT count(*) FROM event_values.audit", token));
        });

    /// <summary>
    /// PostgreSQL rejects login tag filters and honors disabled triggers and its connection-start event-trigger switch.
    /// </summary>
    [TestMethod]
    public Task LoginFiltersAndConnectionBypassFollowPostgresRules()
        => RunIsolated(async (keeper, builder, token) =>
        {
            PostgresException filter = await Assert.ThrowsExactlyAsync<PostgresException>(() => Execute(keeper,
                "CREATE EVENT TRIGGER invalid_login ON login WHEN TAG IN ('CREATE TABLE') EXECUTE FUNCTION event_values.event_action()", token));
            Assert.AreEqual("0A000", filter.SqlState);
            Assert.AreEqual(0L, await Scalar<long>(keeper, "SELECT count(*) FROM pg_event_trigger WHERE evtname='invalid_login'", token));
            await Execute(keeper, """
                CREATE EVENT TRIGGER managed_login ON login EXECUTE FUNCTION event_values.event_action();
                ALTER EVENT TRIGGER managed_login DISABLE;
                """, token);
            await using (var disabled = new NpgsqlConnection(builder.ConnectionString))
            {
                await disabled.OpenAsync(token);
                Assert.AreEqual(42, await Scalar<int>(disabled, "SELECT 42", token));
            }

            Assert.AreEqual(0L, await Scalar<long>(keeper, "SELECT count(*) FROM event_values.audit", token));
            await Execute(keeper, "ALTER EVENT TRIGGER managed_login ENABLE", token);
            builder.Options = "-c event_triggers=false";
            await using (var bypassed = new NpgsqlConnection(builder.ConnectionString))
            {
                await bypassed.OpenAsync(token);
                Assert.AreEqual("off", await Scalar<string>(bypassed, "SHOW event_triggers", token));
            }

            Assert.AreEqual(0L, await Scalar<long>(keeper, "SELECT count(*) FROM event_values.audit", token));
            builder.Options = string.Empty;
            await using var enabled = new NpgsqlConnection(builder.ConnectionString);
            await enabled.OpenAsync(token);
            Assert.AreEqual(1L, await Scalar<long>(keeper, "SELECT count(*) FROM event_values.audit", token));
            Assert.AreEqual("LOGIN", await Scalar<string>(keeper, "SELECT tag FROM event_values.audit", token));
        });

    private async Task RunIsolated(Func<NpgsqlConnection, NpgsqlConnectionStringBuilder, CancellationToken, Task> action)
    {
        CancellationToken token = context.CancellationToken;
        string database = "event_login_" + Guid.NewGuid().ToString("N");
        await using NpgsqlConnection administrator = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        await Execute(administrator, $"CREATE DATABASE {database}", token);
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(administrator.ConnectionString)
            {
                Database = database,
                Pooling = false,
                IncludeErrorDetail = true,
            };
            await using var keeper = new NpgsqlConnection(builder.ConnectionString);
            await keeper.OpenAsync(token);
            await Execute(keeper, "CREATE EXTENSION ankus_test", token);
            await action(keeper, builder, token);
        }
        finally
        {
            await Execute(administrator, $"DROP DATABASE {database} WITH (FORCE)", CancellationToken.None);
        }
    }

    private static async Task Execute(NpgsqlConnection connection, string sql, CancellationToken token)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(token);
    }

    private static async Task<T> Scalar<T>(NpgsqlConnection connection, string sql, CancellationToken token)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return Assert.IsInstanceOfType<T>(await command.ExecuteScalarAsync(token));
    }
}
