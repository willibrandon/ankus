using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies Native AOT range transport, canonicalization, SQL operations and native error recovery.
/// </summary>
/// <param name="context">The current test context.</param>
[TestClass]
public sealed class RangeDatumTests(TestContext context)
{
    /// <summary>
    /// Every supported bound alias and array representation retains the backend binary value through eight ownership paths.
    /// </summary>
    [TestMethod]
    [DataRow("int", "int4range", "range_send", "'[1,9)'")]
    [DataRow("int", "int4range", "range_send", "'empty'")]
    [DataRow("int", "int4range", "range_send", "'(,)'")]
    [DataRow("int", "int4range", "range_send", "'(,2147483647)'")]
    [DataRow("int", "int4range", "range_send", "NULL")]
    [DataRow("long", "int8range", "range_send", "'[-9223372036854775808,9223372036854775807)'")]
    [DataRow("long", "int8range", "range_send", "NULL")]
    [DataRow("numeric", "numrange", "range_send", "'[1.2300,2.450)'")]
    [DataRow("numeric", "numrange", "range_send", "'[-Infinity,NaN]'")]
    [DataRow("numeric", "numrange", "range_send", "NULL")]
    [DataRow("date", "daterange", "range_send", "'[-infinity,infinity]'")]
    [DataRow("date", "daterange", "range_send", "'[4713-01-01 BC,5874897-12-31)'")]
    [DataRow("date", "daterange", "range_send", "NULL")]
    [DataRow("timestamp", "tsrange", "range_send", "'[2000-01-01 00:00:00.000001,294276-12-31 23:59:59.999999]'")]
    [DataRow("timestamp", "tsrange", "range_send", "NULL")]
    [DataRow("timestamp_tz", "tstzrange", "range_send", "'[2024-01-01 01:02:03+05:30,infinity]'")]
    [DataRow("timestamp_tz", "tstzrange", "range_send", "NULL")]
    [DataRow("decimal", "numrange", "range_send", "'[1.2300,79228162514264337593543950335]'")]
    [DataRow("date_only", "daterange", "range_send", "'[0001-01-01,9999-12-31)'")]
    [DataRow("date_time", "tsrange", "range_send", "'[0001-01-01,9999-12-31 23:59:59.999999]'")]
    [DataRow("date_time_offset", "tstzrange", "range_send", "'[2024-01-01 12:13:14+05:30,2024-02-01 01:02:03-07]'")]
    [DataRow("ints", "int4range[]", "array_send", "'[-2:-1][4:5]={{NULL,empty},{\"(,)\",\"[1,9)\"}}'")]
    [DataRow("ints", "int4range[]", "array_send", "'{}'")]
    [DataRow("ints", "int4range[]", "array_send", "NULL")]
    [DataRow("longs", "int8range[]", "array_send", "ARRAY['[-99,100)'::int8range,NULL,'empty']")]
    [DataRow("numerics", "numrange[]", "array_send", "ARRAY['[1.000,2.00]'::numrange,NULL,'[NaN,NaN]']")]
    [DataRow("dates", "daterange[]", "array_send", "ARRAY['[-infinity,infinity]'::daterange,NULL,'empty']")]
    [DataRow("timestamps", "tsrange[]", "array_send", "ARRAY['[4713-01-01 BC,infinity]'::tsrange,NULL,'empty']")]
    [DataRow("timestamp_tzs", "tstzrange[]", "array_send", "ARRAY['[2024-01-01+05:30,infinity]'::tstzrange,NULL,'empty']")]
    [DataRow("decimals", "numrange[]", "array_send", "ARRAY['[1.2300,2.450)'::numrange,NULL,'empty']")]
    [DataRow("date_onlys", "daterange[]", "array_send", "ARRAY['[2024-01-01,2024-01-02)'::daterange,NULL,'empty']")]
    [DataRow("date_times", "tsrange[]", "array_send", "ARRAY['[2024-01-01,2024-01-02)'::tsrange,NULL,'empty']")]
    [DataRow("date_time_offsets", "tstzrange[]", "array_send", "ARRAY['[2024-01-01 00:00+05:30,2024-01-02 00:00-07)'::tstzrange,NULL,'empty']")]
    public Task RangeOwnershipPathsPreserveBinaryValues(string function, string type, string send, string literal)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(RangeOwnershipPathsPreserveBinaryValues), async (connection, transaction, token) =>
        {
            for (int mode = 0; mode <= 7; mode++)
            {
                await using var command = new NpgsqlCommand($"SELECT {send}(datatype.range_{function}(({literal})::{type},{mode})) IS NOT DISTINCT FROM {send}(({literal})::{type})", connection, transaction);
                Assert.IsTrue(Assert.IsInstanceOfType<bool>(await command.ExecuteScalarAsync(token)), $"{function}, mode {mode}");
            }
        }, context.CancellationToken);

    /// <summary>
    /// PostgreSQL canonicalizes managed bounds, including equal ends, discrete successors and ignored unbounded inclusion.
    /// </summary>
    [TestMethod]
    [DataRow(1, 5, false, true, false, "[2,6)")]
    [DataRow(1, 5, true, false, false, "[1,5)")]
    [DataRow(3, 3, true, true, false, "[3,4)")]
    [DataRow(3, 3, true, false, false, "empty")]
    [DataRow(3, 3, false, false, false, "empty")]
    [DataRow(null, null, true, true, false, "(,)")]
    [DataRow(null, 5, true, true, false, "(,6)")]
    [DataRow(1, null, false, true, false, "[2,)")]
    [DataRow(5, 1, true, true, true, "empty")]
    public Task ManagedRangeConstructionCanonicalizes(int? lower, int? upper, bool lowerInclusive, bool upperInclusive, bool empty, string expected)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(ManagedRangeConstructionCanonicalizes), async (connection, transaction, token) =>
        {
            await using var command = new NpgsqlCommand("SELECT datatype.range_construct($1,$2,$3,$4,$5)::text", connection, transaction);
            command.Parameters.Add(new() { NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Integer, Value = (object?)lower ?? DBNull.Value });
            command.Parameters.Add(new() { NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Integer, Value = (object?)upper ?? DBNull.Value });
            command.Parameters.AddWithValue(lowerInclusive);
            command.Parameters.AddWithValue(upperInclusive);
            command.Parameters.AddWithValue(empty);
            Assert.AreEqual(expected, await command.ExecuteScalarAsync(token));
        }, context.CancellationToken);

    /// <summary>
    /// Managed properties distinguish SQL NULL, empty, finite-inclusive and unbounded ranges independently of serialization.
    /// </summary>
    [TestMethod]
    [DataRow(null, "null")]
    [DataRow("empty", "True:False:::False:False")]
    [DataRow("(,)", "False:True:::False:False")]
    [DataRow("(,5]", "False:False::6:False:False")]
    [DataRow("[1,)", "False:False:1::True:False")]
    [DataRow("[1,5]", "False:False:1:6:True:False")]
    public Task RangePropertiesExposeNativeState(string? text, string expected)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(RangePropertiesExposeNativeState), async (connection, transaction, token) =>
        {
            await using var command = new NpgsqlCommand("SELECT datatype.range_inspect($1::int4range)", connection, transaction);
            command.Parameters.Add(new() { NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text, Value = (object?)text ?? DBNull.Value });
            Assert.AreEqual(expected, await command.ExecuteScalarAsync(token));
        }, context.CancellationToken);

    /// <summary>
    /// Parsing/formatting respects subtype scale, discrete canonicalization, infinity and the session timezone and DateStyle.
    /// </summary>
    [TestMethod]
    [DataRow("int4range", "(1,5]")]
    [DataRow("int8range", "[-9223372036854775808,1]")]
    [DataRow("numrange", "[1.2300,2.450]")]
    [DataRow("daterange", "[2024-01-01,2024-01-03]")]
    [DataRow("tsrange", "[2001-01-01 01:02:03.123456,infinity]")]
    [DataRow("tstzrange", "[2024-01-01 12:00+05:30,2024-06-01 12:00-07]")]
    public Task RangeTextOperationsMatchPostgres(string type, string text)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(RangeTextOperationsMatchPostgres), async (connection, transaction, token) =>
        {
            await using var settings = new NpgsqlCommand("SET LOCAL TimeZone = 'America/New_York'; SET LOCAL DateStyle = 'German, DMY'", connection, transaction);
            await settings.ExecuteNonQueryAsync(token);
            await using var command = new NpgsqlCommand($"SELECT datatype.range_parse($1,$2) = ($2::{type})::text", connection, transaction);
            command.Parameters.AddWithValue(type);
            command.Parameters.AddWithValue(text);
            Assert.IsTrue(Assert.IsInstanceOfType<bool>(await command.ExecuteScalarAsync(token)));
        }, context.CancellationToken);

    /// <summary>
    /// All four range-returning operations use subtype-aware native semantics for each built-in range family.
    /// </summary>
    [TestMethod]
    [DataRow("int4range", "[1,10)", "[5,15)")]
    [DataRow("int8range", "[-1000000000000,10)", "[5,15)")]
    [DataRow("numrange", "[1.2300,10.100)", "[5.50,15.500)")]
    [DataRow("daterange", "[2000-01-01,2000-01-10)", "[2000-01-05,2000-01-15)")]
    [DataRow("tsrange", "[2000-01-01,2000-01-10)", "[2000-01-05,2000-01-15)")]
    [DataRow("tstzrange", "[2000-01-01 00:00+03,2000-01-10 00:00+03)", "[2000-01-05 00:00-04,2000-01-15 00:00-04)")]
    [DataRow("int4range", "empty", "[1,3)")]
    [DataRow("int4range", "[1,3)", "empty")]
    [DataRow("int4range", "empty", "empty")]
    public Task RangeSetOperationsMatchPostgres(string type, string left, string right)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(RangeSetOperationsMatchPostgres), async (connection, transaction, token) =>
        {
            for (int operation = 0; operation < 4; operation++)
            {
                string expression = operation switch { 0 => "$2::" + type + " + $3::" + type, 1 => "$2::" + type + " * $3::" + type, 2 => "$2::" + type + " - $3::" + type, _ => $"range_merge($2::{type},$3::{type})" };
                await using var command = new NpgsqlCommand($"SELECT datatype.range_operate($1,$2,$3,$4) = ({expression})::text", connection, transaction);
                command.Parameters.AddWithValue(type);
                command.Parameters.AddWithValue(left);
                command.Parameters.AddWithValue(right);
                command.Parameters.AddWithValue(operation);
                Assert.IsTrue(Assert.IsInstanceOfType<bool>(await command.ExecuteScalarAsync(token)), $"{type}, operation {operation}");
            }
        }, context.CancellationToken);

    /// <summary>
    /// Predicates distinguish endpoints, adjacency, overlap, empty containment and disjoint ranges.
    /// </summary>
    [TestMethod]
    [DataRow("[1,5)", "[5,9)", 5, false, false, false, true)]
    [DataRow("[1,5)", "[2,4)", 1, true, true, true, false)]
    [DataRow("[1,5)", "[4,9)", 0, false, false, true, false)]
    [DataRow("[1,5)", "empty", 4, true, true, false, false)]
    [DataRow("empty", "empty", 1, false, true, false, false)]
    [DataRow("(,)", "[1,5)", int.MaxValue, true, true, true, false)]
    public Task RangePredicatesRespectBoundaries(string left, string right, int element, bool containsElement, bool containsRange, bool overlaps, bool adjacent)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(RangePredicatesRespectBoundaries), async (connection, transaction, token) =>
        {
            await using var command = new NpgsqlCommand("SELECT datatype.range_predicates($1::int4range,$2::int4range,$3)", connection, transaction);
            command.Parameters.AddWithValue(left);
            command.Parameters.AddWithValue(right);
            command.Parameters.AddWithValue(element);
            Assert.AreSequenceEqual([containsElement, containsRange, overlaps, adjacent], Assert.IsInstanceOfType<bool[]>(await command.ExecuteScalarAsync(token)));
        }, context.CancellationToken);

    /// <summary>
    /// Native predicates accept the exact scalar subtype for each range family, including continuous exclusive lower bounds.
    /// </summary>
    [TestMethod]
    [DataRow("int4range", "[1,5)", "[5,9)")]
    [DataRow("int8range", "[1,5)", "[5,9)")]
    [DataRow("numrange", "(1.234,5.670)", "[5.670,9.00)")]
    [DataRow("daterange", "[2000-01-01,2000-01-05)", "[2000-01-05,2000-01-09)")]
    [DataRow("tsrange", "(2000-01-01,2000-01-05)", "[2000-01-05,2000-01-09)")]
    [DataRow("tstzrange", "(2000-01-01 00:00+03,2000-01-05 00:00+03)", "[2000-01-05 00:00+03,2000-01-09 00:00+03)")]
    public Task RangeSubtypePredicatesMatchSql(string type, string left, string right)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(RangeSubtypePredicatesMatchSql), async (connection, transaction, token) =>
        {
            await using var command = new NpgsqlCommand($"""
                SELECT datatype.range_subtype_predicates($1,$2,$3) =
                    ARRAY[a @> lower(a), a @> b, a && b, a -|- b]
                FROM (SELECT $2::{type} a, $3::{type} b) s
                """, connection, transaction);
            command.Parameters.AddWithValue(type);
            command.Parameters.AddWithValue(left);
            command.Parameters.AddWithValue(right);
            Assert.IsTrue(Assert.IsInstanceOfType<bool>(await command.ExecuteScalarAsync(token)));
        }, context.CancellationToken);

    /// <summary>
    /// Disjoint intersection is empty while merge spans the gap and adjacent union joins both inputs.
    /// </summary>
    [TestMethod]
    [DataRow("[1,2)", "[4,5)", 1, "empty")]
    [DataRow("[1,2)", "[4,5)", 2, "[1,2)")]
    [DataRow("[1,2)", "[4,5)", 3, "[1,5)")]
    [DataRow("[1,2)", "[2,5)", 0, "[1,5)")]
    [DataRow("[1,9)", "[0,4)", 2, "[4,9)")]
    [DataRow("[1,9)", "(,)", 2, "empty")]
    public Task RangeSetOperationBoundaryResults(string left, string right, int operation, string expected)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(RangeSetOperationBoundaryResults), async (connection, transaction, token) =>
        {
            await using var command = new NpgsqlCommand("SELECT datatype.range_operate('int4range',$1,$2,$3)", connection, transaction);
            command.Parameters.AddWithValue(left);
            command.Parameters.AddWithValue(right);
            command.Parameters.AddWithValue(operation);
            Assert.AreEqual(expected, await command.ExecuteScalarAsync(token));
        }, context.CancellationToken);

    /// <summary>
    /// Invalid bounds, canonicalization overflow and disjoint results raise PostgreSQL diagnostics after managed unwinding.
    /// </summary>
    [TestMethod]
    [DataRow("datatype.range_construct(5,1,true,false,false)", "22000")]
    [DataRow("datatype.range_construct(2147483647,NULL,false,false,false)", "22003")]
    [DataRow("datatype.range_operate('int4range','[1,2)','[4,5)',0)", "22000")]
    [DataRow("datatype.range_operate('int4range','[1,9)','[4,5)',2)", "22000")]
    [DataRow("datatype.range_decimal('[1,NaN]'::numrange,0)", "38000")]
    [DataRow("datatype.range_decimal('[1,1.00000000000000000000000000001]'::numrange,0)", "38000")]
    [DataRow("datatype.range_date_only('[-infinity,infinity]'::daterange,0)", "38000")]
    [DataRow("datatype.range_date_time('[10000-01-01,10001-01-01)'::tsrange,0)", "38000")]
    [DataRow("datatype.range_date_time_offset('[-infinity,infinity]'::tstzrange,0)", "38000")]
    public Task InvalidRangeConversionsRaiseErrors(string expression, string sqlState)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(InvalidRangeConversionsRaiseErrors), async (connection, transaction, token) =>
        {
            await using var command = new NpgsqlCommand("SELECT " + expression, connection, transaction);
            PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(token));
            Assert.AreEqual(sqlState, error.SqlState);
        }, context.CancellationToken);

    /// <summary>
    /// Canonical values can be inspected in managed code and timezone offsets normalize before range construction.
    /// </summary>
    [TestMethod]
    public Task ManagedCanonicalizationAndOffsetConstruction()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(ManagedCanonicalizationAndOffsetConstruction), async (connection, transaction, token) =>
        {
            await using var command = new NpgsqlCommand("""
                SELECT datatype.range_canonicalize(1,5,false,true),
                    datatype.range_canonicalize(3,3,true,false),
                    range_send(datatype.range_offset_construct()) = range_send('[2024-01-01 21:34:05+00,2024-01-03 10:04:05+00)'::tstzrange)
                """, connection, transaction);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
            Assert.IsTrue(await reader.ReadAsync(token));
            Assert.AreEqual("False:False:2:6:True:False", reader.GetString(0));
            Assert.AreEqual("True:False:::False:False", reader.GetString(1));
            Assert.IsTrue(reader.GetBoolean(2));
        }, context.CancellationToken);

    /// <summary>
    /// Numeric domains and compressed/external range arrays survive nested pointer-free transport and subsequent SPI calls.
    /// </summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task RangeDomainsAndToastedStorage(bool external)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(RangeDomainsAndToastedStorage), async (connection, transaction, token) =>
        {
            string storage = external ? "EXTERNAL" : "EXTENDED";
            await using var command = new NpgsqlCommand($"""
                CREATE DOMAIN pg_temp.numeric_range AS numrange;
                CREATE TEMP TABLE range_storage(r pg_temp.numeric_range, a numrange[]);
                ALTER TABLE range_storage ALTER COLUMN a SET STORAGE {storage};
                INSERT INTO range_storage SELECT '[1.2300,2.450)'::numrange, array_agg('[1.2300,2.450)'::numrange) FROM generate_series(1,10000);
                SELECT pg_column_size(a),
                    range_send(datatype.range_numeric_query('SELECT r FROM range_storage')) = range_send(r),
                    array_send(datatype.range_numerics(a,5)) = array_send(a),
                    range_send(datatype.range_numeric(r,6)) = range_send(r)
                FROM range_storage
                """, connection, transaction);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
            Assert.IsTrue(await reader.ReadAsync(token));
            if (external)
            {
                Assert.IsGreaterThan(100000, reader.GetInt32(0));
            }
            else
            {
                Assert.IsLessThan(10000, reader.GetInt32(0));
            }

            Assert.IsTrue(reader.GetBoolean(1));
            Assert.IsTrue(reader.GetBoolean(2));
            Assert.IsTrue(reader.GetBoolean(3));
        }, context.CancellationToken);

    /// <summary>
    /// Individual ranges with very large numeric bounds survive compressed and external detoasting and retain display scale.
    /// </summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task LargeNumericRangeBoundsOwnDetoastedStorage(bool external)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(LargeNumericRangeBoundsOwnDetoastedStorage), async (connection, transaction, token) =>
        {
            string storage = external ? "EXTERNAL" : "EXTENDED";
            await using var command = new NpgsqlCommand($"""
                CREATE TEMP TABLE large_range_storage(r numrange);
                ALTER TABLE large_range_storage ALTER COLUMN r SET STORAGE {storage};
                INSERT INTO large_range_storage VALUES (numrange(('1'||repeat('23456789',4000)||'.0000')::numeric,
                    ('2'||repeat('23456789',4000)||'.500')::numeric,'[]'));
                SELECT pg_column_size(r),
                    range_send(datatype.range_numeric(r,4)) = range_send(r),
                    range_send(datatype.range_numeric_query('SELECT r FROM large_range_storage')) = range_send(r),
                    scale(lower(datatype.range_numeric(r,7))), scale(upper(datatype.range_numeric(r,7)))
                FROM large_range_storage
                """, connection, transaction);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
            Assert.IsTrue(await reader.ReadAsync(token));
            if (external)
            {
                Assert.IsGreaterThan(30000, reader.GetInt32(0));
            }
            else
            {
                Assert.IsLessThan(2000, reader.GetInt32(0));
            }

            Assert.IsTrue(reader.GetBoolean(1));
            Assert.IsTrue(reader.GetBoolean(2));
            Assert.AreEqual(4, reader.GetInt32(3));
            Assert.AreEqual(3, reader.GetInt32(4));
        }, context.CancellationToken);

    /// <summary>
    /// Repeated native errors preserve successful writes, prepared plans, finally execution and context balance.
    /// </summary>
    [TestMethod]
    public Task RangeFailureRecoveryPreservesSession()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(RangeFailureRecoveryPreservesSession), async (connection, transaction, token) =>
        {
            await using var command = new NpgsqlCommand("SELECT datatype.range_recovery()", connection, transaction);
            Assert.AreEqual("120:40:2:0", await command.ExecuteScalarAsync(token));
        }, context.CancellationToken);
}
