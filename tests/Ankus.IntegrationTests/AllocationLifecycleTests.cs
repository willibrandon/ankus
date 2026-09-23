using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies allocation policy and native storage reclamation at ordinary sizes and explicitly requested huge sizes.
/// </summary>
/// <param name="context">The per-test cancellation and resource evidence context.</param>
[TestClass]
[DoNotParallelize]
public sealed class AllocationLifecycleTests(TestContext context)
{
    private const long Mebibyte = 1024L * 1024;
    private const long Gibibyte = 1024L * Mebibyte;

    /// <summary>
    /// Gets five ordinary-size cases and, only when explicitly requested, five above-limit cases.
    /// </summary>
    public static IEnumerable<TestDataRow<(long InitialSize, long GrownSize, bool TryOperations, int Alignment, bool Zeroed)>> AllocationPolicies
    {
        get
        {
            foreach (TestDataRow<(long, long, bool, int, bool)> row in Cases(Mebibyte + 17, "small"))
            {
                yield return row;
            }

            if (Environment.GetEnvironmentVariable("ANKUS_TEST_HUGE_ALLOCATIONS") == "1")
            {
                foreach (TestDataRow<(long, long, bool, int, bool)> row in Cases(Gibibyte + 17, "huge"))
                {
                    yield return row;
                }
            }
        }
    }

