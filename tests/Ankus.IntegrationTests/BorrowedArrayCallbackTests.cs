using Npgsql;

namespace Ankus.IntegrationTests;

public sealed partial class BorrowedArrayTests
{
    /// <summary>
    /// Returns directly borrowed arrays and cells only after transferring live storage before callback expiration.
    /// </summary>
    /// <param name="expression">The selected array value, including whole-array SQL NULL.</param>
    [TestMethod]
    [DataRow("NULL::text[]")]
    [DataRow("ARRAY[]::text[]")]
    [DataRow("ARRAY[NULL,'last']::text[]")]
    [DataRow("ARRAY[0,NULL,7]")]
    [DataRow("'[-2:-1][4:5]={{first,NULL},{last,tail}}'::text[]")]
    [DataRow("ARRAY[repeat('large 🐘',10000),NULL,'last']")]
    public async Task BorrowedArrayReturnsSurviveCallbackCleanup(string expression)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.IsTrue(await Scalar<bool>(connection, $"""
            SELECT borrowed_arrays.array_view_identity({expression}) IS NOT DISTINCT FROM {expression}
                AND pg_typeof(borrowed_arrays.array_view_identity({expression})) = pg_typeof({expression})
            """));
        Assert.AreEqual(0L, await Scalar<long>(connection,
            "SELECT count(*) FROM ankus_test_memory.contexts WHERE ident LIKE 'Ankus borrowed array%'"));
    }

    /// <summary>
    /// Raw element returns preserve present zero bits, SQL NULL and large by-reference storage after cleanup.
    /// </summary>
    [TestMethod]
    public async Task BorrowedArrayCellReturnsPreserveNullZeroAndOwnedBytes()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.AreEqual(0, await Scalar<int>(connection, "SELECT borrowed_arrays.array_view_first_cell(ARRAY[0,7])"));
        Assert.IsTrue(await Scalar<bool>(connection, "SELECT borrowed_arrays.array_view_first_cell(ARRAY[NULL,7]) IS NULL"));
        Assert.AreEqual(string.Concat(Enumerable.Repeat("large 🐘", 10000)), await Scalar<string>(connection,
            "SELECT borrowed_arrays.array_view_first_cell(ARRAY[repeat('large 🐘',10000),'last'])"));
    }

    /// <summary>
    /// The borrowed SQL NULL bitmap matches pgrx's <c>test_display_get_arr_nullbitmap</c> through both array views.
    /// </summary>
    /// <param name="array">The input array.</param>
    /// <param name="expected">Each bitmap byte in binary for the untyped and typed views.</param>
    [TestMethod]
    [DataRow("ARRAY[1,NULL,3,NULL,5]", "0b00010101|0b00010101")]
    [DataRow("ARRAY[1,2,3,4,5]", "|")]
    [DataRow("ARRAY[NULL,2,3,4,5,6,7,8,9]", "0b11111110,0b00000001|0b11111110,0b00000001")]
    [DataRow("'{{1,NULL},{NULL,4}}'::integer[]", "0b00001001|0b00001001")]
    public async Task BorrowedArraysExposeTheirNullBitmap(string array, string expected)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.AreEqual(expected, await Scalar<string>(connection, $"SELECT borrowed_arrays.array_view_null_bitmap({array})"));
    }

    /// <summary>
    /// Later and nested callbacks cannot revive expired inputs, and forgotten cursors are automatically released.
    /// </summary>
    [TestMethod]
    public async Task BorrowedArrayCallbackLeasesExpireWithoutInvalidatingParents()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.AreEqual("original", await Scalar<string>(connection,
            "SELECT borrowed_arrays.array_view_save(ARRAY['original'])"));
        string[] expired = ["ObjectDisposedException", "ObjectDisposedException", "ObjectDisposedException", "ObjectDisposedException", "0"];
        Assert.AreSequenceEqual(expired, await Scalar<string[]>(connection, "SELECT borrowed_arrays.array_view_expired()"));
        Assert.AreSequenceEqual<string?>(["outer", "nested", "ObjectDisposedException", "outer", "outer"],
            await Scalar<string?[]>(connection, "SELECT borrowed_arrays.array_view_nested(ARRAY['outer'])"));
        Assert.AreSequenceEqual(expired, await Scalar<string[]>(connection, "SELECT borrowed_arrays.array_view_expired()"));
        Assert.AreEqual(connection.ProcessID, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }

    /// <summary>
    /// Set iteration and aggregate finalization retain independent snapshots across managed callback boundaries.
    /// </summary>
    [TestMethod]
    public async Task BorrowedArrayRetainedInputsSurviveSetAndAggregateCallbacks()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.AreSequenceEqual<string?>(["first", null, "last"], await Scalar<string?[]>(connection,
            "SELECT array_agg(value) FROM borrowed_arrays.array_view_rows(ARRAY['first',NULL,'last']) value"));
        Assert.AreEqual("first", await Scalar<string>(connection,
            "SELECT value FROM borrowed_arrays.array_view_rows(ARRAY['first',NULL,'last']) value LIMIT 1"));
        Assert.AreSequenceEqual<string?>(["1009", "25", "1", "3", "True", "3", "1", "first", null, "last"],
            await Scalar<string?[]>(connection, """
                SELECT borrowed_arrays.array_view_first(value ORDER BY ordinal)
                FROM (VALUES (1,NULL::text[]),(2,ARRAY['first',NULL,'last']),(3,ARRAY['ignored'])) input(ordinal,value)
                """));
        Assert.AreEqual(0L, await Scalar<long>(connection,
            "SELECT count(*) FROM ankus_test_memory.contexts WHERE ident LIKE 'Ankus borrowed array%'"));
    }

    /// <summary>
    /// Managed failure expires captured aliases and releases private storage before the backend resumes work.
    /// </summary>
    [TestMethod]
    public async Task BorrowedArrayCallbackErrorsReleaseStorageAndRecover()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => Scalar<object>(connection,
            "SELECT borrowed_arrays.array_view_fail(ARRAY[repeat('owned',10000),NULL])"));
        Assert.AreEqual("38000", error.SqlState);
        Assert.AreEqual("Borrowed array callback failed.", error.MessageText);
        Assert.AreSequenceEqual<string>(["ObjectDisposedException", "ObjectDisposedException", "ObjectDisposedException",
            "ObjectDisposedException", "0"], await Scalar<string[]>(connection, "SELECT borrowed_arrays.array_view_expired()"));
        Assert.AreEqual(42, await Scalar<int>(connection, "SELECT 42"));
        Assert.AreEqual(connection.ProcessID, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }
}
