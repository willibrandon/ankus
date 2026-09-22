using System.Buffers.Binary;
using System.Globalization;
using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>Verifies numeric contracts against independent PostgreSQL casts and backend recovery.</summary>
/// <param name="context">The per-test context.</param>
[TestClass]
public sealed class NumericContractTests(TestContext context)
{
    /// <summary>Checks input and output coercion against PostgreSQL's numeric typmod, including rounding carry.</summary>
    /// <param name="input">The unconstrained numeric text.</param>
    [TestMethod]
    [DataRow("0")]
    [DataRow("1.2300000")]
    [DataRow("1.234")]
    [DataRow("1.235")]
    [DataRow("-1.235")]
    [DataRow("999.994")]
    [DataRow("-999.994")]
    [DataRow("0.000000000000000000000000000000001")]
    [DataRow("8.0000000000000000000000000001")]
    [DataRow("NaN")]
    public Task NumericBoundariesMatchPostgresTypmods(string input)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(NumericBoundariesMatchPostgresTypmods),
            async (connection, transaction, token) =>
            {
                await using var command = new NpgsqlCommand("""
                    SELECT ($1::numeric(5,2))::text, datatype.constrained_numeric_input($1::numeric),
                        datatype.constrained_numeric_output($1::numeric)::text,
                        numeric_send($1::numeric(5,2)), numeric_send(datatype.constrained_numeric_output($1::numeric))
                    """, connection, transaction);
                command.Parameters.AddWithValue(input);
                await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
                Assert.IsTrue(await reader.ReadAsync(token));
                Assert.AreEqual(reader.GetString(0), reader.GetString(1));
                Assert.AreEqual(reader.GetString(0), reader.GetString(2));
                Assert.AreSequenceEqual(reader.GetFieldValue<byte[]>(3), reader.GetFieldValue<byte[]>(4));
            }, context.CancellationToken);

    /// <summary>Coercion precedes checked decimal conversion, and return constraints run after managed decimal creation.</summary>
    [TestMethod]
    public Task DecimalConstraintsAndNullableValuesKeepTheirContracts()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(DecimalConstraintsAndNullableValuesKeepTheirContracts),
            async (connection, transaction, token) =>
            {
                await using var command = new NpgsqlCommand("""
                    SELECT datatype.constrained_decimal_input(8.0000000000000000000000000001),
                        datatype.constrained_decimal_output(1.235)::text,
                        datatype.constrained_numeric_input(NULL), datatype.constrained_decimal_input(NULL),
                        datatype.constrained_numeric_output(NULL), datatype.constrained_decimal_output(NULL),
                        datatype.constrained_whole_input(12.5), datatype.constrained_whole_input(NULL),
                        datatype.constrained_product(1.235, 2.35)::text,
                        ((1.235::numeric(5,2)) * (2.35::numeric(4,1)))::numeric(6,3)::text
                    """, connection, transaction);
                await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
                Assert.IsTrue(await reader.ReadAsync(token));
                Assert.AreEqual("8.00", reader.GetString(0));
                Assert.AreEqual("1.24", reader.GetString(1));
                Assert.AreEqual("managed null", reader.GetString(2));
                Assert.AreEqual("managed null", reader.GetString(3));
                Assert.IsTrue(reader.IsDBNull(4));
                Assert.IsTrue(reader.IsDBNull(5));
                Assert.AreEqual("13", reader.GetString(6));
                Assert.IsTrue(reader.IsDBNull(7));
                Assert.AreEqual("2.976", reader.GetString(8));
                Assert.AreEqual(reader.GetString(9), reader.GetString(8));
            }, context.CancellationToken);

    /// <summary>Checks negative scales and scales above precision, including away-from-zero ties and zero padding.</summary>
    /// <param name="function">The constrained boundary.</param>
    /// <param name="input">The numeric input.</param>
    /// <param name="declaration">The native numeric type modifier.</param>
    [TestMethod]
    [DataRow("constrained_negative_scale", "98500", "2,-3")]
    [DataRow("constrained_negative_scale", "-98500", "2,-3")]
    [DataRow("constrained_negative_scale", "999", "2,-3")]
    [DataRow("constrained_fractional_scale", "0.009994", "3,5")]
    [DataRow("constrained_fractional_scale", "-0.000005", "3,5")]
    [DataRow("constrained_fractional_scale", "0", "3,5")]
    public Task ExtendedScalesMatchPostgres(string function, string input, string declaration)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(ExtendedScalesMatchPostgres),
            async (connection, transaction, token) =>
            {
                await using var command = new NpgsqlCommand($"""
                    SELECT numeric_send($1::numeric({declaration})), numeric_send(datatype.{function}($1::numeric))
                    """, connection, transaction);
                command.Parameters.AddWithValue(input);
                await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
                Assert.IsTrue(await reader.ReadAsync(token));
                Assert.AreSequenceEqual(reader.GetFieldValue<byte[]>(0), reader.GetFieldValue<byte[]>(1));
            }, context.CancellationToken);

    /// <summary>Rejects values outside a declared constraint, including overflow introduced by rounding.</summary>
    /// <param name="function">The constrained boundary.</param>
    /// <param name="input">The out-of-range input.</param>
    [TestMethod]
    [DataRow("constrained_numeric_input", "999.995")]
    [DataRow("constrained_numeric_output", "-999.995")]
    [DataRow("constrained_numeric_input", "Infinity")]
    [DataRow("constrained_numeric_output", "-Infinity")]
    [DataRow("constrained_decimal_input", "1000")]
    [DataRow("constrained_decimal_output", "999.995")]
    [DataRow("constrained_negative_scale", "99500")]
    [DataRow("constrained_fractional_scale", "0.009995")]
    public Task ConstraintOverflowLeavesBackendUsable(string function, string input)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(ConstraintOverflowLeavesBackendUsable),
            async (connection, transaction, token) =>
            {
                await transaction.SaveAsync("constraint_error", token);
                await using var command = new NpgsqlCommand($"SELECT datatype.{function}($1::numeric)", connection, transaction);
                command.Parameters.AddWithValue(input);
                PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(token));
                Assert.AreEqual("22003", error.SqlState);
                Assert.Contains("numeric", error.MessageText);
                await transaction.RollbackAsync("constraint_error", token);
                command.Parameters.Clear();
                command.CommandText = "SELECT datatype.constrained_numeric_input(1.235)";
                Assert.AreEqual("1.24", await command.ExecuteScalarAsync(token));
            }, context.CancellationToken);

    /// <summary>Checks rounded integer casts, float precision, signed zero, subnormal values and special values.</summary>
    /// <param name="input">The numeric to cast.</param>
    /// <param name="type">The PostgreSQL primitive name.</param>
    [TestMethod]
    [DataRow("1.5", "int2")]
    [DataRow("-1.5", "int2")]
    [DataRow("32767.49", "int2")]
    [DataRow("-32768.49", "int2")]
    [DataRow("2147483647.49", "int4")]
    [DataRow("-2147483648.49", "int4")]
    [DataRow("9223372036854775807.49", "int8")]
    [DataRow("-9223372036854775808.49", "int8")]
    [DataRow("1.234567890123456789", "float4")]
    [DataRow("1e-40", "float4")]
    [DataRow("0", "float4")]
    [DataRow("-0", "float4")]
    [DataRow("NaN", "float4")]
    [DataRow("Infinity", "float4")]
    [DataRow("-Infinity", "float4")]
    [DataRow("1.234567890123456789", "float8")]
    public Task PrimitiveCastsMatchServer(string input, string type)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(PrimitiveCastsMatchServer),
            async (connection, transaction, token) =>
            {
                string expected = type.StartsWith("float", StringComparison.Ordinal)
                    ? $"{type}send($1::numeric::{type})" : $"($1::numeric::{type})::text";
                await using var command = new NpgsqlCommand($"SELECT {expected}, datatype.numeric_primitive_cast($1::numeric, '{type}')",
                    connection, transaction);
                command.Parameters.AddWithValue(input);
                await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
                Assert.IsTrue(await reader.ReadAsync(token));
                string expectedText = type switch
                {
                    "float4" => BinaryPrimitives.ReadInt32BigEndian(reader.GetFieldValue<byte[]>(0)).ToString(CultureInfo.InvariantCulture),
                    "float8" => BinaryPrimitives.ReadInt64BigEndian(reader.GetFieldValue<byte[]>(0)).ToString(CultureInfo.InvariantCulture),
                    _ => reader.GetString(0),
                };
                Assert.AreEqual(expectedText, reader.GetString(1));
            }, context.CancellationToken);

    /// <summary>Float4 input conversion must use float4_numeric, rather than widening and using float8 precision.</summary>
    /// <param name="input">The single-precision input text.</param>
    [TestMethod]
    [DataRow("1.23456789")]
    [DataRow("3.4028235e38")]
    [DataRow("1e-40")]
    [DataRow("-0")]
    [DataRow("NaN")]
    [DataRow("Infinity")]
    [DataRow("-Infinity")]
    public Task SinglePrecisionInputUsesServerPrecision(string input)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(SinglePrecisionInputUsesServerPrecision),
            async (connection, transaction, token) =>
            {
                await using var command = new NpgsqlCommand("SELECT numeric_send($1::real::numeric), numeric_send(datatype.numeric_from_single($1::real))",
                    connection, transaction);
                command.Parameters.AddWithValue(input);
                await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
                Assert.IsTrue(await reader.ReadAsync(token));
                Assert.AreSequenceEqual(reader.GetFieldValue<byte[]>(0), reader.GetFieldValue<byte[]>(1));
            }, context.CancellationToken);

    /// <summary>Checks static generic integer conversion reaches all primitive widths in Native AOT.</summary>
    /// <param name="kind">The target type.</param>
    /// <param name="input">The exact input outside narrower primitive ranges.</param>
    [TestMethod]
    [DataRow("sbyte", "-128")]
    [DataRow("byte", "255")]
    [DataRow("short", "-32768")]
    [DataRow("ushort", "65535")]
    [DataRow("int", "-2147483648")]
    [DataRow("uint", "4294967295")]
    [DataRow("long", "-9223372036854775808")]
    [DataRow("ulong", "18446744073709551615")]
    [DataRow("nint", "-42")]
    [DataRow("nuint", "42")]
    [DataRow("Int128", "-170141183460469231731687303715884105728")]
    [DataRow("UInt128", "340282366920938463463374607431768211455")]
    [DataRow("BigInteger", "1234567890123456789012345678901234567890123456789012345678901234567890")]
    public Task GenericIntegersExecuteInNativeAot(string kind, string input)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(GenericIntegersExecuteInNativeAot),
            async (connection, transaction, token) =>
            {
                await using var command = new NpgsqlCommand("SELECT datatype.numeric_generic_integer($1::numeric, $2)::text", connection, transaction);
                command.Parameters.AddWithValue(input);
                command.Parameters.AddWithValue(kind);
                Assert.AreEqual(input, await command.ExecuteScalarAsync(token));
            }, context.CancellationToken);

    /// <summary>Checks native and generated boundary failures preserve writes, plans and managed unwinding without memory growth.</summary>
    /// <param name="kind">The failing route.</param>
    /// <param name="expected">SQLSTATE, invocation/finally counts, context growth, plan result and surviving sum.</param>
    [TestMethod]
    [DataRow("input", "22003:0:0:50:0:42:3")]
    [DataRow("output", "22003:50:50:50:0:42:3")]
    [DataRow("integer", "22003:0:0:50:0:42:3")]
    [DataRow("single", "22003:0:0:50:0:42:3")]
    [DataRow("generic", "38000:0:0:50:0:42:3")]
    public Task NumericContractFailuresPreserveSession(string kind, string expected)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(NumericContractFailuresPreserveSession),
            async (connection, transaction, token) =>
            {
                await using var command = new NpgsqlCommand("SELECT datatype.numeric_contract_recovery($1)", connection, transaction);
                command.Parameters.AddWithValue(kind);
                Assert.AreEqual(expected, await command.ExecuteScalarAsync(token));
            }, context.CancellationToken);

    /// <summary>Checks generic interfaces and mixed primitive operators retain exact arithmetic and display scale.</summary>
    [TestMethod]
    public Task GenericArithmeticPreservesScale()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(GenericArithmeticPreservesScale),
            async (connection, transaction, token) =>
            {
                await using var command = new NpgsqlCommand("""
                    SELECT datatype.numeric_generic_math(2.10, 3.0)::text,
                        (2.10 * 3.0 + 1.2300 + (2.10 + 2) * 3 - 4 + (5 - 3.0))::text
                    """, connection, transaction);
                await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
                Assert.IsTrue(await reader.ReadAsync(token));
                Assert.AreEqual("17.8300", reader.GetString(0));
                Assert.AreEqual(reader.GetString(1), reader.GetString(0));
            }, context.CancellationToken);
}
