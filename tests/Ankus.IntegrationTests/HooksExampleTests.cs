using Ankus.Testing;
using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Executes the ported pgrx hooks sample through its published Native AOT extension.
/// </summary>
/// <param name="context">The current cancellation and diagnostic context.</param>
[TestClass]
public sealed class HooksExampleTests(TestContext context)
{
    /// <summary>
    /// pgrx's structured-diagnostics probe: a unique violation raised beneath the executor hook keeps every field.
    /// </summary>
    private const string DiagnosticProbe = """
        CREATE TEMP TABLE diag_probe (
            k text CONSTRAINT diag_probe_k_unique UNIQUE
        );
        INSERT INTO diag_probe VALUES ('x');

        DO $$
        DECLARE
            violated_constraint text;
            violated_table text;
            violated_detail text;
        BEGIN
            BEGIN
                INSERT INTO diag_probe VALUES ('x');
            EXCEPTION WHEN unique_violation THEN
                GET STACKED DIAGNOSTICS
                    violated_constraint = CONSTRAINT_NAME,
                    violated_table = TABLE_NAME,
                    violated_detail = PG_EXCEPTION_DETAIL;
                RAISE EXCEPTION 'DIAG_PROBE: constraint_name=[%] table_name=[%] detail=[%] sqlerrm=[%]',
                    coalesce(violated_constraint, '<NULL>'),
                    coalesce(violated_table, '<NULL>'),
                    coalesce(violated_detail, '<NULL>'),
                    SQLERRM;
            END;
        END $$;

        DROP TABLE diag_probe;
        """;

    /// <summary>
    /// The exact message raised by pgrx's diagnostics probe when every structured field survives the hooks.
    /// </summary>
    private const string DiagnosticMessage = "DIAG_PROBE: constraint_name=[diag_probe_k_unique] table_name=[diag_probe] " +
        "detail=[Key (k)=(x) already exists.] sqlerrm=[duplicate key value violates unique constraint \"diag_probe_k_unique\"]";

    /// <summary>
    /// Session setup, a rejected top-level statement and its message.
    /// </summary>
    private static readonly (string Setup, string Statement, string Message)[] s_topLevel =
    [
        ("CREATE TEMP TABLE t AS SELECT 1 AS one", "DELETE FROM t", "DELETE queries must have a WHERE clause"),
        ("CREATE TEMP TABLE t AS SELECT 1 AS one; CREATE ROLE ankus_hooks_frank; SET ROLE ankus_hooks_frank", "TRUNCATE t",
            "Only superusers can truncate"),
    ];

