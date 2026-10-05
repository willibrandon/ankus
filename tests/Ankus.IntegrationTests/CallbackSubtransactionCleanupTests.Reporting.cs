using Ankus.Testing;
using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies secondary reporter failures during actual transaction cleanup.
/// </summary>
public sealed partial class CallbackSubtransactionCleanupTests
{
    /// <summary>
    /// A failing warning hook preserves rollback, the primary error and every remaining callback.
    /// </summary>
    /// <param name="abortKind">Zero for explicit rollback, one for failed transaction, or two for savepoint abort.</param>
    /// <param name="persistent">Whether the native hook also rejects warning retries.</param>
    [TestMethod]
    [DataRow(0, false)]
    [DataRow(1, false)]
    [DataRow(2, false)]
    [DataRow(0, true)]
    [DataRow(1, true)]
    [DataRow(2, true)]
    public Task CleanupReporterFailurePreservesAbortAndCallbackDrain(int abortKind, bool persistent)
        => AssertAbortReporterRecoveryAsync(abortKind, persistent, encodingFailure: false);

    /// <summary>
    /// Client encoding conversion failures cannot panic the cluster or leave partially drained callbacks.
    /// </summary>
    /// <param name="abortKind">Zero for explicit rollback, one for failed transaction, or two for savepoint abort.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public Task CleanupEncodingFailurePreservesAbortAndCallbackDrain(int abortKind)
        => AssertAbortReporterRecoveryAsync(abortKind, persistent: false, encodingFailure: true);

    /// <summary>
    /// Exercises warning failures across repeated real transaction boundaries on the same backend.
    /// </summary>
    /// <param name="abortKind">The PostgreSQL abort boundary.</param>
    /// <param name="persistent">Whether the native warning hook remains broken.</param>
    /// <param name="encodingFailure">Whether the diagnostic contains a character outside the client's encoding.</param>
    private async Task AssertAbortReporterRecoveryAsync(int abortKind, bool persistent, bool encodingFailure)
    {
        CancellationToken token = context.CancellationToken;
        PostgresTestClusterOptions options = await IntegrationEnvironment.CreateOptionsAsync(token);
        await using PostgresTestCluster cluster = await PostgresTestCluster.StartAsync(options, token);
        await using NpgsqlConnection connection = await cluster.OpenConnectionAsync(token);
        int backend = connection.ProcessID;
        var notices = new List<PostgresNotice>();
        connection.Notice += (_, args) => notices.Add(args.Notice);
        await using var command = new NpgsqlCommand("CREATE SCHEMA datatype; CREATE EXTENSION ankus_test WITH SCHEMA datatype; " +
            "CREATE SCHEMA tests; CREATE TABLE cleanup_reporter_writes(value integer); " +
            NativeRawCallFixtureCompiler.InstallationSql + "; SET log_min_messages=warning; SET log_error_verbosity=verbose", connection);
        await command.ExecuteNonQueryAsync(token);
        command.CommandText = Resources;
        long[] baseline = Assert.IsInstanceOfType<long[]>(await command.ExecuteScalarAsync(token));
        if (encodingFailure)
        {
            command.CommandText = "SET client_encoding=LATIN1";
            await command.ExecuteNonQueryAsync(token);
        }

        for (int iteration = 0; iteration < 3; iteration++)
        {
            int previousNotices = notices.Count;
            if (!encodingFailure && (!persistent || iteration == 0))
            {
                command.CommandText = "SELECT tests.log_arm('callback café', $1)";
                command.Parameters.AddWithValue(persistent ? 3 : 2);
                await command.ExecuteNonQueryAsync(token);
                command.Parameters.Clear();
            }

            await using (NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token))
            {
                command.Transaction = transaction;
                if (abortKind == 2)
                {
                    await transaction.SaveAsync("reporter_failure", token);
                }

                string prepare = encodingFailure ? "memory_callback_prepare_encoding" : "memory_callback_prepare";
                string arguments = abortKind == 2 ? "2" : "1";
                if (!encodingFailure)
                {
                    arguments += ", true";
                }

                command.CommandText = $"INSERT INTO cleanup_reporter_writes VALUES(73); SELECT datatype.{prepare}({arguments})";
                Assert.AreEqual(42, await command.ExecuteScalarAsync(token));
                if (abortKind != 0)
                {
                    command.CommandText = """
                        DO $primary$ BEGIN
                            RAISE EXCEPTION 'primary reporter failure' USING ERRCODE='23514',
                                DETAIL='primary detail', HINT='primary hint';
                        END $primary$
                        """;
                    PostgresException primary = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteNonQueryAsync(token));
                    Assert.AreEqual("ERROR", primary.InvariantSeverity);
                    Assert.AreEqual("23514", primary.SqlState);
                    Assert.AreEqual("primary reporter failure", primary.MessageText);
                    Assert.AreEqual("primary detail", primary.Detail);
                    Assert.AreEqual("primary hint", primary.Hint);
                }

                if (abortKind == 2)
                {
                    await transaction.RollbackAsync("reporter_failure", token);
                    await transaction.ReleaseAsync("reporter_failure", token);
                    await transaction.CommitAsync(token);
                }
                else
                {
                    await transaction.RollbackAsync(token);
                }

                command.Transaction = null;
            }

            PostgresNotice[] warnings = [.. notices.Skip(previousNotices)];
            if (persistent)
            {
                Assert.IsEmpty(warnings);
                string diagnostic = cluster.ReadServerLog();
                Assert.Contains("WARNING: cleanup reporting failed (22023): native report hook failure", diagnostic);
                Assert.Contains("DETAIL: persistent native hook detail", diagnostic);
                Assert.Contains("HINT: repair the persistent warning hook", diagnostic);
                Assert.Contains("QUERY: SELECT broken_warning_hook", diagnostic);
                Assert.Contains("POSITION: 17", diagnostic);
                Assert.Contains("INTERNAL_POSITION: 3", diagnostic);
            }
            else
            {
                PostgresNotice warning = Assert.ContainsSingle(warnings);
                Assert.AreEqual("WARNING", warning.InvariantSeverity);
                Assert.AreEqual(encodingFailure ? "22P05" : "22023", warning.SqlState);
                Assert.AreEqual(encodingFailure
                    ? "character with byte sequence 0xe2 0x82 0xac in encoding \"UTF8\" has no equivalent in encoding \"LATIN1\""
                    : "native report hook failure", warning.MessageText);
            }

            command.CommandText = "SELECT datatype.memory_callback_implicit_state()";
            Assert.AreEqual("B73,A73|False,False|False|stale", await command.ExecuteScalarAsync(token));
            command.CommandText = "SELECT count(*) FROM cleanup_reporter_writes";
            Assert.AreEqual(0L, await command.ExecuteScalarAsync(token));
            command.CommandText = "SELECT count(*) FROM ankus_test_memory.contexts WHERE ident = 'implicit memory callback'";
            Assert.AreEqual(0L, await command.ExecuteScalarAsync(token));
            command.CommandText = "SELECT tests.raw_call_holdoffs()";
            Assert.AreEqual(0L, await command.ExecuteScalarAsync(token));
            await AssertRecoveredAsync(command, baseline, backend, token);
        }

        string log = cluster.ReadServerLog();
        Assert.DoesNotContain("PANIC:", log);
        Assert.DoesNotContain("reinitializing", log);
        Assert.DoesNotContain("AbortTransaction while", log);
        Assert.DoesNotContain("AbortSubTransaction while", log);
        AssertNoInterruptedAbort(notices);
    }
}
