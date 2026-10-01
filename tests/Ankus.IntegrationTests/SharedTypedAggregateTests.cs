using Npgsql;

namespace Ankus.IntegrationTests;

public sealed partial class AggregateTests
{
    /// <summary>
    /// Published inherited generic helpers share exact closed signatures, preserve distinct state and recover after rejected direct calls.
    /// </summary>
    [TestMethod]
    public Task TypedAggregateInheritedHelpersPreserveClosedIdentityAndRecovery()
        => Run(nameof(TypedAggregateInheritedHelpersPreserveClosedIdentityAndRecovery), async (connection, transaction, token) =>
        {
            int backend = await Scalar<int>(connection, transaction, "SELECT pg_backend_pid()", token);
            await using (var command = new NpgsqlCommand("""
                SELECT aggregate_values.typed_shared_first(v), aggregate_values.typed_shared_second(v),
                    aggregate_values.typed_shared_long(v), sum(v)
                FROM (VALUES(2),(3),(5)) AS input(v)
                """, connection, transaction))
            {
                await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
                Assert.IsTrue(await reader.ReadAsync(token));
                Assert.AreEqual(10, reader.GetInt32(0));
                Assert.AreEqual(17, reader.GetInt32(1));
                Assert.AreEqual(10L, reader.GetInt64(2));
                Assert.AreEqual(reader.GetInt64(3), reader.GetInt64(2));
                Assert.IsFalse(await reader.ReadAsync(token));
            }

            Assert.AreEqual(2L, await Scalar<long>(connection, transaction, """
                SELECT count(*) FROM pg_proc AS functions
                JOIN pg_namespace AS schemas ON schemas.oid = functions.pronamespace
                WHERE schemas.nspname = 'aggregate_values' AND functions.proname = 'typed_shared_step'
                """, token));
            await transaction.SaveAsync("direct_shared", token);
            PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => Scalar<int>(connection, transaction,
                "SELECT aggregate_values.typed_shared_step(0,1)", token));
            Assert.AreEqual("55000", error.SqlState);
            await transaction.RollbackAsync("direct_shared", token);
            Assert.AreEqual(backend, await Scalar<int>(connection, transaction, "SELECT pg_backend_pid()", token));
            Assert.AreEqual(42, await Scalar<int>(connection, transaction,
                "SELECT aggregate_values.typed_shared_first(v) FROM (VALUES(19),(23)) AS input(v)", token));
            Assert.AreEqual(49, await Scalar<int>(connection, transaction,
                "SELECT aggregate_values.typed_shared_second(v) FROM (VALUES(19),(23)) AS input(v)", token));
            Assert.AreEqual(42L, await Scalar<long>(connection, transaction,
                "SELECT aggregate_values.typed_shared_long(v) FROM (VALUES(19),(23)) AS input(v)", token));
        });
}
