using System.Globalization;
using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies the real backend contracts of borrowed Slab, Generation, and headerless Bump allocators.
/// </summary>
/// <param name="context">The per-test cancellation and diagnostic context.</param>
[TestClass]
public sealed class AllocatorContextTests(TestContext context)
{
    private const string Pending = "True|731|731|731|True,True|";
    private const string Reset = "True|stale|stale|stale|False,False|B731:False,A731:False";
    private const string Deleted = "False|stale|stale|stale|False,False|B731:False,A731:False";

    /// <summary>
    /// Native header configuration agrees with the actual running server's assertion configuration.
    /// </summary>
    [TestMethod]
    public Task FixtureHeadersMatchRunningServerAssertions()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(FixtureHeadersMatchRunningServerAssertions), async (connection, transaction, token) =>
        {
            await using var command = new NpgsqlCommand("SELECT tests.allocator_flags()", connection, transaction);
            int flags = Assert.IsInstanceOfType<int>(await command.ExecuteScalarAsync(token));
            command.CommandText = "SHOW debug_assertions";
            string assertions = Assert.IsInstanceOfType<string>(await command.ExecuteScalarAsync(token));
            Assert.AreEqual(assertions == "on", (flags & 1) != 0);
            context.WriteLine($"PostgreSQL {PostgresFixture.Cluster.Installation.Version}; native fixture flags={flags}; debug_assertions={assertions}; MEMORY_CONTEXT_CHECKING={(flags & 2) != 0}.");
        }, context.CancellationToken);

    /// <summary>
    /// Exact byte contents, zeroing, alignment, registry owners, and independent accounting work for borrowed kinds.
    /// </summary>
    /// <param name="kind">Slab, Generation, or Bump.</param>
    /// <param name="alignment">The allocation alignment supported by this allocator.</param>
    [TestMethod]
    [DataRow(0, 1)]
    [DataRow(0, 8)]
    [DataRow(1, 8)]
    [DataRow(1, 4096)]
    [DataRow(2, 8)]
    [DataRow(2, 4096)]
    public Task BorrowedAllocatorsPreserveExactBuffersOwnersAndIndependentAccounting(int kind, int alignment)
        => CheckAsync(kind, $"SELECT datatype.allocator_basics({kind == 0}, {alignment})", "True|True|True|True|True|True|True|True|True");

    /// <summary>
    /// Individual frees invalidate checked aliases without damaging other chunks or preventing later allocation.
    /// </summary>
    /// <param name="kind">Slab or Generation.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    public Task IndividualFreeKeepsControlContentsAndPermitsExactReplacement(int kind)
        => CheckAsync(kind, "SELECT datatype.allocator_individual_free()", "stale|9123|True|-73|True|True");

    /// <summary>
    /// Slab accepts exact-size resize and Generation retains bounded contents through growth and shrink.
    /// </summary>
    /// <param name="kind">Slab or Generation.</param>
    /// <param name="tryResize">Whether resizing requests the no-OOM path.</param>
    [TestMethod]
    [DataRow(0, false)]
    [DataRow(0, true)]
    [DataRow(1, false)]
    [DataRow(1, true)]
    public Task ResizePreservesPrefixZeroGrowthCheckedViewsAndNativeOwner(int kind, bool tryResize)
        => CheckAsync(kind, $"SELECT datatype.allocator_resize({kind == 0}, {tryResize})",
            $"True,True|True|True|True|True|True|True|{(kind == 0 ? 64 : 32)}");

    /// <summary>
    /// No-OOM replacement releases the old external Generation block before reset, preserving bytes and checked views.
    /// </summary>
    [TestMethod]
    public Task GenerationNoOomResizeReclaimsReplacedExternalStorageBeforeReset()
        => WithAllocatorAsync(1, async (command, token) =>
        {
            command.CommandText = "SELECT datatype.allocator_replacement_reclaims()";
            string report = Assert.IsInstanceOfType<string>(await command.ExecuteScalarAsync(token));
            string[] fields = report.Split('|');
            Assert.HasCount(13, fields);
            for (int index = 0; index < 7; index++)
            {
                Assert.AreEqual("True", fields[index], $"Payload or ownership observation {index}.");
            }

            Assert.AreEqual("32", fields[7]);
            long releasedBytes = long.Parse(fields[8], CultureInfo.InvariantCulture);
            long releasedCatalog = long.Parse(fields[9], CultureInfo.InvariantCulture);
            Assert.IsGreaterThanOrEqualTo(2L * 1024 * 1024, releasedBytes, "The replaced external block must be reclaimed before reset.");
            Assert.AreEqual(releasedBytes, releasedCatalog, "Independent native catalog accounting must observe the same released storage.");
            Assert.AreEqual("True", fields[10], "Reset restores the native byte baseline.");
            Assert.AreEqual("True", fields[11], "Reset restores the independent catalog baseline.");
            Assert.AreEqual("stale", fields[12]);
        });

    /// <summary>
    /// Slab rejects all nonmatching allocation sizes, including no-OOM requests, with a live control unchanged.
    /// </summary>
    /// <param name="size">The invalid Slab chunk size.</param>
    /// <param name="tryAllocate">Whether allocation requests the no-OOM path.</param>
    [TestMethod]
    [DataRow(0, false)]
    [DataRow(63, false)]
    [DataRow(65, false)]
    [DataRow(0, true)]
    [DataRow(63, true)]
    [DataRow(65, true)]
    public Task SlabWrongSizeErrorsPreserveControlPointerLengthBytesAndOwner(int size, bool tryAllocate)
        => CheckAsync(0, $"SELECT datatype.allocator_operation_error(0, {size}, {tryAllocate}, 8)",
            $"XX000:unexpected alloc chunk size {size} (expected 64)|64|True|True|True|True|True");

    /// <summary>
    /// Native Slab realloc and no-OOM replacement failures preserve the original allocation and exact diagnostic.
    /// </summary>
    /// <param name="size">The invalid replacement size.</param>
    /// <param name="tryResize">Whether resizing allocates a replacement chunk.</param>
    [TestMethod]
    [DataRow(0, false)]
    [DataRow(63, false)]
    [DataRow(65, false)]
    [DataRow(0, true)]
    [DataRow(63, true)]
    [DataRow(65, true)]
    public Task SlabResizeFailuresPreserveOwnershipAndNativeDiagnostic(int size, bool tryResize)
        => CheckAsync(0, $"SELECT datatype.allocator_operation_error(1, {size}, {tryResize}, 8)",
            $"XX000:{(tryResize ? $"unexpected alloc chunk size {size} (expected 64)" : "slab allocator does not support realloc()")}|64|True|True|True|True|True");

    /// <summary>
    /// Alignment padding cannot silently change the fixed Slab chunk size or consume the existing control.
    /// </summary>
    [TestMethod]
    public Task SlabOverAlignmentReportsNativePaddedSizeAndPreservesControl()
        => WithAllocatorAsync(0, async (command, token) =>
        {
            command.CommandText = "SELECT datatype.allocator_operation_error(3, 64, false, 8)";
            string result = Assert.IsInstanceOfType<string>(await command.ExecuteScalarAsync(token));
            Assert.MatchesRegex(@"^XX000:unexpected alloc chunk size [0-9]+ \(expected 64\)\|64\|True\|True\|True\|True\|True$", result);
        });

    /// <summary>
    /// Bump rejects free and every ordinary, no-OOM, or aligned resize before inspecting a nonexistent chunk header.
    /// </summary>
    /// <param name="operation">Resize or individual free.</param>
    /// <param name="tryOperation">Whether resizing requests no-OOM.</param>
    /// <param name="alignment">Ordinary or over-aligned native storage.</param>
    [TestMethod]
    [DataRow(1, false, 8)]
    [DataRow(1, true, 8)]
    [DataRow(1, false, 4096)]
    [DataRow(1, true, 4096)]
    [DataRow(2, false, 8)]
    [DataRow(2, false, 4096)]
    public Task BumpUnsupportedOperationsRetainPointerLengthContentsAndCheckedOwner(int operation, bool tryOperation, int alignment)
        => CheckAsync(2, $"SELECT datatype.allocator_operation_error({operation}, 128, {tryOperation}, {alignment})",
            $"XX000:{(operation == 1 ? "realloc" : "pfree")} is not supported by the bump memory allocator|64|True|True|True|True|True");

    /// <summary>
    /// Raw transfer preserves exact bytes while old tracked aliases expire and Bump rejects header-based adoption.
    /// </summary>
    /// <param name="kind">Slab, Generation, or Bump.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public Task RawTransferAdoptsSupportedKindsAndRejectsBumpWithoutLosingBytes(int kind)
        => CheckAsync(kind, $"SELECT datatype.allocator_transfer({kind == 2})",
            $"{(kind == 2 ? "XX000:GetMemoryChunkContext is not supported by the bump memory allocator" : "adopted")}|True|stale|{(kind == 2 ? 731 : 9123)}|stale|True");

    /// <summary>
    /// Typed context ownership and one-byte no-OOM values survive supported operations and expire on native reset.
    /// </summary>
    /// <param name="kind">Generation or Bump.</param>
    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    public Task TypedContextValuesRetainValuesAndFailedBumpDisposalCanReleaseOwnership(int kind)
        => CheckAsync(kind, $"SELECT datatype.allocator_typed_values({kind == 2})",
            $"{(kind == 2 ? "XX000:pfree is not supported by the bump memory allocator" : "released")}|0|5,0,731,731,231,17|True|stale,stale,stale,stale");

    /// <summary>
    /// Two native resets invalidate distinct raw generations and run each registered callback once before releasing bytes.
    /// </summary>
    /// <param name="kind">Slab, Generation, or Bump.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public Task RepeatedResetPreservesContextIdentityAndExpiresEachAllocationGeneration(int kind)
        => WithAllocatorAsync(kind, async (command, token) =>
        {
            command.CommandText = "SELECT datatype.allocator_save()";
            Assert.AreEqual(Pending, await command.ExecuteScalarAsync(token));
            command.CommandText = "SELECT datatype.allocator_reset_twice()";
            Assert.AreEqual(Reset + "|True|9123|stale|True|B731:False,A731:False|True", await command.ExecuteScalarAsync(token));
        });

    /// <summary>
    /// Deleting the test-owned parent invalidates borrowed contexts and aliases after callbacks can still read their payload.
    /// </summary>
    /// <param name="kind">Slab, Generation, or Bump.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public Task NativeParentDeletionRunsCallbacksAndInvalidatesBorrowedContextAndAliases(int kind)
        => WithAllocatorAsync(kind, async (command, token) =>
        {
            command.CommandText = "SELECT datatype.allocator_save()";
            Assert.AreEqual(Pending, await command.ExecuteScalarAsync(token));
            command.CommandText = "SELECT tests.allocator_delete()";
            await command.ExecuteNonQueryAsync(token);
            command.CommandText = "SELECT datatype.allocator_saved_state()";
            Assert.AreEqual(Deleted, await command.ExecuteScalarAsync(token));
        });

    /// <summary>
    /// Commit and rollback release transaction-owned special contexts while managed callback observations survive in the backend.
    /// </summary>
    /// <param name="kind">Slab, Generation, or Bump.</param>
    /// <param name="commit">Whether native cleanup is driven by commit rather than rollback.</param>
    [TestMethod]
    [DataRow(0, false)]
    [DataRow(0, true)]
    [DataRow(1, false)]
    [DataRow(1, true)]
    [DataRow(2, false)]
    [DataRow(2, true)]
    public async Task TransactionCleanupRunsCallbacksBeforeExpiringBorrowedAllocations(int kind, bool commit)
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        int backend = connection.ProcessID;
        await using var command = new NpgsqlCommand("BEGIN", connection);
        await command.ExecuteNonQueryAsync(token);
        await CaptureAsync(command, kind, "datatype.allocator_capture()", "Ankus fixture allocator", token);
        command.CommandText = "SELECT datatype.allocator_save()";
        Assert.AreEqual(Pending, await command.ExecuteScalarAsync(token));
        command.CommandText = commit ? "COMMIT" : "ROLLBACK";
        await command.ExecuteNonQueryAsync(token);
        command.CommandText = "SELECT datatype.allocator_saved_state()";
        Assert.AreEqual(Deleted, await command.ExecuteScalarAsync(token));
        await AssertEmptyAndRecoveredAsync(command, backend, token);
    }

    /// <summary>
    /// Name conversion and structured error replay use allocator-independent storage in both UTF8 and LATIN1 databases.
    /// </summary>
    /// <param name="kind">Slab or Bump.</param>
    /// <param name="encoding">The native database encoding.</param>
    /// <param name="error">Whether the managed callback throws instead of reporting a notice.</param>
    [TestMethod]
    [DataRow(0, "UTF8", false)]
    [DataRow(0, "UTF8", true)]
    [DataRow(0, "LATIN1", false)]
    [DataRow(0, "LATIN1", true)]
    [DataRow(2, "UTF8", false)]
    [DataRow(2, "UTF8", true)]
    [DataRow(2, "LATIN1", false)]
    [DataRow(2, "LATIN1", true)]
    public async Task SpecialCurrentContextPreservesEncodedNamesAndOwnedDiagnostics(int kind, string encoding, bool error)
    {
        CancellationToken token = context.CancellationToken;
        string database = "allocator_encoding_" + Guid.NewGuid().ToString("N");
        await using NpgsqlConnection administrator = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        await using (var create = new NpgsqlCommand(
            $"CREATE DATABASE {database} TEMPLATE template0 ENCODING '{encoding}' LC_COLLATE 'C' LC_CTYPE 'C'", administrator))
        {
            await create.ExecuteNonQueryAsync(token);
        }

        try
        {
            var builder = new NpgsqlConnectionStringBuilder(administrator.ConnectionString) { Database = database };
            await using var connection = new NpgsqlConnection(builder.ConnectionString);
            await connection.OpenAsync(token);
            int backend = connection.ProcessID;
            var notices = new List<PostgresNotice>();
            connection.Notice += (_, args) => notices.Add(args.Notice);
            await using var command = new NpgsqlCommand("CREATE EXTENSION ankus_test; CREATE SCHEMA tests;" + AllocatorFixtureCompiler.InstallationSql, connection);
            await command.ExecuteNonQueryAsync(token);
            command.CommandText = "BEGIN";
            await command.ExecuteNonQueryAsync(token);
            if (error)
            {
                PostgresException failure = await Assert.ThrowsExactlyAsync<PostgresException>(
                    () => CaptureAsync(command, kind, "allocator_capture_error()", "Ankus fixture café", token));
                Assert.AreEqual("22023", failure.SqlState);
                Assert.AreEqual("allocator café", failure.MessageText);
                Assert.AreEqual("detail naïve", failure.Detail);
                Assert.AreEqual("hint déjà", failure.Hint);
                Assert.AreEqual("allocator-fixture.cs", failure.File);
                Assert.AreEqual("73", failure.Line);
                Assert.AreEqual("AllocatorCaptureError", failure.Routine);
                command.CommandText = "ROLLBACK";
                await command.ExecuteNonQueryAsync(token);
            }
            else
            {
                await CaptureAsync(command, kind, "allocator_capture_notice()", "Ankus fixture café", token);
                PostgresNotice notice = Assert.ContainsSingle(notices.Where(static item => item.MessageText == "allocator café"));
                Assert.AreEqual("NOTICE", notice.InvariantSeverity);
                Assert.AreEqual("01000", notice.SqlState);
                Assert.AreEqual("detail naïve", notice.Detail);
                Assert.AreEqual("hint déjà", notice.Hint);
                Assert.AreEqual("allocator-fixture.cs", notice.File);
                Assert.AreEqual("73", notice.Line);
                Assert.AreEqual("AllocatorCaptureNotice", notice.Routine);
                command.CommandText = "SELECT count(*) FROM pg_backend_memory_contexts WHERE name = 'Ankus error report'";
                Assert.AreEqual(0L, await command.ExecuteScalarAsync(token));
                command.CommandText = "COMMIT";
                await command.ExecuteNonQueryAsync(token);
            }

            command.CommandText = "SELECT allocator_captured_name()";
            Assert.AreEqual("Ankus fixture café", await command.ExecuteScalarAsync(token));
            command.CommandText = "SHOW server_encoding";
            Assert.AreEqual(encoding, await command.ExecuteScalarAsync(token));
            await AssertEmptyAndRecoveredAsync(command, backend, token);
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE {database} WITH (FORCE)", administrator);
            await drop.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    private Task CheckAsync(int kind, string sql, string expected)
        => WithAllocatorAsync(kind, async (command, token) =>
        {
            command.CommandText = sql;
            Assert.AreEqual(expected, Assert.IsInstanceOfType<string>(await command.ExecuteScalarAsync(token)));
        });

    private Task WithAllocatorAsync(int kind, Func<NpgsqlCommand, CancellationToken, Task> action)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(AllocatorContextTests), async (connection, transaction, token) =>
        {
            int backend = connection.ProcessID;
            await using var command = new NpgsqlCommand { Connection = connection, Transaction = transaction };
            await CaptureAsync(command, kind, "datatype.allocator_capture()", "Ankus fixture allocator", token);
            await action(command, token);
            command.CommandText = "SELECT tests.allocator_delete()";
            await command.ExecuteNonQueryAsync(token);
            await AssertEmptyAndRecoveredAsync(command, backend, token);
        }, context.CancellationToken);

    private static async Task CaptureAsync(NpgsqlCommand command, int kind, string callback, string name, CancellationToken token)
    {
        command.CommandText = "SELECT tests.allocator_create($1, $2::regprocedure, $3)";
        command.Parameters.Clear();
        command.Parameters.AddWithValue(kind);
        command.Parameters.AddWithValue(callback);
        command.Parameters.AddWithValue(name);
        try
        {
            Assert.AreEqual(42, await command.ExecuteScalarAsync(token));
        }
        finally
        {
            command.Parameters.Clear();
        }
    }

    private static async Task AssertEmptyAndRecoveredAsync(NpgsqlCommand command, int backend, CancellationToken token)
    {
        command.CommandText = "SELECT count(*) FROM pg_backend_memory_contexts WHERE name IN ('Ankus allocator fixture', 'Ankus fixture allocator', 'Ankus fixture café', 'Ankus context name', 'Ankus error report')";
        Assert.AreEqual(0L, await command.ExecuteScalarAsync(token));
        command.CommandText = "SELECT 42";
        Assert.AreEqual(42, await command.ExecuteScalarAsync(token));
        NpgsqlConnection? connection = command.Connection;
        Assert.IsNotNull(connection);
        Assert.AreEqual(backend, connection.ProcessID);
    }
}
