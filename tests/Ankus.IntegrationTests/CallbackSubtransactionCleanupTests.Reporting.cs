using Ankus.Testing;
using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies secondary reporter failures during actual transaction cleanup.
/// </summary>
public sealed partial class CallbackSubtransactionCleanupTests
{
    /// <summary>
    /// A failing warning hook is bypassed while rollback, the primary error and every remaining callback survive.
    /// </summary>
    /// <param name="abortKind">Zero for explicit rollback, one for failed transaction, or two for savepoint abort.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public Task CleanupEmergencyLogBypassesHookAndPreservesAbortAndCallbackDrain(int abortKind)
        => AssertAbortReporterRecoveryAsync(abortKind, encodingFailure: false);

    /// <summary>
    /// Client encoding conversion failures cannot panic the cluster or leave partially drained callbacks.
    /// </summary>
    /// <param name="abortKind">Zero for explicit rollback, one for failed transaction, or two for savepoint abort.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public Task CleanupEncodingFailurePreservesAbortAndCallbackDrain(int abortKind)
        => AssertAbortReporterRecoveryAsync(abortKind, encodingFailure: true);

    /// <summary>
    /// Exercises emergency cleanup reporting across repeated real transaction boundaries on the same backend.
    /// </summary>
    /// <param name="abortKind">The PostgreSQL abort boundary.</param>
    /// <param name="encodingFailure">Whether the diagnostic contains a character outside the client's encoding.</param>
    private async Task AssertAbortReporterRecoveryAsync(int abortKind, bool encodingFailure)
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
            if (!encodingFailure)
            {
                command.CommandText = "SELECT tests.log_prefix_arm('callback café')";
                await command.ExecuteNonQueryAsync(token);
            }

            int previousNotices = notices.Count;
            int logStart = cluster.ReadServerLog().Length;
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

            Assert.HasCount(previousNotices, notices);
            string diagnostic = CompletionLog(cluster, logStart, backend);
            Assert.Contains(encodingFailure
                ? "WARNING: 22023: callback \\xE2\\x82\\xAC"
                : "WARNING: 22023: callback caf\\xC3\\xA9", diagnostic);
            Assert.Contains(encodingFailure ? "DETAIL: secondary detail" : "DETAIL: detail na\\xC3\\xAFve", diagnostic);
            Assert.Contains(encodingFailure ? "HINT: secondary hint" : "HINT: hint d\\xC3\\xA9j\\xC3\\xA0", diagnostic);
            if (encodingFailure)
            {
                Assert.Contains("CONTEXT: cleanup context", diagnostic);
                Assert.Contains("SCHEMA: sch\\xC3\\xA9ma", diagnostic);
                Assert.Contains("TABLE: callback_table", diagnostic);
                Assert.Contains("COLUMN: callback_column", diagnostic);
                Assert.Contains("DATATYPE: callback_type", diagnostic);
                Assert.Contains("CONSTRAINT: callback_constraint", diagnostic);
                Assert.Contains("QUERY: SELECT callback", diagnostic);
                Assert.Contains("FILE: MemoryCallbackFunctions.cs", diagnostic);
                Assert.Contains("ROUTINE: MemoryCallbackPrepareEncoding", diagnostic);
                Assert.Contains("DETAIL_LOG: server detail \\xE2\\x82\\xAC", diagnostic);
                Assert.Contains("BACKTRACE: frame one\\nframe two", diagnostic);
                Assert.Contains("POSITION: 17", diagnostic);
                Assert.Contains("INTERNAL_POSITION: 3", diagnostic);
                Assert.Contains("LINE: 911", diagnostic);
            }

            if (!encodingFailure)
            {
                command.CommandText = "SELECT tests.log_prefix_calls()";
                Assert.AreEqual(0L, await command.ExecuteScalarAsync(token));
                command.CommandText = "SELECT tests.log_prefix_active()";
                Assert.IsTrue(Assert.IsInstanceOfType<bool>(await command.ExecuteScalarAsync(token)));
                command.CommandText = "SELECT tests.log_prefix_restore()";
                Assert.IsTrue(Assert.IsInstanceOfType<bool>(await command.ExecuteScalarAsync(token)));
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
