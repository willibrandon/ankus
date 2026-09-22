using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies ordinary aggregate state identity, ownership, shape, and nested native scope restoration.
/// </summary>
public sealed partial class AggregateTests
{
    /// <summary>
    /// Enum labels retain their actual SQL type through nullable state and no-final output.
    /// </summary>
    /// <param name="label">The first nonnull label, distinct from the following row.</param>
    [TestMethod]
    [DataRow("Low")]
    [DataRow("")]
    [DataRow("a'b\\c")]
    public Task EnumStatePreservesLabelAndType(string label)
        => Run(nameof(EnumStatePreservesLabelAndType), async (connection, transaction, token) =>
        {
            await using var command = new NpgsqlCommand("""
                SELECT result::text,pg_typeof(result)::oid='datatype.enum_mood'::regtype::oid
                FROM (SELECT datatype.state_first_mood(v ORDER BY ord) AS result
                    FROM (VALUES(1,NULL::datatype.enum_mood),(2,@label::datatype.enum_mood),(3,'High'::datatype.enum_mood)) AS input(ord,v)) AS aggregate_result
                """, connection, transaction);
            command.Parameters.AddWithValue("label", label);
            await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(token))
            {
                Assert.IsTrue(await reader.ReadAsync(token));
                Assert.AreEqual(label, reader.GetString(0));
                Assert.IsTrue(reader.GetBoolean(1));
                Assert.IsFalse(await reader.ReadAsync(token));
            }

            Assert.IsTrue(await Scalar<bool>(connection, transaction, """
                SELECT datatype.state_first_mood(v) IS NULL FROM (VALUES(NULL::datatype.enum_mood),(NULL)) AS input(v)
                """, token));
            Assert.IsTrue(await Scalar<bool>(connection, transaction, """
                SELECT datatype.state_first_mood(v) IS NULL FROM (SELECT 'Low'::datatype.enum_mood WHERE false) AS input(v)
                """, token));
        });

    /// <summary>
    /// Shaped state preserves every dimension, bound, element and NULL after multiple transitions.
    /// </summary>
    [TestMethod]
    public Task ArrayStateRetainsShapeAndCells()
        => Run(nameof(ArrayStateRetainsShapeAndCells), async (connection, transaction, token) =>
        {
            Assert.AreEqual("[-2:-1][4:6]={{11,NULL,-7},{0,2147483647,-2147483648}}",
                await Scalar<string>(connection, transaction, """
                    SELECT datatype.state_first_array(v ORDER BY ord)::text
                    FROM (VALUES(1,NULL::integer[]),
                        (2,'[-2:-1][4:6]={{11,NULL,-7},{0,2147483647,-2147483648}}'::integer[]),
                        (3,ARRAY[99]),(4,ARRAY[]::integer[]),(5,NULL::integer[])) AS input(ord,v)
                    """, token));
            Assert.AreEqual("{}", await Scalar<string>(connection, transaction, """
                SELECT datatype.state_first_array(v ORDER BY ord)::text
                FROM (VALUES(1,ARRAY[]::integer[]),(2,ARRAY[1])) AS input(ord,v)
                """, token));
            Assert.IsTrue(await Scalar<bool>(connection, transaction, """
                SELECT datatype.state_first_array(v) IS NULL FROM (VALUES(NULL::integer[]),(NULL)) AS input(v)
                """, token));
            Assert.IsTrue(await Scalar<bool>(connection, transaction, """
                SELECT datatype.state_first_array(v) IS NULL FROM (SELECT ARRAY[1] WHERE false) AS input(v)
                """, token));
        });

    /// <summary>
    /// A named composite retains TOAST-sized text, nullable cells and type identity without a final function.
    /// </summary>
    [TestMethod]
    public Task CompositeStateRetainsOwnedFieldsAndIdentity()
        => Run(nameof(CompositeStateRetainsOwnedFieldsAndIdentity), async (connection, transaction, token) =>
        {
            await using (var command = new NpgsqlCommand("""
                SELECT (result).name,(result).age,pg_typeof(result)::oid='tuple_values.dog'::regtype::oid
                FROM (SELECT datatype.state_first_dog(v ORDER BY ord) AS result
                    FROM (VALUES(1,NULL::tuple_values.dog),(2,ROW(repeat('café',10000),NULL)::tuple_values.dog),
                        (3,ROW('later',7)::tuple_values.dog)) AS input(ord,v)) AS aggregate_result
                """, connection, transaction))
            {
                await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
                Assert.IsTrue(await reader.ReadAsync(token));
                Assert.AreEqual(string.Concat(Enumerable.Repeat("café", 10000)), reader.GetString(0));
                Assert.IsTrue(reader.IsDBNull(1));
                Assert.IsTrue(reader.GetBoolean(2));
                Assert.IsFalse(await reader.ReadAsync(token));
            }

            Assert.AreEqual("(,)", await Scalar<string>(connection, transaction, """
                SELECT datatype.state_first_dog(v ORDER BY ord)::text
                FROM (VALUES(1,ROW(NULL,NULL)::tuple_values.dog),(2,ROW('later',7)::tuple_values.dog)) AS input(ord,v)
                """, token));
            Assert.IsTrue(await Scalar<bool>(connection, transaction, """
                SELECT datatype.state_first_dog(v)::text IS NULL FROM (VALUES(NULL::tuple_values.dog),(NULL)) AS input(v)
                """, token));
            Assert.IsTrue(await Scalar<bool>(connection, transaction, """
                SELECT datatype.state_first_dog(v)::text IS NULL FROM (SELECT ROW('ignored',1)::tuple_values.dog WHERE false) AS input(v)
                """, token));
        });

    /// <summary>
    /// Domain state preserves catalog identity and rejects a constraint violation during native result conversion.
    /// </summary>
    [TestMethod]
    public Task CompositeDomainStateEnforcesIdentityAndConstraints()
        => Run(nameof(CompositeDomainStateEnforcesIdentityAndConstraints), async (connection, transaction, token) =>
        {
            Assert.IsTrue(await Scalar<bool>(connection, transaction, """
                SELECT pg_typeof(result)::oid='tuple_values.dog_domain'::regtype::oid
                    AND result::text='(first,7)'
                FROM (SELECT datatype.state_first_dog_domain(v ORDER BY ord) AS result
                    FROM (VALUES(1,NULL::tuple_values.dog_domain),(2,ROW('first',7)::tuple_values.dog_domain),
                        (3,ROW('later',8)::tuple_values.dog_domain)) AS input(ord,v)) AS output
                """, token));
            await transaction.SaveAsync("domain_state", token);
            PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => Execute(connection, transaction, """
                SELECT datatype.state_first_dog_domain(v ORDER BY ord)
                FROM (VALUES(1,ROW('first',7)::tuple_values.dog_domain),(2,ROW('invalid',13)::tuple_values.dog_domain)) AS input(ord,v)
                """, token));
            Assert.AreEqual("23514", error.SqlState);
            Assert.AreEqual("dog_domain_check", error.ConstraintName);
            await transaction.RollbackAsync("domain_state", token);
            Assert.AreEqual("(recovered,9)", await Scalar<string>(connection, transaction, """
                SELECT datatype.state_first_dog_domain(v)::text FROM (VALUES(ROW('recovered',9)::tuple_values.dog_domain)) AS input(v)
                """, token));
        });

    /// <summary>
    /// Typed NULL composite operands use the native record comparator and each invocation's NULL ordering.
    /// </summary>
    /// <param name="order">The SQL ordering and NULL placement.</param>
    /// <param name="expected">The independently specified ordered row values.</param>
    [TestMethod]
    [DataRow("ASC NULLS FIRST", new[] { "NULL", "a:2", "a:NULL", "b:1" })]
    [DataRow("ASC NULLS LAST", new[] { "a:2", "a:NULL", "b:1", "NULL" })]
    [DataRow("DESC NULLS FIRST", new[] { "NULL", "b:1", "a:NULL", "a:2" })]
    [DataRow("DESC NULLS LAST", new[] { "b:1", "a:NULL", "a:2", "NULL" })]
    public Task NamedCompositeNullOperandsUseNativeOrdering(string order, string[] expected)
        => Run(nameof(NamedCompositeNullOperandsUseNativeOrdering), async (connection, transaction, token) =>
        {
            Assert.AreSequenceEqual(expected, await Scalar<string[]>(connection, transaction, $"""
                SELECT datatype.state_ordered_dogs() WITHIN GROUP(ORDER BY v {order})
                FROM (VALUES(ROW('b',1)::tuple_values.dog),(NULL::tuple_values.dog),
                    (ROW('a',NULL)::tuple_values.dog),(ROW('a',2)::tuple_values.dog)) AS input(v)
                """, token));
        });

    /// <summary>
    /// The aggregate's native function OID is active only during its callback, including on a nested error.
    /// </summary>
    /// <param name="fail">Whether the nested aggregate raises an error.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task NestedAggregateRestoresFunctionSchema(bool fail)
        => Run(nameof(NestedAggregateRestoresFunctionSchema), async (connection, transaction, token) =>
        {
            await Execute(connection, transaction, """
                CREATE SCHEMA outer_scope;
                CREATE SCHEMA aggregate_scope;
                CREATE TYPE outer_scope.enum_mood AS ENUM('Low');
                CREATE TYPE aggregate_scope.enum_mood AS ENUM('Low');
                DO $body$ DECLARE p pg_proc; BEGIN
                    SELECT * INTO p FROM pg_proc WHERE oid='datatype.aggregate_scope_witness(boolean)'::regprocedure;
                    EXECUTE format('CREATE FUNCTION outer_scope.invoke(boolean) RETURNS text AS %L,%L LANGUAGE c',p.probin,p.prosrc);
                    SELECT * INTO p FROM pg_proc WHERE oid=(SELECT aggtransfn FROM pg_aggregate
                        WHERE aggfnoid='datatype.state_enum_scope(integer)'::regprocedure);
                    EXECUTE format('CREATE FUNCTION aggregate_scope.step(integer,integer) RETURNS integer AS %L,%L LANGUAGE c',p.probin,p.prosrc);
                END $body$;
                CREATE AGGREGATE aggregate_scope.probe(integer)(SFUNC=aggregate_scope.step,STYPE=integer,INITCOND='0');
                """, token);
            uint outerOid = await Scalar<uint>(connection, transaction, "SELECT 'outer_scope.enum_mood'::regtype::oid", token);
            uint aggregateOid = await Scalar<uint>(connection, transaction, "SELECT 'aggregate_scope.enum_mood'::regtype::oid", token);
            Assert.AreNotEqual(outerOid, aggregateOid);
            string outcome = fail ? "P7821" : "3";
            Assert.AreEqual($"{outerOid}:{aggregateOid}:{outerOid}:{outcome}", await Scalar<string>(connection, transaction,
                $"SELECT outer_scope.invoke({(fail ? "true" : "false")})", token));
            Assert.AreEqual($"{outerOid}:{aggregateOid}:{outerOid}:3", await Scalar<string>(connection, transaction,
                "SELECT outer_scope.invoke(false)", token));
        });
}
