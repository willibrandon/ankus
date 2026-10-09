using System.Globalization;
using System.Text;
using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies <see cref="Rune"/> as varchar, matching pgrx's Rust <c>char</c> round trips, and the stricter rejection
/// of text that is not exactly one Unicode scalar value.
/// </summary>
/// <param name="context">The per-test context.</param>
[TestClass]
public sealed class RuneTests(TestContext context)
{
    /// <summary>
    /// Verifies pgrx's <c>rt_char</c> and <c>takes_char</c> values across one- to four-byte UTF-8 and both UTF-16 widths.
    /// </summary>
    /// <param name="text">The character.</param>
    [TestMethod]
    [DataRow("a")]
    [DataRow("ß")]
    [DataRow("ℝ")]
    [DataRow("💣")]
    [DataRow("🚨")]
    public Task RunesRoundTripAsVarchar(string text)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(RunesRoundTripAsVarchar),
            async (connection, transaction, token) =>
            {
                int scalar = char.ConvertToUtf32(text, 0);
                await using var command = new NpgsqlCommand("""
                    SELECT datatype.rune_round_trip($1), datatype.rune_to_text($1), datatype.rune_scalar($1),
                           datatype.rune_from_scalar($2), pg_typeof(datatype.rune_round_trip($1))::text,
                           octet_length(datatype.rune_from_scalar($2)), datatype.rune_round_trip(NULL) IS NULL
                    """, connection, transaction);
                command.Parameters.AddWithValue(text);
                command.Parameters.AddWithValue(scalar);
                await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
                Assert.IsTrue(await reader.ReadAsync(token));
                Assert.AreEqual(text, reader.GetString(0));
                Assert.AreEqual(text, reader.GetString(1));
                Assert.AreEqual(scalar, reader.GetInt32(2));
                Assert.AreEqual(text, reader.GetString(3));
                Assert.AreEqual("character varying", reader.GetString(4));
                Assert.AreEqual(Encoding.UTF8.GetByteCount(text), reader.GetInt32(5));
                Assert.IsTrue(reader.GetBoolean(6));
            }, context.CancellationToken);

    /// <summary>
    /// Verifies Rune, SQL NULL and pgrx's <c>rt_array_char</c> values, including U+10FFFF, across every SPI path.
    /// </summary>
    /// <param name="mode">The direct, query, plan, session, cursor, retained-plan or edited-row path.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    [DataRow(5)]
    [DataRow(6)]
    [DataRow(7)]
    public Task RunesSurviveEverySpiPath(int mode)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(RunesSurviveEverySpiPath),
            async (connection, transaction, token) =>
            {
                string?[] characters = [null, "a", char.ConvertFromUtf32(0x10FFFF), null, "d", null];
                await using var command = new NpgsqlCommand("""
                    SELECT datatype.exchange_rune('💣', $2), datatype.exchange_rune(NULL, $2) IS NULL,
                           datatype.exchange_runes($1::varchar[], $2), datatype.exchange_runes(NULL, $2) IS NULL,
                           pg_typeof(datatype.exchange_runes($1::varchar[], $2))::text,
                           datatype.exchange_runes('{}', $2) = '{}'
                    """, connection, transaction);
                command.Parameters.AddWithValue(characters);
                command.Parameters.AddWithValue(mode);
                await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
                Assert.IsTrue(await reader.ReadAsync(token));
                Assert.AreEqual("💣", reader.GetString(0));
                Assert.IsTrue(reader.GetBoolean(1));
                Assert.AreSequenceEqual(characters, reader.GetFieldValue<string?[]>(2));
                Assert.IsTrue(reader.GetBoolean(3));
                Assert.AreEqual("character varying[]", reader.GetString(4));
                Assert.IsTrue(reader.GetBoolean(5));
            }, context.CancellationToken);

    /// <summary>
    /// Verifies a Rune vector preserves surrogate pairs as single elements.
    /// </summary>
    [TestMethod]
    public Task RuneVectorsKeepEachScalarValueWhole()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(RuneVectorsKeepEachScalarValueWhole),
            async (connection, transaction, token) =>
            {
                await using var command = new NpgsqlCommand(
                    "SELECT datatype.reverse_runes(ARRAY['a', 'ß', 'ℝ', '💣']::varchar[])", connection, transaction);
                Assert.AreSequenceEqual(["💣", "ℝ", "ß", "a"], Assert.IsInstanceOfType<string[]>(await command.ExecuteScalarAsync(token)));
            }, context.CancellationToken);

    /// <summary>
    /// Verifies SPI reads Rune from any character-text column, and SQL NULL or no rows as a nullable Rune.
    /// </summary>
    /// <param name="sql">The query.</param>
    /// <param name="expected">The expected scalar value, or null for SQL NULL.</param>
    [TestMethod]
    [DataRow("SELECT '💣'::text", "128163")]
    [DataRow("SELECT 'ß'::varchar(1)", "223")]
    [DataRow("SELECT 'a'::char(1)", "97")]
    [DataRow("SELECT ''::text WHERE false", null)]
    [DataRow("SELECT NULL::varchar", null)]
    public Task SpiReadsRunesFromCharacterText(string sql, string? expected)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(SpiReadsRunesFromCharacterText),
            async (connection, transaction, token) =>
            {
                await using var command = new NpgsqlCommand("SELECT datatype.rune_from_query($1)::text", connection, transaction);
                command.Parameters.AddWithValue(sql);
                Assert.AreEqual((object?)expected ?? DBNull.Value, await command.ExecuteScalarAsync(token));
            }, context.CancellationToken);

    /// <summary>
    /// Verifies text that is not exactly one scalar value fails instead of being truncated or read as NULL, as pgrx
    /// would, and the backend remains usable.
    /// </summary>
    /// <param name="expression">The failing SQL expression.</param>
    /// <param name="message">The expected message fragment.</param>
    [TestMethod]
    [DataRow("datatype.rune_round_trip('')", "Empty text has no character to read as a Rune.")]
    [DataRow("datatype.rune_round_trip('ab')", "A Rune holds exactly one Unicode scalar value, but the text has more than one.")]
    [DataRow("datatype.rune_scalar('a' || chr(769))", "A Rune holds exactly one Unicode scalar value, but the text has more than one.")]
    [DataRow("datatype.exchange_rune('', 0)", "Empty text has no character to read as a Rune.")]
    [DataRow("datatype.exchange_runes(ARRAY['a', 'bc']::varchar[], 1)", "A Rune holds exactly one Unicode scalar value, but the text has more than one.")]
    [DataRow("datatype.rune_from_query('SELECT ''a  ''::char(3)')", "A Rune holds exactly one Unicode scalar value, but the text has more than one.")]
    [DataRow("datatype.rune_from_query('SELECT ''''::text')", "Empty text has no character to read as a Rune.")]
    public async Task TextThatIsNotOneScalarValueIsRejected(string expression, string message)
    {
        CancellationToken token = context.CancellationToken;
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        int backend = connection.ProcessID;
        await using var command = new NpgsqlCommand("SELECT " + expression, connection);
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(token));
        Assert.AreEqual(PostgresErrorCodes.ExternalRoutineException, error.SqlState, error.MessageText);
        Assert.Contains(message, error.MessageText);
        command.CommandText = "SELECT datatype.rune_scalar('ℝ')";
        Assert.AreEqual(0x211D, await command.ExecuteScalarAsync(token));
        command.CommandText = "SELECT datatype.rune_round_trip(chr(" + 0x1F4A3.ToString(CultureInfo.InvariantCulture) + "))";
        Assert.AreEqual("💣", await command.ExecuteScalarAsync(token));
        Assert.AreEqual(backend, connection.ProcessID);
    }
}
