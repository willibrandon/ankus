using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies direct native value errors cannot be swallowed before actual rollback.
/// </summary>
/// <param name="context">The test's cancellation and diagnostic context.</param>
[TestClass]
public sealed class ValueOperationRecoveryTests(TestContext context)
{
    /// <summary>
    /// Enumerates each independent native family, nesting boundary and replacement attempt.
    /// </summary>
    /// <returns>The concrete failure and recovery combinations.</returns>
    public static IEnumerable<(string Family, int Mode, bool Replace)> FailureCases()
    {
        string[] families = ["numeric", "temporal", "network", "geometry", "range"];
        bool[] replacements = [false, true];
        foreach (string family in families)
        {
            for (int mode = 0; mode <= 3; mode++)
            {
                foreach (bool replace in replacements)
                {
                    yield return (family, mode, replace);
                }
            }
        }
    }

    /// <summary>
    /// Preserves the original native diagnostics, managed unwind and caller writes through swallowing and explicit recovery.
    /// </summary>
    /// <param name="family">The native value family.</param>
    /// <param name="mode">The direct, nested-session or deliberate recovery boundary.</param>
    /// <param name="replace">Whether user code throws an unrelated replacement exception.</param>
    [TestMethod]
    [DynamicData(nameof(FailureCases))]
    public async Task DirectValueErrorsRequireRollback(string family, int mode, bool replace)
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        int backend = connection.ProcessID;
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token);
        await using var command = new NpgsqlCommand("CREATE TEMP TABLE value_recovery_writes(value integer); INSERT INTO value_recovery_writes VALUES (42)", connection, transaction);
        await command.ExecuteNonQueryAsync(token);
        await transaction.SaveAsync("reference_error", token);
        command.CommandText = family switch
        {
            "numeric" => "SELECT 'not a number'::numeric",
            "temporal" => "SELECT 'not a date'::date",
            "network" => "SELECT '256.0.0.1'::inet",
            "geometry" => "SELECT 'not a point'::point",
            "range" => "SELECT '[5,2)'::int4range",
            _ => throw new ArgumentOutOfRangeException(nameof(family)),
        };
        PostgresException expected = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(token));
        await transaction.RollbackAsync("reference_error", token);
        await transaction.SaveAsync("managed_error", token);
        command.CommandText = "SELECT datatype.value_operation_error_caught($1,$2,$3)";
        command.Parameters.AddWithValue(family);
        command.Parameters.AddWithValue(mode);
        command.Parameters.AddWithValue(replace);
        if (mode == 3)
        {
            Assert.AreEqual(42, await command.ExecuteScalarAsync(token));
        }
        else
        {
            PostgresException actual = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(token));
            Assert.AreEqual(expected.SqlState, actual.SqlState);
            Assert.AreEqual(expected.MessageText, actual.MessageText);
            Assert.AreEqual(expected.Detail, actual.Detail);
            Assert.AreEqual(expected.Hint, actual.Hint);
            Assert.AreEqual(expected.SchemaName, actual.SchemaName);
            Assert.AreEqual(expected.TableName, actual.TableName);
            Assert.AreEqual(expected.ColumnName, actual.ColumnName);
            Assert.AreEqual(expected.DataTypeName, actual.DataTypeName);
            Assert.AreEqual(expected.ConstraintName, actual.ConstraintName);
            Assert.AreEqual(expected.File, actual.File);
            Assert.AreEqual(expected.Line, actual.Line);
            Assert.AreEqual(expected.Routine, actual.Routine);
            await transaction.RollbackAsync("managed_error", token);
        }

        command.Parameters.Clear();
        command.CommandText = "SELECT datatype.unrecovered_error_observations()";
        Assert.AreEqual(mode == 3 ? 15 : 7, await command.ExecuteScalarAsync(token));
        command.CommandText = "INSERT INTO value_recovery_writes VALUES (84); SELECT sum(value) FROM value_recovery_writes";
        Assert.AreEqual(126L, await command.ExecuteScalarAsync(token));
        command.CommandText = "SELECT 42, pg_backend_pid()";
        await using NpgsqlDataReader recovered = await command.ExecuteReaderAsync(token);
        Assert.IsTrue(await recovered.ReadAsync(token));
        Assert.AreEqual(42, recovered.GetInt32(0));
        Assert.AreEqual(backend, recovered.GetInt32(1));
        Assert.IsFalse(await recovered.ReadAsync(token));
    }
}
