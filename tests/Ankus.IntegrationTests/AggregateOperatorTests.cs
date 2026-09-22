using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies custom ordering support, guarded comparator errors, and same-callback recovery.
/// </summary>
public sealed partial class AggregateTests
{
    /// <summary>
    /// A user-defined B-tree comparator changes the actual result independently of CLR or default SQL ordering.
    /// </summary>
    /// <param name="operation">The ordering operator selecting ascending or descending absolute values.</param>
    /// <param name="expected">The independently specified order.</param>
    [TestMethod]
    [DataRow("<^", "1,-2,3,-4")]
    [DataRow(">^", "-4,3,-2,1")]
    public Task CustomOrderingOperatorControlsNativeComparison(string operation, string expected)
        => Run(nameof(CustomOrderingOperatorControlsNativeComparison), async (connection, transaction, token) =>
        {
            await CreateAbsoluteOrdering(connection, transaction, token);
            Assert.AreEqual(expected, await Scalar<string>(connection, transaction, $"""
                SELECT aggregate_values.operator_order(false) WITHIN GROUP(ORDER BY v USING OPERATOR(aggregate_values.{operation}))
                FROM (VALUES(3),(-4),(1),(-2)) AS input(v)
                """, token));
            Assert.AreEqual(expected, await Scalar<string>(connection, transaction, $"""
                SELECT array_to_string(array_agg(v ORDER BY v USING OPERATOR(aggregate_values.{operation})),',')
                FROM (VALUES(3),(-4),(1),(-2)) AS input(v)
                """, token));
        });

    /// <summary>
    /// An ERROR raised by the actual ordering support function is caught in managed code and leaves comparison and SPI usable.
    /// </summary>
    [TestMethod]
    public Task NativeComparatorErrorCanBeCaughtInsideAggregateAndRecovered()
        => Run(nameof(NativeComparatorErrorCanBeCaughtInsideAggregateAndRecovered), async (connection, transaction, token) =>
        {
            await CreateAbsoluteOrdering(connection, transaction, token);
            Assert.AreEqual("P7823:custom aggregate comparison failed:operator detail:-1:42",
                await Scalar<string>(connection, transaction, """
                    SELECT aggregate_values.operator_order(true) WITHIN GROUP(ORDER BY v USING OPERATOR(aggregate_values.<^))
                    FROM (VALUES(13),(1)) AS input(v)
                    """, token));
            await transaction.SaveAsync("operator_error", token);
            PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => Execute(connection, transaction, """
                SELECT aggregate_values.operator_order(false) WITHIN GROUP(ORDER BY v USING OPERATOR(aggregate_values.<^))
                FROM (VALUES(13),(1)) AS input(v)
                """, token));
            Assert.AreEqual("P7823", error.SqlState);
            Assert.AreEqual("custom aggregate comparison failed", error.MessageText);
            Assert.AreEqual("operator detail", error.Detail);
            await transaction.RollbackAsync("operator_error", token);
            Assert.AreEqual("1,-2", await Scalar<string>(connection, transaction, """
                SELECT aggregate_values.operator_order(false) WITHIN GROUP(ORDER BY v USING OPERATOR(aggregate_values.<^))
                FROM (VALUES(-2),(1)) AS input(v)
                """, token));
        });

    private static Task<int> CreateAbsoluteOrdering(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken token)
        => Execute(connection, transaction, """
            CREATE FUNCTION aggregate_values.absolute_compare(a integer,b integer) RETURNS integer LANGUAGE plpgsql IMMUTABLE AS $body$
            BEGIN
                IF a=13 OR b=13 THEN
                    RAISE EXCEPTION 'custom aggregate comparison failed' USING ERRCODE='P7823',DETAIL='operator detail';
                END IF;
                RETURN CASE WHEN abs(a)<abs(b) THEN -1 WHEN abs(a)>abs(b) THEN 1 ELSE 0 END;
            END $body$;
            CREATE FUNCTION aggregate_values.absolute_lt(integer,integer) RETURNS boolean LANGUAGE sql IMMUTABLE AS 'SELECT abs($1)<abs($2)';
            CREATE FUNCTION aggregate_values.absolute_le(integer,integer) RETURNS boolean LANGUAGE sql IMMUTABLE AS 'SELECT abs($1)<=abs($2)';
            CREATE FUNCTION aggregate_values.absolute_eq(integer,integer) RETURNS boolean LANGUAGE sql IMMUTABLE AS 'SELECT abs($1)=abs($2)';
            CREATE FUNCTION aggregate_values.absolute_ge(integer,integer) RETURNS boolean LANGUAGE sql IMMUTABLE AS 'SELECT abs($1)>=abs($2)';
            CREATE FUNCTION aggregate_values.absolute_gt(integer,integer) RETURNS boolean LANGUAGE sql IMMUTABLE AS 'SELECT abs($1)>abs($2)';
            CREATE OPERATOR aggregate_values.<^ (LEFTARG=integer,RIGHTARG=integer,FUNCTION=aggregate_values.absolute_lt);
            CREATE OPERATOR aggregate_values.<=^ (LEFTARG=integer,RIGHTARG=integer,FUNCTION=aggregate_values.absolute_le);
            CREATE OPERATOR aggregate_values.=^ (LEFTARG=integer,RIGHTARG=integer,FUNCTION=aggregate_values.absolute_eq);
            CREATE OPERATOR aggregate_values.>=^ (LEFTARG=integer,RIGHTARG=integer,FUNCTION=aggregate_values.absolute_ge);
            CREATE OPERATOR aggregate_values.>^ (LEFTARG=integer,RIGHTARG=integer,FUNCTION=aggregate_values.absolute_gt);
            CREATE OPERATOR CLASS aggregate_values.absolute_ordering FOR TYPE integer USING btree AS
                OPERATOR 1 aggregate_values.<^,
                OPERATOR 2 aggregate_values.<=^,
                OPERATOR 3 aggregate_values.=^,
                OPERATOR 4 aggregate_values.>=^,
                OPERATOR 5 aggregate_values.>^,
                FUNCTION 1 aggregate_values.absolute_compare(integer,integer);
            """, token);
}
