using Npgsql;

namespace Ankus.IntegrationTests;

public sealed partial class BorrowedArrayTests
{
    /// <summary>
    /// Concrete generated array signatures preserve native values, dimensions, NULLs and exact result types.
    /// </summary>
    /// <param name="function">The generated typed function.</param>
    /// <param name="expression">The independent SQL input.</param>
    [TestMethod]
    [DataRow("typed_array_identity", "NULL::integer[]")]
    [DataRow("typed_array_identity", "ARRAY[]::integer[]")]
    [DataRow("typed_array_identity", "ARRAY[0,NULL,7]")]
    [DataRow("typed_array_identity", "'[-2:-1][4:5]={{0,NULL},{7,19}}'::integer[]")]
    [DataRow("typed_text_array_identity", "NULL::text[]")]
    [DataRow("typed_text_array_identity", "ARRAY[repeat('large 🐘',10000),NULL,'']")]
    [DataRow("typed_bytea_array_identity", "ARRAY[decode('00ff','hex'),NULL,decode('','hex')]")]
    [DataRow("typed_read_only_array_identity", "ARRAY[7,NULL,11]")]
    [DataRow("typed_read_only_array_identity", "NULL::integer[]")]
    [DataRow("typed_composite_array_identity", "ARRAY[ROW('dog',7)::tuple_values.dog,NULL]")]
    [DataRow("typed_composite_array_identity", "NULL::tuple_values.dog[]")]
    [DataRow("typed_enum_array_identity", "ARRAY['High',NULL,'café']::datatype.enum_mood[]")]
    [DataRow("typed_custom_array_identity", "ARRAY['9223372036854775807',NULL,'-9']::custom_values.number[]")]
    public async Task GeneratedTypedArraysPreserveDeclaredResults(string function, string expression)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.IsTrue(await Scalar<bool>(connection, $"""
            SELECT borrowed_arrays.{function}({expression})::text IS NOT DISTINCT FROM ({expression})::text
                AND pg_typeof(borrowed_arrays.{function}({expression})) = pg_typeof({expression})
            """));
        await AssertTypedCallbackOwnersReleased(connection);
    }

    /// <summary>
    /// Array-level and cell-level NULL policies remain independent and reject before invalid managed access.
    /// </summary>
    [TestMethod]
    public async Task GeneratedTypedArraysEnforceNullCellContracts()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.AreEqual(3, await Scalar<int>(connection, "SELECT borrowed_arrays.typed_array_required(ARRAY[0,7,19])"));
        Assert.IsTrue(await Scalar<bool>(connection, "SELECT borrowed_arrays.typed_array_required(NULL::integer[]) IS NULL"));
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => Scalar<object>(connection,
            "SELECT borrowed_arrays.typed_array_required(ARRAY[0,NULL,19])"));
        Assert.AreEqual("38000", error.SqlState);
        Assert.AreEqual("SQL NULL array cells cannot be represented by 'System.Int32'. Use a nullable element type.", error.MessageText);
        await AssertTypedCallbackOwnersReleased(connection);
    }

    /// <summary>
    /// Native return checks reject a compatible element representation carrying a different complete array identity.
    /// </summary>
    [TestMethod]
    public async Task GeneratedTypedArraysRejectWrongNativeReturnIdentity()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        await using var create = new NpgsqlCommand("CREATE DOMAIN pg_temp.typed_array_domain AS integer[]", connection);
        await create.ExecuteNonQueryAsync(context.CancellationToken);
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => Scalar<object>(connection,
            "SELECT borrowed_arrays.typed_array_return_query('SELECT ARRAY[7,NULL]::pg_temp.typed_array_domain')"));
        Assert.AreEqual("42804", error.SqlState);
        Assert.Contains("type", error.MessageText);
        Assert.IsTrue(await Scalar<bool>(connection, """
            SELECT borrowed_arrays.typed_array_return_query('SELECT ARRAY[7,NULL]') IS NOT DISTINCT FROM ARRAY[7,NULL]
            """));
        await AssertTypedCallbackOwnersReleased(connection);
    }

    /// <summary>
    /// Nested and later callbacks observe exact typed-array, cursor and borrowed-cell lease boundaries.
    /// </summary>
    /// <param name="fail">Whether the scalar callback unwinds through a managed exception.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task GeneratedTypedArrayAliasesExpireAtCallbackExit(bool fail)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        if (fail)
        {
            PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => Scalar<object>(connection,
                "SELECT borrowed_arrays.typed_array_callback_save(ARRAY[repeat('owned',10000),NULL],true)"));
            Assert.AreEqual("38000", error.SqlState);
            Assert.AreEqual("Typed array callback failed.", error.MessageText);
        }
        else
        {
            Assert.AreEqual("original", await Scalar<string>(connection,
                "SELECT borrowed_arrays.typed_array_callback_save(ARRAY['original'],false)"));
        }

        string[] expired = ["ObjectDisposedException", "ObjectDisposedException", "ObjectDisposedException", "ObjectDisposedException"];
        Assert.AreSequenceEqual(expired, await Scalar<string[]>(connection, "SELECT borrowed_arrays.typed_array_callback_expired()"));
        Assert.AreSequenceEqual<string>(["outer", "nested", "ObjectDisposedException", "outer", "same cell"],
            await Scalar<string[]>(connection, "SELECT borrowed_arrays.typed_array_nested(ARRAY['outer'])"));
        Assert.AreSequenceEqual(expired, await Scalar<string[]>(connection, "SELECT borrowed_arrays.typed_array_callback_expired()"));
        await AssertTypedCallbackOwnersReleased(connection);
    }

    /// <summary>
    /// SETOF and TABLE results retain input snapshots and original cells across repeated callbacks and early exit.
    /// </summary>
    [TestMethod]
    public async Task GeneratedTypedArrayIteratorsKeepInputSnapshots()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.AreSequenceEqual<string?>(["[-1:1]={7,NULL,11}", null, "[-1:1]={7,NULL,11}"],
            await Scalar<string?[]>(connection, """
                SELECT array_agg(cell::text ORDER BY ordinal)
                FROM borrowed_arrays.typed_array_rows('[-1:1]={7,NULL,11}'::integer[],false) WITH ORDINALITY AS rows(cell,ordinal)
                """));
        Assert.AreSequenceEqual<string?>([null, null, null], await Scalar<string?[]>(connection, """
            SELECT array_agg(cell::text) FROM borrowed_arrays.typed_array_rows(NULL::integer[],false) cell
            """));
        Assert.AreEqual("{7,NULL,11}", await Scalar<string>(connection,
            "SELECT borrowed_arrays.typed_array_rows(ARRAY[7,NULL,11],true)::text LIMIT 1"));
        Assert.AreSequenceEqual<string>(["{7,NULL,11}:107", "{7,NULL,11}:111"], await Scalar<string[]>(connection, """
            SELECT array_agg(cell::text || ':' || position::text ORDER BY position)
            FROM borrowed_arrays.typed_array_table(ARRAY[7,NULL,11])
            """));
        await AssertTypedCallbackOwnersReleased(connection);
    }

    /// <summary>
    /// A resumed iterator failure releases retained typed snapshots and leaves the same backend usable.
    /// </summary>
    [TestMethod]
    public async Task GeneratedTypedArrayIteratorFailureReleasesSnapshots()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => Scalar<object>(connection,
            "SELECT count(*) FROM borrowed_arrays.typed_array_rows(ARRAY[7,NULL,11],true)"));
        Assert.AreEqual("38000", error.SqlState);
        Assert.AreEqual("Typed array iterator failed.", error.MessageText);
        await AssertTypedCallbackOwnersReleased(connection);
    }

    /// <summary>
    /// Internal managed state and concrete SQL array state both survive transition, final and return boundaries.
    /// </summary>
    /// <param name="function">The aggregate storage model.</param>
    [TestMethod]
    [DataRow("typed_array_first")]
    [DataRow("typed_array_sql_first")]
    public async Task GeneratedTypedArrayAggregatesRetainNativeState(string function)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.AreEqual("[-1:1]={7,NULL,11}", await Scalar<string>(connection, $$"""
            SELECT borrowed_arrays.{{function}}(value ORDER BY ordinal)::text
            FROM (VALUES (1,NULL::integer[]),(2,'[-1:1]={7,NULL,11}'::integer[]),(3,ARRAY[0,19])) input(ordinal,value)
            """));
        Assert.IsTrue(await Scalar<bool>(connection, $"""
            SELECT borrowed_arrays.{function}(value) IS NULL FROM (VALUES (NULL::integer[]),(NULL::integer[])) input(value)
            """));
        await AssertTypedCallbackOwnersReleased(connection);
    }

    /// <summary>
    /// Checks independent native owner accounting and backend recovery after each generated callback boundary.
    /// </summary>
    private async Task AssertTypedCallbackOwnersReleased(NpgsqlConnection connection)
    {
        Assert.AreEqual(0L, await Scalar<long>(connection, """
            SELECT count(*) FROM ankus_test_memory.contexts
            WHERE ident LIKE 'Ankus borrowed array%' OR ident LIKE 'Ankus borrowed buffer%'
            """));
        Assert.AreEqual(42, await Scalar<int>(connection, "SELECT 42"));
        Assert.AreEqual(connection.ProcessID, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }
}
