using System.Text.Json;
using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies operators and casts using the published library, catalog contracts and PostgreSQL planner.
/// </summary>
/// <param name="context">The current test context.</param>
[TestClass]
[DoNotParallelize]
public sealed class OperatorCastTests(TestContext context)
{
    /// <summary>
    /// Operators preserve argument order, SQL widths, prefix syntax and nullable dispatch.
    /// </summary>
    [TestMethod]
    [DataRow("7 OPERATOR(operator_values.#-) 23::bigint", "6977")]
    [DataRow("23::bigint OPERATOR(operator_values.-#) 7", "6977")]
    [DataRow("7 OPERATOR(operator_values.#-) 5000000000::bigint", "-4999993000")]
    [DataRow("OPERATOR(operator_values.~#) 13", "-23")]
    [DataRow("NULL::int OPERATOR(operator_values.#-) 23::bigint", null)]
    [DataRow("NULL::int OPERATOR(operator_values.?#) 2", "102")]
    [DataRow("3 OPERATOR(operator_values.?#) NULL::int", "203")]
    [DataRow("NULL::int OPERATOR(operator_values.?#) NULL::int", null)]
    [DataRow("4 OPERATOR(operator_values.@=) 4", "true")]
    [DataRow("4 OPERATOR(operator_values.@=) 5", "false")]
    [DataRow("4 OPERATOR(operator_values.@<>) 5", "true")]
    public Task OperatorsExecuteTheirDeclaredSignatures(string expression, string? expected)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(OperatorsExecuteTheirDeclaredSignatures), async (connection, transaction, token) =>
        {
            await using var command = new NpgsqlCommand($"SET LOCAL search_path = pg_catalog; SELECT ({expression})::text", connection, transaction);
            Assert.AreEqual((object?)expected ?? DBNull.Value, await command.ExecuteScalarAsync(token));
        }, context.CancellationToken);

    /// <summary>
    /// Catalog entries retain function options, planner promises and filled reciprocal operator references.
    /// </summary>
    [TestMethod]
    public Task OperatorCatalogRetainsOptionsAndFillsShells()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(OperatorCatalogRetainsOptionsAndFillsShells), async (connection, transaction, token) =>
        {
            await using var command = new NpgsqlCommand("""
                SELECT mixed.oprcode = 'operator_values.mixed_difference(int,bigint)'::regprocedure,
                    mixed.oprleft = 'integer'::regtype, mixed.oprright = 'bigint'::regtype,
                    mixed.oprresult = 'bigint'::regtype,
                    mixed.oprcom = 'operator_values.-#(bigint,integer)'::regoperator,
                    commuted.oprcom = mixed.oid, commuted.oprcode <> 0,
                    prefix.oprkind::text, prefix.oprleft::oid, prefix.oprright = 'integer'::regtype,
                    equal.oprcom = equal.oid, equal.oprnegate = unequal.oid,
                    unequal.oprnegate = equal.oid, unequal.oprcode <> 0,
                    equal.oprrest = 'pg_catalog.eqsel(internal,oid,internal,integer)'::regprocedure,
                    equal.oprjoin = 'pg_catalog.eqjoinsel(internal,oid,internal,smallint,internal)'::regprocedure,
                    equal.oprcanhash, equal.oprcanmerge,
                    proc.provolatile::text, proc.proparallel::text, proc.proisstrict, proc.procost,
                    (SELECT count(*) FROM pg_depend WHERE classid = 'pg_operator'::regclass
                     AND objid = mixed.oid AND refclassid = 'pg_extension'::regclass AND deptype = 'e')
                FROM pg_operator mixed
                JOIN pg_operator commuted ON commuted.oid = mixed.oprcom
                CROSS JOIN pg_operator prefix
                CROSS JOIN pg_operator equal
                JOIN pg_operator unequal ON unequal.oid = equal.oprnegate
                JOIN pg_proc proc ON proc.oid = mixed.oprcode
                WHERE mixed.oid = 'operator_values.#-(integer,bigint)'::regoperator
                  AND prefix.oid = 'operator_values.~#(NONE,integer)'::regoperator
                  AND equal.oid = 'operator_values.@=(integer,integer)'::regoperator
                """, connection, transaction);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
            Assert.IsTrue(await reader.ReadAsync(token));
            for (int column = 0; column <= 6; column++)
            {
                Assert.IsTrue(reader.GetBoolean(column), $"Mixed operator catalog column {column}.");
            }

            Assert.AreEqual("l", reader.GetString(7));
            Assert.AreEqual(0U, reader.GetFieldValue<uint>(8));
            for (int column = 9; column <= 17; column++)
            {
                Assert.IsTrue(reader.GetBoolean(column), $"Unary or boolean operator catalog column {column}.");
            }

            Assert.AreEqual("i", reader.GetString(18));
            Assert.AreEqual("s", reader.GetString(19));
            Assert.IsTrue(reader.GetBoolean(20));
            Assert.AreEqual(2.5f, reader.GetFloat(21));
            Assert.AreEqual(1L, reader.GetInt64(22));
            Assert.IsFalse(await reader.ReadAsync(token));
        }, context.CancellationToken);

    /// <summary>
    /// Shaped enum arrays survive operator input, nested SPI and array-return conversion.
    /// </summary>
    [TestMethod]
    [DataRow("'[0:1][-3:-2]={{First,NULL},{Second,First}}'")]
    [DataRow("'{}'")]
    [DataRow("NULL")]
    public Task OperatorsPreserveEnumArrayIdentityAndBounds(string literal)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(OperatorsPreserveEnumArrayIdentityAndBounds), async (connection, transaction, token) =>
        {
            for (int mode = 0; mode <= 7; mode++)
            {
                await using var command = new NpgsqlCommand($"""
                    SELECT array_send(({literal})::operator_values.explicit_token[] OPERATOR(operator_values.||#) {mode})
                        IS NOT DISTINCT FROM array_send(({literal})::operator_values.explicit_token[])
                    """, connection, transaction);
                Assert.IsTrue(Assert.IsInstanceOfType<bool>(await command.ExecuteScalarAsync(token)), $"SPI mode {mode}.");
            }
        }, context.CancellationToken);

    /// <summary>
    /// Cast invocation proves source mapping, NULL handling and all PostgreSQL cast-function arguments.
    /// </summary>
    [TestMethod]
    [DataRow("'First'::operator_values.explicit_token::int", "7")]
    [DataRow("CAST('Second'::operator_values.explicit_token AS integer)", "19")]
    [DataRow("NULL::operator_values.explicit_token::int", "101")]
    [DataRow("'Empty'::operator_values.explicit_token::int", null)]
    [DataRow("'Value'::operator_values.assignment_token::int", "23")]
    [DataRow("1 + 'Value'::operator_values.implicit_token", "38")]
    [DataRow("'Value'::operator_values.assignment_token::bigint", "2299")]
    [DataRow("'Value'::operator_values.metadata_token::numeric", "-9")]
    [DataRow("'Value'::operator_values.metadata_token::numeric(3,1)", "1966131")]
    [DataRow("'[0:1][-3:-2]={{First,NULL},{Second,First}}'::operator_values.explicit_token[]::bigint[]", "[0:1][-3:-2]={{107,NULL},{119,107}}")]
    [DataRow("NULL::operator_values.explicit_token[]::bigint[]", null)]
    public Task CastsExecuteTheDeclaredConversion(string expression, string? expected)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(CastsExecuteTheDeclaredConversion), async (connection, transaction, token) =>
        {
            await using var command = new NpgsqlCommand($"SET LOCAL search_path = pg_catalog; SELECT ({expression})::text", connection, transaction);
            Assert.AreEqual((object?)expected ?? DBNull.Value, await command.ExecuteScalarAsync(token));
        }, context.CancellationToken);

    /// <summary>
    /// Assignment accepts assignment and implicit casts, while ordinary function resolution accepts only implicit ones.
    /// </summary>
    [TestMethod]
    public Task CastContextsControlAssignmentAndFunctionResolution()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(CastContextsControlAssignmentAndFunctionResolution), async (connection, transaction, token) =>
        {
            await using var command = new NpgsqlCommand("""
                CREATE TEMP TABLE cast_assignment(value integer);
                INSERT INTO cast_assignment VALUES ('Value'::operator_values.assignment_token), ('Value'::operator_values.implicit_token);
                CREATE TEMP TABLE cast_typmod(value numeric(3,1));
                INSERT INTO cast_typmod VALUES ('Value'::operator_values.metadata_token);
                SELECT (SELECT array_agg(value ORDER BY value) FROM cast_assignment),
                    pg_catalog.int4abs('Value'::operator_values.implicit_token),
                    (SELECT value::text FROM cast_typmod)
                """, connection, transaction);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
            Assert.IsTrue(await reader.ReadAsync(token));
            Assert.AreSequenceEqual([23, 37], reader.GetFieldValue<int[]>(0));
            Assert.AreEqual(37, reader.GetInt32(1));
            Assert.AreEqual("1966130", reader.GetString(2));
        }, context.CancellationToken);

    /// <summary>
    /// Cast catalogs retain their source, target, backing function, argument count and exact conversion context.
    /// </summary>
    [TestMethod]
    public Task CastCatalogRetainsFunctionAndContext()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(CastCatalogRetainsFunctionAndContext), async (connection, transaction, token) =>
        {
            await using var command = new NpgsqlCommand("""
                SELECT conversion.castcontext::text, conversion.castmethod::text, conversion.castfunc = expected.function::regprocedure,
                    proc.pronargs, proc.proisstrict, proc.provolatile::text, proc.proparallel::text,
                    (SELECT count(*) FROM pg_depend WHERE classid = 'pg_cast'::regclass
                     AND objid = conversion.oid AND refclassid = 'pg_extension'::regclass AND deptype = 'e')
                FROM (VALUES
                    (1,'operator_values.explicit_token','integer','operator_values.cast_explicit(operator_values.explicit_token)'),
                    (2,'operator_values.assignment_token','integer','operator_values.cast_assignment(operator_values.assignment_token)'),
                    (3,'operator_values.implicit_token','integer','operator_values.cast_implicit(operator_values.implicit_token)'),
                    (4,'operator_values.assignment_token','bigint','operator_values.cast_two_arguments(operator_values.assignment_token,integer)'),
                    (5,'operator_values.metadata_token','numeric','operator_values.cast_metadata(operator_values.metadata_token,integer,boolean)'),
                    (6,'operator_values.explicit_token[]','bigint[]','operator_values.cast_array(operator_values.explicit_token[])')
                ) expected(ordinal, source, target, function)
                JOIN pg_cast conversion ON conversion.castsource = expected.source::regtype AND conversion.casttarget = expected.target::regtype
                JOIN pg_proc proc ON proc.oid = conversion.castfunc ORDER BY expected.ordinal
                """, connection, transaction);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
            string[] expectedContexts = ["e", "a", "i", "e", "a", "e"];
            short[] expectedArgumentCounts = [1, 1, 1, 2, 3, 1];
            for (int index = 0; index < expectedContexts.Length; index++)
            {
                Assert.IsTrue(await reader.ReadAsync(token), $"Cast {index} is missing.");
                Assert.AreEqual(expectedContexts[index], reader.GetString(0));
                Assert.AreEqual("f", reader.GetString(1));
                Assert.IsTrue(reader.GetBoolean(2));
                Assert.AreEqual(expectedArgumentCounts[index], reader.GetInt16(3));
                Assert.AreEqual(index is not (0 or 5), reader.GetBoolean(4));
                Assert.AreEqual(index == 0 ? "i" : "v", reader.GetString(5));
                Assert.AreEqual(index == 0 ? "s" : "u", reader.GetString(6));
                Assert.AreEqual(1L, reader.GetInt64(7));
            }

            Assert.IsFalse(await reader.ReadAsync(token));
        }, context.CancellationToken);

    /// <summary>
    /// Restricted coercion contexts and extension errors preserve the session after a savepoint rollback.
    /// </summary>
    [TestMethod]
    [DataRow("CREATE TEMP TABLE cast_rejected(value int); INSERT INTO cast_rejected VALUES ('First'::operator_values.explicit_token)", "42804")]
    [DataRow("SELECT pg_catalog.int4abs('First'::operator_values.explicit_token)", "42883")]
    [DataRow("SELECT pg_catalog.int4abs('Value'::operator_values.assignment_token)", "42883")]
    [DataRow("SELECT 1 OPERATOR(operator_values./#) 0", "38000")]
    [DataRow("SELECT 'Invalid'::operator_values.explicit_token::int", "22003")]
    public Task OperatorAndCastFailuresRecoverInTheSameSession(string sql, string state)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(OperatorAndCastFailuresRecoverInTheSameSession), async (connection, transaction, token) =>
        {
            await transaction.SaveAsync("operator_cast_failure", token);
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(token));
            Assert.AreEqual(state, error.SqlState);
            await transaction.RollbackAsync("operator_cast_failure", token);
            command.CommandText = "SELECT ('Second'::operator_values.explicit_token::int) OPERATOR(operator_values.#-) 3::bigint";
            Assert.AreEqual(18997L, await command.ExecuteScalarAsync(token));
        }, context.CancellationToken);

    /// <summary>
    /// Planner flags support real joins when compatible operator families are explicitly declared.
    /// </summary>
    [TestMethod]
    [DataRow("hash", "Hash Join")]
    [DataRow("merge", "Merge Join")]
    public Task DeclaredPlannerOptionsEnableCompatibleJoinPlans(string algorithm, string expectedPlan)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(DeclaredPlannerOptionsEnableCompatibleJoinPlans), async (connection, transaction, token) =>
        {
            await using var command = new NpgsqlCommand("""
                CREATE OPERATOR CLASS operator_values.hash_probe FOR TYPE integer USING hash AS
                    OPERATOR 1 operator_values.@=(integer,integer), FUNCTION 1 pg_catalog.hashint4(integer);
                CREATE OPERATOR CLASS operator_values.btree_probe FOR TYPE integer USING btree AS
                    OPERATOR 1 pg_catalog.<(integer,integer), OPERATOR 2 pg_catalog.<=(integer,integer),
                    OPERATOR 3 operator_values.@=(integer,integer), OPERATOR 4 pg_catalog.>=(integer,integer),
                    OPERATOR 5 pg_catalog.>(integer,integer), FUNCTION 1 pg_catalog.btint4cmp(integer,integer);
                CREATE TEMP TABLE operator_join_left(value int);
                CREATE TEMP TABLE operator_join_right(value int);
                INSERT INTO operator_join_left SELECT generate_series(1,100);
                INSERT INTO operator_join_right SELECT generate_series(50,150);
                ANALYZE operator_join_left;
                ANALYZE operator_join_right;
                SET LOCAL enable_nestloop = off;
                """, connection, transaction);
            await command.ExecuteNonQueryAsync(token);
            command.CommandText = algorithm == "hash"
                ? "SET LOCAL enable_hashjoin = on; SET LOCAL enable_mergejoin = off"
                : "SET LOCAL enable_hashjoin = off; SET LOCAL enable_mergejoin = on";
            await command.ExecuteNonQueryAsync(token);
            const string query = "SELECT l.value, r.value FROM operator_join_left l JOIN operator_join_right r ON l.value OPERATOR(operator_values.@=) r.value";
            command.CommandText = $"EXPLAIN (FORMAT JSON, COSTS OFF) {query}";
            string plan = Assert.IsInstanceOfType<string>(await command.ExecuteScalarAsync(token));
            using JsonDocument document = JsonDocument.Parse(plan);
            Assert.AreEqual(expectedPlan, document.RootElement[0].GetProperty("Plan").GetProperty("Node Type").GetString(), plan);

            command.CommandText = query;
            var values = new List<int>();
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
            {
                Assert.AreEqual(reader.GetInt32(0), reader.GetInt32(1));
                values.Add(reader.GetInt32(0));
            }

            values.Sort();
            Assert.AreSequenceEqual(Enumerable.Range(50, 51), values);
        }, context.CancellationToken);

    /// <summary>
    /// Repeated failures roll back their SPI writes and preserve plans, native contexts and managed finally blocks.
    /// </summary>
    [TestMethod]
    public Task OperatorsAndCastsRecoverInsideGuardedSpi()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(OperatorsAndCastsRecoverInsideGuardedSpi), async (connection, transaction, token) =>
        {
            await using var command = new NpgsqlCommand("SELECT operator_values.operator_cast_recovery()", connection, transaction);
            Assert.AreEqual("40:20:2:0", await command.ExecuteScalarAsync(token));
        }, context.CancellationToken);
}
