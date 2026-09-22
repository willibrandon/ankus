using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies the published native extension by calling it from real PostgreSQL backend processes.
/// </summary>
/// <param name="context">The per-test context.</param>
[TestClass]
public sealed class NativeAotExtensionTests(TestContext context)
{
    /// <summary>
    /// Initializes the shared local PostgreSQL fixture before any integration test runs.
    /// </summary>
    /// <param name="context">The assembly context.</param>
    [AssemblyInitialize]
    public static Task InitializeAssemblyAsync(TestContext context) => PostgresFixture.InitializeAsync(context);

    /// <summary>
    /// Stops the shared PostgreSQL cluster after all integration tests finish.
    /// </summary>
    [AssemblyCleanup]
    public static Task CleanupAssemblyAsync() => PostgresFixture.CleanupAsync();

    /// <summary>
    /// Verifies signed datums and generated argument conversion through the sample's native entry point.
    /// </summary>
    /// <param name="left">The first operand.</param>
    /// <param name="right">The second operand.</param>
    /// <param name="expected">The expected SQL integer result.</param>
    [TestMethod]
    [DataRow(40, 2, 42)]
    [DataRow(-20, 5, -15)]
    [DataRow(0, 0, 0)]
    public Task AddReturnsExpectedInteger(int left, int right, int expected)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(AddReturnsExpectedInteger), async (connection, transaction, token) =>
        {
            await using var command = new NpgsqlCommand("SELECT public.add($1, $2)", connection, transaction);
            command.Parameters.AddWithValue(left);
            command.Parameters.AddWithValue(right);

            object? result = await command.ExecuteScalarAsync(token);

            Assert.AreEqual(expected, Assert.IsInstanceOfType<int>(result));
        }, context.CancellationToken);

    /// <summary>
    /// Verifies PostgreSQL's STRICT contract for either or both null operands.
    /// </summary>
    /// <param name="left">The nullable first operand.</param>
    /// <param name="right">The nullable second operand.</param>
    [TestMethod]
    [DataRow(null, 1)]
    [DataRow(1, null)]
    [DataRow(null, null)]
    public Task StrictFunctionReturnsSqlNull(int? left, int? right)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(StrictFunctionReturnsSqlNull), async (connection, transaction, token) =>
        {
            await using var command = new NpgsqlCommand("SELECT public.add($1, $2)", connection, transaction);
            command.Parameters.Add(new NpgsqlParameter<int?> { TypedValue = left });
            command.Parameters.Add(new NpgsqlParameter<int?> { TypedValue = right });

            object? result = await command.ExecuteScalarAsync(token);

            Assert.AreSame(DBNull.Value, result);
        }, context.CancellationToken);

    /// <summary>
    /// Verifies managed exceptions become SQL errors and the same backend remains usable after transaction rollback.
    /// </summary>
    /// <param name="left">The first operand.</param>
    /// <param name="right">The overflowing second operand.</param>
    [TestMethod]
    [DataRow(int.MaxValue, 1)]
    [DataRow(int.MinValue, -1)]
    public async Task ManagedExceptionBecomesSqlErrorWithoutLosingBackend(int left, int right)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        int backend = connection.ProcessID;
        await using (NpgsqlTransaction transaction = await connection.BeginTransactionAsync(context.CancellationToken))
        {
            await using var command = new NpgsqlCommand("SELECT public.add($1, $2)", connection, transaction);
            command.Parameters.AddWithValue(left);
            command.Parameters.AddWithValue(right);
            PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(
                () => command.ExecuteScalarAsync(context.CancellationToken));

            Assert.AreEqual(PostgresErrorCodes.ExternalRoutineException, error.SqlState);
            Assert.Contains("overflow", error.MessageText);
        }

        await using var succeeding = new NpgsqlCommand("SELECT public.add(40, 2)", connection);
        Assert.AreEqual(42, Assert.IsInstanceOfType<int>(await succeeding.ExecuteScalarAsync(context.CancellationToken)));
        Assert.AreEqual(backend, connection.ProcessID);
    }
}