    /// <summary>
    /// Mirrors each pgrx hook test after loading the library into one backend, which other backends do not observe.
    /// </summary>
    [TestMethod]
    public Task HooksSampleRejectsUnsafeStatementsAfterLoad()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(HooksSampleRejectsUnsafeStatementsAfterLoad),
            async (connection, transaction, token) =>
            {
                await ExecuteAsync(connection, transaction, "CREATE EXTENSION ankus_hooks; LOAD 'Ankus.Examples.Hooks'", token);
                await ErrorTrap.InstallAsync(connection, transaction, token);
                await AssertSafetyCatchAsync(connection, transaction, "ankus_hooks_bob", token);

                // The library is loaded again without installing a second, self-chained hook.
                await ExecuteAsync(connection, transaction, "LOAD 'Ankus.Examples.Hooks'", token);
                await AssertSafetyCatchAsync(connection, transaction, "ankus_hooks_carol", token);

                // Hooks belong to the backend that loaded the library.
                await using NpgsqlConnection other = await PostgresFixture.Cluster.OpenConnectionAsync(token);
                await using var unhooked = new NpgsqlCommand("CREATE TEMP TABLE u AS SELECT 1 AS one; DELETE FROM u; SELECT count(*) FROM u", other);
                Assert.AreEqual(0L, await unhooked.ExecuteScalarAsync(token));
            }, context.CancellationToken);

    /// <summary>
    /// Shared preload installs the hooks in every backend before its first statement, without LOAD or CREATE EXTENSION.
    /// </summary>
    [TestMethod]
    public async Task HooksSampleProtectsEveryPreloadedBackend()
    {
        CancellationToken token = context.CancellationToken;
        PostgresTestClusterOptions defaults = await IntegrationEnvironment.CreateOptionsAsync(token);
        PostgresTestClusterOptions options = new()
        {
            Installation = defaults.Installation,
            DataDirectoryBase = defaults.DataDirectoryBase,
            LogDirectory = defaults.LogDirectory,
            PostgreSqlConfiguration = [.. defaults.PostgreSqlConfiguration, "shared_preload_libraries = 'Ankus.Examples.Hooks'"],
        };
        await using PostgresTestCluster cluster = await PostgresTestCluster.StartAsync(options, token);
        HashSet<int> backends = [];
        for (int index = 0; index < 2; index++)
        {
            await using NpgsqlConnection connection = await cluster.OpenConnectionAsync(token);
            Assert.IsTrue(backends.Add(connection.ProcessID));
            await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token);
            await ErrorTrap.InstallAsync(connection, transaction, token);
            await AssertSafetyCatchAsync(connection, transaction, "ankus_hooks_dave", token);
            await ExecuteAsync(connection, transaction, "LOAD 'Ankus.Examples.Hooks'; CREATE EXTENSION ankus_hooks", token);
            await AssertSafetyCatchAsync(connection, transaction, "ankus_hooks_erin", token);
        }

        // Top-level client statements are rejected as well. Npgsql closes a connection after an XX000 error.
        foreach ((string setup, string statement, string message) in s_topLevel)
        {
            await using NpgsqlConnection connection = await cluster.OpenConnectionAsync(token);
            await using var command = new NpgsqlCommand(setup, connection);
            await command.ExecuteNonQueryAsync(token);
            command.CommandText = statement;
            PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteNonQueryAsync(token));
            Assert.AreEqual("XX000", error.SqlState);
            Assert.AreEqual(message, error.MessageText);
        }
    }

    /// <summary>
    /// Runs pgrx's DELETE, TRUNCATE and diagnostics tests and checks recovery in the same backend.
    /// </summary>
    private static async Task AssertSafetyCatchAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string role,
        CancellationToken token)
    {
        // test_delete_with_where
        await ExecuteAsync(connection, transaction, "CREATE TEMP TABLE t AS SELECT 1 AS one; DELETE FROM t WHERE 0 = 0", token);
        Assert.AreEqual(0L, await ScalarAsync<long>(connection, transaction, "SELECT count(*) FROM t", token));

        // test_delete_without_where. The trap runs each statement through PL/pgSQL's SPI in the same backend.
        await ExecuteAsync(connection, transaction, "INSERT INTO t VALUES (1)", token);
        const string DeleteRejected = "XX000: DELETE queries must have a WHERE clause";
        Assert.AreEqual(DeleteRejected, await ErrorTrap.RunAsync(connection, transaction, "DELETE FROM t", token));
        Assert.AreEqual(DeleteRejected, await ErrorTrap.RunAsync(connection, transaction, "DELETE FROM t AS alias", token));
        Assert.AreEqual("completed", await ErrorTrap.RunAsync(connection, transaction, "DELETE FROM t WHERE one = 2", token));
        Assert.AreEqual(1L, await ScalarAsync<long>(connection, transaction, "SELECT count(*) FROM t", token));

        // test_truncate_from_superuser
        await ExecuteAsync(connection, transaction, "TRUNCATE t", token);
        Assert.AreEqual(0L, await ScalarAsync<long>(connection, transaction, "SELECT count(*) FROM t", token));

        // test_truncate_from_bob: the hook rejects TRUNCATE before PostgreSQL checks table privileges.
        await ExecuteAsync(connection, transaction, $"INSERT INTO t VALUES (1); CREATE ROLE {role}; SET ROLE {role}", token);
        Assert.AreEqual("XX000: Only superusers can truncate", await ErrorTrap.RunAsync(connection, transaction, "TRUNCATE t", token));
        await ExecuteAsync(connection, transaction, "RESET ROLE", token);
        Assert.AreEqual(1L, await ScalarAsync<long>(connection, transaction, "SELECT count(*) FROM t", token));

        // test_guarded_error_preserves_structured_diagnostics
        await AssertFailureAsync(connection, transaction, DiagnosticProbe, "P0001", DiagnosticMessage, token);

        await ExecuteAsync(connection, transaction, "DROP TABLE t", token);
        Assert.AreEqual(connection.ProcessID, await ScalarAsync<int>(connection, transaction, "SELECT pg_backend_pid()", token));
    }

    private static async Task AssertFailureAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        string sql, string sqlState, string message, CancellationToken token)
    {
        await transaction.SaveAsync("hooks_sample_failure", token);
        try
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteNonQueryAsync(token));
            Assert.AreEqual(sqlState, error.SqlState);
            Assert.AreEqual(message, error.MessageText);
        }
        finally
        {
            await transaction.RollbackAsync("hooks_sample_failure", CancellationToken.None);
        }
    }

    private static async Task<T> ScalarAsync<T>(NpgsqlConnection connection, NpgsqlTransaction transaction,
        string sql, CancellationToken token)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        return Assert.IsInstanceOfType<T>(await command.ExecuteScalarAsync(token));
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        string sql, CancellationToken token)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync(token);
    }
}
