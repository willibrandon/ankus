using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies ordered, hypothetical and final-extra contracts against independent PostgreSQL oracles.
/// </summary>
public sealed partial class AggregateTests
{
    /// <summary>
    /// Native ordering direction, NULL placement and text collation determine the exact output array.
    /// </summary>
    /// <param name="order">The SQL direction and explicit NULL placement.</param>
    /// <param name="first">The first array element, represented as a SQL literal.</param>
    [TestMethod]
    [DataRow("ASC NULLS FIRST", "NULL")]
    [DataRow("ASC NULLS LAST", "'B'")]
    [DataRow("DESC NULLS FIRST", "NULL")]
    [DataRow("DESC NULLS LAST", "'é'")]
    public Task OrderedValuesUseNativeCollationDirectionAndNullPlacement(string order, string first)
        => Run(nameof(OrderedValuesUseNativeCollationDirectionAndNullPlacement), async (connection, transaction, token) =>
        {
            await using var command = new NpgsqlCommand($"""
                WITH input(v) AS (VALUES('a'),('é'),(NULL),('B'),('a')),
                actual AS (SELECT aggregate_values.ordered_text() WITHIN GROUP(ORDER BY v COLLATE "C" {order}) AS a,
                    array_agg(v ORDER BY v COLLATE "C" {order}) AS native FROM input)
                SELECT a=native AND cardinality(a)=5 AND a[1] IS NOT DISTINCT FROM {first}::text FROM actual
                """, connection, transaction);
            Assert.IsTrue(Assert.IsInstanceOfType<bool>(await command.ExecuteScalarAsync(token)));
            string expectedKey = await Scalar<string>(connection, transaction, $"""
                SELECT '0:' || 'text'::regtype::oid || ':' || 'pg_catalog.{(order.StartsWith("ASC", StringComparison.Ordinal) ? "<" : ">")}(text,text)'::regoperator::oid || ':' ||
                    '"C"'::regcollation::oid || ':{order.EndsWith("FIRST", StringComparison.Ordinal)}'
                """, token);
            Assert.AreSequenceEqual([expectedKey], await Scalar<string[]>(connection, transaction,
                "SELECT aggregate_values.aggregate_keys()", token));
            string expectedContext = await Scalar<string>(connection, transaction, """
                SELECT 'Aggregate:' || 'aggregate_values.ordered_text(text)'::regprocedure::oid || ':' || '"C"'::regcollation::oid || ':False'
                """, token);
            Assert.AreEqual(expectedContext, await Scalar<string>(connection, transaction, "SELECT aggregate_values.aggregate_context()", token));
            Assert.AreEqual(0, await Scalar<int>(connection, transaction,
                "SELECT cardinality(aggregate_values.ordered_text() WITHIN GROUP(ORDER BY v)) FROM (SELECT ''::text WHERE false) AS input(v)", token));
        });

