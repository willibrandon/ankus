using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies portable formatting against native numeric output and binary ownership after rollback.
/// </summary>
/// <param name="context">The current test context.</param>
[TestClass]
public sealed class NumericBinaryTests(TestContext context)
{
    /// <summary>
    /// Compares native binary output with independent protocol vectors and managed formatting with numeric_out.
    /// </summary>
    /// <param name="text">The canonical input.</param>
    /// <param name="hex">The independent portable binary vector.</param>
    [TestMethod]
    [DataRow("0", "0000000000000000")]
    [DataRow("0.0000", "0000000000000004")]
    [DataRow("1", "00010000000000000001")]
    [DataRow("-42", "0001000040000000002a")]
    [DataRow("9999", "0001000000000000270f")]
    [DataRow("10000", "00010001000000000001")]
    [DataRow("100000000", "00010002000000000001")]
    [DataRow("123.4500", "0002000000000004007b1194")]
    [DataRow("-123.4500", "0002000040000004007b1194")]
    [DataRow("12345.67890", "0003000100000005000109291a85")]
    [DataRow("1.00001", "00030000000000050001000003e8")]
    [DataRow("0.1", "0001ffff0000000103e8")]
    [DataRow("0.0001", "0001ffff000000040001")]
    [DataRow("0.00001", "0001fffe0000000503e8")]
    [DataRow("0.00000001", "0001fffe000000080001")]
    [DataRow("-0.00001", "0001fffe4000000503e8")]
    [DataRow("NaN", "00000000c0000000")]
    [DataRow("Infinity", "00000000d0000020")]
    [DataRow("-Infinity", "00000000f0000020")]
    public Task PortableFormattingMatchesNativeNumeric(string text, string hex)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(PortableFormattingMatchesNativeNumeric),
            async (connection, transaction, token) =>
            {
                await using var command = new NpgsqlCommand("""
                    SELECT encode(numeric_send($1::numeric), 'hex'), $1::numeric::text,
                        datatype.numeric_binary_format($1::numeric), pg_backend_pid()
                    """, connection, transaction);
                command.Parameters.AddWithValue(text);
                int backend;
                if (PostgresFixture.Cluster.Installation.Version.Major < 14 && text is "Infinity" or "-Infinity")
                {
                    command.CommandText = "SELECT pg_backend_pid()";
                    command.Parameters.Clear();
                    backend = Assert.IsInstanceOfType<int>(await command.ExecuteScalarAsync(token));
                    await transaction.SaveAsync("numeric_special", token);
                    command.CommandText = "SELECT datatype.numeric_binary_format($1::numeric)";
                    command.Parameters.AddWithValue(text);
                    PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(token));
                    Assert.AreEqual("22P02", error.SqlState);
                    await transaction.RollbackAsync("numeric_special", token);
                    command.Parameters.Clear();
                    command.CommandText = "SELECT 42, pg_backend_pid()";
                    await using NpgsqlDataReader rejected = await command.ExecuteReaderAsync(token);
                    Assert.IsTrue(await rejected.ReadAsync(token));
                    Assert.AreEqual(42, rejected.GetInt32(0));
                    Assert.AreEqual(backend, rejected.GetInt32(1));
                    return;
                }

                await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(token))
                {
                    Assert.IsTrue(await reader.ReadAsync(token));
                    Assert.AreEqual(hex, reader.GetString(0));
                    Assert.AreEqual(text, reader.GetString(1));
                    Assert.AreEqual(text, reader.GetString(2));
                    backend = reader.GetInt32(3);
                }

                command.Parameters.Clear();
                command.CommandText = "SELECT 42, pg_backend_pid()";
                await using NpgsqlDataReader recovered = await command.ExecuteReaderAsync(token);
                Assert.IsTrue(await recovered.ReadAsync(token));
                Assert.AreEqual(42, recovered.GetInt32(0));
                Assert.AreEqual(backend, recovered.GetInt32(1));
            }, context.CancellationToken);

    /// <summary>
    /// Retains detached values through SPI teardown and division rollback before formatting and repeated arithmetic.
    /// </summary>
    /// <param name="left">The first finite operand.</param>
    /// <param name="right">The second finite operand.</param>
    [TestMethod]
    [DataRow("0.0000", "1.2300")]
    [DataRow("-123456789012345678901234567890.123456789", "0.00000001")]
    [DataRow("100000000", "-10000")]
    public Task OpaqueNumericSurvivesNativeCleanupAndRollback(string left, string right)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(OpaqueNumericSurvivesNativeCleanupAndRollback),
            async (connection, transaction, token) =>
            {
                await using var command = new NpgsqlCommand("""
                    SELECT '22012:' || ($1::numeric + $2::numeric)::text || ':' ||
                        (($1::numeric + $2::numeric + $1::numeric) * $2::numeric)::text,
                        datatype.numeric_binary_reuse($1::numeric, $2::numeric), pg_backend_pid()
                    """, connection, transaction);
                command.Parameters.AddWithValue(left);
                command.Parameters.AddWithValue(right);
                int backend;
                await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(token))
                {
                    Assert.IsTrue(await reader.ReadAsync(token));
                    Assert.AreEqual(reader.GetString(0), reader.GetString(1));
                    backend = reader.GetInt32(2);
                }

                command.Parameters.Clear();
                command.CommandText = "SELECT 42, pg_backend_pid()";
                await using NpgsqlDataReader recovered = await command.ExecuteReaderAsync(token);
                Assert.IsTrue(await recovered.ReadAsync(token));
                Assert.AreEqual(42, recovered.GetInt32(0));
                Assert.AreEqual(backend, recovered.GetInt32(1));
            }, context.CancellationToken);
}
