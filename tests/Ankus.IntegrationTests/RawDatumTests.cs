using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Exercises raw signatures and hand-written type codecs through the real PostgreSQL backend.
/// </summary>
/// <param name="context">The test cancellation context.</param>
[TestClass]
public sealed class RawDatumTests(TestContext context)
{
    /// <summary>
    /// Preserves an unmapped by-value type, every native bit, SQL NULL, and its declared result identity.
    /// </summary>
    /// <param name="expression">The raw input expression.</param>
    [TestMethod]
    [DataRow("'0/0'::pg_lsn")]
    [DataRow("'FFFFFFFF/FFFFFFFF'::pg_lsn")]
    [DataRow("NULL::pg_lsn")]
    public async Task RawScalarsPreserveBitsNullAndExactType(string expression)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.IsTrue(await Scalar<bool>(connection, $"""
            SELECT raw_values.raw_lsn({expression}) IS NOT DISTINCT FROM {expression}
                AND pg_typeof(raw_values.raw_lsn({expression}))::oid = 'pg_lsn'::regtype::oid
            """));
    }

    /// <summary>
    /// Resolves raw pseudotype inputs without losing the caller's concrete type or NULL semantics.
    /// </summary>
    /// <param name="expression">A concrete PostgreSQL input.</param>
    [TestMethod]
    [DataRow("42::integer")]
    [DataRow("'0/1234'::pg_lsn")]
    [DataRow("NULL::text")]
    public async Task RawPseudotypesResolveActualInputs(string expression)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.IsTrue(await Scalar<bool>(connection, $"""
            SELECT raw_values.raw_poly({expression}) IS NOT DISTINCT FROM {expression}
                AND pg_typeof(raw_values.raw_poly({expression}))::oid = pg_typeof({expression})::oid
                AND raw_values.raw_any({expression}) = coalesce(({expression})::text,'NULL')
            """));
    }

    /// <summary>
    /// Native raw internal inputs preserve SQL NULL, a present zero word, and the original writable pointee.
    /// </summary>
    /// <param name="mode">The native input shape.</param>
    /// <param name="expected">The exact callback result.</param>
    [TestMethod]
    [DataRow(0, -1L)]
    [DataRow(1, 0L)]
    [DataRow(2, 42L)]
    public async Task RawInternalInputsPreserveNativeIdentity(int mode, long expected)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.AreEqual(expected, await Scalar<long>(connection,
            $"SELECT tests.internal_invoke('raw_values.raw_internal(internal)',{mode})"));
    }

    /// <summary>
    /// Native callers may provide extra slots but cannot omit declared arguments.
    /// </summary>
    [TestMethod]
    public async Task RawNativeCallsRejectMissingArgumentsAndRecover()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => Scalar<object>(connection,
            "SELECT tests.internal_invoke('datatype.context_snapshot(integer,text)',1)"));
        Assert.AreEqual(PostgresErrorCodes.InvalidParameterValue, error.SqlState);
        Assert.AreEqual("Incorrect argument count for generated Ankus function", error.MessageText);
        Assert.AreEqual(42L, await Scalar<long>(connection,
            "SELECT tests.internal_invoke('raw_values.raw_internal(internal)',2)"));
        Assert.AreEqual(connection.ProcessID, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }

    /// <summary>
    /// Preserves array shape, empty arrays, NULL cells, and whole-array NULLs without managed element conversion.
    /// </summary>
    /// <param name="expression">The typed native array.</param>
    [TestMethod]
    [DataRow("'{}'::pg_lsn[]")]
    [DataRow("'[2:3][-1:0]={{0/0,NULL},{FFFFFFFF/FFFFFFFF,0/2}}'::pg_lsn[]")]
    [DataRow("NULL::pg_lsn[]")]
    public async Task RawArraysPreserveShapeAndUnmappedCells(string expression)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.IsTrue(await Scalar<bool>(connection, $"""
            SELECT raw_values.raw_array({expression}) IS NOT DISTINCT FROM {expression}
                AND pg_typeof(raw_values.raw_array({expression}))::oid = 'pg_lsn[]'::regtype::oid
            """));
    }

    /// <summary>
    /// Executes actual native type input/output callbacks and a generated cast at the full 24-bit boundaries.
    /// </summary>
    /// <param name="value">The custom type value.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(42)]
    [DataRow(16777215)]
    public async Task HandWrittenBaseTypeUsesNativeInputOutputAndCast(int value)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.AreEqual(value, await Scalar<int>(connection, $"SELECT '{value}'::raw_values.u24::integer"));
        Assert.AreEqual(value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            await Scalar<string>(connection, $"SELECT '{value}'::raw_values.u24::text"));
        Assert.AreEqual(DBNull.Value, await Scalar<object>(connection, "SELECT NULL::raw_values.u24::integer"));
    }

    /// <summary>
    /// Rejects custom input outside its representation and preserves the backend for later type callbacks.
    /// </summary>
    /// <param name="input">The invalid type input.</param>
    [TestMethod]
    [DataRow("16777216")]
    [DataRow("-1")]
    [DataRow("invalid")]
    public async Task HandWrittenBaseTypeErrorsPreserveBackendRecovery(string input)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(
            () => Scalar<object>(connection, $"SELECT '{input}'::raw_values.u24"));
        Assert.AreEqual("22003", error.SqlState);
        Assert.AreEqual("value exceeds 24 bits", error.MessageText);
        Assert.AreEqual(42, await Scalar<int>(connection, "SELECT '42'::raw_values.u24::integer"));
        Assert.AreEqual(connection.ProcessID, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }

    /// <summary>
    /// Retains domain identity, whole-composite NULL, external text, and copies beyond temporary owners.
    /// </summary>
    [TestMethod]
    public async Task RawByReferenceValuesRetainOwnershipAndDomainIdentity()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.IsTrue(await Scalar<bool>(connection, """
            CREATE TEMP TABLE raw_toast(value text);
            ALTER TABLE raw_toast ALTER COLUMN value SET STORAGE EXTERNAL;
            INSERT INTO raw_toast VALUES(repeat('héllo 🐘',10000));
            SELECT raw_values.raw_text(value) = value FROM raw_toast
            """));
        Assert.IsTrue(await Scalar<bool>(connection, """
            SELECT raw_values.raw_pair(ROW(42,repeat('owned',10000))::raw_values.pair)::text =
                (ROW(42,repeat('owned',10000))::raw_values.pair)::text
                AND record_send(raw_values.raw_pair(NULL)) IS NULL
                AND raw_values.raw_domain(42) = 42
                AND pg_typeof(raw_values.raw_domain(42))::oid = 'raw_values.positive'::regtype::oid
            """));
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => Scalar<object>(connection, "SELECT raw_values.raw_required()"));
        Assert.AreEqual(PostgresErrorCodes.NotNullViolation, error.SqlState);
        Assert.AreEqual(42, await Scalar<int>(connection, "SELECT raw_values.raw_domain(42)"));
    }

    /// <summary>
    /// Streams and materializes copied raw storage across advances and early termination.
    /// </summary>
    /// <param name="function">The executor mode's callback.</param>
    [TestMethod]
    [DataRow("raw_rows")]
    [DataRow("raw_materialized")]
    public async Task RawSetsRetainRowsNullsAndAllowEarlyExit(string function)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.AreSequenceEqual<int?>([70000, 70000, null], await Scalar<int?[]>(connection,
            $"SELECT array_agg(length(value)) FROM raw_values.{function}(repeat('owned 🐘',10000)) value"));
        Assert.AreEqual("owned", await Scalar<string>(connection, $"SELECT value FROM raw_values.{function}('owned') value LIMIT 1"));
        Assert.AreSequenceEqual<string?>(["0/2|owned", null], await Scalar<string?[]>(connection,
            "SELECT array_agg(position::text||'|'||description) FROM raw_values.raw_table('0/2','owned')"));
    }

    /// <summary>
    /// Expands raw composites into the caller's descriptor in both executor modes.
    /// </summary>
    /// <param name="function">The streaming or materialized callback.</param>
    [TestMethod]
    [DataRow("raw_pairs")]
    [DataRow("raw_pairs_materialized")]
    public async Task RawCompositeSetsPreserveRowsAndNulls(string function)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.AreSequenceEqual<string?>(["42|owned", null], await Scalar<string?[]>(connection,
            $"SELECT array_agg(number::text||'|'||label) FROM raw_values.{function}(ROW(42,'owned')::raw_values.pair)"));
    }

    /// <summary>
    /// Differentiates a NULL wrapper, a typed raw NULL, and a present zero word.
    /// </summary>
    /// <param name="mode">The result shape.</param>
    /// <param name="expected">The expected SQL value.</param>
    [TestMethod]
    [DataRow(0, 42)]
    [DataRow(2, null)]
    [DataRow(4, null)]
    [DataRow(7, 0)]
    public async Task RawResultsPreserveNullAndZero(int mode, int? expected)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.AreEqual(expected.HasValue ? expected.Value : DBNull.Value,
            await Scalar<object>(connection, $"SELECT raw_values.raw_result({mode})"));
    }

    /// <summary>
    /// Rejects wrong exact types even for typed NULLs, and rejects expired owners before reading native storage.
    /// </summary>
    /// <param name="mode">The invalid result shape.</param>
    /// <param name="sqlState">The expected failure category.</param>
    [TestMethod]
    [DataRow(1, "42804")]
    [DataRow(3, "42804")]
    [DataRow(5, "38000")]
    [DataRow(6, "38000")]
    public async Task RawResultErrorsValidateIdentityAndRecover(int mode, string sqlState)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(
            () => Scalar<object>(connection, $"SELECT raw_values.raw_result({mode})"));
        Assert.AreEqual(sqlState, error.SqlState);
        error = await Assert.ThrowsExactlyAsync<PostgresException>(
            () => Scalar<object>(connection, $"SELECT array_agg(value) FROM raw_values.raw_error_rows({mode}) value"));
        Assert.AreEqual(sqlState, error.SqlState);
        Assert.AreEqual(42, await Scalar<int>(connection, "SELECT raw_values.raw_result(0)"));
        Assert.AreEqual(connection.ProcessID, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }

    /// <summary>
    /// Executes one exact scalar result with the test cancellation token.
    /// </summary>
    private async Task<T> Scalar<T>(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(context.CancellationToken);
        while (reader.FieldCount == 0)
        {
            Assert.IsTrue(await reader.NextResultAsync(context.CancellationToken));
        }

        Assert.IsTrue(await reader.ReadAsync(context.CancellationToken));
        return reader.GetFieldValue<T>(0);
    }
}
