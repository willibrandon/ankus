using Npgsql;

namespace Ankus.IntegrationTests;

public sealed partial class NodeTests
{
    /// <summary>
    /// Native list-family tags use List bounds and preserve signed and zero cells.
    /// </summary>
    [TestMethod]
    public Task NativeNodeFormattingUsesIntegerListLayout()
        => CheckAsync("SELECT datatype.node_format_integer_list()", "(i 11 -42 0)");

    /// <summary>
    /// Reset or deletion invalidates the original raw formatter even when the external bytes and a fresh view remain valid.
    /// </summary>
    /// <param name="delete">Whether the lifetime anchor is deleted rather than reset.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task NativeNodeFormattingRetainsRawAnchorGeneration(bool delete)
        => CheckAsync($"SELECT datatype.node_format_raw_lifetime({delete})",
            $"{{RANGETBLREF :rtindex 9}}|{(delete ? "deleted" : "{RANGETBLREF :rtindex 9}")}|True|9");

    /// <summary>
    /// A correctly aligned Node prefix cannot bypass the actual concrete node's stricter native alignment.
    /// </summary>
    [TestMethod]
    public async Task NativeNodeFormattingRejectsMisalignedConcreteRoot()
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        int backend = connection.ProcessID;
        await using var command = new NpgsqlCommand("SELECT datatype.node_format_misaligned()", connection);
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(token));
        Assert.AreEqual("22000", error.SqlState);
        Assert.AreEqual("the node storage does not satisfy its concrete tag layout", error.MessageText);
        command.CommandText = "SELECT datatype.node_format_interior(false)";
        Assert.AreEqual("{RANGETBLREF :rtindex 27}", await command.ExecuteScalarAsync(token));
        command.CommandText = "SELECT pg_backend_pid()";
        Assert.AreEqual(backend, await command.ExecuteScalarAsync(token));
    }

    /// <summary>
    /// Native allocation zeroes payload bytes, writes the tag and preserves formatted text after either ownership cleanup path.
    /// </summary>
    /// <param name="transfer">Whether individual ownership transfers to the context before formatting.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task NodeAllocationZeroesPayloadAndFormattedTextOutlivesOwner(bool transfer)
        => CheckAsync($"SELECT datatype.node_allocate_and_format({transfer})", "True|True|{RANGETBLREF :rtindex 9}|True");

    /// <summary>
    /// Both checked interior offsets and explicit raw root extents reach the same complete concrete node.
    /// </summary>
    /// <param name="raw">Whether the reference uses a raw extent.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task NativeNodeFormattingPreservesInteriorStorage(bool raw)
        => CheckAsync($"SELECT datatype.node_format_interior({raw})", "{RANGETBLREF :rtindex 27}");

    /// <summary>
    /// Nested pointer members are traversed by PostgreSQL and retain their exact fields and native spelling.
    /// </summary>
    [TestMethod]
    public Task NativeNodeFormattingTraversesNestedValues()
        => CheckAsync("SELECT datatype.node_format_nested(false)", "{COLLATEEXPR :arg {RANGETBLREF :rtindex 9} :collOid 123 :location -1}");

    /// <summary>
    /// Incomplete concrete roots fail before native traversal and leave the same backend usable.
    /// </summary>
    /// <param name="raw">Whether the incomplete bound comes from a raw extent or resized checked allocation.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task NativeNodeFormattingRejectsIncompleteRootsAndRecovers(bool raw)
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        int backend = connection.ProcessID;
        await using var command = new NpgsqlCommand($"SELECT datatype.node_format_incomplete({raw})", connection);
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(token));
        Assert.AreEqual("22000", error.SqlState);
        Assert.AreEqual("the node storage does not satisfy its concrete tag layout", error.MessageText);
        command.CommandText = "SELECT datatype.node_format_interior(false)";
        Assert.AreEqual("{RANGETBLREF :rtindex 27}", await command.ExecuteScalarAsync(token));
        command.CommandText = "SELECT pg_backend_pid()";
        Assert.AreEqual(backend, await command.ExecuteScalarAsync(token));
    }

    /// <summary>
    /// Unknown tags preserve PostgreSQL's WARNING and empty-object output rather than inventing an error.
    /// </summary>
    [TestMethod]
    public async Task NativeNodeFormattingPreservesUnknownTagWarning()
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        var notices = new List<PostgresNotice>();
        connection.Notice += (_, args) => notices.Add(args.Notice);
        await using var command = new NpgsqlCommand("SELECT datatype.node_format_unknown()", connection);
        Assert.AreEqual("{}", await command.ExecuteScalarAsync(token));
        PostgresNotice notice = Assert.ContainsSingle(notices);
        Assert.AreEqual("01000", notice.SqlState);
        Assert.AreEqual("could not dump unrecognized node type: -1", notice.MessageText);
        command.CommandText = "SELECT datatype.node_format_interior(false)";
        Assert.AreEqual("{RANGETBLREF :rtindex 27}", await command.ExecuteScalarAsync(token));
    }

    /// <summary>
    /// Repeated native recursion errors reclaim formatting contexts and preserve diagnostics and successful same-session traversal.
    /// </summary>
    [TestMethod]
    public async Task NativeNodeFormattingRecoversFromRecursiveErrorWithoutRetainingContexts()
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        int backend = connection.ProcessID;
        await using var command = new NpgsqlCommand("SET max_stack_depth = '100kB'", connection);
        await command.ExecuteNonQueryAsync(token);
        for (int iteration = 0; iteration < 8; iteration++)
        {
            command.CommandText = "SELECT datatype.node_format_nested(true)";
            PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(token));
            Assert.AreEqual("54001", error.SqlState);
            Assert.AreEqual("stack depth limit exceeded", error.MessageText);
            command.CommandText = "SELECT datatype.node_format_nested(false)";
            Assert.AreEqual("{COLLATEEXPR :arg {RANGETBLREF :rtindex 9} :collOid 123 :location -1}", await command.ExecuteScalarAsync(token));
            command.CommandText = "SELECT count(*) FROM pg_backend_memory_contexts WHERE name = 'Ankus node formatting' OR ident = 'nested native node'";
            Assert.AreEqual(0L, await command.ExecuteScalarAsync(token));
        }

        command.CommandText = "SELECT pg_backend_pid()";
        Assert.AreEqual(backend, await command.ExecuteScalarAsync(token));
    }

    /// <summary>
    /// Native names preserve accents and native token escaping in both UTF8 and LATIN1 databases.
    /// </summary>
    /// <param name="encoding">The server database encoding to exercise.</param>
    [TestMethod]
    [DataRow("UTF8")]
    [DataRow("LATIN1")]
    public async Task NativeNodeFormattingConvertsServerEncoding(string encoding)
    {
        CancellationToken token = context.CancellationToken;
        string database = "node_encoding_" + Guid.NewGuid().ToString("N");
        await using NpgsqlConnection administrator = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        await using (var create = new NpgsqlCommand(
            $"CREATE DATABASE {database} TEMPLATE template0 ENCODING '{encoding}' LC_COLLATE 'C' LC_CTYPE 'C'", administrator))
        {
            await create.ExecuteNonQueryAsync(token);
        }

        try
        {
            var builder = new NpgsqlConnectionStringBuilder(administrator.ConnectionString) { Database = database, Pooling = false };
            await using var connection = new NpgsqlConnection(builder.ConnectionString);
            await connection.OpenAsync(token);
            await using var command = new NpgsqlCommand("CREATE EXTENSION ankus_test", connection);
            await command.ExecuteNonQueryAsync(token);
            command.CommandText = "SELECT public.node_format_alias(convert_to('café name', current_setting('server_encoding')))";
            Assert.AreEqual("{ALIAS :aliasname café\\ name :colnames <>}", await command.ExecuteScalarAsync(token));
            command.CommandText = "SELECT public.node_format_alias(convert_to('', current_setting('server_encoding')))";
            Assert.AreEqual("{ALIAS :aliasname \"\" :colnames <>}", await command.ExecuteScalarAsync(token));
            command.CommandText = "SELECT public.node_format_alias(NULL)";
            Assert.AreEqual("{ALIAS :aliasname <> :colnames <>}", await command.ExecuteScalarAsync(token));
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE {database} WITH (FORCE)", administrator);
            await drop.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }
}