    /// <summary>
    /// Two-key hypothetical ranking preserves NULL rows, direct argument ordering and exact key metadata.
    /// </summary>
    /// <param name="direct">The typed direct argument expressions.</param>
    /// <param name="order">The SQL ordering expressions.</param>
    /// <param name="expected">The independently expected hypothetical rank.</param>
    [TestMethod]
    [DataRow("'a',2", "text ASC NULLS LAST, number DESC NULLS FIRST", 3L)]
    [DataRow("NULL::text,2", "text ASC NULLS LAST, number DESC NULLS FIRST", 5L)]
    [DataRow("'a',NULL::integer", "text ASC NULLS LAST, number DESC NULLS FIRST", 1L)]
    [DataRow("'a',2", "text DESC NULLS FIRST, number ASC NULLS LAST", 4L)]
    public Task HypotheticalRankMatchesNativeMultipleKeysAndNullRows(string direct, string order, long expected)
        => Run(nameof(HypotheticalRankMatchesNativeMultipleKeysAndNullRows), async (connection, transaction, token) =>
        {
            await using var command = new NpgsqlCommand($"""
                SELECT aggregate_values.hypothetical_rank({direct}) WITHIN GROUP(ORDER BY {order}),
                    rank({direct}) WITHIN GROUP(ORDER BY {order})
                FROM (VALUES('a',1),('a',3),('b',2),(NULL,2),('a',NULL)) AS input(text,number)
                """, connection, transaction);
            await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(token))
            {
                Assert.IsTrue(await reader.ReadAsync(token));
                Assert.AreEqual(expected, reader.GetInt64(0));
                Assert.AreEqual(reader.GetInt64(1), reader.GetInt64(0));
                Assert.IsFalse(await reader.ReadAsync(token));
            }

            string[] keys = await Scalar<string[]>(connection, transaction, "SELECT aggregate_values.aggregate_keys()", token);
            bool textAscending = order.StartsWith("text ASC", StringComparison.Ordinal);
            bool numberDescending = order.Contains("number DESC", StringComparison.Ordinal);
            string[] expectedKeys = await Scalar<string[]>(connection, transaction, $"""
                SELECT ARRAY[
                    '0:' || 'text'::regtype::oid || ':' || 'pg_catalog.{(textAscending ? "<" : ">")}(text,text)'::regoperator::oid || ':' ||
                        '"default"'::regcollation::oid || ':{!textAscending}',
                    '1:' || 'integer'::regtype::oid || ':' || 'pg_catalog.{(numberDescending ? ">" : "<")}(integer,integer)'::regoperator::oid || ':0:{numberDescending}']
                """, token);
            Assert.AreSequenceEqual(expectedKeys, keys);
            Assert.AreEqual(1L, await Scalar<long>(connection, transaction, """
                SELECT aggregate_values.hypothetical_rank('a',1) WITHIN GROUP(ORDER BY text,number)
                FROM (SELECT ''::text,1 WHERE false) AS input(text,number)
                """, token));
        });

    /// <summary>
    /// PostgreSQL sends NULL dummy arguments to both ordinary and moving finals.
    /// </summary>
    [TestMethod]
    public Task FinalExtraArgumentsRemainNullInOrdinaryAndMovingExecution()
        => Run(nameof(FinalExtraArgumentsRemainNullInOrdinaryAndMovingExecution), async (connection, transaction, token) =>
        {
            Assert.AreEqual(60, await Scalar<int>(connection, transaction,
                "SELECT aggregate_values.extra_sum(v) FROM (VALUES(10),(20),(30)) AS input(v)", token));
            Assert.AreEqual(0, await Scalar<int>(connection, transaction,
                "SELECT aggregate_values.extra_sum(v) FROM (SELECT 1 WHERE false) AS input(v)", token));
            int[] expected = [10, 30, 50];
            Assert.AreSequenceEqual(expected, await Scalar<int[]>(connection, transaction, """
                SELECT array_agg(total ORDER BY v) FROM (
                    SELECT v, aggregate_values.extra_sum(v) OVER(ORDER BY v ROWS 1 PRECEDING) AS total
                    FROM (VALUES(10),(20),(30)) AS input(v)) AS output
                """, token));
        });

    /// <summary>
    /// Native comparator errors, worker calls and nested aggregates cannot corrupt the active callback context.
    /// </summary>
    [TestMethod]
    public Task ComparisonErrorsAndNestedScopesRestoreTheActiveAggregate()
        => Run(nameof(ComparisonErrorsAndNestedScopesRestoreTheActiveAggregate), async (connection, transaction, token) =>
        {
            Assert.AreEqual("3:42804:protected:11:P7820:-1:42", await Scalar<string>(connection, transaction, """
                SELECT aggregate_values.comparison_probe() WITHIN GROUP(ORDER BY value)
                FROM (VALUES(7),(3)) AS input(value)
                """, token));
            Assert.AreEqual(42, await Scalar<int>(connection, transaction, "SELECT 42", token));
        });

    /// <summary>
    /// A foreign internal pointer is rejected before the native bridge reads an Ankus ownership header.
    /// </summary>
    [TestMethod]
    public Task ForeignInternalStateFailsSafelyAndRecovers()
        => Run(nameof(ForeignInternalStateFailsSafelyAndRecovers), async (connection, transaction, token) =>
        {
            await Reset(connection, transaction, "normal", token);
            await Execute(connection, transaction, """
                CREATE AGGREGATE aggregate_values.foreign_state(bigint) (
                    SFUNC=pg_catalog.int8_avg_accum, STYPE=internal,
                    FINALFUNC=aggregate_values.managed_sum_final)
                """, token);
            await transaction.SaveAsync("foreign_state", token);
            PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => Execute(connection, transaction,
                "SELECT aggregate_values.foreign_state(v) FROM (VALUES(1::bigint),(2)) AS input(v)", token));
            Assert.AreEqual("55000", error.SqlState);
            Assert.AreEqual("Invalid or expired Ankus aggregate state", error.MessageText);
            await transaction.RollbackAsync("foreign_state", token);
            Assert.AreEqual(0, (await Status(connection, transaction, token))[5]);
            Assert.AreEqual(42L, await Scalar<long>(connection, transaction,
                "SELECT aggregate_values.managed_sum(v) FROM (VALUES(20),(22)) AS input(v)", token));
            await AssertReleased(connection, transaction, 1, token);
        });

    /// <summary>
    /// The SQL internal type does not permit one Ankus callback to read another managed payload's concrete type.
    /// </summary>
    [TestMethod]
    public Task WrongManagedPayloadTypeFailsAndReleasesTheActualOwner()
        => Run(nameof(WrongManagedPayloadTypeFailsAndReleasesTheActualOwner), async (connection, transaction, token) =>
        {
            await Reset(connection, transaction, "normal", token);
            await Execute(connection, transaction, """
                CREATE AGGREGATE aggregate_values.wrong_payload(integer) (
                    SFUNC=aggregate_values.managed_sum_transition, STYPE=internal,
                    FINALFUNC=aggregate_values.parallel_sum_final)
                """, token);
            await transaction.SaveAsync("payload_type", token);
            PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => Execute(connection, transaction,
                "SELECT aggregate_values.wrong_payload(v) FROM (VALUES(1),(2)) AS input(v)", token));
            Assert.AreEqual("38000", error.SqlState);
            Assert.AreEqual("The aggregate state has a different managed payload type.", error.MessageText);
            await transaction.RollbackAsync("payload_type", token);
            await AssertReleased(connection, transaction, 1, token);
            await Reset(connection, transaction, "normal", token);
            Assert.AreEqual(42L, await Scalar<long>(connection, transaction,
                "SELECT aggregate_values.managed_sum(v) FROM (VALUES(20),(22)) AS input(v)", token));
            await AssertReleased(connection, transaction, 1, token);
        });

    /// <summary>
    /// Direct volatile expressions are evaluated once per group, including an empty ungrouped aggregate.
    /// </summary>
    [TestMethod]
    public Task OrderedDirectArgumentsEvaluateOncePerGroupIncludingEmptyInput()
        => Run(nameof(OrderedDirectArgumentsEvaluateOncePerGroupIncludingEmptyInput), async (connection, transaction, token) =>
        {
            await Execute(connection, transaction, "CREATE SEQUENCE aggregate_values.direct_evaluations", token);
            Assert.AreEqual(3L, await Scalar<long>(connection, transaction, """
                SELECT sum(rank)::bigint FROM (
                    SELECT g,aggregate_values.hypothetical_rank(nextval('aggregate_values.direct_evaluations')::text,1)
                        WITHIN GROUP(ORDER BY text,number) AS rank
                    FROM (VALUES(1,'a',1),(1,'b',2),(2,'c',3),(3,'d',4)) AS input(g,text,number)
                    GROUP BY g) AS groups
                """, token));
            Assert.AreEqual(3L, await Scalar<long>(connection, transaction,
                "SELECT last_value FROM aggregate_values.direct_evaluations", token));
            Assert.AreEqual(1L, await Scalar<long>(connection, transaction, """
                SELECT aggregate_values.hypothetical_rank(nextval('aggregate_values.direct_evaluations')::text,1)
                    WITHIN GROUP(ORDER BY text,number)
                FROM (SELECT ''::text,1 WHERE false) AS input(text,number)
                """, token));
            Assert.AreEqual(4L, await Scalar<long>(connection, transaction,
                "SELECT last_value FROM aggregate_values.direct_evaluations", token));
        });

}
