using Npgsql;

namespace Ankus.IntegrationTests;

public sealed partial class CStringTests
{
    /// <summary>
    /// Owned and borrowed results survive each result callback while preserving exact bytes and SQL NULL.
    /// </summary>
    [TestMethod]
    public async Task CStringSetResultsPreserveEachRowAndReleaseOwners()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.AreSequenceEqual<string?>(["80ff", null, "c3a9"], await Scalar<string?[]>(connection,
            "SELECT ARRAY(SELECT encode(cstrings.owned_bytes(cstrings.owned_results(cstrings.create_cstring('\\x80ff'::bytea))),'hex'))"));
        Assert.AreSequenceEqual<string?>(["80ff:80ff", null], await Scalar<string?[]>(connection, """
            SELECT ARRAY(SELECT encode(cstrings.owned_bytes(owned),'hex') || ':' || encode(cstrings.borrowed_bytes(borrowed),'hex')
                FROM cstrings.table_results(cstrings.create_cstring('\x80ff'::bytea)))
            """));
        int before = await Scalar<int>(connection, "SELECT cstrings.disposed_results()");
        Assert.AreSequenceEqual<string?>(["80ff", null, "80ff"], await Scalar<string?[]>(connection,
            "SELECT ARRAY(SELECT encode(cstrings.borrowed_bytes(cstrings.borrowed_results(cstrings.create_cstring('\\x80ff'::bytea),false)),'hex'))"));
        Assert.AreEqual(before + 1, await Scalar<int>(connection, "SELECT cstrings.disposed_results()"));
        await AssertCleanBackend(connection);
    }

    /// <summary>
    /// Early query termination and an iterator exception both dispose retained native inputs exactly once.
    /// </summary>
    /// <param name="fail">Whether to consume the failing second row instead of ending after the first.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CStringSetResultsCleanUpAfterEarlyExitOrError(bool fail)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        int before = await Scalar<int>(connection, "SELECT cstrings.disposed_results()");
        string rows = "SELECT encode(cstrings.owned_bytes(cstrings.borrowed_results(cstrings.create_cstring('\\xff'::bytea),true)),'hex')";
        await using var command = new NpgsqlCommand(fail ? $"SELECT ARRAY({rows})" : rows + " LIMIT 1", connection);
        if (fail)
        {
            PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(context.CancellationToken));
            Assert.AreEqual("38000", error.SqlState);
            Assert.AreEqual("C-string iterator failed.", error.MessageText);
        }
        else
        {
            Assert.AreEqual("ff", await command.ExecuteScalarAsync(context.CancellationToken));
        }

        Assert.AreEqual(before + 1, await Scalar<int>(connection, "SELECT cstrings.disposed_results()"));
        await AssertCleanBackend(connection);
    }

    /// <summary>
    /// Array vectors, shapes, finite view factories and provisional SPI cleanup execute through real PostgreSQL.
    /// </summary>
    [TestMethod]
    public async Task CStringArraysAndFailedSpiResultsPreserveContracts()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.AreSequenceEqual<string?>([null, "", "80FF", null, "", "80FF", "41", null, "", "80FF", "41",
            null, "", "80FF", "41", null, "", "80FF", "41"],
            await Scalar<string?[]>(connection, "SELECT cstrings.array_boundaries()"));
        Assert.AreSequenceEqual<string>(["InvalidCastException", "0"],
            await Scalar<string[]>(connection, "SELECT cstrings.conversion_failure()"));
        await AssertCleanBackend(connection);
    }
}
