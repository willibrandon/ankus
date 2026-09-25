using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies custom range subtype conversion in the published Native AOT PostgreSQL extension.
/// </summary>
/// <param name="context">The test cancellation context.</param>
[TestClass]
public sealed class DatumRangeTests(TestContext context)
{
    /// <summary>
    /// Variable-length bounds survive detoasting and independent result disposal without freeing caller-owned writer aliases.
    /// </summary>
    [TestMethod]
    public async Task MappedRangesPreserveVariableLengthBoundsAndBorrowedWriters()
    {
        await using NpgsqlConnection connection = await Open();
        Assert.AreEqual("alpha|zulu", await Scalar<string>(connection, "SELECT range_mappings.read_words('[alpha,zulu)')"));
        Assert.IsTrue(await Scalar<bool>(connection, "SELECT range_mappings.expired()"));
        Assert.AreEqual("[alpha,zulu)", await Scalar<string>(connection, "SELECT range_mappings.echo_words('[alpha,zulu)')::text"));
        Assert.AreEqual("[alpha,zulu)|alpha", await Scalar<string>(connection, "SELECT range_mappings.borrowed_word()"));
        Assert.IsTrue(await Scalar<bool>(connection, "SELECT range_mappings.echo_words(NULL) IS NULL"));
        Assert.AreEqual("empty", await Scalar<string>(connection, "SELECT range_mappings.echo_words('empty')::text"));
        Assert.AreEqual("(,)", await Scalar<string>(connection, "SELECT range_mappings.echo_words('(,)')::text"));
        await Execute(connection, """
            CREATE TEMP TABLE saved_words(value range_mappings.words);
            INSERT INTO saved_words SELECT range_mappings.words(repeat('a猫',5000),repeat('z🐘',5000),'(]');
            """);
        Assert.IsTrue(await Scalar<bool>(connection, """
            SELECT range_send(range_mappings.echo_words(value))=range_send(value)
                AND range_mappings.read_words(value)=repeat('a猫',5000)||'|'||repeat('z🐘',5000)
            FROM saved_words
            """));
        Assert.AreEqual(connection.ProcessID, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }

    /// <summary>
    /// NULL, empty and infinite ends never construct or invoke a scalar converter; finite bounds retain independent values.
    /// </summary>
    [TestMethod]
    public async Task MappedRangesConvertOnlyFiniteBoundsAndShareScalarFactory()
    {
        await using NpgsqlConnection connection = await Open();
        Assert.AreEqual("null", await Scalar<string>(connection, "SELECT range_mappings.read(NULL)"));
        Assert.AreEqual("empty", await Scalar<string>(connection, "SELECT range_mappings.read('empty')"));
        Assert.AreEqual("infinite|infinite|False|False", await Scalar<string>(connection, "SELECT range_mappings.read('(,)')"));
        Assert.IsTrue(await Scalar<bool>(connection, "SELECT range_mappings.echo(NULL) IS NULL"));
        Assert.AreEqual("empty", await Scalar<string>(connection, "SELECT range_mappings.echo('empty')::text"));
        Assert.AreEqual("(,)", await Scalar<string>(connection, "SELECT range_mappings.echo('(,)')::text"));
        Assert.AreEqual("0|0|0", await Scalar<string>(connection, "SELECT range_mappings.counts()"));
        Assert.AreEqual("1003|1008|True|False", await Scalar<string>(connection, "SELECT range_mappings.read('[3,8)')"));
        Assert.AreEqual("1|2|0", await Scalar<string>(connection, "SELECT range_mappings.counts()"));
        Assert.IsTrue(await Scalar<bool>(connection, "SELECT range_mappings.expired()"));
        Assert.AreEqual("[6,12)", await Scalar<string>(connection, "SELECT range_mappings.make(1005,1011,false,true)::text"));
        Assert.AreEqual("1|2|2", await Scalar<string>(connection, "SELECT range_mappings.counts()"));
        Assert.AreEqual("infinite|1008|False|False", await Scalar<string>(connection, "SELECT range_mappings.read('(,8)')"));
        Assert.AreEqual("1003|infinite|True|False", await Scalar<string>(connection, "SELECT range_mappings.read('[3,)')"));
        Assert.AreEqual("1|4|2", await Scalar<string>(connection, "SELECT range_mappings.counts()"));
        Assert.AreEqual("[5,)", await Scalar<string>(connection, "SELECT range_mappings.make(1005,NULL,true,true)::text"));
        Assert.AreEqual("(,12)", await Scalar<string>(connection, "SELECT range_mappings.make(NULL,1011,true,true)::text"));
        Assert.AreEqual("1|4|4", await Scalar<string>(connection, "SELECT range_mappings.counts()"));
    }

    /// <summary>
    /// Typed and raw SPI values outlive owners, and range arrays preserve dimensions, NULL cells and empty ranges.
    /// </summary>
    [TestMethod]
    public async Task MappedRangesPreserveSpiOwnershipAndArrayShape()
    {
        await using NpgsqlConnection connection = await Open();
        Assert.AreEqual("1003|1008|True|False|1011|1017|True|False|1003|1008|True|False", await Scalar<string>(connection,
            "SELECT range_mappings.raw_and_spi()"));
        Assert.IsTrue(await Scalar<bool>(connection, "SELECT range_mappings.expired()"));
        Assert.IsTrue(await Scalar<bool>(connection, "SELECT range_mappings.echo_array(NULL) IS NULL"));
        Assert.AreEqual("{}", await Scalar<string>(connection, "SELECT range_mappings.echo_array('{}')::text"));
        const string array = "'[-2:-1][4:5]={{NULL,empty},{\"(,)\",\"[1,9)\"}}'::int4range[]";
        Assert.IsTrue(await Scalar<bool>(connection, "SELECT array_send(range_mappings.echo_array(" + array + ")) = array_send(" + array + ")"));
        Assert.AreEqual("[-2:-1][4:5]", await Scalar<string>(connection, "SELECT array_dims(range_mappings.echo_array(" + array + "))"));
        Assert.AreEqual("int4range[]", await Scalar<string>(connection, "SELECT pg_typeof(range_mappings.echo_array(" + array + "))::text"));
    }

    /// <summary>
    /// Allowlisted native operations preserve custom input and result conversion in both Boolean and range-valued paths.
    /// </summary>
    [TestMethod]
    public async Task MappedRangesExecuteEveryNativeOperation()
    {
        await using NpgsqlConnection connection = await Open();
        string[] expected = ["[1,6)", "[1,9)", "[4,6)", "[1,4)", "[1,9)"];
        for (int operation = 0; operation < expected.Length; operation++)
        {
            Assert.AreEqual(expected[operation], await Scalar<string>(connection,
                $"SELECT range_mappings.combine('[1,6)','[4,9)',{operation})::text"), $"operation {operation}");
        }

        Assert.IsTrue(await Scalar<bool>(connection, "SELECT range_mappings.test('[1,6)','empty',1005,0)"));
        Assert.IsFalse(await Scalar<bool>(connection, "SELECT range_mappings.test('[1,6)','empty',1006,0)"));
        Assert.IsTrue(await Scalar<bool>(connection, "SELECT range_mappings.test('[1,6)','[2,5)',0,1)"));
        Assert.IsFalse(await Scalar<bool>(connection, "SELECT range_mappings.test('[1,6)','[2,7)',0,1)"));
        Assert.IsTrue(await Scalar<bool>(connection, "SELECT range_mappings.test('[1,6)','[5,9)',0,2)"));
        Assert.IsFalse(await Scalar<bool>(connection, "SELECT range_mappings.test('[1,6)','[6,9)',0,2)"));
        Assert.IsTrue(await Scalar<bool>(connection, "SELECT range_mappings.test('[1,6)','[6,9)',0,3)"));
        Assert.IsFalse(await Scalar<bool>(connection, "SELECT range_mappings.test('[1,6)','[7,9)',0,3)"));
        Assert.AreEqual("[1,9)", await Scalar<string>(connection, "SELECT range_mappings.combine('[1,3)','[7,9)',4)::text"));
        Assert.AreEqual("empty", await Scalar<string>(connection, "SELECT range_mappings.combine('[1,3)','[7,9)',2)::text"));
        PostgresException disjoint = await Assert.ThrowsExactlyAsync<PostgresException>(() => Execute(connection,
            "SELECT range_mappings.combine('[1,3)','[7,9)',1)"));
        Assert.AreEqual("22000", disjoint.SqlState);
        Assert.AreEqual(connection.ProcessID, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
        Assert.AreEqual("[1,3)", await Scalar<string>(connection, "SELECT range_mappings.combine('[1,3)','empty',0)::text"));
    }

    /// <summary>
    /// Owned domain ranges keep continuous flags and skip CHECK execution on reads while enforcing it on writes.
    /// </summary>
    [TestMethod]
    public async Task MappedDomainRangesKeepIdentityAndCheckOnlyAssignments()
    {
        await using NpgsqlConnection connection = await Open();
        Assert.AreEqual("1001|1005|False|True|(1,5]", await Scalar<string>(connection,
            "SELECT range_mappings.parse_domain('(1,5]')"));
        Assert.AreEqual("[1,5)", await Scalar<string>(connection, "SELECT range_mappings.make_domain(1001,1005)::text"));
        Assert.AreEqual("range_mappings.bounds", await Scalar<string>(connection, "SELECT pg_typeof(range_mappings.make_domain(1001,1005))::text"));
        Assert.AreEqual("True|True|[1,7)", await Scalar<string>(connection, "SELECT range_mappings.domain_operations()"));
        Assert.IsTrue(await Scalar<bool>(connection, "SELECT range_mappings.try_parse_domain('(1,5]')"));
        Assert.IsFalse(await Scalar<bool>(connection, "SELECT range_mappings.try_parse_domain('[5,1)')"));
        Assert.IsFalse(await Scalar<bool>(connection, "SELECT range_mappings.try_parse_domain('broken')"));
        await Execute(connection, "CREATE TEMP TABLE saved_range(value range_mappings.bounds); INSERT INTO saved_range VALUES ('[1,5)'); SET ankus.range_reject='on';");
        Assert.AreEqual("1001|1005|True|False", await Scalar<string>(connection, "SELECT range_mappings.read_domain(value) FROM saved_range"));
        Assert.IsTrue(await Scalar<bool>(connection, "SELECT range_mappings.expired()"));
        PostgresException constraint = await Assert.ThrowsExactlyAsync<PostgresException>(() => Execute(connection,
            "SELECT range_mappings.make_domain(1001,1005)"));
        Assert.AreEqual("23514", constraint.SqlState);
        Assert.AreEqual("bound_check", constraint.ConstraintName);
        await Execute(connection, "SET ankus.range_reject='off'");
        Assert.AreEqual("[1,5)", await Scalar<string>(connection, "SELECT range_mappings.make_domain(1001,1005)::text"));
        Assert.AreEqual(connection.ProcessID, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }

    /// <summary>
    /// Exact range and scalar identities are checked before NULL bypass or scalar construction.
    /// </summary>
    /// <param name="absent">Whether the raw range is SQL NULL.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task MappedRangesRejectWrongRangeAndSubtypeBeforeConstruction(bool absent)
    {
        await using NpgsqlConnection connection = await Open();
        string argument = absent ? "true" : "false";
        PostgresException range = await Assert.ThrowsExactlyAsync<PostgresException>(() => Execute(connection,
            "SELECT range_mappings.wrong_oid(" + argument + ")"));
        Assert.AreEqual("38000", range.SqlState);
        Assert.AreEqual("PostgreSQL datum type OID 3926 does not match mapped type OID 3904.", range.MessageText);
        PostgresException subtype = await Assert.ThrowsExactlyAsync<PostgresException>(() => Execute(connection,
            "SELECT range_mappings.wrong_subtype(" + argument + ")"));
        Assert.AreEqual("38000", subtype.SqlState);
        Assert.AreEqual("PostgreSQL range subtype OID 23 does not match mapped bound type OID 20.", subtype.MessageText);
        Assert.AreEqual("0|0|0", await Scalar<string>(connection, "SELECT range_mappings.counts()"));
        Assert.AreEqual("1001|1005|True|False", await Scalar<string>(connection, "SELECT range_mappings.read('[1,5)')"));
        Assert.AreEqual(connection.ProcessID, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }

    /// <summary>
    /// Reader and writer failures preserve owned diagnostics, expire borrowed bounds and recover in the same backend.
    /// </summary>
    [TestMethod]
    public async Task MappedRangeErrorsCleanUpAndPreserveDiagnostics()
    {
        await using NpgsqlConnection connection = await Open();
        PostgresException read = await Assert.ThrowsExactlyAsync<PostgresException>(() => Execute(connection,
            "SELECT range_mappings.read('[9001,9005)')"));
        Assert.AreEqual("P8611", read.SqlState);
        Assert.AreEqual("mapped range reader failed", read.MessageText);
        Assert.AreEqual("bound 9001", read.Detail);
        Assert.AreEqual("choose another bound", read.Hint);
        Assert.IsTrue(await Scalar<bool>(connection, "SELECT range_mappings.expired()"));
        PostgresException write = await Assert.ThrowsExactlyAsync<PostgresException>(() => Execute(connection,
            "SELECT range_mappings.make(1001,9012,true,false)"));
        Assert.AreEqual("P8612", write.SqlState);
        Assert.AreEqual("mapped range writer failed", write.MessageText);
        Assert.AreEqual("bound 9012", write.Detail);
        Assert.AreEqual("choose another bound", write.Hint);
        PostgresException nullBound = await Assert.ThrowsExactlyAsync<PostgresException>(() => Execute(connection,
            "SELECT range_mappings.make(1001,9013,true,false)"));
        Assert.AreEqual("38000", nullBound.SqlState);
        Assert.AreEqual("A finite range bound writer cannot return SQL NULL.", nullBound.MessageText);
        Assert.AreEqual("1003|1008|True|False", await Scalar<string>(connection, "SELECT range_mappings.read('[3,8)')"));
        Assert.AreEqual("[5,11)", await Scalar<string>(connection, "SELECT range_mappings.make(1005,1011,true,false)::text"));
        Assert.AreEqual(connection.ProcessID, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }

    /// <summary>
    /// Opens an independent backend for lazy-construction and session recovery evidence.
    /// </summary>
    private Task<NpgsqlConnection> Open() => PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);

    /// <summary>
    /// Returns one independently expected PostgreSQL value.
    /// </summary>
    private async Task<T> Scalar<T>(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return Assert.IsInstanceOfType<T>(await command.ExecuteScalarAsync(context.CancellationToken));
    }

    /// <summary>
    /// Completes a statement before checking observable diagnostics and same-session recovery.
    /// </summary>
    private async Task Execute(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(context.CancellationToken);
    }
}
