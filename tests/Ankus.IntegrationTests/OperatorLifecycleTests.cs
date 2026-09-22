using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Executes the relocatable operator/cast sample and verifies extension ownership throughout its lifecycle.
/// </summary>
/// <param name="context">The current test context.</param>
[TestClass]
public sealed class OperatorLifecycleTests(TestContext context)
{
    /// <summary>
    /// Operator shells, functions, types and casts install together, survive relocation and disappear on drop.
    /// </summary>
    [TestMethod]
    public Task OperatorCastSampleRelocatesAndReinstalls()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(OperatorCastSampleRelocatesAndReinstalls), async (connection, transaction, token) =>
        {
            await using var command = new NpgsqlCommand("""
                CREATE SCHEMA operator_first;
                CREATE SCHEMA operator_second;
                CREATE SCHEMA operator_shadow;
                CREATE TYPE operator_shadow.priority AS ENUM ('Low','Normal','High');
                CREATE EXTENSION ankus_operators WITH SCHEMA operator_first;
                SET LOCAL search_path = operator_shadow, pg_catalog;
                SELECT 'High'::operator_first.priority::int,
                    'High'::operator_first.priority OPERATOR(operator_first.===) 'High'::operator_first.priority,
                    'Low'::operator_first.priority OPERATOR(operator_first.!==) 'Normal'::operator_first.priority
                """, connection, transaction);
            await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(token))
            {
                Assert.IsTrue(await reader.ReadAsync(token));
                Assert.AreEqual(30, reader.GetInt32(0));
                Assert.IsTrue(reader.GetBoolean(1));
                Assert.IsTrue(reader.GetBoolean(2));
            }

            command.CommandText = """
                WITH expected(classid, objid) AS (
                    VALUES ('pg_type'::regclass, 'operator_first.priority'::regtype::oid),
                        ('pg_proc'::regclass, 'operator_first.equal(operator_first.priority,operator_first.priority)'::regprocedure::oid),
                        ('pg_proc'::regclass, 'operator_first.not_equal(operator_first.priority,operator_first.priority)'::regprocedure::oid),
                        ('pg_proc'::regclass, 'operator_first.score(operator_first.priority)'::regprocedure::oid),
                        ('pg_operator'::regclass, 'operator_first.===(operator_first.priority,operator_first.priority)'::regoperator::oid),
                        ('pg_operator'::regclass, 'operator_first.!==(operator_first.priority,operator_first.priority)'::regoperator::oid)
                    UNION ALL SELECT 'pg_cast'::regclass, oid FROM pg_cast
                        WHERE castsource = 'operator_first.priority'::regtype AND casttarget = 'integer'::regtype)
                SELECT count(*) FROM expected x JOIN pg_depend d ON d.classid = x.classid AND d.objid = x.objid
                    JOIN pg_extension e ON d.refclassid = 'pg_extension'::regclass AND d.refobjid = e.oid
                    WHERE d.deptype = 'e' AND e.extname = 'ankus_operators'
                """;
            Assert.AreEqual(7L, await command.ExecuteScalarAsync(token));

            command.CommandText = """
                ALTER EXTENSION ankus_operators SET SCHEMA operator_second;
                SELECT 'Low'::operator_second.priority::int,
                    'Low'::operator_second.priority OPERATOR(operator_second.===) 'High'::operator_second.priority,
                    'operator_second.priority'::regtype::oid
                """;
            uint movedType;
            await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(token))
            {
                Assert.IsTrue(await reader.ReadAsync(token));
                Assert.AreEqual(10, reader.GetInt32(0));
                Assert.IsFalse(reader.GetBoolean(1));
                movedType = reader.GetFieldValue<uint>(2);
            }

            command.CommandText = "DROP EXTENSION ankus_operators";
            await command.ExecuteNonQueryAsync(token);
            command.CommandText = """
                SELECT to_regtype('operator_second.priority') IS NULL
                    AND NOT EXISTS (SELECT 1 FROM pg_operator WHERE oprnamespace = 'operator_second'::regnamespace)
                    AND NOT EXISTS (SELECT 1 FROM pg_proc WHERE pronamespace = 'operator_second'::regnamespace)
                    AND NOT EXISTS (SELECT 1 FROM pg_cast WHERE castsource = $1)
                """;
            command.Parameters.Add(new NpgsqlParameter<uint> { TypedValue = movedType, NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Oid });
            Assert.IsTrue(Assert.IsInstanceOfType<bool>(await command.ExecuteScalarAsync(token)));
            command.Parameters.Clear();

            command.CommandText = """
                CREATE EXTENSION ankus_operators WITH SCHEMA operator_first;
                SELECT 'operator_first.priority'::regtype::oid,
                    'Normal'::operator_first.priority::int,
                    'Low'::operator_first.priority OPERATOR(operator_first.!==) 'High'::operator_first.priority
                """;
            await using NpgsqlDataReader reinstalled = await command.ExecuteReaderAsync(token);
            Assert.IsTrue(await reinstalled.ReadAsync(token));
            Assert.AreNotEqual(movedType, reinstalled.GetFieldValue<uint>(0));
            Assert.AreEqual(20, reinstalled.GetInt32(1));
            Assert.IsTrue(reinstalled.GetBoolean(2));
        }, context.CancellationToken);
}