    /// <summary>
    /// Allocation, above-limit growth, small shrink, individual free, and deletion preserve policy and exact live values.
    /// </summary>
    /// <param name="initialSize">The initial native payload size.</param>
    /// <param name="grownSize">The native payload size after growth.</param>
    /// <param name="tryOperations">Whether allocation and resize use no-OOM calls.</param>
    /// <param name="alignment">The requested native pointer alignment.</param>
    /// <param name="zeroed">Whether initial storage and the complete growth tail are zeroed.</param>
    [TestMethod]
    [DynamicData(nameof(AllocationPolicies))]
    public async Task AllocationPoliciesPreserveBytesOwnershipAndReclamation(long initialSize, long grownSize, bool tryOperations, int alignment, bool zeroed)
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        Assert.IsFalse(new NpgsqlConnectionStringBuilder(connection.ConnectionString).Pooling, "Resource limits must belong to a fresh unpooled backend.");
        int backend = connection.ProcessID;
        await using var warm = new NpgsqlCommand("SELECT datatype.echo(42::integer)", connection) { CommandTimeout = 90 };
        Assert.AreEqual(42, await warm.ExecuteScalarAsync(token));
        warm.CommandText = "SET statement_timeout = '90s'";
        await warm.ExecuteNonQueryAsync(token);
        if (initialSize > Gibibyte - 1)
        {
            warm.CommandText = "SHOW debug_assertions";
            Assert.AreEqual("off", await warm.ExecuteScalarAsync(token), "Huge allocation admission requires the release PostgreSQL target.");
            warm.CommandText = "SELECT tests.allocator_flags()";
            Assert.AreEqual(0, await warm.ExecuteScalarAsync(token), "The selected native fixture headers must have assertions and memory checking disabled.");
            string mode = $"PostgreSQL {PostgresFixture.Cluster.Installation.Version}; debug_assertions=off; initial={initialSize}; grown={grownSize}; try={tryOperations}; alignment={alignment}; zeroed={zeroed}";
            await HugeAllocationResources.RunAsync(backend, mode, context,
                () => AssertLifecycleAsync(connection, backend, initialSize, grownSize, tryOperations, alignment, zeroed, token), token);
        }
        else
        {
            await AssertLifecycleAsync(connection, backend, initialSize, grownSize, tryOperations, alignment, zeroed, token);
        }
    }

    private static IEnumerable<TestDataRow<(long, long, bool, int, bool)>> Cases(long initialSize, string scale)
    {
        long grownSize = initialSize + Mebibyte;
        yield return new((initialSize, grownSize, false, 0, false)) { DisplayName = $"{scale}: ordinary allocation and resize" };
        yield return new((initialSize, grownSize, true, 0, false)) { DisplayName = $"{scale}: no-OOM allocation and resize" };
        yield return new((initialSize, grownSize, false, 64, false)) { DisplayName = $"{scale}: 64-byte aligned allocation and resize" };
        yield return new((initialSize, grownSize, true, 64, false)) { DisplayName = $"{scale}: 64-byte aligned no-OOM allocation and resize" };
        yield return new((initialSize, grownSize, false, 0, true)) { DisplayName = $"{scale}: zeroed allocation and growth" };
    }

    private async Task AssertLifecycleAsync(NpgsqlConnection connection, int backend, long initialSize, long grownSize, bool tryOperations, int alignment, bool zeroed, CancellationToken token)
    {
        int options = zeroed ? 5 : 4;
        await using var command = new NpgsqlCommand("SELECT * FROM datatype.memory_allocation_lifecycle($1, $2, $3, $4, $5)", connection)
        {
            CommandTimeout = 90,
        };
        command.Parameters.AddWithValue(initialSize);
        command.Parameters.AddWithValue(grownSize);
        command.Parameters.AddWithValue(tryOperations);
        command.Parameters.AddWithValue(alignment);
        command.Parameters.AddWithValue(zeroed);
        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(token))
        {
            Assert.AreEqual(11, reader.FieldCount);
            string[] names = ["stage", "length", "options", "alignment", "owner", "native_bytes", "catalog_bytes", "values", "view", "zeroes", "aligned"];
            for (int column = 0; column < names.Length; column++)
            {
                Assert.AreEqual(names[column], reader.GetName(column));
            }

            AllocationObservation baseline = await ReadAsync(reader, token);
            AssertStage(baseline, "baseline", 0, 0, 0, true, "", "", "", false);
            Assert.IsGreaterThan(0L, baseline.NativeBytes);
            AllocationObservation allocated = await ReadAsync(reader, token);
            AssertStage(allocated, "allocated", initialSize, options, alignment, true, "17,73,231", "17,231", zeroed ? "0,0,0" : "unchecked", true);
            Assert.IsGreaterThanOrEqualTo(initialSize, allocated.NativeBytes - baseline.NativeBytes, "Native allocated storage must include the requested payload.");
            AllocationObservation grown = await ReadAsync(reader, token);
            AssertStage(grown, "grown", grownSize, options, alignment, true, "17,73,231,99,142", "17,231", zeroed ? "all-zero" : "unchecked", true);
            Assert.IsGreaterThanOrEqualTo(grownSize, grown.NativeBytes - baseline.NativeBytes, "The larger payload must be present in native accounting.");
            AllocationObservation shrunk = await ReadAsync(reader, token);
            AssertStage(shrunk, "shrunk", 128, options, alignment, true, "17,73", "17,range", "", true);
            Assert.IsGreaterThan(initialSize - 128, grown.NativeBytes - shrunk.NativeBytes, "Shrink must reclaim the previous payload before any reset or deletion.");
            AllocationObservation freed = await ReadAsync(reader, token);
            AssertStage(freed, "freed", 0, 0, 0, true, "42", "stale,stale", "", false);
            Assert.AreEqual(baseline.NativeBytes, freed.NativeBytes, "Individual free must restore the still-live owner's native baseline.");
            Assert.AreEqual(baseline.CatalogBytes, freed.CatalogBytes, "The independent catalog must observe reclamation before owner deletion.");
            AllocationObservation deleted = await ReadAsync(reader, token);
            AssertStage(deleted, "deleted", 0, 0, 0, false, "", "stale,stale", "", false);
            Assert.AreEqual(0L, deleted.NativeBytes);
            Assert.AreEqual(0L, deleted.CatalogBytes);
            Assert.IsFalse(await reader.ReadAsync(token), "The lifecycle must return exactly six observed stages.");
            context.WriteLine($"Allocation lifecycle native/catalog bytes: baseline={baseline.NativeBytes}, allocated={allocated.NativeBytes}, grown={grown.NativeBytes}, shrunk={shrunk.NativeBytes}, freed={freed.NativeBytes}, deleted={deleted.NativeBytes}.");
        }

        command.Parameters.Clear();
        command.CommandText = "SELECT pg_backend_pid(), 42";
        await using NpgsqlDataReader recovery = await command.ExecuteReaderAsync(token);
        Assert.IsTrue(await recovery.ReadAsync(token));
        Assert.AreEqual(backend, recovery.GetInt32(0));
        Assert.AreEqual(42, recovery.GetInt32(1));
        Assert.IsFalse(await recovery.ReadAsync(token));
        Assert.AreEqual(backend, connection.ProcessID);
    }

    private static async Task<AllocationObservation> ReadAsync(NpgsqlDataReader reader, CancellationToken token)
    {
        Assert.IsTrue(await reader.ReadAsync(token), "A required native lifecycle stage is missing.");
        return new(reader.GetString(0), reader.GetInt64(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetBoolean(4),
            reader.GetInt64(5), reader.GetInt64(6), reader.GetString(7), reader.GetString(8), reader.GetString(9), reader.GetBoolean(10));
    }

    private static void AssertStage(AllocationObservation actual, string stage, long length, int options, int alignment, bool owner, string values, string view, string zeroes, bool aligned)
    {
        Assert.AreEqual(stage, actual.Stage);
        Assert.AreEqual(length, actual.Length, stage + " length");
        Assert.AreEqual(options, actual.Options, stage + " policies");
        Assert.AreEqual(alignment, actual.Alignment, stage + " alignment");
        Assert.AreEqual(owner, actual.Owner, stage + " owner");
        Assert.AreEqual(values, actual.Values, stage + " exact payload");
        Assert.AreEqual(view, actual.View, stage + " checked views");
        Assert.AreEqual(zeroes, actual.Zeroes, stage + " zero initialization");
        Assert.AreEqual(aligned, actual.Aligned, stage + " native pointer alignment");
        Assert.AreEqual(actual.NativeBytes, actual.CatalogBytes, stage + " independent catalog accounting");
    }

    private sealed record AllocationObservation(string Stage, long Length, int Options, int Alignment, bool Owner,
        long NativeBytes, long CatalogBytes, string Values, string View, string Zeroes, bool Aligned);
}
